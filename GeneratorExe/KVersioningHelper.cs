using FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Utils;

namespace Versioning {

  public partial class KVersioningHelper {

    public void ImportGitCommentsIntoChangelog(
      string versioninfoFile = "versioninfo.json",
      string changeLogFile = "changelog.md",
      string ignoreBySubstring = "VERSIONING"
    ) {

      string lastVersionTag = "";
      DateTime lastVersionTime = DateTime.MinValue;
      this.TryLoadVersionAndDateFromVersioninfoFile(versioninfoFile, out lastVersionTag, out lastVersionTime);

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

      var allLinesOfChanglogFile = new List<string>();

      using (FileStream fs = new FileStream(changeLogFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite)) {
        using (StreamReader sr = new StreamReader(fs, Encoding.Default)) {
          while (!sr.EndOfStream) {
            string line = sr.ReadLine();
            if (line == null) {
              break;
            }
            allLinesOfChanglogFile.Add(line);
          };
        }
      }

      bool fileIsNew = (allLinesOfChanglogFile.Count() == 0);
      if (fileIsNew) {
        allLinesOfChanglogFile.Add(startMarker);
        allLinesOfChanglogFile.Add("This files contains a version history including all changes relevant for semantic versioning...");
        allLinesOfChanglogFile.Add("*(it is automatically maintained using the ['KornSW-VersioningUtil'](https://github.com/KornSW/VersioningUtil))*");
        allLinesOfChanglogFile.Add("");
        allLinesOfChanglogFile.Add("");
        allLinesOfChanglogFile.Add(upcommingChangesMarker);
      }

      int upcommingChangesIndex = FindIndex(allLinesOfChanglogFile, upcommingChangesMarker);

      if (string.IsNullOrWhiteSpace(lastVersionTag)) {
        int idx = FindIndex(allLinesOfChanglogFile, releasedVersionMarker, upcommingChangesIndex);
        if(idx > 0) {
          string latestReleasedVersionLine = allLinesOfChanglogFile[idx];
          lastVersionTag = latestReleasedVersionLine.Substring(releasedVersionMarker.Length).Trim();
        }
      }

      bool success = this.GetGitHistorySinceTag(
        (commitDate, commitMessage) => {
          allLinesOfChanglogFile.Insert(upcommingChangesIndex + 1, "* " + commitMessage);
        },
        //lastVersionTag,
        lastVersionTime,
        ignoreBySubstring
      );

      File.WriteAllLines(changeLogFile, allLinesOfChanglogFile.ToArray(), Encoding.Default);

    }

    public void GetGitHistorySinceTag(string startAfterTag = "", DateTime startAfterDate = default, string ignoreBySubstring = "VERSIONING") {
      this.GetGitHistorySinceTag(
        (d,m) => { },
        //startAfterTag,
        startAfterDate,
        ignoreBySubstring
      );
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
        allLines.Add(startMarker);
        allLines.Add("This files contains a version history including all changes relevant for semantic versioning...");
        allLines.Add("*(it is automatically maintained using the ['KornSW-VersioningUtil'](https://github.com/KornSW/VersioningUtil))*");
        allLines.Add("");
        allLines.Add("");
      }

      if(ignoreSemantic.Split(';').Where((s)=> this.MatchesWildcardMask(preReleaseSemantic,s)).Any()) {
        preReleaseSemantic = "";
      }

      VersionInfo info = KVersioningHelper.ProcessMarkdownAndCreateNewVersion(
        allLines,
        preReleaseSemantic,
        (string lastVersionStringFromMdHeadline) => { //onNoChangesFound:
          //...fallback to git-history
          List<string> gitHistoryLines = new List<string>();
          Console.WriteLine("Found no new entries within Changelog - doing fallback via git-history [BETA]...");
          this.GetGitHistorySinceTag(
            (d,msg)=> gitHistoryLines.Add(msg),
            //lastVersionStringFromMdHeadline,
            default
          );
          string[] changesFromGit = gitHistoryLines.ToArray();
          foreach (string change in changesFromGit) {
            Console.WriteLine(" detected git commit: " + change);
          }
          return changesFromGit;
        }     
      );

      Console.WriteLine(info.currentVersionWithSuffix);
      Console.WriteLine("---");
      Console.WriteLine(info.versionNotes);
      Console.WriteLine("---");

      File.WriteAllLines(changeLogFile, allLines.ToArray(), Encoding.Default);

