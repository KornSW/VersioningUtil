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
      string versionToWrite = versionInfo.currentVersionWithSuffix;

      versionToWrite = versionToWrite.Replace("*","0");

      Console.WriteLine($"Writing Version '{versionToWrite}' into '{_FileFullName}'");

      int matchCount = 0;
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearch, $"<version>{versionToWrite}</version>", ref matchCount
      );

      Console.WriteLine($"  Processed {matchCount} matches...");
      if (matchCount > 0) {
        FileIoHelper.WriteFile(_FileFullName, rawContent);
      }
    }

    public void WritePackageDependencies(DependencyInfo[] packageDependencies, bool addNew, bool updateExisiting, bool deleteOthers) {

      if (addNew) {
        throw new NotImplementedException("adding new dependencies is currenlty not supported");
      }
      if (deleteOthers) {
        throw new NotImplementedException("deleting dependencies is currenlty not supported");
      }

      if (updateExisiting) {

        string rawContent = File.ReadAllText(_FileFullName, Encoding.UTF8);

        bool fileChanged = false;
        foreach (var packageDependency in packageDependencies) {
          int matchCount = 0;
          rawContent = FileIoHelper.Replace(
            rawContent,
            $"<dependency id=\"{packageDependency.TargetPackageId}\" version=\"[a-zA-Z0-9.,)(\\[\\]\\-\\*]*\"",
            $"<dependency id=\"{packageDependency.TargetPackageId}\" version=\"{packageDependency.TargetPackageVersionConstraint}\"", ref matchCount
          );
          if (matchCount > 0) {
            Console.WriteLine($"  Processed ref version of {matchCount} matches for \"{packageDependency.TargetPackageId}\" to \"{packageDependency.TargetPackageVersionConstraint}\"");
            fileChanged = true;
          }
        }

        if (fileChanged) {
          FileIoHelper.WriteFile(_FileFullName, rawContent);
        }

      }

    }

    private string _RegexSearchDep = "<dependency id=\"[a-zA-Z0-9.\\-\\*]*\" version=\"[a-zA-Z0-9.,)(\\[\\]\\-\\*]*\"";
    public DependencyInfo[] ReadPackageDependencies() {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.UTF8);

      //TODO: support für targetFramwork - filter!!!

      return Regex.Matches(rawContent, _RegexSearchDep).Select((m) => {
        string[] slt = m.Value.Split('"');
        return new DependencyInfo(slt[1], slt[3]);
      }).ToArray();
    }

  }

}
