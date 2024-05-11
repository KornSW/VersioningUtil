using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Versioning.ShouldBeLibrary;

namespace Versioning.TextProcessing {

  internal class MarkDownProcessor {

    public static VersionInfo ProcessMarkdownAndCreateNewVersion(List<string> allLines, string preReleaseSemantic = "") {

      var versionInfo = new VersionInfo {
        changeGrade = "fix",
        versionDateInfo = DateTime.Now.ToString("yyyy-MM-dd"),
        versionTimeInfo = DateTime.Now.ToString("HH:mm:ss"),
        preReleaseSuffix = ""
      };

      if (!string.IsNullOrWhiteSpace(preReleaseSemantic)) versionInfo.preReleaseSuffix = preReleaseSemantic.Trim().ToLower();

      int startIndex = ListUtil.FindIndex(allLines, Conventions.startMarker);

      if (startIndex < 0) throw new ApplicationException($"The given file does not contain the StartMarker '{Conventions.startMarker}'.");

      Version lastVersion = new Version(0, 0, 0);
      Version lastVersionOrPre = new Version(0, 0, 0);
      int upcommingChangesIndex = ListUtil.FindIndex(allLines, Conventions.upcommingChangesMarker);
      int releasedVersionIndex = ListUtil.FindIndex(allLines, Conventions.releasedVersionMarker);

      if (releasedVersionIndex >= 0) {
        var lastVersionString = allLines[releasedVersionIndex].TrimStart().Substring(Conventions.releasedVersionMarker.TrimStart().Length);
        Version.TryParse(lastVersionString, out lastVersion);
        lastVersionOrPre = lastVersion;
      }

      bool wasPrereleased = false;
      bool mpvReachedTrigger = false;
      if (upcommingChangesIndex < 0) {
        if (releasedVersionIndex < 0) {
          allLines.Add(Conventions.upcommingChangesMarker);//+ " (not yet released)");
          upcommingChangesIndex = allLines.Count() - 1;
        } else {
          allLines.Insert(releasedVersionIndex, Conventions.upcommingChangesMarker);// + " (not yet released)");
          upcommingChangesIndex = releasedVersionIndex;
          releasedVersionIndex++;
        }
      } else {
        var upcommingChangesDetails = allLines[upcommingChangesIndex].TrimStart().Substring(Conventions.upcommingChangesMarker.TrimStart().Length).Trim();
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
        string currentLine = allLines[i].Replace("**" + Conventions.minorMarker + "**", Conventions.minorMarker).Replace("**" + Conventions.majorMarker + "**", Conventions.majorMarker);
        if (currentLine.Contains(Conventions.mpvReachedTriggerWord, StringComparison.InvariantCultureIgnoreCase)) {
          mpvReachedTrigger = true;
          skipAdd = true;
          mvpTriggerMessage = currentLine;
        }
        if (
          !String.IsNullOrWhiteSpace(currentLine) &&
          currentLine.Trim() != "*(none)*" &&
          !currentLine.Contains("released **")
        ) {
          if (currentLine.Contains(Conventions.majorMarker, StringComparison.InvariantCultureIgnoreCase)) {
            if (!skipAdd) majorChanges.Add(currentLine.Trim());
            versionInfo.changeGrade = "major";
          } else if (currentLine.Contains(Conventions.minorMarker, StringComparison.InvariantCultureIgnoreCase)) {
            if (!skipAdd) minorChanges.Add(currentLine.Trim());
            if (versionInfo.changeGrade == "patch") {
              versionInfo.changeGrade = "minor";
            }
          } else {
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
      } else {
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
        allLines.Insert(insertAt, " - " + info.Replace(Conventions.majorMarker, "**" + Conventions.majorMarker + "**"));
        versionNotes.AppendLine("- " + info);
        insertAt++;
        releasedVersionIndex++;
      }
      foreach (var minorChange in minorChanges) {
        var info = minorChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info.Replace(Conventions.minorMarker, "**" + Conventions.minorMarker + "**"));
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
        } else {
          //bei prereleases zu major=0 (also in der alpha phase) gibt es max ein minor-increment (da sind breaking changes erlaubt);
          versionInfo.currentMinor++;
          versionInfo.currentFix = 0;
        }
      } else if (versionInfo.changeGrade == "minor" || preAlreadyIncreasedMinor) {
        versionInfo.currentMinor++;
        versionInfo.currentFix = 0;
      } else {
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
          if (versionInfo.preReleaseSuffix == "official") {
            //nur hochzziehen wenn nötig
            versionInfo.currentFix = lastVersionOrPre.Build;
          } else {
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

      if (versionInfo.preReleaseSuffix == "") {

        //upcomming wird zu release
        allLines[upcommingChangesIndex] = Conventions.releasedVersionMarker.TrimEnd() + $" {currentVersion.ToString(3)}";

        //neuer upcomming wird erstellt
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, "*(none)*");
        allLines.Insert(upcommingChangesIndex, "");
        allLines.Insert(upcommingChangesIndex, Conventions.upcommingChangesMarker.TrimEnd());// + " (not yet released)");

        versionInfo.currentVersionWithSuffix = versionInfo.currentVersion;
      } else {
        //upcomming wird einfach aktualisiert
        allLines[upcommingChangesIndex] = Conventions.upcommingChangesMarker.TrimEnd() + $" ({currentVersion.ToString(3)}-{versionInfo.preReleaseSuffix})";
        versionInfo.currentVersionWithSuffix = versionInfo.currentVersion + "-" + versionInfo.preReleaseSuffix;
      }

      return versionInfo;
    }



  }

}
