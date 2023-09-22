﻿using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Versioning;

namespace FileIO {

  public interface IVersionContainer {

    void WriteVersion(VersionInfo versionInfo);

    VersionInfo ReadVersion();

  }

}