      //sicher ist sicher...
      info.versionNotes = info.versionNotes.Replace("\"","'").Replace("\\", "/");

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
      //if (
      //  ext == ".nuspec" || ext == ".vbproj" || ext == ".csproj" || nam == "package.json" || nam == "assemblyinfo.vb" || nam == "assemblyinfo.cs"
      //) {
        this.SetVersion(info.currentVersionWithSuffix, targetFile);
      //}
      //else {
      //  using (FileStream fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write)) {
      //    using (StreamWriter sw = new StreamWriter(fs, Encoding.Default)) {
      //      sw.WriteLine("{");
      //      sw.WriteLine($"  \"currentVersion\": \"{info.currentVersion}\",");
      //      sw.WriteLine($"  \"currentVersionWithSuffix\": \"{info.currentVersionWithSuffix}\",");
      //      sw.WriteLine($"  \"releaseType\": \"{info.releaseType}\",");
      //      sw.WriteLine($"  \"previousVersion\": \"{info.previousVersion}\",");
      //      sw.WriteLine($"  \"changeGrade\": \"{info.changeGrade}\",");
      //      sw.WriteLine($"  \"currentMajor\": {info.currentMajor},");
      //      sw.WriteLine($"  \"currentMinor\": {info.currentMinor},");
      //      sw.WriteLine($"  \"currentPatch\": {info.currentFix},");
      //      sw.WriteLine($"  \"versionDateInfo\": \"{info.versionDateInfo}\",");
      //      sw.WriteLine($"  \"versionTimeInfo\": \"{info.versionTimeInfo}\",");
      //      sw.WriteLine($"  \"versionNotes\": \"{info.versionNotes.Replace(Environment.NewLine, "\\n").Replace("\"", "\\\"").Replace("\\", "\\\\")}\"");
      //      sw.WriteLine("}");
      //      sw.Flush();
      //    }
      //  }
      //}
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

    public void SetPrerelease(
      string prereleaseSuffix,
      string targetFilesToProcess
    ) {

      prereleaseSuffix = prereleaseSuffix.Replace("\"", "");
      if(prereleaseSuffix.StartsWith("-")) {
        prereleaseSuffix = prereleaseSuffix.Substring(1);
      }
        
      string[] allFileFullNames = this.ListFiles(targetFilesToProcess);
      foreach (var fileFullName in allFileFullNames) {
        Console.WriteLine($"Processing '{fileFullName}'...");
        try {
          IVersionContainer tgt = InitializeVersionContainerByFileType(fileFullName);
          VersionInfo vers = tgt.ReadVersion();
          vers.releaseType = prereleaseSuffix;
          vers.VersionPartFields2CurrentVersion(true);
          tgt.WriteVersion(vers);
        }
        catch (Exception ex) {
          Console.WriteLine(ex.Message);
        }
      }

    }

    /// <summary>
    /// SAMPLE: SetVersion "1.2.3-alpha" "**\*.csproj;**\*.vbproj;**\MyProject\AssemblyInfo.cs;**\MyProject\AssemblyInfo.vb;"
    /// </summary>
    /// <param name="semanticVersion">
    ///   can also contain a suffix like '-prerelease'
    /// </param>
    /// <param name="targetFilesToProcess">
    /// multiple minimatch-patterns (separated by ;) to address one or more
    /// 'package.json' files in NPM format OR
    /// '.nuspec' files in NuGetFormat OR
    /// '.vbproj'/'.csproj' files in .net CORE project format OR
    /// 'AssemblyInfo.vb'/'AssemblyInfo.cs' files containing Assembly Attributes
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
    /// SAMPLE: ImportVersion "**\*.csproj;**\*.vbproj;**\MyProject\AssemblyInfo.cs;**\MyProject\AssemblyInfo.vb;" "myLib.nuspec" 
    /// </summary>
    /// <param name="metaDataSourceFile">
    /// a single file name, which can be 
    /// a 'package.json' file in NPM format OR
    /// a '.nuspec' file in NuGetFormat OR
    /// a metaDataDumpFile (written by the 'PushVersionOnChangelog' method of this tool)
    /// </param>
    /// <param name="targetFilesToProcess">
    /// multiple minimatch-patterns (separated by ;) to address one or more
    /// 'package.json' files in NPM format OR
    /// '.nuspec' files in NuGetFormat OR
    /// '.vbproj'/'.csproj' files in .net CORE project format OR
    /// 'AssemblyInfo.vb'/'AssemblyInfo.cs' files containing Assembly Attributes
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
    /// reads a file and prints information about its dependencies
    /// SAMPLE: ListDependencies "myLib.nuspec"
    /// </summary>
    /// <param name="fileToAnalyze"></param>
    public void ListDependencies(
      string fileToAnalyze
    ) {
      try {
        IVersionContainer tgt = InitializeVersionContainerByFileType(fileToAnalyze);
        var deps = tgt.ReadPackageDependencies(true).OrderBy(d => d.DedicatedToTargetFramework).ToArray();
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
          Console.Write(dep.TargetPackageVersionConstraint);
          if (string.IsNullOrWhiteSpace(dep.DedicatedToTargetFramework)){
            Console.WriteLine();
          }
          else {
            Console.WriteLine($" [{dep.DedicatedToTargetFramework}]");
          }
           
        }
        Console.WriteLine();
        Console.WriteLine("-----------------------------------------------");
      }
      catch (Exception ex) {
        Console.WriteLine(ex.Message);
      }
    }

