using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Versioning.ShouldBeLibrary;

namespace Versioning.TextProcessing {

  public class MarkDownProcessor {

    public static VersionInfo ProcessMarkdownAndCreateNewVersion(List<string> allLines, string preReleaseSemantic = "") {

      int startRowIndex = ListUtil.FindRowIndex(allLines, Conventions.startMarker);

      if (startRowIndex < 0) throw new ApplicationException($"The given file does not contain the StartMarker '{Conventions.startMarker}'.");

      // Fetch the old version from the first line containing "## v "...

      Version oldVersion = new Version(0, 0, 0);

      Version oldVersionOrPre = new Version(0, 0, 0);

      int oldVersionRowIndex = ListUtil.FindRowIndex(allLines, Conventions.releasedVersionMarker);

      if (oldVersionRowIndex >= 0) {
        var oldVersionAsString = allLines[oldVersionRowIndex].TrimStart().Substring(Conventions.releasedVersionMarker.TrimStart().Length);
        Version.TryParse(oldVersionAsString, out oldVersion);
        oldVersionOrPre = oldVersion;
      }

      bool wasPreReleased = false;

      // Look for "## Upcoming Changes" row ...

      int upcommingChangesRowIndex = ListUtil.FindRowIndex(allLines, Conventions.upcommingChangesMarker);

      if (upcommingChangesRowIndex < 0) { // ... doesn't exist => Insert one
        if (oldVersionRowIndex < 0) {
          allLines.Add(Conventions.upcommingChangesMarker);//+ " (not yet released)");
          upcommingChangesRowIndex = allLines.Count - 1;
        } else {
          allLines.Insert(oldVersionRowIndex, Conventions.upcommingChangesMarker);// + " (not yet released)");
          upcommingChangesRowIndex = oldVersionRowIndex;
          oldVersionRowIndex++;
        }
      } else { // ... found => 
        string upcommingChangesLineRightPart = allLines[upcommingChangesRowIndex].TrimStart().Substring(Conventions.upcommingChangesMarker.TrimStart().Length).Trim();
        if (upcommingChangesLineRightPart.StartsWith("(")) {
          upcommingChangesLineRightPart = upcommingChangesLineRightPart.Substring(1);
        }
        string oldPreReleaseVersionAsString = upcommingChangesLineRightPart.Replace("-", " ").Split(' ')[0];
        if (oldPreReleaseVersionAsString.Contains(".")) {
          wasPreReleased = Version.TryParse(oldPreReleaseVersionAsString, out oldVersionOrPre);
        }
      }

      if (oldVersionRowIndex < 0) {
        oldVersionRowIndex = allLines.Count();//nirvana
      }

      // read the section between "## Upcoming Changes" and "## v " => collect rows and group them

      // HACK: Ein MarkDown Aufzählungspunkt MUSS hier aus GENAU EINER ZEILE bestehen - er darf sich nicht über mehrere Zeilen erstrecken!

      var versionInfo = new VersionInfo {
        changeGrade = "fix",
        versionDateInfo = DateTime.Now.ToString("yyyy-MM-dd"),
        versionTimeInfo = DateTime.Now.ToString("HH:mm:ss"),
        preReleaseSuffix = ""
      };

      if (!string.IsNullOrWhiteSpace(preReleaseSemantic)) versionInfo.preReleaseSuffix = preReleaseSemantic.Trim().ToLower();

      int changes = 0;
      int linecount = 0;
      var patchChanges = new List<string>();
      var minorChanges = new List<string>();
      var majorChanges = new List<string>();
      string mvpTriggerMessage = null;

      bool mpvReachedTrigger = false;

      for (int i = (upcommingChangesRowIndex + 1); i < oldVersionRowIndex; i++) {

        bool skipAdd = false;

        // read & sanitize the current line (by removing the markdown  stuff "**" from "new Feature" & "breaking Change"

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
      } // next

      // apply information from collected rows

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

      // alle alten zeilen löschen

      allLines.RemoveRange(upcommingChangesRowIndex + 1, linecount);
      oldVersionRowIndex -= linecount;

      // alle zeilen neu einfügen

      var versionNotes = new StringBuilder();
      var insertAt = upcommingChangesRowIndex + 1;
      foreach (var majorChange in majorChanges) {
        var info = majorChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info.Replace(Conventions.majorMarker, "**" + Conventions.majorMarker + "**"));
        versionNotes.AppendLine("- " + info);
        insertAt++;
        oldVersionRowIndex++;
      }
      foreach (var minorChange in minorChanges) {
        var info = minorChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info.Replace(Conventions.minorMarker, "**" + Conventions.minorMarker + "**"));
        versionNotes.AppendLine("- " + info);
        insertAt++;
        oldVersionRowIndex++;
      }
      foreach (var patchChange in patchChanges) {
        var info = patchChange.Trim();
        if (info.StartsWith("* ")) { info = info.Substring(1).Trim(); }
        if (info.StartsWith("- ")) { info = info.Substring(1).Trim(); }
        allLines.Insert(insertAt, " - " + info);
        versionNotes.AppendLine("- " + info);
        insertAt++;
        oldVersionRowIndex++;
      }

