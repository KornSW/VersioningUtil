using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Versioning;

namespace Utils {

  public class DependencyUpdateHelper {

    private readonly Func<IEnumerable<DependencyInfo>> _DependencyGetter;
    private readonly Action<IEnumerable<DependencyInfo>> _DependencySetter;

    public DependencyUpdateHelper(
      Func<IEnumerable<DependencyInfo>>dependencyGetter,
      Action<IEnumerable<DependencyInfo>> dependencySetter
    ) {
      _DependencyGetter = dependencyGetter;
      _DependencySetter = dependencySetter;
    }

    public bool SkipSorting { get; set; } = false;

    public void WritePackageDependencies2(
      DependencyInfo[] packageDependencies,
      bool addNew, bool updateExisiting, bool deleteOthers,
      string onlyForTargetFramework
    ) {

      Func<IEnumerable<DependencyInfo>> filteredDependencyGetter = _DependencyGetter;
      Func<IEnumerable<DependencyInfo>> dependenciesToSkipGetter = ()=> Enumerable.Empty<DependencyInfo>();

      if (!string.IsNullOrWhiteSpace(onlyForTargetFramework)) {
        filteredDependencyGetter = ()=> _DependencyGetter().Where((d) => onlyForTargetFramework.Equals(d.DedicatedToTargetFramework));
        dependenciesToSkipGetter = () => _DependencyGetter().Where((d) => !onlyForTargetFramework.Equals(d.DedicatedToTargetFramework));

        packageDependencies = packageDependencies.Where(
          (d) => onlyForTargetFramework.Equals(d.DedicatedToTargetFramework) || 
          string.IsNullOrWhiteSpace(d.DedicatedToTargetFramework) 
        ).ToArray();

        //foreach (DependencyInfo dependency in packageDependencies) {
        //  //ensure framework match also for wildcards (to get them added exclusively for the target framework)
        //  dependency.DedicatedToTargetFramework = onlyForTargetFramework;
        //}
      }

      Dictionary<DependencyInfo,bool> depsToEnsure = new Dictionary<DependencyInfo, bool>(
         filteredDependencyGetter().Select((d) => new KeyValuePair<DependencyInfo, bool>(d, false))
      );

      const bool notOrphaned = true;

      foreach (DependencyInfo dependencyToWrite in packageDependencies) {


        bool foundAnyMatch = false;
        foreach(KeyValuePair<DependencyInfo, bool> match in depsToEnsure.Where(
          (kvp)=> kvp.Key.TargetPackageId == dependencyToWrite.TargetPackageId && (
            string.IsNullOrWhiteSpace(dependencyToWrite.DedicatedToTargetFramework) ||
            kvp.Key.DedicatedToTargetFramework == dependencyToWrite.DedicatedToTargetFramework
          )
        ).ToArray()) {

          foundAnyMatch = true;
          depsToEnsure[match.Key] = notOrphaned;
          if (updateExisiting) {
            match.Key.TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint;
          }
        }

        if (!foundAnyMatch && addNew) {
          if(!string.IsNullOrWhiteSpace(onlyForTargetFramework) && onlyForTargetFramework != dependencyToWrite.DedicatedToTargetFramework) {
            DependencyInfo clone = new DependencyInfo { 
              DedicatedToTargetFramework = onlyForTargetFramework,
              TargetPackageId = dependencyToWrite.TargetPackageId,
              TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint
            };
            depsToEnsure.Add(clone, notOrphaned);
          }
          else {
            depsToEnsure.Add(dependencyToWrite, notOrphaned);
          }      
        }

      }

      IEnumerable<DependencyInfo> entriesToPreserve;
      if (deleteOthers) {
        entriesToPreserve = depsToEnsure.Where((kvp) => kvp.Value == notOrphaned).Select((kvp) => kvp.Key);
      }
      else {
        entriesToPreserve = depsToEnsure.Select((kvp)=>kvp.Key);
      }

      IEnumerable<DependencyInfo> result = dependenciesToSkipGetter().Union(entriesToPreserve);

      if (!SkipSorting) {
        result = result.OrderBy((d) => d.DedicatedToTargetFramework);
      }
      _DependencySetter(result);

    }





