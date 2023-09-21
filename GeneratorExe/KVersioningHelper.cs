﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Versioning;

namespace Versioning {

  public class KVersioningHelper {

    /// <summary>
    /// Uses a given changeLogFile a leading database to store the current version of an product as
    /// well as a list of upcomming changes, that will lead to a new version.
    /// SAMPLE: CreateNewVersionOnChangelog "myPackage.nuspec" "changelog.md"
    /// NOTE: if you want to patch more than a single 'package.json' or '.nuspec' file,
    /// then you need to use the 'CopyVersion' command afterwards (it supports minimatch-patterns)
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

      var info = KVersioningHelper.ProcessMarkdownAndCreateNewVersion(allLines, preReleaseSemantic);

      Console.WriteLine(info.currentVersionWithSuffix);
      Console.WriteLine("---");
      Console.WriteLine(info.versionNotes);
      Console.WriteLine("---");

      File.WriteAllLines(changeLogFile, allLines.ToArray(), Encoding.Default);

      if (string.IsNullOrWhiteSpace(targetFile)) {
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
            sw.WriteLine($"  \"currentVersion\": \"{info.currentVersion}\"");
            sw.WriteLine($"  \"currentVersionWithSuffix\": \"{info.currentVersionWithSuffix}\"");
            sw.WriteLine($"  \"releaseType\": \"{info.releaseType}\"");
            sw.WriteLine($"  \"previousVersion\": \"{info.previousVersion}\"");
            sw.WriteLine($"  \"changeGrade\": \"{info.changeGrade}\"");
            sw.WriteLine($"  \"currentMajor\": {info.currentMajor}");
            sw.WriteLine($"  \"currentMinor\": {info.currentMinor}");
            sw.WriteLine($"  \"currentPatch\": {info.currentPatch}");
            sw.WriteLine($"  \"versionDateInfo\": \"{info.versionDateInfo}\"");
            sw.WriteLine($"  \"versionTimeInfo\": \"{info.versionTimeInfo}\"");
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


     //TODO: wir brauchen noch opensource SINGLE-FILE Json reader/writer + XML damit wir referenzfrei bleiben!








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







      //string nam = Path.GetFileName(targetFileToProcess).ToLower();
      //string ext = Path.GetExtension(targetFileToProcess).ToLower();
      //if (
      //  ext == ".nuspec" || ext == ".vbproj" || ext == ".csproj" || nam == "package.json" || nam == "assemblyinfo.vb" || nam == "assemblyinfo.cs"
      //) {
        
      //}
      //else { 
      //}







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













    }

    /// <summary>
    /// SAMPLE: CopyVersion "**\*.csproj;**\*.vbproj;**\MyProject\AssemblyInfo.cs;**\MyProject\AssemblyInfo.vb;" "myLib.nuspec" 
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
    public void CopyVersion(
      string targetFilesToProcess,
      string metaDataSourceFile = "versioninfo.json"
    ) {













    }

    /// <summary>
    /// SAMPLE: SetVersionToRefEntry "GreatExternalLib" "1.2.3-alpha" "**\*.nuspec;**\package.json;!**\node_modules\**"
    /// </summary>
    /// <param name="refPackageId">
    /// </param>
    /// <param name="semanticVersion">
    ///   can also contain a suffix like '-prerelease'
    /// </param>
    /// <param name="targetFilesToProcess">
    /// multiple minimatch-patterns (separated by ;) to address one or more
    /// 'package.json' files in NPM format OR
    /// '.nuspec' files in NuGetFormat
    /// WARNING: '.vbproj'/'.csproj' files in .net CORE project format ARE CURRENTLY NOT SUPPORTED!
    /// </param>
    public void SetVersionToRefEntry(
      string refPackageId,
      string semanticVersion,
      string targetFilesToProcess
    ) {












    }

