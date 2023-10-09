using System;
using System.IO;
using System.Text;
using Versioning;

namespace FileIO {

  public class FilePlaceholderResolver {

    private VersionInfo _VersionInfo;

    public FilePlaceholderResolver(VersionInfo source) {
      _VersionInfo = source;
    }

    public void ResolvePlaceholders(string fileFullName) {
      string rawContent = File.ReadAllText(fileFullName, Encoding.Default);

      //TODO: auf regex umstellen, damit dateien nur geschrieben werden müssen, wenn sich auch wa geändert hat...

      rawContent = rawContent.Replace(
        "{{currentVersion}}", _VersionInfo.currentVersion, StringComparison.InvariantCultureIgnoreCase
      );
      rawContent = rawContent.Replace(
        "{{currentVersionWithSuffix}}", _VersionInfo.currentVersionWithSuffix, StringComparison.InvariantCultureIgnoreCase
      );
      rawContent = rawContent.Replace(
        "{{versionDateInfo}}", _VersionInfo.versionDateInfo, StringComparison.InvariantCultureIgnoreCase
      );
      rawContent = rawContent.Replace(
        "{{versionTimeInfo}}", _VersionInfo.versionTimeInfo, StringComparison.InvariantCultureIgnoreCase
      );

      //TODO: escaping based on file-extension (html/md/...)
      rawContent = rawContent.Replace(
        "{{versionNotes}}", _VersionInfo.versionNotes, StringComparison.InvariantCultureIgnoreCase
      );

      using (FileStream fs = new FileStream(fileFullName, FileMode.Create, FileAccess.Write)) {
        using (StreamWriter sw = new StreamWriter(fs, Encoding.Default)) {
          sw.Write(rawContent);
          sw.Flush();
        }
      }

    }

  }

}
