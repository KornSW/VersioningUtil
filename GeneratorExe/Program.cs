using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace CodeGeneration {

  public class Program {

    static int Main(string[] args) {
      try {

        Console.WriteLine("evaluating...");

        string inputFileName = "./changelog.md";
        string outputFileName = "./versioninfo.json";
        string preReleaseSemantic = "";

        if (args != null && args.Length > 0) {
          preReleaseSemantic = args[0].Trim();
        }

        if (!Path.IsPathRooted(inputFileName)) {
          inputFileName = Path.Combine(Environment.CurrentDirectory, inputFileName);
        }
        inputFileName = Path.GetFullPath(inputFileName);

        if (!Path.IsPathRooted(outputFileName)) {
          outputFileName = Path.Combine(Environment.CurrentDirectory, outputFileName);
        }
        outputFileName = Path.GetFullPath(outputFileName);

        var allLines = new List<string>();

        using (FileStream fs = new FileStream(inputFileName, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite)) {
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
          allLines.Add("*(it is automatically maintained using the 'VersioningUtil' by [KornSW](https://github.com/KornSW))*");
          allLines.Add("");
          allLines.Add("");
        }

      var info = Process(allLines, preReleaseSemantic);

        //if (fileIsNew) {
        //  allLines.Add("");
        //  allLines.Add("");
        //  allLines.Add("");
        //  allLines.Add("");
        //  allLines.Add("-----");

        //}


        Console.WriteLine(info.currentVersionWithSuffix);
        Console.WriteLine("---");
        Console.WriteLine(info.versionNotes);
        Console.WriteLine("---");

        File.WriteAllLines(inputFileName, allLines.ToArray(), Encoding.Default);

        using (FileStream fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write)) {
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
            sw.WriteLine($"  \"versionNotes\": \"{info.versionNotes.Replace(Environment.NewLine,"\\n").Replace("\"","\\\"")}\"");
            sw.WriteLine("}");
            sw.Flush();
          }
        }

      }
      catch (Exception ex) {
        Console.WriteLine("ERROR: " + ex.Message);
        return 1;
      }
      Console.WriteLine("completed...");
      return 0;
    }

    public class VersionInfo {
      public String previousVersion { get; set; } = "0.0.0";
      public String currentVersion { get; set; } = "0.0.0";
      public int currentMajor { get; set; } = 0;
      public int currentMinor { get; set; } = 0;
      public int currentPatch { get; set; } = 0;
      public String changeGrade { get; set; } = "patch";
      public String releaseType { get; set; } = "official";
      public String currentVersionWithSuffix { get; set; } = "0.0.0";
      public String versionTimeInfo { get; set; } = DateTime.Now.ToString("HH:mm:ss");
      public String versionDateInfo { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
      public String versionNotes { get; set; } = "";
    }

    static string startMarker = "# Change log";
    static string upcommingChangesMarker = "## Upcoming Changes";
    static string releasedVersionMarker = "## v ";

    static string mpvReachedTriggerWord = "**MVP**";
    static string minorMarker = "new Feature";
    static string majorMarker = "breaking Change";

    static VersionInfo Process(List<string> allLines, string preReleaseSemantic = "") {
      var versionInfo = new VersionInfo();

      versionInfo.releaseType = "official";
      if (!string.IsNullOrWhiteSpace(preReleaseSemantic)) {
        versionInfo.releaseType = preReleaseSemantic.Trim().ToLower();
      }

      int startIndex = FindIndex(allLines, startMarker);
      if (startIndex < 0) {
        throw new ApplicationException($"The given file does not contain the StartMarker '{startMarker}'.");
      }

      Version lastVersion = new Version(0,0,0);
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
        allLines.Insert(insertAt, " - " + info.Replace( majorMarker, "**" + majorMarker + "**"));
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
      insertAt +=3;
      releasedVersionIndex+=3;

      versionInfo.versionNotes = versionNotes.ToString();

      versionInfo.currentMajor = lastVersion.Major;
      versionInfo.currentMinor = lastVersion.Minor;
      versionInfo.currentPatch = lastVersion.Build;

      var preAlreadyIncreasedMajor = (lastVersion.Major < lastVersionOrPre.Major);
      var preAlreadyIncreasedMinor = (lastVersion.Minor < lastVersionOrPre.Minor);
      var preAlreadyIncreasedPatch = (lastVersion.Build < lastVersionOrPre.Build);

      //höhere version berechnen
      if (versionInfo.changeGrade == "major" || preAlreadyIncreasedMajor) {
        if(versionInfo.currentMajor > 0 || mpvReachedTrigger) {
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
      if(versionInfo.currentMajor == 0 && versionInfo.currentMinor == 0) {
        versionInfo.currentMinor = 1;
        versionInfo.currentPatch = 0;
      }

     if(versionInfo.currentMajor == 0 && mpvReachedTrigger) {
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
    static int VersionCompare(int ma1, int mi1, int p1, int ma2, int mi2, int p2) {
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

    static int FindIndex(List<string> list, string searchString, int startAt = 0) {
      for (int i = startAt; i < list.Count(); i++) {
        if(list[i].Contains(searchString, StringComparison.InvariantCultureIgnoreCase)) {
          return i;
        }
      }
      return -1;
    }
  }

}
