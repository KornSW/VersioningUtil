using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versioning {

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

}
