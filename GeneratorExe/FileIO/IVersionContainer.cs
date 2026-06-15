using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Versioning;

namespace FileIO {

  public interface IVersionContainer : IDependencyScopeCapabilities {

    void WriteVersion(VersionInfo versionInfo);

    VersionInfo ReadVersion();

    void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew, bool updateExisiting, bool deleteOthers,
      bool allowDowngrade, string onlyForTargetFramework
    );

    DependencyInfo[] ReadPackageDependencies(bool includeFrameworkInfo);

  }

  public interface IDependencyScopeCapabilities {

    /// <summary>
    /// Gets whether this container can represent framework-specific dependency scopes at all.
    /// </summary>
    bool CanRepresentDependencyScopes();

    /// <summary>
    /// Gets whether this container currently uses framework-specific dependency scopes.
    /// </summary>
    bool UsesDependencyScopes();

    /// <summary>
    /// Gets the currently existing dependency scopes.
    /// </summary>
    string[] GetDependencyScopes();

  }

}
