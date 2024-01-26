using System;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Versioning;

namespace FileIO {

  public class PackagesConfigFileAccessor : IVersionContainer {

    private string _FileFullName;

    public PackagesConfigFileAccessor(string fileFullName) {
      _FileFullName = Path.GetFullPath(fileFullName);
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public VersionInfo ReadVersion() {
      //TODO: load VsProjFileAccessor here and redirect call...
      throw new NotImplementedException("A 'packages.config' file does not contain version-information for its owner\"");
    }  
    public void WriteVersion(VersionInfo versionInfo) {
      //TODO: load VsProjFileAccessor here and redirect call...
      throw new NotImplementedException("A 'packages.config' file does not contain version-information for its owner");
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
            $"<package id=\"{packageDependency.TargetPackageId}\" version=\"[a-zA-Z0-9.\\-\\*]*\"",
            $"<package id=\"{packageDependency.TargetPackageId}\" version=\"{packageDependency.TargetPackageVersionConstraint.ToString(true)}\"", ref matchCount
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

    private string _RegexSearch = "<package id=\"[a-zA-Z0-9.\\-\\*]*\" version=\"[a-zA-Z0-9.\\-\\*]*\"";
    public DependencyInfo[] ReadPackageDependencies() {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.UTF8);

      return Regex.Matches(rawContent, _RegexSearch).Select((m) => {
        string[] slt = m.Value.Split('"');
        return new DependencyInfo(slt[1], slt[3]);
      }).ToArray();
    }

    public static void CreateNewFile(string pkgCfgFileName) {
      File.WriteAllText(pkgCfgFileName, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<packages>\r\n</packages>\r\n", Encoding.UTF8);
      Thread.Sleep(700);
    }

  }

}
