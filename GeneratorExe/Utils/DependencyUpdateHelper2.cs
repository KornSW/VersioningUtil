using FileIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Versioning;

namespace Utils {

  /// <summary>
  /// Synchronizes dependency entries while resolving dependency scopes from the incoming DependencyInfo array.
  /// The source side is inferred from the provided dependencies.
  /// The target side is described by the target container capabilities.
  /// </summary>
  public class DependencyUpdateHelper2 {

    private readonly Func<IEnumerable<DependencyInfo>> _DependencyGetter;
    private readonly Action<IEnumerable<DependencyInfo>> _DependencySetter;
    private readonly IDependencyScopeCapabilities _TargetScopeCapabilities;

    public DependencyUpdateHelper2(
      IDependencyScopeCapabilities targetScopeCapabilities,
      Func<IEnumerable<DependencyInfo>> dependencyGetter,
      Action<IEnumerable<DependencyInfo>> dependencySetter
    ) {
      _DependencyGetter = dependencyGetter;
      _DependencySetter = dependencySetter;
      _TargetScopeCapabilities = targetScopeCapabilities;
    }

    public bool SkipSorting { get; set; } = false;

    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew,
      bool updateExisiting,
      bool deleteOthers,
      bool allowDowngrade,
      string targetFrameworkMode
    ) {
      WritePackageDependencies(
        packageDependencies, 
        addNew, updateExisiting,
        deleteOthers, allowDowngrade,
        targetFrameworkMode,
        new string[] { "*" },
        new string[] { }
      );
    }

