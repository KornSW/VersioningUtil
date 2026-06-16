using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using System.Xml;
using Utils;
using Versioning;

namespace FileIO {

  public partial class VsProjFileAccessor : IVersionContainer {

    private string _FileFullName;

    /// <summary>
    /// nur die dependencies IN der projekt-datei (d.h. bei .net-fx projekte die assembly-referenzen, die in verbindung mit nuget-packeten stehen) - nicht die packages.config-datei, die bei .net-fx projekte führend ist!
    /// </summary>
    internal DependencyUpdateHelper2 _LocalUpdateHelper;

    public VsProjFileAccessor(string fileFullName) {

      _FileFullName = Path.GetFullPath(fileFullName);

      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }

      _LocalUpdateHelper = new DependencyUpdateHelper2(this,
        () => ReadPackageDependenciesLocal(false), (deps) => OverwriteAllLocalPackageDependencies(deps.ToArray())
      );

    }

    public bool IsCSharp() {
      return _FileFullName.EndsWith(".csproj");
    }

    public bool IsVb() {
      return _FileFullName.EndsWith(".vbproj");
    }


    private int _IsDotNetCoreFormat = -1;
    public bool IsDotNetCoreFormat() {
      if (_IsDotNetCoreFormat == -1) {
        string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
        bool isDotNetLegacy = rawContent.Contains("<TargetFrameworkVersion>");
        _IsDotNetCoreFormat = isDotNetLegacy ? 0 : 1;
      }
      return _IsDotNetCoreFormat == 1;
    }

    public VersionInfo ReadVersion() {
      IVersionContainer target = this.GetContainerOfVersion();
      if (target != this) {
        return target.ReadVersion();
      }

      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      var versionInfo = new VersionInfo();

      var matchVers = Regex.Matches(rawContent, _RegexSearchVer).FirstOrDefault();
      if (matchVers != null) {
        versionInfo.currentVersionWithSuffix = matchVers.Value.Substring(9, matchVers.Value.Length - 19);
      }
      else {
        var matchAssVers = Regex.Matches(rawContent, _RegexSearchAssVer).FirstOrDefault();
        if (matchAssVers != null) {
          versionInfo.currentVersionWithSuffix = matchAssVers.Value.Substring(17, matchAssVers.Value.Length - 35);
        }
      }

      //if (versionInfo.currentVersionWithSuffix.EndsWith(".*")) {
      //  versionInfo.currentVersionWithSuffix = versionInfo.currentVersionWithSuffix.Substring(
      //    0, versionInfo.currentVersionWithSuffix.Length - 2
      //  );
      //}

      versionInfo.CurrentVersionWithSuffix2CurrentVersionAndPrereleaseSuffx(true);

      Console.WriteLine($"Loaded Version '{versionInfo.currentVersionWithSuffix}' from '{_FileFullName}'");
      return versionInfo;
    }

    // ONLY .NET CORE!!!!!

    //<PropertyGroup>
    //  <Version>1.0.0-foo</Version>                 <<<<<<<<<< the Package-Version
    //  <AssemblyVersion>2.0.0.0</AssemblyVersion>
    //  <FileVersion>2.0.0.0</FileVersion>
    //</PropertyGroup>

    private string _RegexSearchVer = "(<Version\\>[a-zA-Z0-9.\\-\\*]*<\\/Version\\>)";
    private string _RegexSearchAssVer = "(<AssemblyVersion\\>[a-zA-Z0-9.\\-\\*]*<\\/AssemblyVersion\\>)";
    private string _RegexSearchFileVer = "(<FileVersion\\>[a-zA-Z0-9.\\-\\*]*<\\/FileVersion\\>)";