      //zusatzzeilen

      allLines.Insert(insertAt, "");
      allLines.Insert(insertAt, "");
      allLines.Insert(insertAt, "");
      insertAt += 3;
      oldVersionRowIndex += 3;

      versionInfo.versionNotes = versionNotes.ToString();

      versionInfo.currentMajor = oldVersion.Major;
      versionInfo.currentMinor = oldVersion.Minor;
      versionInfo.currentFix = oldVersion.Build;

      var preAlreadyIncreasedMajor = (oldVersion.Major < oldVersionOrPre.Major);
      var preAlreadyIncreasedMinor = (oldVersion.Minor < oldVersionOrPre.Minor);
      var preAlreadyIncreasedPatch = (oldVersion.Build < oldVersionOrPre.Build);

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

      // ganz am anfang kann man nicht mit einer 0.0.x starten -> 0.1 ist das minimum
      if (versionInfo.currentMajor == 0 && versionInfo.currentMinor == 0) {
        versionInfo.currentMinor = 1;
        versionInfo.currentFix = 0;
      }

      if (versionInfo.currentMajor == 0 && mpvReachedTrigger) {
        versionInfo.currentMajor = 1;
        versionInfo.currentMinor = 0;
        versionInfo.currentFix = 0;
      }

      // last prerelese mit einbeziehen
      if (wasPreReleased) {

        //var posWhenNewMeothThanPre = VersionCompare(
        //  versionInfo.currentMajor, versionInfo.currentMinor, versionInfo.currentPatch,
        //  lastVersionOrPre.Major, lastVersionOrPre.Minor, lastVersionOrPre.Build
        //);

        if (versionInfo.currentMajor == oldVersionOrPre.Major &&
          versionInfo.currentMinor == oldVersionOrPre.Minor &&
          versionInfo.currentFix <= oldVersionOrPre.Build
        ) {
          if (versionInfo.preReleaseSuffix == "official") {
            //nur hochzziehen wenn nötig
            versionInfo.currentFix = oldVersionOrPre.Build;
          } else {
            //weiter zählen
            versionInfo.currentFix = oldVersionOrPre.Build + 1;
          }

        }

      }

      Version currentVersion = new Version(versionInfo.currentMajor, versionInfo.currentMinor, versionInfo.currentFix);
      versionInfo.currentVersion = currentVersion.ToString(3);
      versionInfo.previousVersion = oldVersion.ToString(3);

      // neue version setzen

      allLines.Insert(upcommingChangesRowIndex + 1, $"released **{versionInfo.versionDateInfo}**, including:");

      if (versionInfo.preReleaseSuffix == "") {

        // Replace "## Upcoming Changes" by "## v 1.2.3"

        allLines[upcommingChangesRowIndex] = Conventions.releasedVersionMarker.TrimEnd() + $" {currentVersion.ToString(3)}";

        // neuer upcomming wird erstellt

        allLines.Insert(upcommingChangesRowIndex, "");
        allLines.Insert(upcommingChangesRowIndex, "");
        allLines.Insert(upcommingChangesRowIndex, "");
        allLines.Insert(upcommingChangesRowIndex, "*(none)*");
        allLines.Insert(upcommingChangesRowIndex, "");
        allLines.Insert(upcommingChangesRowIndex, Conventions.upcommingChangesMarker.TrimEnd());// + " (not yet released)");

        versionInfo.currentVersionWithSuffix = versionInfo.currentVersion;

      } else {

        // upcomming wird einfach aktualisiert

        allLines[upcommingChangesRowIndex] = Conventions.upcommingChangesMarker.TrimEnd() + $" ({currentVersion.ToString(3)}-{versionInfo.preReleaseSuffix})";
        versionInfo.currentVersionWithSuffix = versionInfo.currentVersion + "-" + versionInfo.preReleaseSuffix;
      }

      return versionInfo;
    }

  }

}
