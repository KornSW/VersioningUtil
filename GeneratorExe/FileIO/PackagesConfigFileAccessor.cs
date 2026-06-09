using System;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Utils;
using Versioning;
using System.Xml;
using System.Xml.Linq;

namespace FileIO {

  public class PackagesConfigFileAccessor : IVersionContainer {

    private string _FileFullName;
    private Action<PackagesConfigFileAccessor> _OnChangedCallback;
    private Version _NetFxVersionOfParentProject;

    private VsProjFileAccessor _ProjectFileAccessor = null;

    public PackagesConfigFileAccessor(
      string fileFullName
    ) {
      //HIER MUSS DASPARENT PROJECT SELBST GESUCHT WERDEN
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }

      _FileFullName = Path.GetFullPath(fileFullName);
      string projectFileFullName = Directory.GetFiles(Path.GetDirectoryName(fileFullName), "*.csproj|*.vbproj").FirstOrDefault();

      if(!string.IsNullOrWhiteSpace(projectFileFullName)) {
        _ProjectFileAccessor = new VsProjFileAccessor(projectFileFullName);
        _NetFxVersionOfParentProject = _ProjectFileAccessor.GetDotNetVersion();
        //_OnChangedCallback = _ProjectFileAccessor.OnPackageConfigChanged;
      }
      else {
        _NetFxVersionOfParentProject = new Version(4,8,1);
        //_OnChangedCallback = (p) => { /*do nothing*/ };
      }

    }