    /// <summary>
    /// SAMPLE: CopyVersionToDependencyEntry "MyLib" "**\*.nuspec;**\package.json;!**\node_modules\**" "myLib.nuspec"
    /// </summary>
    /// <param name="refPackageId">
    /// </param>
    /// <param name="metaDataSourceFile">
    /// a single file name, which can be 
    /// a 'package.json' file in NPM format OR
    /// a '.nuspec' file in NuGetFormat OR
    /// a metaDataDumpFile (written by the 'PushVersionOnChangelog' method of this tool)
    /// </param>
    /// <param name="targetFilesToProcess">
    /// multiple minimatch-patterns (separated by ;) to address one or more
    /// 'package.json' files in NPM format OR
    /// '.nuspec' files in NuGetFormat
    /// WARNING: '.vbproj'/'.csproj' files in .net CORE project format ARE CURRENTLY NOT SUPPORTED!
    /// </param>
    /// <param name="contraintType">
    ///  "SEM-SAFE": require the given version or any newer, as long as the major version is the same;
    ///  "MIN": require the given version or any newer (including new major versions;
    ///  "EXACT": require exactly the given version  
    /// </param> 
    /// <param name="onlyForTargetFramework">
    ///  if set, then any dependency entries under the given targetFramework-group will be updated (e.g. 'net10.0' or '.NETFramework4.8') 
    /// </param>
    public void CopyVersionToDependencyEntry(
      string refPackageId,
      string targetFilesToProcess,
      string metaDataSourceFile = "versioninfo.json",
      string contraintType = "SEM-SAFE",
      bool allowDowngrade = true,
      string onlyForTargetFramework = null
    ) {
      IVersionContainer src = InitializeVersionContainerByFileType(metaDataSourceFile);
      if (src == null) {
        Console.WriteLine("Invalid metaDataSourceFile '" + metaDataSourceFile + "'");
        return;
      }
      VersionInfo vers = src.ReadVersion();
      this.SetVersionToDependencyEntry(refPackageId, vers.currentVersionWithSuffix, targetFilesToProcess, contraintType, allowDowngrade, onlyForTargetFramework);
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
    /// <param name="onlyForTargetFramework">
    ///  if set, then any dependency entries under the given targetFramework-group will be updated (e.g. 'net10.0' or '.NETFramework4.8') 
    /// </param>
    public void SetVersionToDependencyEntry(
      string dependencyPackageId,
      string newDependentVersion,
      string targetFilesToProcess,
      string contraintType = "SEM-SAFE",
      bool allowDowngrade = true,
      string onlyForTargetFramework = null
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
        targetFilesToProcess, false, true, false, onlyForTargetFramework, allowDowngrade, dep
      );
    }

