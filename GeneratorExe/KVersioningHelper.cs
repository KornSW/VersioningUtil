using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FileIO;
using Utils;
using Versioning.ShouldBeLibrary;
using Versioning.TextProcessing;

namespace Versioning {

  public class KVersioningHelper {

    public void ImportGitCommentsIntoChangelog(
      string versioninfoFile = "versioninfo.json",
      string changeLogFile = "changelog.md",
      string ignoreBySubstring = "(last version"
    ) {

      DateTime lastVersionTime = DateTime.MinValue;
      IVersionContainer src = InitializeVersionContainerByFileType(versioninfoFile);
      if (src != null) {
        VersionInfo vers = src.ReadVersion();
        if(!DateTime.TryParse(vers.versionDateInfo + " " + vers.versionTimeInfo,out lastVersionTime)) {
          Console.WriteLine($"Versioninfo file '{versioninfoFile}' does not contain valid time information!");
          return;
        }
      }

      if(lastVersionTime == DateTime.MinValue) {
        Console.WriteLine($"Importing comments from GIT-History since repository creation:");     
      }
      else {
        Console.WriteLine($"Importing comments from GIT-History since {lastVersionTime:yyyy-MM-dd} {lastVersionTime:HH:mm:ss} (last version):");
      }
      Console.WriteLine();

      if (!Path.IsPathRooted(changeLogFile)) {
        changeLogFile = Path.Combine(Environment.CurrentDirectory, changeLogFile);
      }
      changeLogFile = Path.GetFullPath(changeLogFile);

      var allLines = new List<string>();

      using (FileStream fs = new FileStream(changeLogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite)) {
        using (StreamReader sr = new StreamReader(fs, Encoding.Default)) {
          while (!sr.EndOfStream) {
            string line = sr.ReadLine();
            if (line == null) {
              break;
            }
            allLines.Add(line);
          };
        }
      }

      bool fileIsNew = (allLines.Count() == 0);
      if (fileIsNew) {
        allLines.Add(Conventions.startMarker);
        allLines.Add("This files contains a version history including all changes relevant for semantic versioning...");
        allLines.Add("*(it is automatically maintained using the ['KornSW-VersioningUtil'](https://github.com/KornSW/VersioningUtil))*");
        allLines.Add("");
        allLines.Add("");
        allLines.Add(Conventions.upcommingChangesMarker);
      }

      int upcommingChangesIndex = ListUtil.FindIndex(allLines, Conventions.upcommingChangesMarker);

      System.Diagnostics.Process p = new System.Diagnostics.Process();

      p.StartInfo.FileName = "git";
      p.StartInfo.UseShellExecute = false;
      //file:///C:/Program%20Files/Git/mingw64/share/doc/git-doc/git-log.html
      p.StartInfo.Arguments = "log --no-decorate --pretty=format:[%ai]%s";
      p.StartInfo.CreateNoWindow = true;
      p.StartInfo.RedirectStandardOutput = true;
      p.StartInfo.RedirectStandardError = true;

      p.EnableRaisingEvents = true;
      p.Start();
      var output = p.StandardOutput.ReadToEnd();

      var hsr = new StringReader(output);
      string historyline = hsr.ReadLine();
      while (!string.IsNullOrWhiteSpace(historyline)) {
        if (historyline.StartsWith("[")){
          string datePart = historyline.Substring(1,19); //[2024-02-16 16:28:37 +0100]
          string subject = historyline.Substring(27);
          if(string.IsNullOrWhiteSpace(ignoreBySubstring) || !subject.Contains(ignoreBySubstring)) {
            if(DateTime.Parse(datePart) > lastVersionTime) {
              Console.WriteLine(datePart + ": " + subject);
              allLines.Insert(upcommingChangesIndex + 1, "* " + subject);
            }
          }
 
        }
        historyline = hsr.ReadLine();
      }

      //using (StreamReader s = p.StandardError) {
      //  string error = s.ReadToEnd();
      //  p.WaitForExit(20000);
      //}

      File.WriteAllLines(changeLogFile, allLines.ToArray(), Encoding.Default);

    }

