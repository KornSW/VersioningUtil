using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Versioning;

namespace FileIO {

  public class AssemblyInfoFileAccessor : IVersionContainer {

    private string _FileFullName;

    public AssemblyInfoFileAccessor(string fileFullName) {
      _FileFullName = fileFullName;
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public VersionInfo ReadVersion() {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      var versionInfo = new VersionInfo();

      var matchVers = Regex.Matches(rawContent, _RegexSearchVers).FirstOrDefault();
      if (matchVers != null) {
        versionInfo.currentVersion = matchVers.Value.Substring(26, matchVers.Value.Length - 28);
      }

      var matchInfoVers = Regex.Matches(rawContent, _RegexSearchInfoVers).FirstOrDefault();
      if (matchInfoVers != null) {
        versionInfo.currentVersionWithSuffix = matchInfoVers.Value.Substring(39, matchInfoVers.Value.Length - 41);
      }

      if (string.IsNullOrWhiteSpace(versionInfo.currentVersionWithSuffix)) {
        versionInfo.CurrentVersionAndPrereleaseSuffix2CurrentVersionWithSuffix();
      }
      else if (matchVers == null) {
        versionInfo.CurrentVersionWithSuffix2CurrentVersionAndPrereleaseSuffx();
      }

      versionInfo.CurrentVersion2VersionPartFields();

      Console.WriteLine($"Loaded Version '{versionInfo.currentVersionWithSuffix}' from '{_FileFullName}'");
      return versionInfo;
    }

    //CS
    //[assembly: AssemblyVersion("1.0.0.*")]
    //[assembly: AssemblyInformationalVersion("1.0.0-localbuild")]
    ////DONT ADD: [assembly: AssemblyFileVersion] !!!

    //VB
    //<Assembly: AssemblyInformationalVersion("23.913.*")> 

    private string _RegexSearchVers = "ssembly: AssemblyVersion\\(\"[a-zA-Z0-9.\\-\\*]*\"\\)";
    private string _RegexSearchInfoVers = "ssembly: AssemblyInformationalVersion\\(\"[a-zA-Z0-9.\\-\\*]*\"\\)";
    private string _RegexSearchFileVers = "ssembly: AssemblyFileVersion[0-9\\.\"\\(\\)]*";

    public void WriteVersion(VersionInfo versionInfo) {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      Console.WriteLine($"Writing Version '{versionInfo.currentVersionWithSuffix}' into '{_FileFullName}'");

      int matchCount = 0;
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearchVers, $"ssembly: AssemblyVersion(\"{versionInfo.currentVersion}\")", ref matchCount
      );
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearchInfoVers, $"ssembly: AssemblyInformationalVersion(\"{versionInfo.currentVersionWithSuffix}\")", ref matchCount
      );
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearchFileVers, $"ssembly: AssemblyFileVersion(\"{versionInfo.currentVersion}\")", ref matchCount
      );

      Console.WriteLine($"Replaced {matchCount} matches...");
      if (matchCount > 0) {
        FileIoHelper.WriteFile(_FileFullName, rawContent);
      }
    }

  }

}
