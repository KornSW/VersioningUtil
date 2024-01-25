using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Versioning;

namespace FileIO {

  public class VsProjFileAccessor : IVersionContainer {

    private string _FileFullName;

    public VsProjFileAccessor(string fileFullName) {
      _FileFullName = Path.GetFullPath(fileFullName);
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

    public bool IsDotNetCoreFormat() {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      bool isDotNetLegacy = rawContent.Contains("<TargetFrameworkVersion>");
      return !isDotNetLegacy;
    }

    private IVersionContainer GetContainerOfDependencies(bool createExternaFiles) {
      if (this.IsDotNetCoreFormat()) {
        return this;
      }
      else {
        string pkgCfgFileName = Path.Combine(Path.GetDirectoryName(_FileFullName), "packages.config");
        if (!File.Exists(pkgCfgFileName)) {
          if (!createExternaFiles) {
            return new InMemoryContainer();
          }
          PackagesConfigFileAccessor.CreateNewFile(pkgCfgFileName);
        }
        return new PackagesConfigFileAccessor(pkgCfgFileName);
      }
    }

    public void WritePackageDependencies(DependencyInfo[] packageDependencies, bool addNew, bool updateExisiting, bool deleteOthers) {
      IVersionContainer target = this.GetContainerOfDependencies(true);
      if(target != this) {
        target.WritePackageDependencies(packageDependencies, addNew, updateExisiting, deleteOthers);
        return;
      }

      if(addNew) {
        throw new NotImplementedException("adding new dependencies is currenlty not supported");
      }
      if (deleteOthers) {
        throw new NotImplementedException("deleting dependencies is currenlty not supported");
      }

      if (updateExisiting) {

        string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);

        bool fileChanged = false; 
        foreach (var packageDependency in packageDependencies) {
          int matchCount = 0;
          rawContent = FileIoHelper.Replace(
            rawContent,
            $"<PackageReference Include=\"{packageDependency.TargetPackageId}\" Version=\"[a-zA-Z0-9.\\-\\*]*\"",
            $"<PackageReference Include=\"{packageDependency.TargetPackageId}\" Version=\"{packageDependency.TargetPackageVersionConstraint}\"", ref matchCount
          );
          if(matchCount > 0) {
            Console.WriteLine($"Replaced ref version of {matchCount} matches for \"{packageDependency.TargetPackageId}\" to \"{packageDependency.TargetPackageVersionConstraint}\"");
            fileChanged = true;
          } 
        }

        if (fileChanged) {
          FileIoHelper.WriteFile(_FileFullName, rawContent);
        }

      }

    }

    private string _RegexSearch = "<PackageReference Include=\"[a-zA-Z0-9.\\-\\*]*\" Version=\"[a-zA-Z0-9.\\-\\*]*\"";
    public DependencyInfo[] ReadPackageDependencies() {
      IVersionContainer target = this.GetContainerOfDependencies(true);
      if (target != this) {
        return target.ReadPackageDependencies();
      }

      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);

      return Regex.Matches(rawContent, _RegexSearch).Select((m) => {
        string[] slt = m.Value.Split('"');
        return new DependencyInfo(slt[1], slt[3]);
      }).ToArray();

    }

  }

}
