using System.Collections.Generic;
using System.Linq;
using Versioning;

namespace FileIO {

  public class InMemoryContainer : IVersionContainer {

    private VersionInfo _CurrentVersion = new VersionInfo();

    private Dictionary<string, string> _CurrentDependencies = new Dictionary<string, string>();

    public InMemoryContainer() {
    }

    public VersionInfo ReadVersion() {
      return _CurrentVersion;
    }

    public void WriteVersion(VersionInfo versionInfo) {
      _CurrentVersion = versionInfo;
    }

    public void WritePackageDependencies(DependencyInfo[] packageDependencies, bool addNew, bool updateExisiting, bool deleteOthers) {
      List<string> othersToDelete;
      if (deleteOthers) {
        othersToDelete = _CurrentDependencies.Keys.ToList();
      } else {
        othersToDelete = new List<string>();
      }
      foreach (DependencyInfo dependencyToWrite in packageDependencies) {
        if (othersToDelete.Contains(dependencyToWrite.TargetPackageId)) {
          othersToDelete.Remove(dependencyToWrite.TargetPackageId);
        }
        if (_CurrentDependencies.ContainsKey(dependencyToWrite.TargetPackageId)) {
          if (updateExisiting) {
            _CurrentDependencies[dependencyToWrite.TargetPackageId] = dependencyToWrite.TargetPackageId;
          }
        } else {
          if (addNew) {
            _CurrentDependencies.Add(dependencyToWrite.TargetPackageId, dependencyToWrite.TargetPackageId);
          }
        }
      }
      foreach (string packageId in othersToDelete) {
        _CurrentDependencies.Remove(packageId);
      }
    }

    public DependencyInfo[] ReadPackageDependencies() {
      return _CurrentDependencies.Select((kvp) => new DependencyInfo {
        TargetPackageId = kvp.Key,
        TargetPackageVersionConstraint = new VersionContraint(kvp.Value)
      }).ToArray();
    }

  }

}