    /// <summary>
    /// Writes package dependencies according to the requested update mode.
    /// </summary>
    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew,
      bool updateExisiting,
      bool deleteOthers,
      string onlyForTargetFramework
    ) {
      if (packageDependencies == null) {
        packageDependencies = new DependencyInfo[0];
      }

      DependencyInfo[] existingDependencies = _DependencyGetter()
        .Where((dependency) => dependency != null)
        .ToArray();

      DependencyInfo[] dependenciesToModify = existingDependencies;
      DependencyInfo[] dependenciesToSkip = new DependencyInfo[0];

      if (!string.IsNullOrWhiteSpace(onlyForTargetFramework)) {
        dependenciesToModify = existingDependencies
          .Where((dependency) => {
            return string.Equals(
              dependency.DedicatedToTargetFramework,
              onlyForTargetFramework,
              StringComparison.OrdinalIgnoreCase
            );
          })
          .ToArray();

        dependenciesToSkip = existingDependencies
          .Where((dependency) => {
            return !string.Equals(
              dependency.DedicatedToTargetFramework,
              onlyForTargetFramework,
              StringComparison.OrdinalIgnoreCase
            );
          })
          .ToArray();

        packageDependencies = packageDependencies
          .Where((dependency) => {
            if (dependency == null) {
              return false;
            }

            if (string.IsNullOrWhiteSpace(dependency.DedicatedToTargetFramework)) {
              return true;
            }

            return string.Equals(
              dependency.DedicatedToTargetFramework,
              onlyForTargetFramework,
              StringComparison.OrdinalIgnoreCase
            );
          })
          .ToArray();
      }

      List<DependencyInfo> result = dependenciesToModify.ToList();
      bool changed = false;

      foreach (DependencyInfo dependencyToWrite in packageDependencies) {
        DependencyInfo[] matches = result
          .Where((existingDependency) => {
            return this.IsMatchingDependency(existingDependency, dependencyToWrite);
          })
          .ToArray();

        if (matches.Length > 0) {
          foreach (DependencyInfo match in matches) {
            if (updateExisiting) {
              if (!this.HasSameVersion(match, dependencyToWrite)) {
                match.TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint;
                changed = true;
              }
            }
          }
        }
        else {
          if (addNew) {
            DependencyInfo dependencyToAdd = dependencyToWrite;

            if (!string.IsNullOrWhiteSpace(onlyForTargetFramework)) {
              if (!string.Equals(
                dependencyToWrite.DedicatedToTargetFramework,
                onlyForTargetFramework,
                StringComparison.OrdinalIgnoreCase
              )) {
                dependencyToAdd = new DependencyInfo {
                  DedicatedToTargetFramework = onlyForTargetFramework,
                  TargetPackageId = dependencyToWrite.TargetPackageId,
                  TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint
                };
              }
            }

            result.Add(dependencyToAdd);
            changed = true;
          }
        }
      }

      if (deleteOthers) {
        DependencyInfo[] filteredResult = result
          .Where((existingDependency) => {
            return packageDependencies.Any((dependencyToWrite) => {
              return this.IsMatchingDependency(existingDependency, dependencyToWrite);
            });
          })
          .ToArray();

        if (filteredResult.Length != result.Count) {
          changed = true;
        }

        result = filteredResult.ToList();
      }

      if (!changed) {
        return;
      }

      DependencyInfo[] finalResult = dependenciesToSkip
        .Concat(result)
        .ToArray();

      if (!SkipSorting) {
        finalResult = finalResult
          .OrderBy((dependency) => dependency.DedicatedToTargetFramework)
          .ThenBy((dependency) => dependency.TargetPackageId)
          .ToArray();
      }

      _DependencySetter(finalResult);
    }


    /// <summary>
    /// Determines whether two dependency entries describe the same package scope.
    /// </summary>
    private bool IsMatchingDependency(DependencyInfo existingDependency, DependencyInfo dependencyToWrite) {
      if (existingDependency == null) {
        return false;
      }

      if (dependencyToWrite == null) {
        return false;
      }

      if (!string.Equals(
        existingDependency.TargetPackageId,
        dependencyToWrite.TargetPackageId,
        StringComparison.OrdinalIgnoreCase
      )) {
        return false;
      }

      if (string.IsNullOrWhiteSpace(dependencyToWrite.DedicatedToTargetFramework)) {
        return true;
      }

      return string.Equals(
        existingDependency.DedicatedToTargetFramework,
        dependencyToWrite.DedicatedToTargetFramework,
        StringComparison.OrdinalIgnoreCase
      );
    }

    /// <summary>
    /// Determines whether two dependency entries use the same version constraint.
    /// </summary>
    private bool HasSameVersion(DependencyInfo existingDependency, DependencyInfo dependencyToWrite) {
      if (existingDependency.TargetPackageVersionConstraint == null) {
        return dependencyToWrite.TargetPackageVersionConstraint == null;
      }

      if (dependencyToWrite.TargetPackageVersionConstraint == null) {
        return false;
      }

      return string.Equals(
        existingDependency.TargetPackageVersionConstraint.ToString(),
        dependencyToWrite.TargetPackageVersionConstraint.ToString(),
        StringComparison.Ordinal
      );
    }
  }

}
