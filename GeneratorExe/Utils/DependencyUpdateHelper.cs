using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
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
    public bool FrameworkSpecificMatching { get; set; } = false;

    //public void WritePackageDependencies2(
    //  DependencyInfo[] packageDependencies,
    //  bool addNew, bool updateExisiting, bool deleteOthers,
    //  bool allowDowngrade, string onlyForTargetFramework
    //) {

    //  Func<IEnumerable<DependencyInfo>> filteredDependencyGetter = _DependencyGetter;
    //  Func<IEnumerable<DependencyInfo>> dependenciesToSkipGetter = ()=> Enumerable.Empty<DependencyInfo>();

    //  if (!string.IsNullOrWhiteSpace(onlyForTargetFramework)) {
    //    filteredDependencyGetter = ()=> _DependencyGetter().Where((d) => onlyForTargetFramework.Equals(d.DedicatedToTargetFramework));
    //    dependenciesToSkipGetter = () => _DependencyGetter().Where((d) => !onlyForTargetFramework.Equals(d.DedicatedToTargetFramework));

    //    packageDependencies = packageDependencies.Where(
    //      (d) => onlyForTargetFramework.Equals(d.DedicatedToTargetFramework) || 
    //      string.IsNullOrWhiteSpace(d.DedicatedToTargetFramework) 
    //    ).ToArray();

    //    //foreach (DependencyInfo dependency in packageDependencies) {
    //    //  //ensure framework match also for wildcards (to get them added exclusively for the target framework)
    //    //  dependency.DedicatedToTargetFramework = onlyForTargetFramework;
    //    //}
    //  }

    //  Dictionary<DependencyInfo,bool> depsToEnsure = new Dictionary<DependencyInfo, bool>(
    //     filteredDependencyGetter().Select((d) => new KeyValuePair<DependencyInfo, bool>(d, false))
    //  );

    //  const bool notOrphaned = true;

    //  foreach (DependencyInfo dependencyToWrite in packageDependencies) {


    //    bool foundAnyMatch = false;
    //    foreach(KeyValuePair<DependencyInfo, bool> match in depsToEnsure.Where(
    //      (kvp)=> kvp.Key.TargetPackageId == dependencyToWrite.TargetPackageId && (
    //        string.IsNullOrWhiteSpace(dependencyToWrite.DedicatedToTargetFramework) ||
    //        kvp.Key.DedicatedToTargetFramework == dependencyToWrite.DedicatedToTargetFramework
    //      )
    //    ).ToArray()) {

    //      foundAnyMatch = true;
    //      depsToEnsure[match.Key] = notOrphaned;
    //      if (updateExisiting) {
    //        match.Key.TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint;
    //      }
    //    }

    //    if (!foundAnyMatch && addNew) {
    //      if(!string.IsNullOrWhiteSpace(onlyForTargetFramework) && onlyForTargetFramework != dependencyToWrite.DedicatedToTargetFramework) {
    //        DependencyInfo clone = new DependencyInfo { 
    //          DedicatedToTargetFramework = onlyForTargetFramework,
    //          TargetPackageId = dependencyToWrite.TargetPackageId,
    //          TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint
    //        };
    //        depsToEnsure.Add(clone, notOrphaned);
    //      }
    //      else {
    //        depsToEnsure.Add(dependencyToWrite, notOrphaned);
    //      }      
    //    }

    //  }

    //  IEnumerable<DependencyInfo> entriesToPreserve;
    //  if (deleteOthers) {
    //    entriesToPreserve = depsToEnsure.Where((kvp) => kvp.Value == notOrphaned).Select((kvp) => kvp.Key);
    //  }
    //  else {
    //    entriesToPreserve = depsToEnsure.Select((kvp)=>kvp.Key);
    //  }

    //  IEnumerable<DependencyInfo> result = dependenciesToSkipGetter().Union(entriesToPreserve);

    //  if (!SkipSorting) {
    //    result = result.OrderBy((d) => d.DedicatedToTargetFramework);
    //  }
    //  _DependencySetter(result);

    //}

    /// <summary>
    /// Writes package dependencies according to the requested update mode.
    /// </summary>
    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew,
      bool updateExisiting,
      bool deleteOthers,
      bool allowDowngrade,
      string onlyForTargetFramework
    ) {
      if (packageDependencies == null) {
        packageDependencies = new DependencyInfo[0];
      }

      var originalColor = Console.ForegroundColor;

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
        Console.WriteLine($"Skipping {dependenciesToSkip.Length} dependencies on target belonging to other frameworks...");

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

      bool matchWhenEmptyFxInfoOnLeft = true;
      if (this.FrameworkSpecificMatching) {
        matchWhenEmptyFxInfoOnLeft = false;
      }

      foreach (DependencyInfo dependencyToWrite in packageDependencies) {
        Console.Write("   " + dependencyToWrite.ToString() + " -> ");

        DependencyInfo[] matches = result
          .Where((existingDependency) => {
            return this.IsMatchingDependency(existingDependency, dependencyToWrite, matchWhenEmptyFxInfoOnLeft, true);
          })
          .ToArray();

        if (matches.Length > 0) {
          foreach (DependencyInfo match in matches) {

            if (updateExisiting) {
              if (this.ShouldUpdateVersion(match, dependencyToWrite, allowDowngrade)) {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"UPDATED (from '{match.TargetPackageVersionConstraint.ToString(true)}' to '{dependencyToWrite.TargetPackageVersionConstraint.ToString(true)}')");
                Console.ForegroundColor = originalColor;
                match.TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint;
                changed = true;
              }
              else {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"skipped (existing version is {match.TargetPackageVersionConstraint.ToString(true)})");
                Console.ForegroundColor = originalColor;
              }
            }
            else {
              Console.ForegroundColor = ConsoleColor.Gray;
              Console.WriteLine("skipped (update-exisiting not requestd)");
              Console.ForegroundColor = originalColor;
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
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"ADDED NEW ('{dependencyToAdd.TargetPackageVersionConstraint.ToString(true)}')");
            Console.ForegroundColor = originalColor;
            changed = true;
          }
          else {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("skipped (add-new not requested)");
            Console.ForegroundColor = originalColor;

          }
        }
      }

      if (deleteOthers) {
        DependencyInfo[] filteredResult = result
          .Where((existingDependency) => {
            return packageDependencies.Any((dependencyToWrite) => {
              return this.IsMatchingDependency(existingDependency, dependencyToWrite, matchWhenEmptyFxInfoOnLeft, true);
            });
          })
          .ToArray();

        if (filteredResult.Length != result.Count) {
          changed = true;
          foreach (DependencyInfo deletedDependency in result.Except(filteredResult)) {
            Console.Write($"   {deletedDependency.ToString()} -> "); 
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"DELETED"); 
            Console.ForegroundColor = originalColor;
          }
        }
        else {
          Console.ForegroundColor = ConsoleColor.Gray;
          Console.WriteLine("   Deletion -> skipped (no items to delete)");
          Console.ForegroundColor = originalColor;

        }
        result = filteredResult.ToList();

      }
      else {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("   Any deletion -> skipped (not requested)");
        Console.ForegroundColor = originalColor;

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
    private bool IsMatchingDependency(
      DependencyInfo leftDependency, DependencyInfo rightDependency,
      bool matchWhenEmptyFxInfoOnLeft, bool matchWhenEmptyFxInfoOnRight
    ) {
      if (leftDependency == null) {
        return false;
      }

      if (rightDependency == null) {
        return false;
      }

      if (!string.Equals(
        leftDependency.TargetPackageId,
        rightDependency.TargetPackageId,
        StringComparison.OrdinalIgnoreCase
      )) {
        return false;
      }

      if (matchWhenEmptyFxInfoOnLeft && string.IsNullOrWhiteSpace(leftDependency.DedicatedToTargetFramework)) {
        return true;
      }
      if (matchWhenEmptyFxInfoOnRight && string.IsNullOrWhiteSpace(rightDependency.DedicatedToTargetFramework)) {
        return true;
      }

      return string.Equals(
        leftDependency.DedicatedToTargetFramework,
        rightDependency.DedicatedToTargetFramework,
        StringComparison.OrdinalIgnoreCase
      );
    }

    /// <summary>
    /// Determines whether two dependency entries use the same version constraint.
    /// </summary>
    private bool ShouldUpdateVersion(
      DependencyInfo existingDependency, DependencyInfo dependencyToWrite, bool allowDowngrade
    ) {

      if (existingDependency.TargetPackageVersionConstraint == null) {
        return dependencyToWrite.TargetPackageVersionConstraint != null;
      }

      if (dependencyToWrite.TargetPackageVersionConstraint == null) {
        return true;
      }

      string existingVersionString = existingDependency.TargetPackageVersionConstraint.ToString(cleanMinVersion: true);
      string incommingVersionString = dependencyToWrite.TargetPackageVersionConstraint.ToString(cleanMinVersion: true);
      int existingVersionPrereleaseIndex = existingVersionString.IndexOf('-');
      int incommingVersionPrereleaseIndex = incommingVersionString.IndexOf('-');
      Version existingVersion = Version.Parse(existingVersionPrereleaseIndex >= 0 ? existingVersionString.Substring(0, existingVersionPrereleaseIndex) : existingVersionString);
      Version incommingVersion = Version.Parse(incommingVersionPrereleaseIndex >= 0 ? incommingVersionString.Substring(0, incommingVersionPrereleaseIndex) : incommingVersionString); 

      if (existingVersion < incommingVersion) {
        return true; //REGULAR-HIGHER
      }
      if (existingVersion > incommingVersion) {
        return allowDowngrade; //REGULAR-LOWER
      }
      //REGULAR-EQUAL:
      if (existingVersionPrereleaseIndex < 0) {
        if (incommingVersionPrereleaseIndex < 0) {
          return false; //(none is prerelease) -> nothing to do
        }
        else {
          return allowDowngrade; //transition from non-prerelease to prerelease (=DOWNGRADE)
        }
      }
      else {
        if (incommingVersionPrereleaseIndex < 0) {
          return true; //transition from prerelease to non-prerelease (=UPGRADE)

        }
        else {
          return true; //(both are prelease) -> update should set the new one (sidewart)
        }
      }

    }

  }

}
