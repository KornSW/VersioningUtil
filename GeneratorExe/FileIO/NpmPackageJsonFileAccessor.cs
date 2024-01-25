using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Versioning;

namespace FileIO {

  public class NpmPackageJsonFileAccessor : IVersionContainer {

    private string _FileFullName;

    public NpmPackageJsonFileAccessor(string fileFullName) {
      _FileFullName = Path.GetFullPath(fileFullName);
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public VersionInfo ReadVersion() {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      var versionInfo = new VersionInfo();

      var matchVers = Regex.Matches(rawContent, _RegexSearch).FirstOrDefault();
      if (matchVers != null) {
        versionInfo.currentVersionWithSuffix = matchVers.Value.Substring(12, matchVers.Value.Length - 13);
      }

      versionInfo.CurrentVersionWithSuffix2CurrentVersionAndPrereleaseSuffx(true);

      Console.WriteLine($"Loaded Version '{versionInfo.currentVersionWithSuffix}' from '{_FileFullName}'");
      return versionInfo;
    }

  //  {
  //     "version": "1.0.0",

    private string _RegexSearch = "\"version\": \"[a-zA-Z0-9.\\-\\*]*\"";

    public void WriteVersion(VersionInfo versionInfo) {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      Console.WriteLine($"Writing Version '{versionInfo.currentVersionWithSuffix}' into '{_FileFullName}'");

      int matchCount = 0;
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearch, $"\"version\": \"{versionInfo.currentVersionWithSuffix}\"", ref matchCount
      );

      Console.WriteLine($"Replaced {matchCount} matches...");
      if (matchCount > 0) {
        FileIoHelper.WriteFile(_FileFullName, rawContent);
      }
    }

    public void WritePackageDependencies(DependencyInfo[] readPackageDependencies, bool addNew, bool updateExisiting, bool deleteOthers) {
    }

    public DependencyInfo[] ReadPackageDependencies() {
      return new DependencyInfo[] { };
    }

  }

}
