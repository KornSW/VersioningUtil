using System;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Versioning;

namespace FileIO {

  public class CompiledAssembyFileAccessor : IVersionContainer {

    private string _FileFullName;

    public CompiledAssembyFileAccessor(string fileFullName) {
      _FileFullName = Path.GetFullPath(fileFullName);
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public VersionInfo ReadVersion() {
      var versionInfo = new VersionInfo();

      AssemblyName aName = AssemblyName.GetAssemblyName(_FileFullName);

      versionInfo.currentVersion = aName.Version.ToString(3);
      versionInfo.preReleaseSuffix = ""; //TODO: unknwon...

      versionInfo.CurrentVersion2VersionPartFields();
      versionInfo.CurrentVersionAndPrereleaseSuffix2CurrentVersionWithSuffix();

      Console.WriteLine($"Loaded Version '{versionInfo.currentVersionWithSuffix}' from '{_FileFullName}'");
      return versionInfo;

    }

    public void WriteVersion(VersionInfo versionInfo) {
      throw new InvalidOperationException("This tool cannot write matadata into a compiled assembly!");  
    }

    public void WritePackageDependencies(DependencyInfo[] readPackageDependencies, bool addNew, bool updateExisiting, bool deleteOthers) {
    }

    public DependencyInfo[] ReadPackageDependencies() {
      return new DependencyInfo[] { };
    }

  }

}