    /// <summary>
    /// SAMPLE: CopyVersionToRefEntry "MyLib" "**\*.nuspec;**\package.json;!**\node_modules\**" "myLib.nuspec"
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
    public void CopyVersionToRefEntry(
      string refPackageId,
      string targetFilesToProcess,
      string metaDataSourceFile = "versioninfo.json"
    ) {











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

    public bool MatchesWildcardMask(string stringToEvaluate, string pattern, bool ignoreCasing = true) {
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

    static string startMarker = "# Change log";
    static string upcommingChangesMarker = "## Upcoming Changes";
    static string releasedVersionMarker = "## v ";
    static string mpvReachedTriggerWord = "**MVP**";
    static string minorMarker = "new Feature";
    static string majorMarker = "breaking Change";

    private static VersionInfo ProcessMarkdownAndCreateNewVersion(List<string> allLines, string preReleaseSemantic = "") {
      var versionInfo = new VersionInfo();

      versionInfo.releaseType = "official";
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

      if (releasedVersionIndex >= 0) {
        var lastVersionString = allLines[releasedVersionIndex].TrimStart().Substring(releasedVersionMarker.TrimStart().Length);
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
      int linecount = 0;
      var patchChanges = new List<string>();
      var minorChanges = new List<string>();
      var majorChanges = new List<string>();
      string mvpTriggerMessage = null;

      for (int i = (upcommingChangesIndex + 1); i < releasedVersionIndex; i++) {
        bool skipAdd = false;
        string currentLine = allLines[i].Replace("**" + minorMarker + "**", minorMarker).Replace("**" + majorMarker + "**", majorMarker);
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
            if (versionInfo.changeGrade == "patch") {
              versionInfo.changeGrade = "minor";
            }
          }
          else {
            if (!skipAdd) patchChanges.Add(currentLine.Trim());
          }
          changes++;
        }
        linecount++;
      }

      if (changes == 0) {
        string assumedChange = "* new revision without significant changes";
        patchChanges.Add(assumedChange);
        //allLines.Insert(releasedVersionIndex, assumedChange);
        //releasedVersionIndex++;
        changes = 1;
      }
      else {
        patchChanges.Sort(StringComparer.OrdinalIgnoreCase);
        minorChanges.Sort(StringComparer.OrdinalIgnoreCase);
        majorChanges.Sort(StringComparer.OrdinalIgnoreCase);
        if (mvpTriggerMessage != null) {
          //ganz nach oben!!!
          majorChanges.Insert(0, mvpTriggerMessage);
        }
      }

      //alle alten zeilen lschen
      allLines.RemoveRange(upcommingChangesIndex + 1, linecount);
      releasedVersionIndex -= linecount;

      //alle zeilen neu einfügen
      var versionNotes = new StringBuilder();
      var insertAt = upcommingChangesIndex + 1;
      foreach (var majorChange in majorChanges) {
        var info = majorChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info.Replace(majorMarker, "**" + majorMarker + "**"));
        versionNotes.AppendLine("- " + info);
        insertAt++;
        releasedVersionIndex++;
      }
      foreach (var minorChange in minorChanges) {
        var info = minorChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info.Replace(minorMarker, "**" + minorMarker + "**"));
        versionNotes.AppendLine("- " + info);
        insertAt++;
        releasedVersionIndex++;
      }
      foreach (var patchChange in patchChanges) {
        var info = patchChange.Trim();
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
      versionInfo.currentPatch = lastVersion.Build;

      var preAlreadyIncreasedMajor = (lastVersion.Major < lastVersionOrPre.Major);
      var preAlreadyIncreasedMinor = (lastVersion.Minor < lastVersionOrPre.Minor);
      var preAlreadyIncreasedPatch = (lastVersion.Build < lastVersionOrPre.Build);

      //höhere version berechnen
      if (versionInfo.changeGrade == "major" || preAlreadyIncreasedMajor) {
        if (versionInfo.currentMajor > 0 || mpvReachedTrigger) {
          versionInfo.currentMajor++;
          versionInfo.currentMinor = 0;
          versionInfo.currentPatch = 0;
        }
        else {
          //bei prereleases zu major=0 (also in der alpha phase) gibt es max ein minor-increment (da sind breaking changes erlaubt);
          versionInfo.currentMinor++;
          versionInfo.currentPatch = 0;
        }
      }
      else if (versionInfo.changeGrade == "minor" || preAlreadyIncreasedMinor) {
        versionInfo.currentMinor++;
        versionInfo.currentPatch = 0;
      }
      else {
        versionInfo.currentPatch++;
      }

      //gan am anfang kann man nicht mit einer 0.0.x starten -> 0.1 ist das minimom
      if (versionInfo.currentMajor == 0 && versionInfo.currentMinor == 0) {
        versionInfo.currentMinor = 1;
        versionInfo.currentPatch = 0;
      }

      if (versionInfo.currentMajor == 0 && mpvReachedTrigger) {
        versionInfo.currentMajor = 1;
        versionInfo.currentMinor = 0;
        versionInfo.currentPatch = 0;
      }

      //last prerelese mit einbeziehen
      if (wasPrereleased) {

        //var posWhenNewMeothThanPre = VersionCompare(
        //  versionInfo.currentMajor, versionInfo.currentMinor, versionInfo.currentPatch,
        //  lastVersionOrPre.Major, lastVersionOrPre.Minor, lastVersionOrPre.Build
        //);

        if (versionInfo.currentMajor == lastVersionOrPre.Major &&
          versionInfo.currentMinor == lastVersionOrPre.Minor &&
          versionInfo.currentPatch <= lastVersionOrPre.Build
        ) {
          if (versionInfo.releaseType == "official") {
            //nur hochzziehen wenn nötig
            versionInfo.currentPatch = lastVersionOrPre.Build;
          }
          else {
            //weiter zählen
            versionInfo.currentPatch = lastVersionOrPre.Build + 1;
          }

        }

      }

      Version currentVersion = new Version(versionInfo.currentMajor, versionInfo.currentMinor, versionInfo.currentPatch);
      versionInfo.currentVersion = currentVersion.ToString(3);
      versionInfo.previousVersion = lastVersion.ToString(3);

      //neuen version setzen
      allLines.Insert(upcommingChangesIndex + 1, $"released **{versionInfo.versionDateInfo}**, including:");

      if (versionInfo.releaseType == "official") {

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

    #endregion

  }

}
