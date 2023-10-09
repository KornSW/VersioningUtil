using System;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Versioning;

namespace FileIO {

  public class NuspecFileAccessor : IVersionContainer {

    private string _FileFullName;

    public NuspecFileAccessor(string fileFullName) {
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
        versionInfo.currentVersionWithSuffix = matchVers.Value.Substring(9, matchVers.Value.Length - 19);
      }

      versionInfo.CurrentVersionWithSuffix2CurrentVersionAndPrereleaseSuffx(true);

      Console.WriteLine($"Loaded Version '{versionInfo.currentVersionWithSuffix}' from '{_FileFullName}'");
      return versionInfo;
    }

    /*
     * <?xml version="1.0"?>
       <package>
         <metadata>
           <version>2.0.6-FF-FF</version> 
     */

    private string _RegexSearch = "<version>[a-zA-Z0-9.\\-\\*]*</version>";

    public void WriteVersion(VersionInfo versionInfo) {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      Console.WriteLine($"Writing Version '{versionInfo.currentVersionWithSuffix}' into '{_FileFullName}'");

      int matchCount = 0;
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearch, $"<version>{versionInfo.currentVersionWithSuffix}</version>", ref matchCount
      );

      Console.WriteLine($"Replaced {matchCount} matches...");
      if (matchCount > 0) {
        FileIoHelper.WriteFile(_FileFullName, rawContent);
      }
    }

  }

}
