using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Utils;
using Versioning;

namespace FileIO {

  public class InMemoryContainer : IVersionContainer {

    private VersionInfo _CurrentVersion = new VersionInfo();
    private DependencyInfo[] _CurrentDependencies = Array.Empty<DependencyInfo>();

    public InMemoryContainer() {
    }

    public VersionInfo ReadVersion() {
      return _CurrentVersion;
    }

    public void WriteVersion(VersionInfo versionInfo) {
      _CurrentVersion = versionInfo;
    }

    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew, bool updateExisiting, bool deleteOthers,
      bool allowDowngrade, string onlyForTargetFramework
    ) {

      DependencyUpdateHelper updateHelper = new DependencyUpdateHelper(
         ()=> _CurrentDependencies, (deps) => _CurrentDependencies = deps.ToArray()
      );

      updateHelper.WritePackageDependencies(
        packageDependencies, addNew, updateExisiting, deleteOthers, allowDowngrade, onlyForTargetFramework
      );

    }

    public DependencyInfo[] ReadPackageDependencies(bool includeFrameworkInfo) {
      return _CurrentDependencies.ToArray();
    }

  }

}