    private void SetVersionToDependencyEntryInternal(
      string targetFilesToProcess, bool addNew, bool updateExisiting, bool deleteOthers,
      string onlyForTargetFramework,bool allowDowngrade, params DependencyInfo[] newDependencies
    ) {
      string[] allFileFullNames = this.ListFiles(targetFilesToProcess);
      foreach (string fileFullName in allFileFullNames) {
        Console.WriteLine($"Processing '{fileFullName}'...");
        try {
          IVersionContainer tgt = InitializeVersionContainerByFileType(fileFullName);
          tgt.WritePackageDependencies(
            newDependencies, 
            addNew, updateExisiting, deleteOthers, allowDowngrade,
            onlyForTargetFramework, new string[] { "*" }, new string[] { }
          );
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
      string constraintType = "KEEP",
      bool addNew = true,
      bool updateExisiting = true,
      bool deleteOthers = false,
      bool allowDowngrade = true,
      string onlyForTargetFramework = null
    ) {
      DependencyInfo[] srcDependencies;
      try {
        IVersionContainer tgt = InitializeVersionContainerByFileType(sourceFileToReadDependencies);
        srcDependencies = tgt.ReadPackageDependencies(true);

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

      if (constraintType == "EXACT") {  
        foreach (var dep in srcDependencies) {
          dep.TargetPackageVersionConstraint.SetVersionShouldBeExact(dep.TargetPackageVersionConstraint.ToString(true));
        }
      }
      else if (constraintType == "MIN") {;
        foreach (var dep in srcDependencies) {
          dep.TargetPackageVersionConstraint.SetVersionShouldBeGreaterThanOrEqual(dep.TargetPackageVersionConstraint.ToString(true));
        }
      }
      else if (constraintType == "KEEP") {
      }
      else { //SEM-SAFE
        foreach (var dep in srcDependencies) {
          dep.TargetPackageVersionConstraint.SetVersionShouldBeInNonBreakingRange(dep.TargetPackageVersionConstraint.ToString(true));
        }
      }

      Console.WriteLine("Will use the following set of dependencies to be ensured:");
      foreach (DependencyInfo dep in srcDependencies) {
        Console.WriteLine("   " + dep.ToString());
      }
      Console.WriteLine();

      this.SetVersionToDependencyEntryInternal(
        targetFilesToUpdate, addNew, updateExisiting, deleteOthers, onlyForTargetFramework, allowDowngrade, srcDependencies
      );
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
    /// Just reads the version information out of a given file and prints it to the console
    /// </summary>
    /// <param name="metaDataSourceFile"></param>
    public void InjectIntoAzureDevOpsBuildNumber(string metaDataSourceFile = "versioninfo.json") {
      try {
        IVersionContainer tgt = InitializeVersionContainerByFileType(metaDataSourceFile);
        VersionInfo vers = tgt.ReadVersion();

        Console.WriteLine($"Setting AzureDevOps-BUILD-NUMBER to '{vers.currentVersionWithSuffix}'...");
        Console.WriteLine($"##vso[build.updatebuildnumber]{vers.currentVersionWithSuffix}");

      }
      catch (Exception ex) {
        Console.WriteLine(ex.Message);
      }
    }

    private void SetAzureDevOpsVariable(string name, object value) {
      Console.WriteLine($"Setting AzureDevOps-Variable $({name}) to '{value}'...");
      Console.WriteLine($"##vso[task.setvariable variable={name}]{value}");
    }

    /// <summary>
    /// Just reads the version information out of a given file and prints it to the console
    /// </summary>
    /// <param name="metaDataSourceFile"></param>
    public void InjectIntoAzureDevOpsVariables(string metaDataSourceFile = "versioninfo.json") {
      try {
        IVersionContainer tgt = InitializeVersionContainerByFileType(metaDataSourceFile);
        VersionInfo vers = tgt.ReadVersion();

        SetAzureDevOpsVariable("PreviousVersion", vers.previousVersion);
        SetAzureDevOpsVariable("ChangeGrade", vers.changeGrade);
        SetAzureDevOpsVariable("CurrentVersion", vers.currentVersion);
        SetAzureDevOpsVariable("PreReleaseSuffix", vers.releaseType);
        SetAzureDevOpsVariable("ReleaseType", vers.releaseType);
        SetAzureDevOpsVariable("CurrentVersionWithSuffix", vers.currentVersionWithSuffix);
        SetAzureDevOpsVariable("CurrentMajor", vers.currentMajor);
        SetAzureDevOpsVariable("CurrentMinor", vers.currentMinor);
        SetAzureDevOpsVariable("CurrentFix", vers.currentFix);
        SetAzureDevOpsVariable("VersionDateInfo", vers.versionDateInfo);
        SetAzureDevOpsVariable("VersionTimeInfo", vers.versionTimeInfo);
        SetAzureDevOpsVariable("VersionNotes", vers.versionNotes);
      }
      catch (Exception ex) {
        Console.WriteLine(ex.Message);
      }
    }

    /// <summary>
    /// You can use this method to test you minimatch patterns...
    /// </summary>
    /// <param name="minimatchPatterns"></param>
    /// <param name="expandSolutionFiles"></param>
    /// <returns></returns>
    public string[] ListFiles(string minimatchPatterns, bool expandSolutionFiles = true) {
      string[] result = this.ListFilesInternal(minimatchPatterns);

      if (!expandSolutionFiles) {
        return result;
      }

      List<string> expanded = new List<string>();
      foreach (string entry in result) {

        if (!entry.EndsWith(".sln", StringComparison.InvariantCulture)) {
          if (!expanded.Contains(entry)) {
            expanded.Add(entry);
          }
        }
        else {
          Console.WriteLine($"Expanding Solution-File '{entry}'...");
          string[] projFileFullNames = SlnFileHelper.GetProjectFileFullNames(entry);
          foreach (string projFileFullName in projFileFullNames) {
            Console.Write($"   found '{Path.GetFileName(projFileFullName)}'...  ");
            if (File.Exists(projFileFullName)) {
              Console.WriteLine($"     (exising)"); 
              if (!expanded.Contains(projFileFullName)) {
                expanded.Add(projFileFullName);
              }
            }
            else {
              Console.WriteLine($"     (NOT EXISITING !!!)");
            }
          }
        }

      }

      return expanded.ToArray();
    }

    private string[] ListFilesInternal(string minimatchPatterns) {

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
        else if(Path.GetFileName(minimatchPatterns).Equals("versioninfo.json", StringComparison.CurrentCultureIgnoreCase)) {
          //special case: a versioninfo.json can be written from scratch, if not exisiting!
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
    /// ma1 more than ma2:  1
    /// ma1 less then ma2: -1
    /// </summary>
    /// <param name="ma1"></param>
    /// <param name="mi1"></param>
    /// <param name="p1"></param>
    /// <param name="ma2"></param>
    /// <param name="mi2"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
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

    private static int FindIndex(List<string> list, string searchString, int startAt = 0) {
      for (int i = startAt; i < list.Count(); i++) {
        if (list[i].Contains(searchString, StringComparison.InvariantCultureIgnoreCase)) {
          return i;
        }
      }
      return -1;
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

    private bool TryLoadVersionAndDateFromVersioninfoFile(
      string versioninfoFile,
      out string currentVersionWithSuffix,
      out DateTime currentVersionDateTime
    ) {

      IVersionContainer src = InitializeVersionContainerByFileType(versioninfoFile);
      if (src != null && File.Exists(versioninfoFile)) {
        VersionInfo vers = src.ReadVersion();
        currentVersionWithSuffix = vers.currentVersionWithSuffix;
        return DateTime.TryParse(vers.versionDateInfo + " " + vers.versionTimeInfo, out currentVersionDateTime);
      }

      currentVersionWithSuffix = null;
      currentVersionDateTime = default;

      return false;
    }

    private bool GetGitHistorySinceTag(
    Action<DateTime, string> callback,
      DateTime startAfterDate = default,
      string ignoreBySubstring = "VERSIONING"
    ) {
      try {

        string tagFilter = string.Empty;

        {
          System.Diagnostics.Process process = new System.Diagnostics.Process();

          process.StartInfo.FileName = "git";
          process.StartInfo.UseShellExecute = false;
          process.StartInfo.Arguments = "tag --merged HEAD --sort=-creatordate";
          process.StartInfo.CreateNoWindow = true;
          process.StartInfo.RedirectStandardOutput = true;
          process.StartInfo.RedirectStandardError = true;
          process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

          process.Start();

          string tagsOutput = process.StandardOutput.ReadToEnd();

          using (StringReader reader = new StringReader(tagsOutput)) {
            string tag = reader.ReadLine();

            while (!string.IsNullOrWhiteSpace(tag)) {

              Match match = Regex.Match(
                tag,
                @"(?<!\d)(\d+\.\d+(?:\.\d+){0,2})(?!\d)"
              );

              if (match.Success) {
                Version version;

                if (Version.TryParse(match.Groups[1].Value, out version)) {
                  tagFilter = $"{tag.Trim()}..HEAD";
                  Console.WriteLine($"Using version tag '{tag.Trim()}' as changelog boundary");
                  break;
                }
              }

              tag = reader.ReadLine();
            }
          }
        }

        System.Diagnostics.Process logProcess = new System.Diagnostics.Process();

        logProcess.StartInfo.FileName = "git";
        logProcess.StartInfo.UseShellExecute = false;
        logProcess.StartInfo.Arguments = $"log {tagFilter} --pretty=format:%ai%x1f%s";
        logProcess.StartInfo.CreateNoWindow = true;
        logProcess.StartInfo.RedirectStandardOutput = true;
        logProcess.StartInfo.RedirectStandardError = true;
        logProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;

        logProcess.Start();

        string output = logProcess.StandardOutput.ReadToEnd();

        int importedCommitCount = 0;

        using (StringReader reader = new StringReader(output)) {
          string historyLine = reader.ReadLine();

          while (!string.IsNullOrWhiteSpace(historyLine)) {

            string[] columns = historyLine.Split('\x1f');

            if (columns.Length >= 2) {

              DateTime commitDate = DateTime.Parse(columns[0].Substring(0, 19));

              string commitMessage = columns[1]
                .Replace("[skip ci]", "")
                .Trim();

              if (
                commitDate > startAfterDate &&
                (
                  string.IsNullOrWhiteSpace(ignoreBySubstring) ||
                  !commitMessage.Contains(ignoreBySubstring)
                )
              ) {

                callback.Invoke(commitDate, commitMessage);

                importedCommitCount++;

                if (importedCommitCount >= 20) {
                  Console.WriteLine("Stopping GIT-History import after 20 commits.");
                  break;
                }
              }
            }

            historyLine = reader.ReadLine();
          }
        }

        return true;
      }
      catch (Exception ex) {
        Console.WriteLine("ERROR: " + ex.Message);
        return false;
      }
    }

    #endregion

    #region " MD Processing "

    static string startMarker = "# Change log";
    static string upcommingChangesMarker = "## Upcoming Changes";
    static string releasedVersionMarker = "## v ";
    static string mpvReachedTriggerWord = "**MVP**";
    static string minorMarker = "new Feature";
    static string majorMarker = "breaking Change";

    private static VersionInfo ProcessMarkdownAndCreateNewVersion(
      List<string> allLines,
      string preReleaseSemantic = "",    
      Func<string, string[]> fallbackChangesEvaluator = null
    ) {

      var versionInfo = new VersionInfo();
      versionInfo.changeGrade = "fix";
      versionInfo.versionDateInfo = DateTime.Now.ToString("yyyy-MM-dd");
      versionInfo.versionTimeInfo = DateTime.Now.ToString("HH:mm:ss");

      versionInfo.releaseType = "";
      if (!string.IsNullOrWhiteSpace(preReleaseSemantic)) {
        versionInfo.releaseType = preReleaseSemantic.Trim().ToLower();
      }

      int startIndex = FindIndex(allLines, startMarker);
      if (startIndex < 0) {
        throw new ApplicationException($"The given file does not contain the StartMarker '{startMarker}'.");
      }

      Version lastVersion = new Version(0, 0, 0);
      Version lastVersionOrPre = new Version(0, 0, 0);
      int upcommingChangesIndex = FindIndex(allLines, upcommingChangesMarker);
      int releasedVersionIndex = FindIndex(allLines, releasedVersionMarker);

      string lastVersionString = null;
      if (releasedVersionIndex >= 0) {
        lastVersionString = allLines[releasedVersionIndex].TrimStart().Substring(releasedVersionMarker.TrimStart().Length);
        Console.WriteLine($"Last version found within Changelog-File: '{lastVersionString}'");
        Version.TryParse(lastVersionString, out lastVersion);
        lastVersionOrPre = lastVersion;
      }

      bool wasPrereleased = false;
      bool mpvReachedTrigger = false;
      if (upcommingChangesIndex < 0) {
        if (releasedVersionIndex < 0) {
          allLines.Add(upcommingChangesMarker);//+ " (not yet released)");
          upcommingChangesIndex = allLines.Count() - 1;
        }
        else {
          allLines.Insert(releasedVersionIndex, upcommingChangesMarker);// + " (not yet released)");
          upcommingChangesIndex = releasedVersionIndex;
          releasedVersionIndex++;
        }
      }
      else {
        var upcommingChangesDetails = allLines[upcommingChangesIndex].TrimStart().Substring(upcommingChangesMarker.TrimStart().Length).Trim();
        if (upcommingChangesDetails.StartsWith("(")) {
          upcommingChangesDetails = upcommingChangesDetails.Substring(1);
        }
        var v = upcommingChangesDetails.Replace("-", " ").Split(' ')[0];
        if (v.Contains(".")) {
          wasPrereleased = Version.TryParse(v, out lastVersionOrPre);
        }
      }
      if (releasedVersionIndex < 0) {
        releasedVersionIndex = allLines.Count();//nirvana
      }

      int changes = 0;
      int upcommingLinesToRemoveCount = 0;
      var patchChanges = new List<string>();
      var minorChanges = new List<string>();
      var majorChanges = new List<string>();
      string mvpTriggerMessage = null;

      List<string> relevantChangesLines = new List<string>();
      for (int i = (upcommingChangesIndex + 1); i < releasedVersionIndex; i++) {
        if(!string.IsNullOrWhiteSpace(allLines[i]) && allLines[i].Trim() != "*(none)*") {
          relevantChangesLines.Add(allLines[i]);
        }
        upcommingLinesToRemoveCount++;
      }

      //fallback (z.b. auf git-history)
      if (relevantChangesLines.Count() == 0 && fallbackChangesEvaluator != null) {
        Console.WriteLine("No changes found in the changelog, trying to evaluate changes from git history...");
        relevantChangesLines = fallbackChangesEvaluator.Invoke(lastVersionString).ToList();
      }

      foreach (string relevantChangesLine in relevantChangesLines) {
        bool skipAdd = false;

        string currentLine = relevantChangesLine.Replace("**" + minorMarker + "**", minorMarker).Replace("**" + majorMarker + "**", majorMarker);
        if (currentLine.Contains(mpvReachedTriggerWord, StringComparison.InvariantCultureIgnoreCase)) {
          mpvReachedTrigger = true;
          skipAdd = true;
          mvpTriggerMessage = currentLine;
        }
        if (
          !String.IsNullOrWhiteSpace(currentLine) &&
          currentLine.Trim() != "*(none)*" &&
          !currentLine.Contains("released **")
        ) {
          if (currentLine.Contains(majorMarker, StringComparison.InvariantCultureIgnoreCase)) {
            if (!skipAdd) majorChanges.Add(currentLine.Trim());
            versionInfo.changeGrade = "major";
          }
          else if (currentLine.Contains(minorMarker, StringComparison.InvariantCultureIgnoreCase)) {
            if (!skipAdd) minorChanges.Add(currentLine.Trim());
            if (versionInfo.changeGrade == "fix") {
              versionInfo.changeGrade = "minor";
            }
          }
          else {
            if (!skipAdd) patchChanges.Add(currentLine.Trim());
          }
          changes++;
        }
      }

      if (changes == 0) {
        string assumedChange = "* New revision without significant changes";
        patchChanges.Add(assumedChange);
        //allLines.Insert(releasedVersionIndex, assumedChange);
        //releasedVersionIndex++;
        changes = 1;
      }
      else {
        //patchChanges.Sort(StringComparer.OrdinalIgnoreCase);
        //minorChanges.Sort(StringComparer.OrdinalIgnoreCase);
        //majorChanges.Sort(StringComparer.OrdinalIgnoreCase);
        if (mvpTriggerMessage != null) {
          //ganz nach oben!!!
          majorChanges.Insert(0, mvpTriggerMessage);
        }
      }

      //alle alten zeilen lschen
      allLines.RemoveRange(upcommingChangesIndex + 1, upcommingLinesToRemoveCount);
      releasedVersionIndex -= upcommingLinesToRemoveCount;

      //alle zeilen neu einfügen
      StringBuilder versionNotes = new StringBuilder();
      int insertAt = upcommingChangesIndex + 1;
      foreach (string majorChange in majorChanges) {
        string info = majorChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info.Replace(majorMarker, "**" + majorMarker + "**"));
        versionNotes.AppendLine("- " + info);
        insertAt++;
        releasedVersionIndex++;
      }
      foreach (string minorChange in minorChanges) {
        string info = minorChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info.Replace(minorMarker, "**" + minorMarker + "**"));
        versionNotes.AppendLine("- " + info);
        insertAt++;
        releasedVersionIndex++;
      }
      foreach (string patchChange in patchChanges) {
        string info = patchChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info);
        versionNotes.AppendLine("- " + info);
        insertAt++;
        releasedVersionIndex++;
      }

      //zusatzzeilen
      allLines.Insert(insertAt, "");
      allLines.Insert(insertAt, "");
      allLines.Insert(insertAt, "");
      insertAt += 3;
      releasedVersionIndex += 3;

      versionInfo.versionNotes = versionNotes.ToString();

      versionInfo.currentMajor = lastVersion.Major;
      versionInfo.currentMinor = lastVersion.Minor;
      versionInfo.currentFix = lastVersion.Build;

      var preAlreadyIncreasedMajor = (lastVersion.Major < lastVersionOrPre.Major);
      var preAlreadyIncreasedMinor = (lastVersion.Minor < lastVersionOrPre.Minor);
      var preAlreadyIncreasedPatch = (lastVersion.Build < lastVersionOrPre.Build);

      //höhere version berechnen
      if (versionInfo.changeGrade == "major" || preAlreadyIncreasedMajor) {
        if (versionInfo.currentMajor > 0 || mpvReachedTrigger) {
          versionInfo.currentMajor++;
          versionInfo.currentMinor = 0;
          versionInfo.currentFix = 0;
        }
        else {
          //bei prereleases zu major=0 (also in der alpha phase) gibt es max ein minor-increment (da sind breaking changes erlaubt);
          versionInfo.currentMinor++;
          versionInfo.currentFix = 0;
        }
      }
      else if (versionInfo.changeGrade == "minor" || preAlreadyIncreasedMinor) {
        versionInfo.currentMinor++;
        versionInfo.currentFix = 0;
      }
      else {
        versionInfo.currentFix++;
      }

      //gan am anfang kann man nicht mit einer 0.0.x starten -> 0.1 ist das minimom
      if (versionInfo.currentMajor == 0 && versionInfo.currentMinor == 0) {
        versionInfo.currentMinor = 1;
        versionInfo.currentFix = 0;
      }

      if (versionInfo.currentMajor == 0 && mpvReachedTrigger) {
        versionInfo.currentMajor = 1;
        versionInfo.currentMinor = 0;
        versionInfo.currentFix = 0;
      }

      //last prerelese mit einbeziehen
      if (wasPrereleased) {

        //var posWhenNewMeothThanPre = VersionCompare(
        //  versionInfo.currentMajor, versionInfo.currentMinor, versionInfo.currentPatch,
        //  lastVersionOrPre.Major, lastVersionOrPre.Minor, lastVersionOrPre.Build
        //);

        if (versionInfo.currentMajor == lastVersionOrPre.Major &&
          versionInfo.currentMinor == lastVersionOrPre.Minor &&
          versionInfo.currentFix <= lastVersionOrPre.Build
        ) {
          if (versionInfo.releaseType == "official") {
            //nur hochzziehen wenn nötig
            versionInfo.currentFix = lastVersionOrPre.Build;
          }
          else {
            //weiter zählen
            versionInfo.currentFix = lastVersionOrPre.Build + 1;
          }

        }

      }

      Version currentVersion = new Version(versionInfo.currentMajor, versionInfo.currentMinor, versionInfo.currentFix);
      versionInfo.currentVersion = currentVersion.ToString(3);
      versionInfo.previousVersion = lastVersion.ToString(3);

      //neuen version setzen
      allLines.Insert(upcommingChangesIndex + 1, $"released **{versionInfo.versionDateInfo}**, including:");

      if (versionInfo.releaseType == "") {

        //upcomming wird zu release
        allLines[upcommingChangesIndex] = releasedVersionMarker.TrimEnd() + $" {currentVersion.ToString(3)}";

        //neuer upcomming wird erstellt
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, "*(none)*");
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, upcommingChangesMarker.TrimEnd());// + " (not yet released)");

        versionInfo.currentVersionWithSuffix = versionInfo.currentVersion;
      }
      else {
        //upcomming wird einfach aktualisiert
        allLines[upcommingChangesIndex] = upcommingChangesMarker.TrimEnd() + $" ({currentVersion.ToString(3)}-{versionInfo.releaseType})";
        versionInfo.currentVersionWithSuffix = versionInfo.currentVersion + "-" + versionInfo.releaseType;
      }

      return versionInfo;
    }

    private IVersionContainer InitializeVersionContainerByFileType(string fileFullName) {

      if (fileFullName.Contains("*")) {
        string[] matches = this.ListFiles(fileFullName);
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
      else if (fName.EndsWith(".nuspec", StringComparison.InvariantCultureIgnoreCase) || fName.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase)) {
        return new NuspecFileAccessor(fileFullName);
      }
      else if (fName.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase) || fName.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase)) {
        return new CompiledAssembyFileAccessor(fileFullName);
      }

      throw new NotImplementedException("Unsupported File: " + fName);
      //return null;
    }