    public void WriteVersion(VersionInfo versionInfo) {
      IVersionContainer target = this.GetContainerOfVersion();
      if (target != this) {
        target.WriteVersion(versionInfo);
        return;
      }

      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      Console.WriteLine($"Writing Version '{versionInfo.currentVersionWithSuffix}' into '{_FileFullName}'");

      int matchCount = 0;
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearchVer, $"<Version>{versionInfo.currentVersionWithSuffix}</Version>", ref matchCount
      );
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearchAssVer, $"<AssemblyVersion>{versionInfo.currentVersion}</AssemblyVersion>", ref matchCount
      );
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearchFileVer, $"<FileVersion>{versionInfo.currentVersion}</FileVersion>", ref matchCount
      );

      Console.WriteLine($"  Processed {matchCount} matches...");
      if (matchCount > 0) {
        FileIoHelper.WriteFile(_FileFullName, rawContent);
      }
    }

    private IVersionContainer GetContainerOfVersion() {
      if (this.IsDotNetCoreFormat()) {
        return this;
      }
      else if(this.IsCSharp()){
        string assemblyInfoFileFullName = Path.Combine(
          Path.GetDirectoryName(_FileFullName), "Properties\\AssemblyInfo.cs"
        );
        return new AssemblyInfoFileAccessor(assemblyInfoFileFullName);
      }
      else if (this.IsVb()) {
        string assemblyInfoFileFullName = Path.Combine(
          Path.GetDirectoryName(_FileFullName), "My Project\\AssemblyInfo.vb"
        );
        return new AssemblyInfoFileAccessor(assemblyInfoFileFullName);
      }
      else {
        throw new Exception("Unknown format of Projekt-File");
      }
    }

    private PackagesConfigFileAccessor _PackagesConfig = null;
    private IVersionContainer GetContainerOfDependencies(bool createExternaFiles) {
      if (this.IsDotNetCoreFormat()) {
        return this;
      }
      else {
        string pkgCfgFileName = Path.Combine(Path.GetDirectoryName(_FileFullName), "packages.config");
        if (!File.Exists(pkgCfgFileName)) {
          if (!createExternaFiles) {
            return new InMemoryContainer();
          }
          PackagesConfigFileAccessor.CreateNewFile(pkgCfgFileName);
        }
        //für .net-fx (wo es das projekt mit seinen assembly-refs + die packages.config gibt,
        //sehen wir letztere als führend!
        if (_PackagesConfig == null) { 
          _PackagesConfig = new PackagesConfigFileAccessor(pkgCfgFileName, this);
        }
        return _PackagesConfig;
      }
    }


    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew, bool updateExisiting, bool deleteOthers, bool allowDowngrade, 
      string onlyForTargetFramework, string[] packageIdWhitelist, string[] packageIdBlacklist
    ) {

      IVersionContainer target = this.GetContainerOfDependencies(true);
      if(target != this) {
        //für .net-fx wird hier auf die packages.config umgeleitet...
        target.WritePackageDependencies(
          packageDependencies, 
          addNew, updateExisiting, deleteOthers, allowDowngrade,
          onlyForTargetFramework, packageIdWhitelist, packageIdBlacklist
        );
        return;
      }

      _LocalUpdateHelper.WritePackageDependencies(
        packageDependencies, addNew, updateExisiting, deleteOthers, allowDowngrade, onlyForTargetFramework
      );

    }

    private void OverwriteAllLocalPackageDependencies(DependencyInfo[] newDependencies) {

      XDocument document = XDocument.Load(_FileFullName, LoadOptions.PreserveWhitespace);
      XElement rootElement = document.Root;

      if (rootElement == null) {
        throw new InvalidDataException("The project file does not contain a root element.");
      }

      bool fileChanged = false;

      if (this.IsDotNetCoreFormat()) {
        fileChanged = this.OverwriteDotNetCorePackageReferences(document, rootElement, newDependencies);
      }
      else {
        fileChanged = this.OverwriteDotNetFrameworkNugetReferences(document, rootElement, newDependencies);
      }

      if (fileChanged) {
        this.SaveXmlDocument(document);
      }

      return;


      // HIER ÄNDERN //

      //wichtig: über alle ItemGroups suchen!
      //+beim hinzufügen neuer referenzen: darauf achten, dass diese in der richtigen ItemGroup landen
      //(nämlich der, wo auch die anderen package-referenzen drin sind!) - sonst neue, direkt hinter der letzten propertygroup anlegen!!

      //AUßerdem: nicht einfach löschen und neu anlagen, damit manuelle sortierung oder kommentare erhalten bleiben -
      //sondern wirklich nur die zeilen mit den referenzen anpassen!


      if (this.IsDotNetCoreFormat()) {


      //string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);


      //  //UMBAU AUF XML
      //  bool fileChanged = false; 
      //  foreach (var packageDependency in newDependencies) {
      //    int matchCount = 0;
      //    rawContent = FileIoHelper.Replace(
      //      rawContent,
      //      $"<PackageReference Include=\"{packageDependency.TargetPackageId}\" Version=\"[a-zA-Z0-9.\\-\\*]*\"",
      //      $"<PackageReference Include=\"{packageDependency.TargetPackageId}\" Version=\"{packageDependency.TargetPackageVersionConstraint.ToString(true)}\"", ref matchCount
      //    );
      //    if(matchCount > 0) {
      //      Console.WriteLine($"  Processed ref version of {matchCount} matches for \"{packageDependency.TargetPackageId}\" to \"{packageDependency.TargetPackageVersionConstraint}\"");
      //      fileChanged = true;
      //    } 
      //  }

      //  if (fileChanged) {
      //    FileIoHelper.WriteFile(_FileFullName, rawContent);
      //  }




      }
      else { //hier gehts NUR im die asslembly-referenzen, die in verbindung mit nuget-packeten stehen!!!!

        //WICHTIG: auch wenn das hier formal ein OverwriteAll ist,
        //bezieht sich dies ausschlißelich auf mit nuget-packeten in verbindung stehende
        //assembly-referenzen! - die anderen müssen erhalten bleiben, genauso wie etwaige <Private>True</Private>-konten!!!

        string packagesFullDirectoryName = this.GetPackagesFullDirectoryName();



        /*
          beim aktualisieren wird man die assemblynamen bereits kennen - hier einfach nur die hint-pfade und (aber nur falls existent)
          die Version= innerhlab des Include=" anpassen
          
         challenge beim NEU hinzufügen: 
         1:wir kennen den oder die konkreten dll-namen nicht
         2:wir müssen den best-verträglichen framework unterordner zb. "lib\net48\" im packet finden!

        einzige chance -> wenn packet schon im ordner liegt 
        (achtung hier darauf achen wenn es der GetDotNetCoreCentralizedPackagesDir() ist, 
        dann gibt ist die struktur \{packageId}\{version}\lib\{framework}\{dlls} - also nochmal eine ebene mehr als bei einem lokalen packages-ordner)
        und die version stimmt, dann können wir von dort die dll-namen und den framework-ordner ermitteln, und damit die referenz in der csproj anlegen oder updaten
       
        fallbkack 1 wäre die orientierung an älteren packetversionen die da liegen

        fallback 2 basiet auf der mutmaßung, dass die dll-namen dem paketnamen gleichen +
        ein best-verträglicher framework-ordner (z.b. net48) existiert!
         

        ALL DA MINIMAL INVASIV
       */



      }

      /////////////////
    }

    public string TryFindSolutionDirectory() {
      string dir = Path.GetDirectoryName(_FileFullName);
      while (!string.IsNullOrEmpty(dir)) {
        if (Directory.GetFiles(dir, "*.sln").Length > 0) {
          return dir;
        }
        dir = Path.GetDirectoryName(dir);
      }
      return null;
    }



   // private string _RegexSearch = "<PackageReference Include=\"[a-zA-Z0-9.\\-\\*]*\" Version=\"[a-zA-Z0-9.\\-\\*]*\"";

    public DependencyInfo[] ReadPackageDependencies(bool includeFrameworkInfo) {

      IVersionContainer target = this.GetContainerOfDependencies(true);
      if (target != this) {//für .net-fx wird hier auf die packages.config umgeleitet...
        return target.ReadPackageDependencies(includeFrameworkInfo);
      }

      return ReadPackageDependenciesLocal(includeFrameworkInfo);
    }

    private DependencyInfo[] ReadPackageDependenciesLocal(bool includeFrameworkInfo) {

      XDocument document = XDocument.Load(_FileFullName, LoadOptions.PreserveWhitespace);
      XElement rootElement = document.Root;

      if (rootElement == null) {
        throw new InvalidDataException("The project file does not contain a root element.");
      }

      if (this.IsDotNetCoreFormat()) {
        return this.ReadDotNetCorePackageReferences(rootElement, includeFrameworkInfo);
      }
      else {
        return this.ReadDotNetFrameworkNugetReferences(rootElement, includeFrameworkInfo);
      }

    }

    public bool CanRepresentDependencyScopes() {
      //HACK: ich glaube irgendwie geht das doch bei multi-target projekten...
      return false;
    }

    public bool UsesDependencyScopes() {
      //HACK: ich glaube irgendwie geht das doch bei multi-target projekten...
      return false;
    }

    public string[] GetDependencyScopes() {
      return this.GetDotNetVersionRaw().Split(";");
    }

    #region HELPER 1 

    //internal void OnPackageConfigChanged(IVersionContainer packageConfig) {

    //  //syncchinisiert die Abhängigkeiten aus der packages.config (führend)
    //  //zurück in die .csproj/.vbproj, damit sie dort auch immer aktuell sind

    //  DependencyUpdateHelper updateHelper = new DependencyUpdateHelper(
    //     () => ReadPackageDependenciesLocal(false), (deps) => OverwriteAllLocalPackageDependencies(deps.ToArray())
    //  );

    //  DependencyInfo[] realExsistingPackageEntries = packageConfig.ReadPackageDependencies(true);

    //  updateHelper.WritePackageDependencies(
    //    realExsistingPackageEntries, true, true, true, true, null
    //  );

    //}

    /// <summary>
    /// vor allem bei .NET Framework Projekten wichtig,
    /// da die Abhängigkeiten in der packages.config hier nicht automatisch installiert werden
    /// - hier hilft diese methode und korrigiert hint-pfad oder fürgt assembly-referenzen hinzu
    /// (EIN RESTORE FINDET AKT. NiCHT AUTOMATISCH STATT)
    /// </summary>
    /// <returns></returns>
    public void EnsureDependenciesAreInstalled() {     
      if (this.IsDotNetCoreFormat()) {
        return;
      }

      throw new NotImplementedException("");
 
    }

    public bool HasNugetPackage(string packageId) {
      return this.ReadPackageDependencies(false).Any((d) => d.TargetPackageId == packageId);
    }

    /// <summary>
    /// Reads the target .NET version from the Visual Basic project file.
    /// </summary>
    public Version GetDotNetVersion() {
      string rawVersion = GetDotNetVersionRaw();
      return ParseDotNetVersion(rawVersion);
    }


    private string _CachedDotNetVersionRaw = null;

    public string GetDotNetVersionRaw() {
      if (_CachedDotNetVersionRaw != null) {
        return _CachedDotNetVersionRaw;
      }
      XDocument document = XDocument.Load(this._FileFullName, LoadOptions.PreserveWhitespace);

      XElement targetFrameworkVersionElement = document
        .Descendants()
        .FirstOrDefault((element) => {
          return string.Equals(element.Name.LocalName, "TargetFrameworkVersion", StringComparison.OrdinalIgnoreCase);
        });

      if (targetFrameworkVersionElement != null) {
        return targetFrameworkVersionElement.Value;
      }

      XElement targetFrameworkElement = document
        .Descendants()
        .FirstOrDefault((element) => {
          return string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase);
        });

      if (targetFrameworkElement != null) {
        _CachedDotNetVersionRaw = targetFrameworkElement.Value;
        return _CachedDotNetVersionRaw;
      }

      throw new InvalidDataException("The project file does not contain TargetFrameworkVersion or TargetFramework.");
    }
    
    string _PackagesFullDirectoryName = null;
    public string GetPackagesFullDirectoryName() {

      if(_PackagesFullDirectoryName != null) {
        return _PackagesFullDirectoryName;
      }

      string solutionDir = TryFindSolutionDirectory();

      if (string.IsNullOrWhiteSpace(solutionDir)) {

        if (IsDotNetCoreFormat()) {
          _PackagesFullDirectoryName = GetDotNetCoreCentralizedPackagesDir();
        }
        else {
          //verzweiflungstat (da kein bezug zu einer solution besteht) - erraten aus hint-pfaden!
          string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
          string[] hintPaths = Regex.Matches(rawContent, "<HintPath>[a-zA-Z0-9\\\\.\\-\\*]*</HintPath>").Select((m) => {
            return m.Value.Substring(10, m.Value.Length - 21);
          }).ToArray();

          _PackagesFullDirectoryName = TryAssumePackagesFullDirectoryNameFromHintPaths(hintPaths);
        }

        return _PackagesFullDirectoryName;
      }

      string potentialExistingNugetConfig = Path.Combine(solutionDir, "nuget.config");
      _PackagesFullDirectoryName = TryReadRepositoryPathFromNugetConfig(potentialExistingNugetConfig);
      if (string.IsNullOrWhiteSpace(_PackagesFullDirectoryName)) {
         potentialExistingNugetConfig = Path.Combine(solutionDir, ".nuget", "nuget.config");
         _PackagesFullDirectoryName = TryReadRepositoryPathFromNugetConfig(potentialExistingNugetConfig);
      }
  
      if (string.IsNullOrWhiteSpace(_PackagesFullDirectoryName)) {
        if (IsDotNetCoreFormat()) {
          _PackagesFullDirectoryName = GetDotNetCoreCentralizedPackagesDir();
        }
        else {
          _PackagesFullDirectoryName = Path.Combine(solutionDir, "packages");
        }
      }
      if(_PackagesFullDirectoryName == null) {
        _PackagesFullDirectoryName = string.Empty;
      }
      return _PackagesFullDirectoryName;
    }

    private static string GetDotNetCoreCentralizedPackagesDir() {
      string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      string centralPackagesDir = Path.Combine(userProfile, ".nuget", "packages");
      return centralPackagesDir;
    }

    internal string TryAssumePackagesFullDirectoryNameFromHintPaths(string[] hintPaths) {
      foreach (string hintPath in hintPaths) {
        int idx = hintPath.IndexOf("\\lib\\net");
        if (idx > 0) {
          //Beispiel:  "..\..\vendor\nuget\SmartStandards.Logging.3.3.1"
          string[] parts = hintPath.Substring(0, idx).Split("\\");
          string assumedPackageNameFolder = parts[parts.Length - 1];
          string[] assumedPackageNameFolderTokens = assumedPackageNameFolder.Split(".");
          if (
            assumedPackageNameFolderTokens.Length > 4 &&
            //die letzten 3 tokens des ordnernamens sind zahlen (version) - das spricht stark dafür, dass es sich hier um
            //einen nuget-paketordner handelt, und wir von dort aus zurück zum packages-ordner kommen können
            int.TryParse(assumedPackageNameFolderTokens[assumedPackageNameFolderTokens.Length - 1], out int _) &&
            int.TryParse(assumedPackageNameFolderTokens[assumedPackageNameFolderTokens.Length - 2], out int _) &&
            int.TryParse(assumedPackageNameFolderTokens[assumedPackageNameFolderTokens.Length - 3], out int _)
          ) {
            string projectDir = Path.GetDirectoryName(_FileFullName);
            string assumedPackageDir = Path.GetFullPath(Path.Combine(projectDir, hintPath.Substring(0, idx - assumedPackageNameFolder.Length)));
            return assumedPackageDir;
          }
          parts[parts.Length - 1].Split(".")[0] = parts[parts.Length - 1].Split(".")[0] + ".*";
          return hintPath.Substring(0, idx);
        }
      }
      return null;
    }


    internal static string TryReadRepositoryPathFromNugetConfig(string nugetConfigFullName) {
      if (!File.Exists(nugetConfigFullName)) {
        return null;
      }

      XDocument document = XDocument.Load(nugetConfigFullName, LoadOptions.PreserveWhitespace);
      XElement rootElement = document.Root;

      if (rootElement == null) {
        return null;
      }

      string configuredRepositoryPath = rootElement.Descendants("add").Where((element) => {
        XAttribute keyAttribute = element.Attribute("key");
        return keyAttribute != null && keyAttribute.Value == "repositoryPath";
      }).Select((element) => {
        XAttribute valueAttribute = element.Attribute("value");
        if (valueAttribute == null) {
          return null;
        }
        return valueAttribute.Value;
      }).FirstOrDefault();

      if (!string.IsNullOrWhiteSpace(configuredRepositoryPath)) {
        configuredRepositoryPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(nugetConfigFullName), configuredRepositoryPath));
      }

      return configuredRepositoryPath;
    }



    /// <summary>
    /// Parses a .NET framework/version string from project files.
    /// </summary>
    internal static Version ParseDotNetVersion(string rawValue) {
      if (string.IsNullOrWhiteSpace(rawValue)) {
        throw new InvalidDataException("The target framework value is empty.");
      }

      string value = rawValue.Trim();

      if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) {
        value = value.Substring(1);
      }
      else if (value.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)) {
        value = value.Substring("netcoreapp".Length);
      }
      else if (value.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)) {
        value = value.Substring("netstandard".Length);
      }
      else if (value.StartsWith("net", StringComparison.OrdinalIgnoreCase)) {
        value = value.Substring("net".Length);

        if (value.Length == 2 && Char.IsDigit(value[0]) && Char.IsDigit(value[1])) {
          value = value[0] + "." + value[1];
        }
      }

      int suffixIndex = value.IndexOf('-');

      if (suffixIndex >= 0) {
        value = value.Substring(0, suffixIndex);
      }


      //alte .net-fx schreibweisen wie "4" oder "481" 
      if (!value.Contains(".") && int.TryParse(value, out int numericValue)) {

        if(value.Length == 1) {
          return new Version(int.Parse(value), 0);
        }
        if (value.Length == 2) {
          return new Version(int.Parse(value.Substring(0,1)), int.Parse(value.Substring(1, 1)));
        }
        if (value.Length == 3) {
          return new Version(int.Parse(value.Substring(0, 1)), int.Parse(value.Substring(1, 1)), int.Parse(value.Substring(2, 1)));
        }
        return new Version(numericValue, 0);
      }
      if (!Version.TryParse(value, out Version parsedVersion)) {
        throw new InvalidDataException("Could not parse target framework value: " + rawValue);
      }

      return parsedVersion;
    }

    #endregion

  }

}
