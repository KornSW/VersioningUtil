using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Versioning;

namespace FileIO {

  public class VsProjFileAccessor : IVersionContainer {

    private string _FileFullName;

    public VsProjFileAccessor(string fileFullName) {
      _FileFullName = fileFullName;
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public VersionInfo ReadVersion() {
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

      Console.WriteLine($"Replaced {matchCount} matches...");
      if (matchCount > 0) {
        FileIoHelper.WriteFile(_FileFullName, rawContent);
      }
    }

  }

}