    #endregion

    #region " NugetUpdates "

    /// <summary>
    /// Runs the 'dotnet add'-command for .NET core projects or the 'nuget.exe' for .NET framework projects to
    /// update a NuGet-Package and respects the packages-folder for the given solution-file!
    /// </summary>
    /// <param name="solutionFiles">
    /// multiple minimatch-patterns (separated by ;) to address one or more .sln-files
    /// </param>
    /// <param name="packageId"></param>
    /// <param name="newPackageVersion"></param>
    public void UpdatePackageReference(
      string solutionFiles, string packageId, string newPackageVersion, bool allowDowngrade = false
    ) {

      //nur für test-zwecke - normalerweise natürlich nicht bei einem update!!!
      const bool addIfNotExisting = false;

      string[] fileFullNames = this.ListFiles(solutionFiles);
      DependencyInfo newDependency = new DependencyInfo(packageId, newPackageVersion);

      foreach ( string fileFullName in fileFullNames) {

        if (fileFullName.EndsWith(".sln")) {
          Console.WriteLine($"Processing SOLUTION '{fileFullName}'...");
          string[] projFileFullNames = SlnFileHelper.GetProjectFileFullNames(fileFullName);
          foreach (string projFileFullName in projFileFullNames) {
            Console.Write($"   Processing '{Path.GetFileName(projFileFullName)}'...  ");
            if (File.Exists(projFileFullName)) {
              VsProjFileAccessor projFile = new VsProjFileAccessor(projFileFullName);
              if (projFile.IsDotNetCoreFormat()) {
                Console.WriteLine($"     (.NET CORE)");
              }
              else {
                Console.WriteLine($"     (.NET Framework)");
              }

              projFile.WritePackageDependencies(
                new DependencyInfo[] { newDependency },
                addNew: addIfNotExisting, updateExisiting: true, deleteOthers: false, 
                allowDowngrade: allowDowngrade,
                onlyForTargetFramework: null,
                packageIdWhitelist: new string[] { "*" },
                packageIdBlacklist: new string[] { }
              );

            }
            else {
              Console.WriteLine($"     (NOT EXISITING !!!)");
            }
          }
        }
        else {
          IVersionContainer tgt = InitializeVersionContainerByFileType(fileFullName);
          Console.Write($"   Processing '{Path.GetFileName(fileFullName)}'...  ");
          if(tgt is VsProjFileAccessor) {
            if ((tgt as VsProjFileAccessor).IsDotNetCoreFormat()) {
              Console.Write($"     (.NET CORE)");
            }
            else {
              Console.Write($"     (.NET Framework)");
            }
          }
          Console.WriteLine();

          tgt.WritePackageDependencies(
            new DependencyInfo[] { newDependency }, 
            addNew: addIfNotExisting, updateExisiting: true, deleteOthers: false,
            allowDowngrade: allowDowngrade, onlyForTargetFramework: null,
            packageIdWhitelist: new string[] { "*" },
            packageIdBlacklist: new string[] { }
          );

        }

      }

    }

