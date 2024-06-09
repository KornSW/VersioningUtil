using System;

namespace Versioning {

  public class VersionInfo {

    public String changeGrade { get; set; } = "initial";

    public int currentMajor { get; set; } = 0;
    
    public int currentMinor { get; set; } = 0;
    
    /// <summary>
    ///   Sometimes this is called "patch", but we prefer "fix".
    /// </summary>
    public int currentFix { get; set; } = 0;

    public String currentVersion { get; set; } = "0.0.0";
    
    public String currentVersionWithSuffix { get; set; } = "0.0.0";

    public String preReleaseSuffix { get; set; } = "";
    
    public String previousVersion { get; set; } = "0.0.0";

    public String versionNotes { get; set; } = "";

    public String versionDateInfo { get; set; } = "1900-01-01";

    public String versionTimeInfo { get; set; } = "00:00:00";

    public void CurrentVersionAndPrereleaseSuffix2CurrentVersionWithSuffix() {
      ParseVersion(this.currentVersion, out int maj, out int min, out int fix, out string preReleaseSuffix);
      this.currentVersionWithSuffix = $"{maj}.{min}.{fix}";
      if (!String.IsNullOrWhiteSpace(this.preReleaseSuffix)) {
        this.currentVersionWithSuffix = this.currentVersionWithSuffix + "-" + this.preReleaseSuffix;
      }
    }
    public void CurrentVersionWithSuffix2CurrentVersionAndPrereleaseSuffx(bool alsoUpdateVersionPartFields = true) {
      ParseVersion(this.currentVersionWithSuffix, out int maj, out int min, out int fix, out string preReleaseSuffix);
      this.currentVersion = $"{maj}.{min}.{fix}";
      this.preReleaseSuffix = preReleaseSuffix;
      if (alsoUpdateVersionPartFields) {
        this.CurrentVersion2VersionPartFields();
      }
    }
    
    public void VersionPartFields2CurrentVersion(bool alsoUpdateCurrentVersionWithSuffix = true) {
      this.currentVersion = $"{currentMajor}.{currentMinor}.{currentFix}";
      if (alsoUpdateCurrentVersionWithSuffix) {
        this.CurrentVersionAndPrereleaseSuffix2CurrentVersionWithSuffix();
      }
    }
    public void CurrentVersion2VersionPartFields() {
      ParseVersion(currentVersion, out int maj, out int min, out int fix, out string preReleaseSuffix);
      currentMajor = maj;
      currentMinor = min;
      currentFix = fix;
    }

    /// <summary>
    ///  increases currentMajor AND sets currentMinor+currentPatch to ZERO
    /// </summary>
    public void IncreaseCurrentMajor(bool minorIfPreMvp, bool alsoUpdateOtherConsolidatedFields = true) {
      if (minorIfPreMvp && currentMajor == 0) {
        currentMinor++;
        currentFix = 0;
      } else {
        currentMajor++;
        currentMinor = 0;
        currentFix = 0;
      }
      if (alsoUpdateOtherConsolidatedFields) {
        this.VersionPartFields2CurrentVersion(true);
      }
    }

    /// <summary>
    ///  increases currentMinor AND sets currentPatch to ZERO
    /// </summary>
    public void IncreaseCurrentMinor(bool alsoUpdateOtherConsolidatedFields = true) {
      currentMinor++;
      currentFix = 0;
      if (alsoUpdateOtherConsolidatedFields) {
        this.VersionPartFields2CurrentVersion(true);
      }
    }

    /// <summary>
    ///  increases currentPatch
    /// </summary>
    public void IncreaseCurrentFix(bool alsoUpdateOtherConsolidatedFields = true) {
      currentFix++;
      if (alsoUpdateOtherConsolidatedFields) {
        this.VersionPartFields2CurrentVersion(true);
      }
    }
    
    public void RecalculateChangeGradeBasedOnPreviousVersion() {
      ParseVersion(previousVersion, out int pMaj, out int pMin, out int pFix, out string pPreReleaseSuffix);
      ParseVersion(currentVersion, out int cMaj, out int cMin, out int cFix, out string cPreReleaseSuffix);
      if (cMaj > pMaj) {
        changeGrade = "major";
      } else if (cMin > pMin) {
        changeGrade = "minor";
      } else if (cFix > pFix) {
        changeGrade = "fix";
      } else {
        changeGrade = "initial";
      }
    }
    
    #region " Helpers "

    private static void ParseVersion(string version, out int major, out int minor, out int fix, out string preReleaseSuffix) {

      var versionPortion = version;

      preReleaseSuffix = "";

      int idx = version.IndexOf("-");

      if (idx != -1) {
        versionPortion = version.Substring(0, idx);
        preReleaseSuffix = version.Substring(idx + 1);
      }

      var parts = versionPortion.Split('.');
      if (Int32.TryParse(parts[0], out int result)) {
        major = result;
      } else {
        major = 0;
      }
      if (parts.Length > 1 && Int32.TryParse(parts[1], out int result2)) {
        minor = result2;
      } else {
        minor = 0;
      }
      if (parts.Length > 2 && Int32.TryParse(parts[2], out int result3)) {
        fix = result3;
      } else {
        fix = 0;
      }

    }

    #endregion

  }

}