    public PackagesConfigFileAccessor(
      string fileFullName, VsProjFileAccessor parentProject
    ) {
      _FileFullName = Path.GetFullPath(fileFullName);
      _ProjectFileAccessor = parentProject;
      _NetFxVersionOfParentProject = _ProjectFileAccessor.GetDotNetVersion();
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public PackagesConfigFileAccessor(
      string fileFullName, Version netFxVersionOfParentProject,
      Action<PackagesConfigFileAccessor> onChangedCallback = null
    ) {
      _FileFullName = Path.GetFullPath(fileFullName);
      _OnChangedCallback = onChangedCallback;
      _NetFxVersionOfParentProject = netFxVersionOfParentProject;
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public VersionInfo ReadVersion() {
      //TODO: load VsProjFileAccessor here and redirect call...
      throw new NotImplementedException("A 'packages.config' file does not contain version-information for its owner\"");
    }  
    public void WriteVersion(VersionInfo versionInfo) {
      //TODO: load VsProjFileAccessor here and redirect call...
      throw new NotImplementedException("A 'packages.config' file does not contain version-information for its owner");
    }

    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew, bool updateExisiting, bool deleteOthers,
      string onlyForTargetFramework
    ) {

      DependencyUpdateHelper updateHelper = new DependencyUpdateHelper(
         () => ReadPackageDependencies(true), (deps) => OverwriteAllPackageDependencies(deps.ToArray())
      );

      updateHelper.WritePackageDependencies(
        packageDependencies, addNew, updateExisiting, deleteOthers, onlyForTargetFramework
      );

      if(_ProjectFileAccessor != null) {

        //wir als führende einheit schieben zusätzlich zurück ins parent-projekt...
        _ProjectFileAccessor._LocalUpdateHelper.WritePackageDependencies(
          packageDependencies, addNew, updateExisiting, deleteOthers, onlyForTargetFramework
        );

      }

    }

    public void OverwriteAllPackageDependencies(DependencyInfo[] newDependencies) {

      XDocument document = XDocument.Load(_FileFullName, LoadOptions.PreserveWhitespace);
      XElement rootElement = document.Root;

      if (rootElement == null) {
        throw new InvalidDataException("The packages.config file does not contain a root element.");
      }

      DependencyInfo[] relevantDependencies = newDependencies
        .Where((dependency) => IsDependencyRelevantForThisPackagesConfig(dependency))
        .ToArray();

      bool fileChanged = false;

      foreach (DependencyInfo packageDependency in relevantDependencies) {
        XElement packageElement = rootElement
          .Elements("package")
          .FirstOrDefault((element) => {
            XAttribute idAttribute = element.Attribute("id");

            if (idAttribute == null) {
              return false;
            }

            return string.Equals(
              idAttribute.Value,
              packageDependency.TargetPackageId,
              StringComparison.OrdinalIgnoreCase
            );
          });

        if (packageElement != null) {
          string versionText = packageDependency.TargetPackageVersionConstraint.ToString(true);
          XAttribute versionAttribute = packageElement.Attribute("version");

          if (versionAttribute == null) {
            packageElement.SetAttributeValue("version", versionText);
            fileChanged = true;
          }
          else if (!string.Equals(versionAttribute.Value, versionText, StringComparison.Ordinal)) {
            versionAttribute.Value = versionText;
            fileChanged = true;
          }

        }
        else {
          XElement newPackageElement = new XElement("package");
          newPackageElement.Add(new XAttribute("id", packageDependency.TargetPackageId));
          newPackageElement.Add(new XAttribute("version", packageDependency.TargetPackageVersionConstraint.ToString(true)));
          if (!string.IsNullOrWhiteSpace(packageDependency.DedicatedToTargetFramework)) {
            newPackageElement.Add(new XAttribute("targetFramework", packageDependency.DedicatedToTargetFramework));
          }
          rootElement.Add(new XText("  "));
          rootElement.Add(newPackageElement);
          rootElement.Add(new XText(Environment.NewLine));
          fileChanged = true;
        }
      }

      if (fileChanged) {
        XmlWriterSettings settings = new XmlWriterSettings();
        settings.Encoding = FileIoHelper.DetectFileEncoding(_FileFullName);
        settings.Indent = false;
        settings.OmitXmlDeclaration = false;

        using (XmlWriter writer = XmlWriter.Create(_FileFullName, settings)) {
          document.Save(writer);
        }

        //gibt dem projekt, welchem wir als packages.config untergeornet sind, die chance,
        //seine eigenen hint-pfade usw auch mit zu aktualisieren
        if (_OnChangedCallback != null) {
          _OnChangedCallback(this);
        }

      }

      // HIER ÄNDERN //

      //string rawContent = File.ReadAllText(_FileFullName, Encoding.UTF8);

      //  bool fileChanged = false;
      //  foreach (var packageDependency in newDependencies) {
      //    int matchCount = 0;
      //    rawContent = FileIoHelper.Replace(
      //      rawContent,
      //      $"<package id=\"{packageDependency.TargetPackageId}\" version=\"[a-zA-Z0-9.\\-\\*]*\"",
      //      $"<package id=\"{packageDependency.TargetPackageId}\" version=\"{packageDependency.TargetPackageVersionConstraint.ToString(true)}\"", ref matchCount
      //    );
      //    if (matchCount > 0) {
      //      Console.WriteLine($"  Processed ref version of {matchCount} matches for \"{packageDependency.TargetPackageId}\" to \"{packageDependency.TargetPackageVersionConstraint}\"");
      //      fileChanged = true;
      //    }
      //  }

      //  if (fileChanged) {
      //    FileIoHelper.WriteFile(_FileFullName, rawContent);
      //  }



      ////////////////////



 

    }


    //private string _RegexSearch = "<package id=\"[a-zA-Z0-9.\\-\\*]*\" version=\"[a-zA-Z0-9.\\-\\*]*\"";
    public DependencyInfo[] ReadPackageDependencies(bool includeFrameworkInfo) {

      XDocument document = XDocument.Load(_FileFullName, LoadOptions.PreserveWhitespace);
      XElement rootElement = document.Root;

      if (rootElement == null) {
        throw new InvalidDataException("The packages.config file does not contain a root element.");
      }

      return rootElement
        .Elements("package")
        .Select((element) => {
          XAttribute idAttribute = element.Attribute("id");
          XAttribute versionAttribute = element.Attribute("version");

          if (idAttribute == null || versionAttribute == null) {
            return null;
          }

          return new DependencyInfo(idAttribute.Value, versionAttribute.Value);
        })
        .Where((dependency) => dependency != null)
        .ToArray();

      //string rawContent = File.ReadAllText(_FileFullName, Encoding.UTF8);

      //return Regex.Matches(rawContent, _RegexSearch).Select((m) => {
      //  string[] slt = m.Value.Split('"');
      //  return new DependencyInfo(slt[1], slt[3]);
      //}).ToArray();

    }
    /// <summary>
    /// Determines whether the given dependency is relevant for this packages.config file.
    /// </summary>
    private bool IsDependencyRelevantForThisPackagesConfig(DependencyInfo dependency) {

      if (dependency.TargetPackageVersionConstraint == null) {
        return false;
      }

      if (string.IsNullOrWhiteSpace(dependency.DedicatedToTargetFramework)) {
        return true;
      }

      if (!dependency.DedicatedToTargetFramework.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }

      Version targetNetFxVersion;
      if (!Version.TryParse(dependency.DedicatedToTargetFramework.Substring(".NETFramework".Length), out targetNetFxVersion)) {
        return false;
      }

      return ( //jaja - etwas fuzzy - aber passt ins reale leben....
       _NetFxVersionOfParentProject.Major == targetNetFxVersion.Major &&
       _NetFxVersionOfParentProject.Minor == targetNetFxVersion.Minor
      );

    }

    public static void CreateNewFile(string pkgCfgFileName) {
      File.WriteAllText(
        pkgCfgFileName,
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<packages>\r\n</packages>\r\n",
        new UTF8Encoding(true)
      );
      Thread.Sleep(700);
    }

  }

}