    #endregion

    /// <summary>
    /// Executes the full Workflow:
    /// 1: search recursively for a changelog.md file
    /// 2: create a new changelog-based version (with fallback to git history)
    /// 3: tell to Azure DevOps to update the buildnumber
    /// 4: inject version-variables to the Azure DevOps pipeline
    /// 5: update the version in all project files (and other files) which are matching a given minimatch pattern
    /// 6: process all nuspec files (recursively) to update their dependency-constraints (if they contain a preprocessor-configuration)
    /// </summary>
    public void DoMagic(
      string entryDirectory = "",
      string preReleaseSemantic = "",
      string ignoreSemantic = "master;main;rel-*"
    ) {

      if (string.IsNullOrWhiteSpace(entryDirectory)) {
        entryDirectory = Environment.CurrentDirectory;
      }
      entryDirectory = Path.GetFullPath(entryDirectory);
      if (!Directory.Exists(entryDirectory)) {
        throw new DirectoryNotFoundException("The entry directory does not exist: " + entryDirectory);
      }

      string previousCurrentDirectory = Environment.CurrentDirectory;

      try {
        Environment.CurrentDirectory = entryDirectory;

        string[] changelogFiles = Directory
          .GetFiles(entryDirectory, "changelog.md", SearchOption.AllDirectories)
          .Where((fileFullName) => {
            return !fileFullName.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) &&
              !fileFullName.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) &&
              !fileFullName.Contains("\\packages\\", StringComparison.OrdinalIgnoreCase) &&
              !fileFullName.Contains("\\node_modules\\", StringComparison.OrdinalIgnoreCase);
          })
          .ToArray();