    /// <summary>
    /// Writes package dependencies according to the requested update mode.
    /// </summary>
    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew,
      bool updateExisiting,
      bool deleteOthers,
      bool allowDowngrade,
      string targetFrameworkMode,
      string[] packageIdWhitelist,
      string[] packageIdBlacklist
    ) {
      if (packageDependencies == null) {
        packageDependencies = new DependencyInfo[0];
      }

      DependencyInfo[] resolvedDependencies = this.ResolveDependencyScopes(
        packageDependencies,
        targetFrameworkMode
      );

      DependencyInfo[] existingDependencies = _DependencyGetter()
        .Where((dependency) => {
          return dependency != null;
        })
        .ToArray();

      string[] affectedScopes = resolvedDependencies
        .Select((dependency) => {
          return dependency.DedicatedToTargetFramework;
        })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

      DependencyInfo[] dependenciesToModify = existingDependencies
        .Where((dependency) => {
          return affectedScopes.Contains(
            dependency.DedicatedToTargetFramework,
            StringComparer.OrdinalIgnoreCase
          );
        })
        .ToArray();

      DependencyInfo[] dependenciesToSkip = existingDependencies
        .Where((dependency) => {
          return !affectedScopes.Contains(
            dependency.DedicatedToTargetFramework,
            StringComparer.OrdinalIgnoreCase
          );
        })
        .ToArray();

      DependencyInfo[] protectedDependencies = dependenciesToModify
        .Where((dependency) => {
          return !this.IsPackageAllowed(
            dependency.TargetPackageId,
            packageIdWhitelist,
            packageIdBlacklist
          );
        })
        .ToArray();

      DependencyInfo[] modifiableDependencies = dependenciesToModify
        .Where((dependency) => {
          return this.IsPackageAllowed(
            dependency.TargetPackageId,
            packageIdWhitelist,
            packageIdBlacklist
          );
        })
        .ToArray();

      List<DependencyInfo> result = modifiableDependencies.ToList();
      bool changed = false;
      ConsoleColor originalColor = Console.ForegroundColor;

      Console.WriteLine($"   Dependencies in writable scope(s): {dependenciesToModify.Length}");
      Console.WriteLine($"   Dependencies preserved outside writable scope(s): {dependenciesToSkip.Length}");

      foreach (DependencyInfo dependencyToWrite in resolvedDependencies) {

        if (!this.IsPackageAllowed(
          dependencyToWrite.TargetPackageId,
          packageIdWhitelist,
          packageIdBlacklist
        )) {
          continue;
        }

        Console.Write("   " + dependencyToWrite.ToString() + " -> ");

        DependencyInfo[] matches = result
          .Where((existingDependency) => {
            return this.IsSameDependencyScope(existingDependency, dependencyToWrite);
          })
          .ToArray();

        if (matches.Length == 0) {
          if (addNew) {
            result.Add(dependencyToWrite);
            changed = true;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"ADDED NEW ('{dependencyToWrite.TargetPackageVersionConstraint.ToString(true)}')");
            Console.ForegroundColor = originalColor;
          }
          else {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("skipped (add-new not requested)");
            Console.ForegroundColor = originalColor;
          }

          continue;
        }

        foreach (DependencyInfo match in matches) {
          if (!updateExisiting) {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("skipped (update-existing not requested)");
            Console.ForegroundColor = originalColor;
            continue;
          }

          if (this.ShouldUpdateVersion(match, dependencyToWrite, allowDowngrade)) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"UPDATED (from '{match.TargetPackageVersionConstraint}' to '{dependencyToWrite.TargetPackageVersionConstraint}')");
            Console.ForegroundColor = originalColor;

            match.TargetPackageVersionConstraint = dependencyToWrite.TargetPackageVersionConstraint;
            changed = true;
          }
          else {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"skipped (existing version is {match.TargetPackageVersionConstraint})");
            Console.ForegroundColor = originalColor;
          }
        }
      }

      if (deleteOthers) {
        DependencyInfo[] filteredResult = result
          .Where((existingDependency) => {
            return resolvedDependencies.Any((dependencyToWrite) => {
              return this.IsSameDependencyScope(existingDependency, dependencyToWrite);
            });
          })
          .ToArray();

        if (filteredResult.Length != result.Count) {
          DependencyInfo[] deletedDependencies = result
            .Except(filteredResult)
            .ToArray();

          foreach (DependencyInfo deletedDependency in deletedDependencies) {
            Console.Write("   " + deletedDependency.ToString() + " -> ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("DELETED");
            Console.ForegroundColor = originalColor;
          }

          result = filteredResult.ToList();
          changed = true;
        }
        else {
          Console.ForegroundColor = ConsoleColor.Gray;
          Console.WriteLine("   Deletion -> skipped (no items to delete)");
          Console.ForegroundColor = originalColor;
        }
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
        .Concat(protectedDependencies)
        .Concat(result)
        .ToArray();

      if (!SkipSorting) {
        finalResult = finalResult
          .OrderBy((dependency) => {
            return dependency.DedicatedToTargetFramework;
          })
          .ThenBy((dependency) => {
            return dependency.TargetPackageId;
          })
          .ToArray();
      }

      _DependencySetter(finalResult);
    }

    /// <summary>
    /// Resolves all incoming source dependencies into their target scopes.
    /// </summary>
    private DependencyInfo[] ResolveDependencyScopes(
      DependencyInfo[] packageDependencies,
      string targetFrameworkMode
    ) {
      bool targetUsesScopes = false;
      string[] targetScopes = new string[0];

      if (_TargetScopeCapabilities != null) {
        targetUsesScopes = _TargetScopeCapabilities.UsesDependencyScopes();
        targetScopes = _TargetScopeCapabilities.GetDependencyScopes();

        if (targetScopes == null) {
          targetScopes = new string[0];
        }
      }

      bool sourceUsesScopes = packageDependencies.Any((dependency) => {
        return dependency != null &&
          !string.IsNullOrWhiteSpace(dependency.DedicatedToTargetFramework);
      });

      List<DependencyInfo> resolvedDependencies = new List<DependencyInfo>();

      foreach (DependencyInfo dependency in packageDependencies) {
        if (dependency == null) {
          continue;
        }

        string sourceScope = dependency.DedicatedToTargetFramework;

        string[] resolvedTargetScopes = this.ResolveTargetScopes(
          sourceScope,
          sourceUsesScopes,
          targetUsesScopes,
          targetScopes,
          targetFrameworkMode
        );

        foreach (string resolvedTargetScope in resolvedTargetScopes) {
          DependencyInfo resolvedDependency = new DependencyInfo {
            TargetPackageId = dependency.TargetPackageId,
            TargetPackageVersionConstraint = dependency.TargetPackageVersionConstraint,
            DedicatedToTargetFramework = resolvedTargetScope
          };

          resolvedDependencies.Add(resolvedDependency);
        }
      }

      return resolvedDependencies
        .GroupBy((dependency) => {
          return this.CreateDependencyKey(dependency);
        }, StringComparer.OrdinalIgnoreCase)
        .Select((group) => {
          return group.Last();
        })
        .ToArray();
    }

    /// <summary>
    /// Resolves the target scopes for one dependency.
    /// </summary>
    private string[] ResolveTargetScopes(
      string sourceScope,
      bool sourceUsesScopes,
      bool targetUsesScopes,
      string[] targetScopes,
      string targetFrameworkMode
    ) {
      string mode = targetFrameworkMode;

      if (mode != null) {
        mode = mode.Trim();
      }

      bool sourceHasScope = !string.IsNullOrWhiteSpace(sourceScope);

      if (!targetUsesScopes) {
        if (string.Equals(mode, "=", StringComparison.OrdinalIgnoreCase) && sourceUsesScopes) {
          throw new InvalidOperationException(
            "Source-scope-preserving dependency output was requested, but the target does not use dependency scopes."
          );
        }

        return new string[] {
          null
        };
      }

      if (string.IsNullOrWhiteSpace(mode)) {
        if (sourceUsesScopes) {
          if (!sourceHasScope) {
            throw new InvalidOperationException(
              "Automatic dependency scope resolution detected scoped source dependencies, but one dependency has no source scope."
            );
          }

          return new string[] {
            sourceScope
          };
        }

        return targetScopes;
      }

      if (string.Equals(mode, "*", StringComparison.OrdinalIgnoreCase)) {
        if (sourceUsesScopes) {
          if (!sourceHasScope) {
            throw new InvalidOperationException(
              "All-target dependency scope resolution was interpreted as source-scope-preserving, but one dependency has no source scope."
            );
          }

          return new string[] {
            sourceScope
          };
        }

        return targetScopes;
      }

      if (string.Equals(mode, "=", StringComparison.OrdinalIgnoreCase)) {
        if (sourceUsesScopes) {
          if (!sourceHasScope) {
            throw new InvalidOperationException(
              "Source-scope-preserving dependency output was requested, but one dependency has no source scope."
            );
          }

          return new string[] {
            sourceScope
          };
        }

        return targetScopes;
      }

      if (!targetScopes.Contains(mode, StringComparer.OrdinalIgnoreCase)) {
        throw new InvalidOperationException(
          "A concrete dependency scope was requested, but the target does not contain this dependency scope: " + mode
        );
      }

      return new string[] {
        mode
      };
    }

    /// <summary>
    /// Determines whether two dependency entries represent the same package inside the same dependency scope.
    /// </summary>
    private bool IsSameDependencyScope(
      DependencyInfo leftDependency,
      DependencyInfo rightDependency
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

      return string.Equals(
        leftDependency.DedicatedToTargetFramework,
        rightDependency.DedicatedToTargetFramework,
        StringComparison.OrdinalIgnoreCase
      );
    }

    /// <summary>
    /// Determines whether the target dependency version constraint should be replaced.
    /// </summary>
    private bool ShouldUpdateVersion(
      DependencyInfo existingDependency,
      DependencyInfo dependencyToWrite,
      bool allowDowngrade
    ) {
      if (existingDependency.TargetPackageVersionConstraint == null) {
        return dependencyToWrite.TargetPackageVersionConstraint != null;
      }

      if (dependencyToWrite.TargetPackageVersionConstraint == null) {
        return false;
      }

      string existingConstraint = existingDependency.TargetPackageVersionConstraint.ToString();
      string incomingConstraint = dependencyToWrite.TargetPackageVersionConstraint.ToString();

      if (string.Equals(existingConstraint, incomingConstraint, StringComparison.Ordinal)) {
        return false;
      }

      string existingVersionString = existingDependency.TargetPackageVersionConstraint.ToString(cleanMinVersion: true);
      string incomingVersionString = dependencyToWrite.TargetPackageVersionConstraint.ToString(cleanMinVersion: true);

      int existingVersionPrereleaseIndex = existingVersionString.IndexOf('-');
      int incomingVersionPrereleaseIndex = incomingVersionString.IndexOf('-');

      string existingComparableVersion = existingVersionString;
      string incomingComparableVersion = incomingVersionString;

      if (existingVersionPrereleaseIndex >= 0) {
        existingComparableVersion = existingVersionString.Substring(0, existingVersionPrereleaseIndex);
      }

      if (incomingVersionPrereleaseIndex >= 0) {
        incomingComparableVersion = incomingVersionString.Substring(0, incomingVersionPrereleaseIndex);
      }

      Version existingVersion;
      Version incomingVersion;

      if (!Version.TryParse(existingComparableVersion, out existingVersion)) {
        return true;
      }

      if (!Version.TryParse(incomingComparableVersion, out incomingVersion)) {
        return true;
      }

      if (existingVersion < incomingVersion) {
        return true;
      }

      if (existingVersion > incomingVersion) {
        return allowDowngrade;
      }

      if (existingVersionPrereleaseIndex < 0 && incomingVersionPrereleaseIndex >= 0) {
        return allowDowngrade;
      }

      return true;
    }

    /// <summary>
    /// Creates a stable comparison key from package id and dependency scope.
    /// </summary>
    private string CreateDependencyKey(DependencyInfo dependency) {
      string packageId = dependency.TargetPackageId;

      if (packageId == null) {
        packageId = string.Empty;
      }

      string scope = dependency.DedicatedToTargetFramework;

      if (scope == null) {
        scope = string.Empty;
      }

      return packageId.ToLowerInvariant() + "|" + scope.ToLowerInvariant();
    }

    /// <summary>
    /// Determines whether a package id is allowed by whitelist and blacklist rules.
    /// </summary>
    private bool IsPackageAllowed(
      string packageId,
      string[] packageIdWhitelist,
      string[] packageIdBlacklist
    ) {
      if (string.IsNullOrWhiteSpace(packageId)) {
        return false;
      }

      if (packageIdWhitelist == null || packageIdWhitelist.Length == 0) {
        packageIdWhitelist = new string[] { "*" };
      }

      if (packageIdBlacklist == null) {
        packageIdBlacklist = new string[0];
      }

      bool isWhitelisted = packageIdWhitelist.Any((pattern) => {
        return this.MatchesWildcardMask(packageId, pattern);
      });

      if (!isWhitelisted) {
        return false;
      }

      bool isBlacklisted = packageIdBlacklist.Any((pattern) => {
        return this.MatchesWildcardMask(packageId, pattern);
      });

      return !isBlacklisted;
    }

    /// <summary>
    /// Checks whether a string matches a simple wildcard mask.
    /// </summary>
    private bool MatchesWildcardMask(string value, string pattern) {
      if (string.IsNullOrWhiteSpace(value)) {
        return false;
      }

      if (string.IsNullOrWhiteSpace(pattern)) {
        return false;
      }

      string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";

      return Regex.IsMatch(
        value,
        regexPattern,
        RegexOptions.IgnoreCase
      );
    }

  }

}