    /// <summary>
    /// Uses a given changeLogFile a leading database to store the current version of an product as
    /// well as a list of upcomming changes, that will lead to a new version.
    /// SAMPLE: CreateNewVersionOnChangelog "myPackage.nuspec" "changelog.md"
    /// NOTE: if you want to patch more than a single 'package.json' or '.nuspec' file,
    /// then you need to use the 'ImportVersion' command afterwards (it supports minimatch-patterns)
    /// </summary>
    /// <param name="targetFile">
    /// a single file name, which can be 
    /// a 'package.json' file in NPM format (must exist and will be patched) OR
    /// a '.nuspec' file in NuGetFormat (must exist and will be patched) OR
    /// any '.json' filename (will be overwritten!) using a simple json structure defined by this tool
    /// </param>
    /// <param name="changeLogFile">
    /// A 'changelog.md' file, that has a special structure (the initial one will be created automatically!)
    /// </param>
    /// <param name="preReleaseSemantic">
    /// if specified, the versioning logic will create a so called 'prerelease' using the given string a semantical label
    /// </param>
    /// <param name="ignoreSemantic">
    /// can be used to define a blacklist of semantical names, that should NOT be treated as pre-release.
    /// This is very helpfull, if youre using this tool inside of a build-pipeline and passing the branch-name to the
    /// preReleaseSemantic parameter. Then youll be able to declare that branch names via ignoreSemantic parameter,
    /// which are representing regular releases!
    /// </param>
    public void CreateNewVersionOnChangelog (
      string targetFile = "versioninfo.json",
      string changeLogFile = "changelog.md",
      string preReleaseSemantic = "",
      string ignoreSemantic = "master;main;rel-*"
    ) {

      if (!Path.IsPathRooted(changeLogFile)) {
        changeLogFile = Path.Combine(Environment.CurrentDirectory, changeLogFile);
      }
      changeLogFile = Path.GetFullPath(changeLogFile);

      var allLines = new List<string>();

      using (FileStream fs = new FileStream(changeLogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite)) {
        using (StreamReader sr = new StreamReader(fs, Encoding.Default)) {
          while (!sr.EndOfStream) {
            string line = sr.ReadLine();
            if (line == null) {
              break;
            }
            allLines.Add(line);
          };
        }
      }

      bool fileIsNew = (allLines.Count() == 0);
      if (fileIsNew) {
        allLines.Add(Conventions.startMarker);
        allLines.Add("This files contains a version history including all changes relevant for semantic versioning...");
        allLines.Add("*(it is automatically maintained using the ['KornSW-VersioningUtil'](https://github.com/KornSW/VersioningUtil))*");
        allLines.Add("");
        allLines.Add("");
      }

      if(ignoreSemantic.Split(';').Where((s)=> this.MatchesWildcardMask(preReleaseSemantic,s)).Any()) {
        preReleaseSemantic = "";
      }

      var info = MarkDownProcessor.ProcessMarkdownAndCreateNewVersion(allLines, preReleaseSemantic);

      Console.WriteLine(info.currentVersionWithSuffix);
      Console.WriteLine("---");
      Console.WriteLine(info.versionNotes);
      Console.WriteLine("---");

      File.WriteAllLines(changeLogFile, allLines.ToArray(), Encoding.Default);

      if (string.IsNullOrWhiteSpace(targetFile)) {
        return;
      }

      if (targetFile.Contains("*")) {
        this.SetVersion(info.currentVersionWithSuffix, targetFile);
        return;
      }

      if (!Path.IsPathRooted(targetFile)) {
        targetFile = Path.Combine(Environment.CurrentDirectory, targetFile);
      }
      targetFile = Path.GetFullPath(targetFile);
      string nam = Path.GetFileName(targetFile).ToLower();
      string ext = Path.GetExtension(targetFile).ToLower();
      if (
        ext == ".nuspec" || ext == ".vbproj" || ext == ".csproj" || nam == "package.json" || nam == "assemblyinfo.vb" || nam == "assemblyinfo.cs"
      ) {
        this.SetVersion(info.currentVersionWithSuffix, targetFile);
      }
      else {
        using (FileStream fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write)) {
          using (StreamWriter sw = new StreamWriter(fs, Encoding.Default)) {
            sw.WriteLine("{");
            sw.WriteLine($"  \"currentVersion\": \"{info.currentVersion}\",");
            sw.WriteLine($"  \"currentVersionWithSuffix\": \"{info.currentVersionWithSuffix}\",");
            sw.WriteLine($"  \"releaseType\": \"{info.preReleaseSuffix}\",");
            sw.WriteLine($"  \"previousVersion\": \"{info.previousVersion}\",");
            sw.WriteLine($"  \"changeGrade\": \"{info.changeGrade}\",");
            sw.WriteLine($"  \"currentMajor\": {info.currentMajor},");
            sw.WriteLine($"  \"currentMinor\": {info.currentMinor},");
            sw.WriteLine($"  \"currentPatch\": {info.currentFix},");
            sw.WriteLine($"  \"versionDateInfo\": \"{info.versionDateInfo}\",");
            sw.WriteLine($"  \"versionTimeInfo\": \"{info.versionTimeInfo}\",");
            sw.WriteLine($"  \"versionNotes\": \"{info.versionNotes.Replace(Environment.NewLine, "\\n").Replace("\"", "\\\"")}\"");
            sw.WriteLine("}");
            sw.Flush();
          }
        }
      }
    }

    /// <summary>
    /// SAMPLE: ReplaceVersionPlaceholders "**\\*.html;**\\*.md" "mylib.nuspec"
    /// </summary>
    /// <param name="metaDataSourceFile">
    /// a single file name, which can be 
    /// a 'package.json' file in NPM format OR
    /// a '.nuspec' file in NuGetFormat OR
    /// a metaDataDumpFile (written by the 'PushVersionOnChangelog' method of this tool)
    /// </param>
    /// <param name="targetFilesToProcess">
    /// multiple minimatch-patterns (separated by ;) to address one or more
    /// plaintext based files containing version-placeholders like:
    /// {currentVersion}, {currentVersionWithSuffix}, ...
    /// (take a look at the metaDataDumpFile to see all keys!)
    /// </param>
    public void ReplaceVersionPlaceholders(
      string targetFilesToProcess,
      string metaDataSourceFile = "versioninfo.json"
    ) {
      IVersionContainer src = InitializeVersionContainerByFileType(metaDataSourceFile);
      if (src == null) {
        Console.WriteLine("Invalid metaDataSourceFile '" + metaDataSourceFile + "'");
        return;
      }
      VersionInfo vers = src.ReadVersion();
      var res = new FilePlaceholderResolver(vers);
      string[] allFileFullNames = this.ListFiles(targetFilesToProcess);
      foreach (var fileFullName in allFileFullNames) {
        Console.WriteLine($"Processing '{fileFullName}'...");
        try {
          res.ResolvePlaceholders(fileFullName);
        }
        catch (Exception ex) {
          Console.WriteLine(ex.Message);
        }
      }
    }

    /// <summary>
    /// SAMPLE: IncreaseVersion 1 "**\\*.nuspec;**\\package.json;!**\\node_modules\\*"
    /// </summary>
    /// <param name="majority">defines, which digit of the semantic version should be increased (1=MAJOR,2=MINOR,3=FIX)</param>
    /// <param name="targetFilesToProcess">
    /// multiple minimatch-patterns (separated by ;) to address one or more
    /// 'package.json' files in NPM format OR
    /// '.nuspec' files in NuGetFormat OR
    /// '.vbproj'/'.csproj' files in .net CORE project format OR
    /// 'AssemblyInfo.vb'/'AssemblyInfo.cs' files containing Assembly Attributes
    /// </param>
    public void IncreaseVersion(
      int majority,
      string targetFilesToProcess
    ) {

      string[] allFileFullNames = this.ListFiles(targetFilesToProcess);
      foreach (var fileFullName in allFileFullNames) {
        Console.WriteLine($"Processing '{fileFullName}'...");
        try {
          IVersionContainer tgt = InitializeVersionContainerByFileType(fileFullName);
          VersionInfo vers = tgt.ReadVersion();

          if(majority == 1) {
            vers.IncreaseCurrentMajor(true, true);
          }
          else if (majority == 2) {
            vers.IncreaseCurrentMinor(true);
          }
          else {
            vers.IncreaseCurrentFix(true);
          }

          tgt.WriteVersion(vers);
        }
        catch (Exception ex) {
          Console.WriteLine(ex.Message);
        }
      }

    }

    /// <summary>
    ///   SAMPLE: SetVersion "1.2.3-alpha" "**\*.csproj;**\*.vbproj;**\MyProject\AssemblyInfo.cs;**\MyProject\AssemblyInfo.vb;"
    /// </summary>
    /// <param name="semanticVersion">
    ///   can also contain a suffix like '-prerelease'
    /// </param>
    /// <param name="targetFilesToProcess">
    ///   multiple minimatch-patterns (separated by ;) to address one or more
    ///   'package.json' files in NPM format OR
    ///   '.nuspec' files in NuGetFormat OR
    ///   '.vbproj'/'.csproj' files in .net CORE project format OR
    ///   'AssemblyInfo.vb'/'AssemblyInfo.cs' files containing Assembly Attributes
    /// </param>
    public void SetVersion(
      string semanticVersion,
      string targetFilesToProcess
    ) {

      VersionInfo vers = new VersionInfo();
      vers.currentVersionWithSuffix = semanticVersion;
      vers.CurrentVersionWithSuffix2CurrentVersionAndPrereleaseSuffx(true);

      string[] allFileFullNames = this.ListFiles(targetFilesToProcess);
      foreach (var fileFullName in allFileFullNames) {
        Console.WriteLine($"Processing '{fileFullName}'...");
        try {
          IVersionContainer tgt = InitializeVersionContainerByFileType(fileFullName);
          tgt.WriteVersion(vers);
        }
        catch (Exception ex) {
          Console.WriteLine(ex.Message);
        }
      }

    }

    /// <summary>
    ///   SAMPLE: ImportVersion "**\*.csproj;**\*.vbproj;**\MyProject\AssemblyInfo.cs;**\MyProject\AssemblyInfo.vb;" "myLib.nuspec" 
    /// </summary>
    /// <param name="metaDataSourceFile">
    ///   a single file name, which can be 
    ///   a 'package.json' file in NPM format OR
    ///   a '.nuspec' file in NuGetFormat OR
    ///   a metaDataDumpFile (written by the 'PushVersionOnChangelog' method of this tool)
    /// </param>
    /// <param name="targetFilesToProcess">
    ///   multiple minimatch-patterns (separated by ;) to address one or more
    ///   'package.json' files in NPM format OR
    ///   '.nuspec' files in NuGetFormat OR
    ///   '.vbproj'/'.csproj' files in .net CORE project format OR
    ///   'AssemblyInfo.vb'/'AssemblyInfo.cs' files containing Assembly Attributes
    /// </param>
    public void ImportVersion(
      string targetFilesToProcess,
      string metaDataSourceFile = "versioninfo.json"
    ) {
      IVersionContainer src = InitializeVersionContainerByFileType(metaDataSourceFile);
      if (src == null) {
        Console.WriteLine("Invalid metaDataSourceFile '" + metaDataSourceFile + "'");
        return;
      }
      VersionInfo vers = src.ReadVersion();
      string[] allFileFullNames = this.ListFiles(targetFilesToProcess);
      foreach (var fileFullName in allFileFullNames) {
        Console.WriteLine($"Processing '{fileFullName}'...");
        try {
          IVersionContainer tgt = InitializeVersionContainerByFileType(fileFullName);
          tgt.WriteVersion(vers);
        }
        catch (Exception ex) {
          Console.WriteLine(ex.Message);
        }
      }
    }

    /// <summary>
    ///   reads a file and prints information about its dependencies
    ///   SAMPLE: ListDependencies "myLib.nuspec"
    /// </summary>
    /// <param name="fileToAnalyze"></param>
    public void ListDependencies(
      string fileToAnalyze
    ) {
      try {
        IVersionContainer tgt = InitializeVersionContainerByFileType(fileToAnalyze);
        var deps = tgt.ReadPackageDependencies();
        int len = 30;
        foreach (var dep in deps) {
          if(len < dep.TargetPackageId.Length) {
            len = dep.TargetPackageId.Length;
          }     
        }
        len = len + 2;
        Console.WriteLine("---------------- DEPENDENCIES -----------------");
        Console.WriteLine();
        foreach (var dep in deps) {
          Console.Write("  ");
          Console.Write(dep.TargetPackageId);
          Console.Write(new string(' ', len - dep.TargetPackageId.Length));
          Console.WriteLine(dep.TargetPackageVersionConstraint);
        }
        Console.WriteLine();
        Console.WriteLine("-----------------------------------------------");
      }
      catch (Exception ex) {
        Console.WriteLine(ex.Message);
      }
    }

    /// <summary>
    ///   SAMPLE: CopyVersionToDependencyEntry "MyLib" "**\*.nuspec;**\package.json;!**\node_modules\**" "myLib.nuspec"
    /// </summary>
    /// <param name="refPackageId">
    /// </param>
    /// <param name="metaDataSourceFile">
    ///   a single file name, which can be 
    ///   a 'package.json' file in NPM format OR
    ///   a '.nuspec' file in NuGetFormat OR
    ///   a metaDataDumpFile (written by the 'PushVersionOnChangelog' method of this tool)
    /// </param>
    /// <param name="targetFilesToProcess">
    ///   multiple minimatch-patterns (separated by ;) to address one or more
    ///   'package.json' files in NPM format OR
    ///   '.nuspec' files in NuGetFormat
    ///   WARNING: '.vbproj'/'.csproj' files in .net CORE project format ARE CURRENTLY NOT SUPPORTED!
    /// </param>
    /// <param name="contraintType">
    ///   "SEM-SAFE": require the given version or any newer, as long as the major version is the same;
    ///   "MIN": require the given version or any newer (including new major versions;
    ///   "EXACT": require exactly the given version  
    /// </param> 
    public void CopyVersionToDependencyEntry(
      string refPackageId,
      string targetFilesToProcess,
      string metaDataSourceFile = "versioninfo.json",
      string contraintType = "SEM-SAFE"
    ) {
      IVersionContainer src = InitializeVersionContainerByFileType(metaDataSourceFile);
      if (src == null) {
        Console.WriteLine("Invalid metaDataSourceFile '" + metaDataSourceFile + "'");
        return;
      }
      VersionInfo vers = src.ReadVersion();
      this.SetVersionToDependencyEntry(refPackageId, vers.currentVersionWithSuffix, targetFilesToProcess, contraintType);
    }

    /// <summary>
    /// SAMPLE: SetVersionToDependencyEntry "GreatExternalLib" "1.2.3-alpha" "**\*.nuspec;**\package.json;!**\node_modules\**"
    /// </summary>
    /// <param name="dependencyPackageId">
    /// </param>
    /// <param name="newDependentVersion">
    ///   can also contain a suffix like '-prerelease'
    /// </param>
    /// <param name="targetFilesToProcess">
    /// multiple minimatch-patterns (separated by ;) to address one or more
    /// 'package.json' files in NPM format OR
    /// '.nuspec' files in NuGetFormat
    /// </param>
    /// <param name="contraintType">
    ///  "SEM-SAFE": require the given version or any newer, as long as the major version is the same;
    ///  "MIN": require the given version or any newer (including new major versions;
    ///  "EXACT": require exactly the given version  
    /// </param>
    public void SetVersionToDependencyEntry(
      string dependencyPackageId,
      string newDependentVersion,
      string targetFilesToProcess,
      string contraintType = "SEM-SAFE"
    ) {
      DependencyInfo dep = new DependencyInfo(dependencyPackageId, newDependentVersion);
      if (contraintType == "EXACT") {
        dep.TargetPackageVersionConstraint.SetVersionShouldBeExact(newDependentVersion);
      }
      else if (contraintType == "MIN") {
        dep.TargetPackageVersionConstraint.SetVersionShouldBeGreaterThanOrEqual(newDependentVersion);
      }
      else if (contraintType == "KEEP") {
      }
      else { //SEM-SAFE
        dep.TargetPackageVersionConstraint.SetVersionShouldBeInNonBreakingRange(newDependentVersion);
      }
      this.SetVersionToDependencyEntryInternal(
        targetFilesToProcess, false, true, false, dep
      );
    }

    private void SetVersionToDependencyEntryInternal(
      string targetFilesToProcess, bool addNew, bool updateExisiting, bool deleteOthers,
      params DependencyInfo[] newDependencies
    ) {
      string[] allFileFullNames = this.ListFiles(targetFilesToProcess);
      foreach (var fileFullName in allFileFullNames) {
        Console.WriteLine($"Processing '{fileFullName}'...");
        try {
          IVersionContainer tgt = InitializeVersionContainerByFileType(fileFullName);
          tgt.WritePackageDependencies(newDependencies, addNew, updateExisiting, deleteOthers);
        }
        catch (Exception ex) {
          Console.WriteLine(ex.Message);
        }
      }
    }

    public void CopyDependencyEntries(
      string targetFilesToUpdate,
      string sourceFileToReadDependencies,
      string packageIdWhitelist="*",
      string packageIdBlacklist = "",
      string contraintType = "KEEP",
      bool addNew = true,
      bool updateExisiting = true,
      bool deleteOthers = false
    ) {
      DependencyInfo[] srcDependencies;
      try {
        IVersionContainer tgt = InitializeVersionContainerByFileType(sourceFileToReadDependencies);
        srcDependencies = tgt.ReadPackageDependencies();
      }
      catch (Exception ex) {
        Console.WriteLine(ex.Message);
        return;
      }
      if(packageIdWhitelist != "*" && packageIdWhitelist != "") {
        var split = packageIdWhitelist.Split(',');
        srcDependencies = srcDependencies.Where((d)=> split.Contains(d.TargetPackageId)).ToArray();
      }
      if (packageIdBlacklist != "") {
        var split = packageIdWhitelist.Split(',');
        srcDependencies = srcDependencies.Where((d) => !split.Contains(d.TargetPackageId)).ToArray();
      }

      if (contraintType == "EXACT") {  
        foreach (var dep in srcDependencies) {
          dep.TargetPackageVersionConstraint.SetVersionShouldBeExact(dep.TargetPackageVersionConstraint.ToString(true));
        }
      }
      else if (contraintType == "MIN") {;
        foreach (var dep in srcDependencies) {
          dep.TargetPackageVersionConstraint.SetVersionShouldBeGreaterThanOrEqual(dep.TargetPackageVersionConstraint.ToString(true));
        }
      }
      else if (contraintType == "KEEP") {
      }
      else { //SEM-SAFE
        foreach (var dep in srcDependencies) {
          dep.TargetPackageVersionConstraint.SetVersionShouldBeInNonBreakingRange(dep.TargetPackageVersionConstraint.ToString(true));
        }
      }

      this.SetVersionToDependencyEntryInternal(targetFilesToUpdate, addNew, updateExisiting, deleteOthers, srcDependencies);
    }

    /// <summary>
    /// Just reads the version information out of a given file and prints it to the console
    /// </summary>
    /// <param name="metaDataSourceFile"></param>
    public void ReadVersion(string metaDataSourceFile = "versioninfo.json") {
      try {
        IVersionContainer tgt = InitializeVersionContainerByFileType(metaDataSourceFile);
        VersionInfo vers = tgt.ReadVersion();

        Console.WriteLine();
        Console.WriteLine($"  current version (w suffix): {vers.currentVersionWithSuffix}");
        Console.WriteLine($"  current version:            {vers.currentVersion}");

        if(!string.IsNullOrWhiteSpace (vers.previousVersion) && vers.previousVersion != "0.0.0") {
          Console.WriteLine();
          Console.WriteLine($"  previous version:           {vers.previousVersion}");
          Console.WriteLine($"  change grade:               {vers.changeGrade}");
        }

        if (!string.IsNullOrWhiteSpace(vers.versionDateInfo) && vers.versionDateInfo != "1900-01-01") {
          Console.WriteLine();
          Console.WriteLine($"  date:                       {vers.versionDateInfo}");
          Console.WriteLine($"  time:                       {vers.versionTimeInfo}");
        }

        if (!string.IsNullOrWhiteSpace(vers.versionNotes)) {
          Console.WriteLine();
          Console.WriteLine($"  version notes:");
          Console.WriteLine($"  {vers.versionNotes.Replace(Environment.NewLine, Environment.NewLine + "  ")}");
        }

      }   
      catch (Exception ex) {
        Console.WriteLine(ex.Message);
      }
    }

    /// <summary>
    ///   You can use this method to test you minimatch patterns...
    /// </summary>
    public string[] ListFiles(string minimatchPatterns) {

      //IF CALLED WITH SINGLE FILE
      if (!minimatchPatterns.Contains("*") && !minimatchPatterns.Contains(";") && !minimatchPatterns.Contains("!")) {
        if (!Path.IsPathRooted(minimatchPatterns)) {
          minimatchPatterns = Path.Combine(Environment.CurrentDirectory, minimatchPatterns);
        }
        minimatchPatterns = Path.GetFullPath(minimatchPatterns);
        if (File.Exists(minimatchPatterns)) {
          Console.WriteLine($"Checking existence of '{minimatchPatterns}' ... OK");
          return new string[] { minimatchPatterns };
        }
        else {
          Console.WriteLine($"Checking existence of '{minimatchPatterns}' ... not found !");
          return new string[] { };
        }
      }

      IEnumerable<string> result = new string[] { };
      var opt = new Options();
      opt.NoCase = true;

      var patterns = minimatchPatterns.Split(";");

      foreach (var pattern in patterns.Where((p) => !p.StartsWith("!"))) {
        var cleaned = pattern.Replace("/", "\\");

        string startDir = "";
        string dynamicSubPath = "";

        if (!Path.IsPathRooted(cleaned)) {
          cleaned = Path.Combine(Environment.CurrentDirectory, cleaned);
        }

        int idxFirstStar = cleaned.IndexOf("*");

        if (idxFirstStar < 0) {
          startDir = Path.GetDirectoryName(cleaned);
          dynamicSubPath = Path.GetFileName(cleaned);
        }
        else {
          int sepIdx = cleaned.Substring(0, idxFirstStar).LastIndexOf(Path.DirectorySeparatorChar);
          startDir = cleaned.Substring(0, sepIdx);
          dynamicSubPath = cleaned.Substring(sepIdx + 1);
        }
        //normalize
        startDir = Path.GetFullPath(startDir);
        int startDirLength = startDir.Length + 1;

        Console.WriteLine($"Search on '{startDir}' for '{dynamicSubPath}'");

        DirectoryInfo di = new DirectoryInfo(startDir);
        var fileFullNames = di.GetFiles("*.*", searchOption: SearchOption.AllDirectories).Select(
          (fi) => fi.FullName.Substring(startDirLength).Replace("\\", "/")
        ).ToArray();

        result = result.Union(Minimatcher.Filter(fileFullNames, dynamicSubPath.Replace("\\", "/"), opt).Select(
          (matchFileName) => Path.Combine(startDir, matchFileName.Replace("/", "\\"))
        ));

      }

      result = result.Distinct();

      foreach (var pattern in patterns.Where((p) => p.StartsWith("!"))) {
        var cleaned = pattern.Replace("/", "\\").Substring(1);//remove the "!"

        Console.WriteLine($"Removing matches for '{cleaned}'");

        if (!Path.IsPathRooted(cleaned)) {
          cleaned = Path.Combine(Environment.CurrentDirectory, cleaned);
        }
        int idxFirstStar = cleaned.IndexOf("*");
        if (idxFirstStar < 0) {
          //normalize
          cleaned = Path.GetFullPath(cleaned);
        }
        else {
          string startDir = "";
          string dynamicSubPath = "";
          int sepIdx = cleaned.Substring(0, idxFirstStar).LastIndexOf(Path.DirectorySeparatorChar);
          startDir = cleaned.Substring(0, sepIdx);
          dynamicSubPath = cleaned.Substring(sepIdx + 1);
          //normalize
          startDir = Path.GetFullPath(startDir);
          cleaned = Path.Combine(startDir, dynamicSubPath);
        }

        string pat = "!" + cleaned.Replace("\\", "/");
        result = result.Where( //             (our results are fully rooted VVVVV remove workdir to match in the seldom case, taht the pattern donst starts with **\)
          (f) => Minimatcher.Check(f.Replace(Environment.CurrentDirectory + "\\", "").Replace("\\", "/"), pat, opt)
        );
      }

      return result.ToArray();
    }

    #region " INTERNAL HELPERS "

    /// <summary>
    ///   ma1 more than ma2:  1
    ///   ma1 less then ma2: -1
    /// </summary>
    private static int VersionCompare(int ma1, int mi1, int p1, int ma2, int mi2, int p2) {
      if (ma1 > ma2) {
        return 1;
      }
      else if (ma1 < ma2) {
        return -1;
      }
      if (mi1 > mi2) {
        return 2;
      }
      else if (mi1 < mi2) {
        return -2;
      }
      if (p1 > p2) {
        return 3;
      }
      else if (p1 < p2) {
        return -3;
      }
      return 0;
    }

   

    internal bool MatchesWildcardMask(string stringToEvaluate, string pattern, bool ignoreCasing = true) {
      var indexOfDoubleDot = pattern.IndexOf("..", StringComparison.Ordinal);
      if ((indexOfDoubleDot >= 0)) {
        for (var i = indexOfDoubleDot; i <= pattern.Length - 1; i++) {
          if ((pattern[i] != '.'))
            return false;
        }
      }

      string normalizedPatternString = Regex.Replace(pattern, @"\.+$", "");
      bool endsWithDot = (normalizedPatternString.Length != pattern.Length);
      int endCharCount = 0;

      if ((endsWithDot)) {
        var lastNonWildcardPosition = normalizedPatternString.Length - 1;

        while (lastNonWildcardPosition >= 0) {
          var currentChar = normalizedPatternString[lastNonWildcardPosition];
          if ((currentChar == '*'))
            endCharCount += short.MaxValue;
          else if ((currentChar == '?'))
            endCharCount += 1;
          else
            break;
          lastNonWildcardPosition -= 1;
        }

        if ((endCharCount > 0))
          normalizedPatternString = normalizedPatternString.Substring(0, lastNonWildcardPosition + 1);
      }

      bool endsWithWildcardDot = endCharCount > 0;
      bool endsWithDotWildcardDot = (endsWithWildcardDot && normalizedPatternString.EndsWith("."));

      if ((endsWithDotWildcardDot))
        normalizedPatternString = normalizedPatternString.Substring(0, normalizedPatternString.Length - 1);

      normalizedPatternString = Regex.Replace(normalizedPatternString, @"(?!^)(\.\*)+$", ".*");

      var escapedPatternString = Regex.Escape(normalizedPatternString);
      string prefix;
      string suffix;

      if ((endsWithDotWildcardDot)) {
        prefix = "^" + escapedPatternString;
        suffix = @"(\.[^.]{0," + endCharCount + "})?$";
      }
      else if ((endsWithWildcardDot)) {
        prefix = "^" + escapedPatternString;
        suffix = "[^.]{0," + endCharCount + "}$";
      }
      else {
        prefix = "^" + escapedPatternString;
        suffix = "$";
      }

      if ((prefix.EndsWith(@"\.\*") && prefix.Length > 5)) {
        prefix = prefix.Substring(0, prefix.Length - 4);
        suffix = Convert.ToString(@"(\..*)?") + suffix;
      }

      var expressionString = prefix.Replace(@"\*", ".*").Replace(@"\?", "[^.]?") + suffix;

      if ((ignoreCasing))
        return Regex.IsMatch(stringToEvaluate, expressionString, RegexOptions.IgnoreCase);
      else
        return Regex.IsMatch(stringToEvaluate, expressionString);
    }

    #endregion

    #region " MD Processing "

    

    private IVersionContainer InitializeVersionContainerByFileType(string fileFullName) {

      if (fileFullName.Contains("*")) {
        var matches = this.ListFiles(fileFullName);
        if (matches.Length == 0) {
          throw new FileNotFoundException($"There is no file matching the pattern '{fileFullName}'");
        }
        else if (matches.Length > 1) {
          throw new Exception($"There is more than one file matching the pattern '{fileFullName}'. Please concretize the pattern to choose between '{string.Join("' / '", matches)}')");
        }
        else {
          fileFullName = matches[0];
        }
      }
      else {
        fileFullName = Path.GetFullPath(fileFullName);
      }

      string fName = Path.GetFileName(fileFullName);

      if (fName.Equals("package.json", StringComparison.InvariantCultureIgnoreCase)) {
        return new NpmPackageJsonFileAccessor(fileFullName);
      }
      if (fName.Equals("packages.config", StringComparison.InvariantCultureIgnoreCase)) {
        return new PackagesConfigFileAccessor(fileFullName);
      }
      else if (fName.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase)) {
        return new VersionInfoJsonFileAccessor(fileFullName);
      }
      else if (fName.EndsWith(".vbproj", StringComparison.InvariantCultureIgnoreCase) || fName.EndsWith(".csproj", StringComparison.InvariantCultureIgnoreCase)) {
        return new VsProjFileAccessor(fileFullName);
      }
      else if (fName.EndsWith(".vb", StringComparison.InvariantCultureIgnoreCase) || fName.EndsWith(".cs", StringComparison.InvariantCultureIgnoreCase)) {
        return new AssemblyInfoFileAccessor(fileFullName);
      }
      else if (fName.EndsWith(".nuspec", StringComparison.InvariantCultureIgnoreCase)) {
        return new NuspecFileAccessor(fileFullName);
      }
      else if (fName.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase) || fName.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase)) {
        return new CompiledAssembyFileAccessor(fileFullName);
      }

      throw new NotImplementedException("Unsupported File: " + fName);
      //return null;
    }

    #endregion

  }

}