        if (changelogFiles.Length == 0) {
          throw new FileNotFoundException("Could not find a changelog.md file below: " + entryDirectory);
        }

        if (changelogFiles.Length > 1) {
          throw new InvalidOperationException(
            "Found more than one changelog.md file. Please run DoMagic from a more specific directory: " +
            string.Join(" / ", changelogFiles)
          );
        }

        string changelogFile = changelogFiles[0];
        string versionInfoFile = Path.Combine(Path.GetDirectoryName(changelogFile), "versioninfo.json");

        this.CreateNewVersionOnChangelog(
          versionInfoFile,
          changelogFile, 
          preReleaseSemantic,
          ignoreSemantic
        );

        this.InjectIntoAzureDevOpsBuildNumber(versionInfoFile);
        this.InjectIntoAzureDevOpsVariables(versionInfoFile);

        string versionTargetPattern =
          "**\\*.csproj;" +
          "**\\*.vbproj;" +
          //"**\\*.nuspec;" +
          "**\\package.json;" +
          "**\\AssemblyInfo.cs;" +
          "**\\AssemblyInfo.vb;" +
          "!**\\bin\\**;" +
          "!**\\obj\\**;" +
          "!**\\packages\\**;" +
          "!**\\node_modules\\**";

        this.ImportVersion(
          versionTargetPattern,
          versionInfoFile
        );

        string nuspecTargetPattern =
          "**\\*.nuspec;" +
          "!**\\bin\\**;" +
          "!**\\obj\\**;" +
          //"!**\\packages\\**;" +
          "!**\\node_modules\\**";

        this.ProcessNuspecFiles(
          nuspecTargetPattern,
          versionInfoFile
        );
      }
      finally {
        Environment.CurrentDirectory = previousCurrentDirectory;
      }
    }

  }

}
