﻿using Versioning;

namespace FileIO {

  public interface IVersionContainer {

    void WriteVersion(VersionInfo versionInfo);

    VersionInfo ReadVersion();

    void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew, bool updateExisiting, bool deleteOthers
    );

    DependencyInfo[] ReadPackageDependencies();

  }

}
