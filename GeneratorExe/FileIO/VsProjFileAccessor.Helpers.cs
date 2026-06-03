using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using System.Xml;
using Utils;
using Versioning;
using System.Reflection;

namespace FileIO {

  public partial class VsProjFileAccessor {

    /// <summary>
    /// Reads SDK-style PackageReference entries from a .NET Core / modern SDK project file.
    /// </summary>
    private DependencyInfo[] ReadDotNetCorePackageReferences(XElement rootElement, bool includeFrameworkInfo) {
      XName packageReferenceName = rootElement.Name.Namespace + "PackageReference";

      return rootElement.Descendants(packageReferenceName).Select((element) => {
        string packageId = this.ReadAttributeValue(element, "Include");

        if (string.IsNullOrWhiteSpace(packageId)) {
          //das hier treffen wir an, wenn die PackageReference mit Update
          //statt Include arbeitet, z.B. in einem zentralen Paketmanagement Szenario
          packageId = this.ReadAttributeValue(element, "Update");
        }

        if (string.IsNullOrWhiteSpace(packageId)) {
          return null;
        }

        string version = this.ReadAttributeValue(element, "Version");

        if (string.IsNullOrWhiteSpace(version)) {
          XElement versionElement = element
            .Elements(rootElement.Name.Namespace + "Version")
            .FirstOrDefault();

          if (versionElement != null) {
            version = versionElement.Value;
          }
        }

        if (string.IsNullOrWhiteSpace(version)) {
          return null;
        }

        DependencyInfo dependencyInfo = new DependencyInfo(packageId, version);

        if (includeFrameworkInfo) {
          dependencyInfo.DedicatedToTargetFramework = this.GetDotNetVersionRaw();
        }

        return dependencyInfo;
      }).Where((dependencyInfo) => {
        return dependencyInfo != null;
      }).ToArray();
    }

    /// <summary>
    /// Updates SDK-style PackageReference entries without rebuilding unrelated XML structure.
    /// </summary>
    private bool OverwriteDotNetCorePackageReferences(
      XDocument document,
      XElement rootElement,
      DependencyInfo[] newDependencies
    ) {

      XName itemGroupName = rootElement.Name.Namespace + "ItemGroup";
      XName packageReferenceName = rootElement.Name.Namespace + "PackageReference";

      DependencyInfo[] relevantDependencies = newDependencies
        .Where((dependency) => {
          return this.IsDependencyRelevantForProject(dependency);
        })
        .ToArray();

      XElement[] existingPackageReferences = rootElement
        .Descendants(packageReferenceName)
        .ToArray();

      bool fileChanged = false;

      foreach (XElement packageReferenceElement in existingPackageReferences) {
        string packageId = this.ReadAttributeValue(packageReferenceElement, "Include");

        if (string.IsNullOrWhiteSpace(packageId)) {
          packageId = this.ReadAttributeValue(packageReferenceElement, "Update");
        }

        DependencyInfo matchingDependency = relevantDependencies
          .FirstOrDefault((dependency) => {
            return string.Equals(
              dependency.TargetPackageId,
              packageId,
              StringComparison.OrdinalIgnoreCase
            );
          });

        if (matchingDependency == null) {
          packageReferenceElement.Remove();
          fileChanged = true;
        }
        else {
          string version = matchingDependency.TargetPackageVersionConstraint.ToString(true);

          if (this.SetPackageReferenceVersion(packageReferenceElement, version)) {
            fileChanged = true;
          }
        }
      }

      foreach (DependencyInfo dependency in relevantDependencies) {
        bool alreadyExists = rootElement
          .Descendants(packageReferenceName)
          .Any((element) => {
            string packageId = this.ReadAttributeValue(element, "Include");

            if (string.IsNullOrWhiteSpace(packageId)) {
              packageId = this.ReadAttributeValue(element, "Update");
            }

            return string.Equals(
              packageId,
              dependency.TargetPackageId,
              StringComparison.OrdinalIgnoreCase
            );
          });

        if (!alreadyExists) {
          XElement itemGroupElement = this.GetOrCreatePackageReferenceItemGroup(rootElement);
          XElement packageReferenceElement = new XElement(
            packageReferenceName,
            new XAttribute("Include", dependency.TargetPackageId),
            new XAttribute("Version", dependency.TargetPackageVersionConstraint.ToString(true))
          );

          this.AddElementWithProjectIndent(itemGroupElement, packageReferenceElement);
          fileChanged = true;
        }
      }

      return fileChanged;
    }

    /// <summary>
    /// Reads legacy NuGet-related assembly references from a .NET Framework project file.
    /// </summary>
    private DependencyInfo[] ReadDotNetFrameworkNugetReferences(XElement rootElement, bool includeFrameworkInfo) {
      XName referenceName = rootElement.Name.Namespace + "Reference";
      XName hintPathName = rootElement.Name.Namespace + "HintPath";

      string packagesFullDirectoryName = this.GetPackagesFullDirectoryName();
      string projectDirectoryName = Path.GetDirectoryName(_FileFullName);

      return rootElement
        .Descendants(referenceName)
        .Select((referenceElement) => {
          XElement hintPathElement = referenceElement.Element(hintPathName);

          if (hintPathElement == null) {
            return null;
          }

          NetFxProjFileReferenceEntry referenceInfo = this.TryReadReferenceEntriesRelatedToNuget(
            projectDirectoryName,
            packagesFullDirectoryName,
            hintPathElement.Value,
            includeFrameworkInfo
          );

          if (referenceInfo == null) {
            return null;
          }

          DependencyInfo dependencyInfo = new DependencyInfo(referenceInfo.PackageId, referenceInfo.PackageVersion);

          if (includeFrameworkInfo) {
            dependencyInfo.DedicatedToTargetFramework = referenceInfo.TargetFramework;
          }

          return dependencyInfo;
        })
        .Where((dependencyInfo) => {
          return dependencyInfo != null;
        })
        .ToArray();
    }

    /// <summary>
    /// Updates legacy NuGet-related assembly references in a .NET Framework project file.
    /// </summary>
    private bool OverwriteDotNetFrameworkNugetReferences(
      XDocument document,
      XElement rootElement,
      DependencyInfo[] newDependencies
    ) {
      XName referenceName = rootElement.Name.Namespace + "Reference";
      XName hintPathName = rootElement.Name.Namespace + "HintPath";

      string packagesFullDirectoryName = this.GetPackagesFullDirectoryName();
      string projectDirectoryName = Path.GetDirectoryName(this._FileFullName);

      DependencyInfo[] relevantDependencies = newDependencies
        .Where((dependency) => {
          return this.IsDependencyRelevantForProject(dependency);
        })
        .ToArray();

      bool fileChanged = false;

      XElement[] referenceElements = rootElement
        .Descendants(referenceName)
        .ToArray();

      foreach (XElement referenceElement in referenceElements) {
        XElement hintPathElement = referenceElement.Element(hintPathName);

        if (hintPathElement == null) {
          continue;
        }

        NetFxProjFileReferenceEntry referenceInfo = this.TryReadReferenceEntriesRelatedToNuget(
          projectDirectoryName,
          packagesFullDirectoryName,
          hintPathElement.Value,
          false
        );

        if (referenceInfo == null) {
          continue;
        }

        DependencyInfo matchingNewDependency = relevantDependencies
          .FirstOrDefault((dependency) => {
            return string.Equals(
              dependency.TargetPackageId,
              referenceInfo.PackageId,
              StringComparison.OrdinalIgnoreCase
            );
          });

        if (matchingNewDependency == null) {
          referenceElement.Remove();
          fileChanged = true;
        }
        else if(referenceInfo.PackageVersion != matchingNewDependency.TargetPackageVersionConstraint.ToString(true)) {

          NugetDeliveredDll candidate = this.TryFindBestNugetLibraryCandidate(
            packagesFullDirectoryName,
            matchingNewDependency,
            referenceInfo.AssemblyFileName
          );

          if (candidate != null) {
            string relativeHintPath = this.GetRelativeProjectPath(projectDirectoryName, candidate.AssemblyFullName);

            if (!string.Equals(hintPathElement.Value, relativeHintPath, StringComparison.OrdinalIgnoreCase)) {
              hintPathElement.Value = relativeHintPath;
              fileChanged = true;

              //nur wenn der HintPath tatsächlich geändert wurde, versuchen wir die Version im Include-Attribut zu aktualisieren, da das ein ziemlich riskanter Eingriff ist, der die Projektdatei schnell unbrauchbar machen kann, wenn er falsch gemacht wird. Es gibt nämlich keine Garantie, dass die Version überhaupt in der Include-Attribut existiert oder dass sie in einem erwarteten Format vorliegt.
              //Deshalb belassen wir sie lieber unverändert, wenn wir den HintPath nicht anpassen mussten.
              this.UpdateReferenceIncludeVersion(referenceElement, matchingNewDependency, true, candidate.AssemblyFullName);

            }
     
          }
        }
      }

      foreach (DependencyInfo dependency in relevantDependencies) {
        bool alreadyExists = rootElement
          .Descendants(referenceName)
          .Any((referenceElement) => {
            XElement hintPathElement = referenceElement.Element(hintPathName);

            if (hintPathElement == null) {
              return false;
            }

            NetFxProjFileReferenceEntry referenceInfo = this.TryReadReferenceEntriesRelatedToNuget(
              projectDirectoryName,
              packagesFullDirectoryName,
              hintPathElement.Value,
              false
            );

            if (referenceInfo == null) {
              return false;
            }

            return string.Equals(
              referenceInfo.PackageId,
              dependency.TargetPackageId,
              StringComparison.OrdinalIgnoreCase
            );
          });

        if (!alreadyExists) {
          NugetDeliveredDll[] candidates = this.TryFindOrAsumeConcreteDllsForNugetDependency(
            packagesFullDirectoryName,
            dependency
          );

          foreach (NugetDeliveredDll candidate in candidates) {
            XElement itemGroupElement = this.GetOrCreateReferenceItemGroup(rootElement);
            XElement referenceElement = this.CreateLegacyReferenceElement(
              rootElement,
              projectDirectoryName,
              dependency,
              candidate
            );

            this.AddElementWithProjectIndent(itemGroupElement, referenceElement);
            fileChanged = true;
          }
        }
      }

      return fileChanged;
    }

    /// <summary>
    /// Determines whether a dependency is relevant for the current project target framework.
    /// </summary>
    private bool IsDependencyRelevantForProject(DependencyInfo dependency) {
      if (dependency == null) {
        return false;
      }

      if (string.IsNullOrWhiteSpace(dependency.TargetPackageId)) {
        return false;
      }

      if (dependency.TargetPackageVersionConstraint == null) {
        return false;
      }

      if (string.IsNullOrWhiteSpace(dependency.DedicatedToTargetFramework)) {
        return true;
      }

      string dedicatedFramework = dependency.DedicatedToTargetFramework.Trim();

      //Match frameworkMatch = Regex.Match(
      //  dedicatedFramework,
      //  @"^net(\d+)",
      //  RegexOptions.IgnoreCase
      //);

      //if (frameworkMatch.Success) {
      //  if (Int32.TryParse(frameworkMatch.Groups[1].Value, out Int32 majorVersion)) {
      //    if (majorVersion > 4) {
      //      return false;
      //    }
      //  }
      //}

      string projectFramework = this.GetDotNetVersion().ToString();
      string targetFx = ParseDotNetVersion(dedicatedFramework).ToString();

      return string.Equals(
        targetFx,
        projectFramework,
        StringComparison.OrdinalIgnoreCase
      );

    }

    /// <summary>
    /// Sets the version of a PackageReference while preserving its existing shape where possible.
    /// </summary>
    private bool SetPackageReferenceVersion(XElement packageReferenceElement, string version) {
      XAttribute versionAttribute = packageReferenceElement.Attribute("Version");

      if (versionAttribute != null) {
        if (!string.Equals(versionAttribute.Value, version, StringComparison.Ordinal)) {
          versionAttribute.Value = version;
          return true;
        }

        return false;
      }

      XElement versionElement = packageReferenceElement
        .Elements(packageReferenceElement.Name.Namespace + "Version")
        .FirstOrDefault();

      if (versionElement != null) {
        if (!string.Equals(versionElement.Value, version, StringComparison.Ordinal)) {
          versionElement.Value = version;
          return true;
        }

        return false;
      }

      packageReferenceElement.SetAttributeValue("Version", version);
      return true;
    }

    /// <summary>
    /// Gets an ItemGroup suitable for PackageReference entries or creates a new one near the property groups.
    /// </summary>
    private XElement GetOrCreatePackageReferenceItemGroup(XElement rootElement) {
      XName itemGroupName = rootElement.Name.Namespace + "ItemGroup";
      XName packageReferenceName = rootElement.Name.Namespace + "PackageReference";

      XElement itemGroupElement = rootElement
        .Elements(itemGroupName)
        .FirstOrDefault((element) => {
          return element.Elements(packageReferenceName).Any();
        });

      if (itemGroupElement != null) {
        return itemGroupElement;
      }

      itemGroupElement = new XElement(itemGroupName);
      this.InsertItemGroupAfterLastPropertyGroup(rootElement, itemGroupElement);
      return itemGroupElement;
    }

    /// <summary>
    /// Gets an ItemGroup suitable for Reference entries or creates a new one near existing references.
    /// </summary>
    private XElement GetOrCreateReferenceItemGroup(XElement rootElement) {
      XName itemGroupName = rootElement.Name.Namespace + "ItemGroup";
      XName referenceName = rootElement.Name.Namespace + "Reference";

      XElement itemGroupElement = rootElement
        .Elements(itemGroupName)
        .FirstOrDefault((element) => {
          return element.Elements(referenceName).Any();
        });

      if (itemGroupElement != null) {
        return itemGroupElement;
      }

      itemGroupElement = new XElement(itemGroupName);
      this.InsertItemGroupAfterLastPropertyGroup(rootElement, itemGroupElement);
      return itemGroupElement;
    }

    /// <summary>
    /// Inserts a new ItemGroup after the last PropertyGroup where possible.
    /// </summary>
    private void InsertItemGroupAfterLastPropertyGroup(XElement rootElement, XElement itemGroupElement) {
      XName propertyGroupName = rootElement.Name.Namespace + "PropertyGroup";

      XElement lastPropertyGroupElement = rootElement
        .Elements(propertyGroupName)
        .LastOrDefault();

      if (lastPropertyGroupElement != null) {
        lastPropertyGroupElement.AddAfterSelf(
          new XText(Environment.NewLine + "  "),
          itemGroupElement
        );
      }
      else {
        rootElement.Add(
          new XText(Environment.NewLine + "  "),
          itemGroupElement
        );
      }
    }

    /// <summary>
    /// Adds an element to an ItemGroup using the normal Visual Studio project indentation.
    /// </summary>
    private void AddElementWithProjectIndent(XElement itemGroupElement, XElement childElement) {
      itemGroupElement.Add(
        new XText("  "),
        childElement,
        new XText(Environment.NewLine + "  ")
      );
    }

    /// <summary>
    /// Creates a legacy Reference element with HintPath for a NuGet package assembly.
    /// </summary>
    private XElement CreateLegacyReferenceElement(
      XElement rootElement,
      string projectDirectoryName,
      DependencyInfo dependency,
      NugetDeliveredDll candidate
    ) {

      XName referenceName = rootElement.Name.Namespace + "Reference";
      XName hintPathName = rootElement.Name.Namespace + "HintPath";

      string assemblyName = Path.GetFileNameWithoutExtension(candidate.AssemblyFullName);
      string relativeHintPath = this.GetRelativeProjectPath(projectDirectoryName, candidate.AssemblyFullName);

      XElement referenceElement = new XElement(
        referenceName,
        new XAttribute("Include", assemblyName),
        new XText(Environment.NewLine + "      "),
        new XElement(hintPathName, relativeHintPath),
        new XText(Environment.NewLine + "    ")
      );

      return referenceElement;
    }

    /// <summary>
    /// Updates the Version part inside a legacy Reference Include attribute if that part already exists.
    /// </summary>
    private bool UpdateReferenceIncludeVersion(XElement referenceElement, DependencyInfo dependency, bool removeAnyVersionInfo, string assemblyFullName) {
      XAttribute includeAttribute = referenceElement.Attribute("Include");

      if (includeAttribute == null) {
        return false;
      }

      //TODO: lieber aus der assemblyFullName (falls exisitent die konkrete version rausholen=

      string includeValue = includeAttribute.Value;

      if (!includeValue.Contains("Version=")) {
        return false;
      }

      string cleanVersion = dependency.TargetPackageVersionConstraint.ToString(true);
      string assemblyVersion = cleanVersion;

      if (assemblyVersion.Split('.').Length == 3) {
        assemblyVersion = assemblyVersion + ".0";
      }

      string newIncludeValue;
      if (removeAnyVersionInfo) {
        newIncludeValue = Path.GetFileNameWithoutExtension(assemblyFullName);
      }
      else {
        newIncludeValue = Regex.Replace(
           includeValue,
           @"Version=[^,\s]+",
           "Version=" + assemblyVersion
         );
      }

      if (string.Equals(includeValue, newIncludeValue, StringComparison.Ordinal)) {
        return false;
      }

      includeAttribute.Value = newIncludeValue;
      return true;
    }

    ///// <summary>
    ///// Tries to find all best matching assembly candidates for a NuGet package.
    ///// If the package exsits, it will take exact there dlls, if not it will
    ///// look for older versions and take those dll-names as fallback candidates
    ///// for the (a little risky) HintPath generation. This is the only way to
    ///// support updates without running a full restore first!
    ///// </summary>
    //private NugetDeliveredDll[] TryFindOrAsumeConcreteDllsForNugetDependency2 (
    //  string packagesFullDirectoryName,
    //  DependencyInfo dependency
    //) {

    //  string packageVersion = dependency.TargetPackageVersionConstraint.ToString(true);
    //  string packageDirectoryName = this.TryFindBestPackageDirectory(
    //    packagesFullDirectoryName,
    //    dependency.TargetPackageId,
    //    packageVersion
    //  );

    //  if (string.IsNullOrWhiteSpace(packageDirectoryName)) {
    //    string fallbackFrameworkName = this.GetLegacyNugetFrameworkFolderName();
    //    string fallbackAssemblyFullName = Path.Combine(
    //      packagesFullDirectoryName,
    //      dependency.TargetPackageId + "." + packageVersion,
    //      "lib",
    //      fallbackFrameworkName,
    //      dependency.TargetPackageId + ".dll"
    //    );

    //    return new NugetDeliveredDll[] {
    //      new NugetDeliveredDll(fallbackAssemblyFullName, fallbackFrameworkName)
    //    };
    //  }

    //  string libDirectoryName = Path.Combine(packageDirectoryName, "lib");

    //  if (!Directory.Exists(libDirectoryName)) {
    //    return new NugetDeliveredDll[0];
    //  }

    //  string frameworkDirectoryName = this.TryFindBestFrameworkDirectory(libDirectoryName);

    //  if (string.IsNullOrWhiteSpace(frameworkDirectoryName)) {
    //    return new NugetDeliveredDll[0];
    //  }

    //  return Directory
    //    .GetFiles(frameworkDirectoryName, "*.dll")
    //    .Select((assemblyFullName) => {
    //      return new NugetDeliveredDll(
    //        assemblyFullName,
    //        Path.GetFileName(frameworkDirectoryName)
    //      );
    //    })
    //    .ToArray();
    //}

    /// <summary>
    /// Tries to find all best matching assembly candidates for a NuGet package.
    /// If the package exsits, it will take exact there dlls, if not it will
    /// look for older versions and take those dll-names as fallback candidates
    /// for the (a little risky) HintPath generation. This is the only way to
    /// support updates without running a full restore first!
    /// </summary>
    private NugetDeliveredDll[] TryFindOrAsumeConcreteDllsForNugetDependency(
      string packagesFullDirectoryName,
      DependencyInfo dependency
    ) {
      if (dependency == null) {
        return new NugetDeliveredDll[0];
      }

      if (string.IsNullOrWhiteSpace(packagesFullDirectoryName)) {
        return new NugetDeliveredDll[0];
      }

      if (string.IsNullOrWhiteSpace(dependency.TargetPackageId)) {
        return new NugetDeliveredDll[0];
      }

      if (dependency.TargetPackageVersionConstraint == null) {
        return new NugetDeliveredDll[0];
      }

      string packageId = dependency.TargetPackageId;
      string targetVersion = dependency.TargetPackageVersionConstraint.ToString(true);

      string targetPackageDirectoryName = this.GetNugetPackageDirectoryName(
        packagesFullDirectoryName,
        packageId,
        targetVersion
      );

      NugetDeliveredDll[] exactCandidates = this.TryFindDllsInExistingPackageDirectory(
        targetPackageDirectoryName
      );

      if (exactCandidates.Length > 0) {
        return exactCandidates;
      }

      NugetDeliveredDll[] templateCandidates = this.TryFindDllsFromOlderPackageFolderAsTemplate(
        packagesFullDirectoryName,
        packageId,
        targetVersion,
        targetPackageDirectoryName
      );

      if (templateCandidates.Length > 0) {
        return templateCandidates;
      }

      string fallbackFrameworkName = this.GetLegacyNugetFrameworkFolderName();
      string fallbackAssemblyFullName = Path.Combine(
        targetPackageDirectoryName,
        "lib",
        fallbackFrameworkName,
        packageId + ".dll"
      );

      return new NugetDeliveredDll[] {
        new NugetDeliveredDll(fallbackAssemblyFullName, fallbackFrameworkName)
      };
    }

    /// <summary>
    /// Builds the package directory name for classic packages-folder and centralized NuGet cache layouts.
    /// </summary>
    private string GetNugetPackageDirectoryName(
      string packagesFullDirectoryName,
      string packageId,
      string packageVersion
    ) {
      string classicPackageDirectoryName = Path.Combine(
        packagesFullDirectoryName,
        packageId + "." + packageVersion
      );

      if (Directory.Exists(classicPackageDirectoryName)) {
        return classicPackageDirectoryName;
      }

      string centralizedPackageDirectoryName = Path.Combine(
        packagesFullDirectoryName,
        packageId.ToLowerInvariant(),
        packageVersion
      );

      if (Directory.Exists(Path.Combine(packagesFullDirectoryName, packageId.ToLowerInvariant()))) {
        return centralizedPackageDirectoryName;
      }

      return classicPackageDirectoryName;
    }

    /// <summary>
    /// Tries to find DLLs in an existing package directory using the best compatible framework folder.
    /// </summary>
    private NugetDeliveredDll[] TryFindDllsInExistingPackageDirectory(string packageDirectoryName) {
      if (string.IsNullOrWhiteSpace(packageDirectoryName)) {
        return new NugetDeliveredDll[0];
      }

      string libDirectoryName = Path.Combine(packageDirectoryName, "lib");

      if (!Directory.Exists(libDirectoryName)) {
        return new NugetDeliveredDll[0];
      }

      string frameworkDirectoryName = this.TryFindBestFrameworkDirectory(libDirectoryName);

      if (string.IsNullOrWhiteSpace(frameworkDirectoryName)) {
        return new NugetDeliveredDll[0];
      }

      return Directory
        .GetFiles(frameworkDirectoryName, "*.dll")
        .Select((assemblyFullName) => {
          return new NugetDeliveredDll(
            assemblyFullName,
            Path.GetFileName(frameworkDirectoryName)
          );
        })
        .ToArray();
    }

    /// <summary>
    /// Uses older installed package versions only as templates for lib/framework/dll names.
    /// Returned candidates still point to the requested target package directory.
    /// </summary>
    private NugetDeliveredDll[] TryFindDllsFromOlderPackageFolderAsTemplate(
      string packagesFullDirectoryName,
      string packageId,
      string targetVersion,
      string targetPackageDirectoryName
    ) {
      if (!Directory.Exists(packagesFullDirectoryName)) {
        return new NugetDeliveredDll[0];
      }

      string[] packageDirectories = Directory
        .GetDirectories(packagesFullDirectoryName, packageId + ".*")
        .Where((directoryName) => {
          string foundPackageId = null;
          string foundPackageVersion = null;

          if (!this.TrySplitPackageFolderName(Path.GetFileName(directoryName), out foundPackageId, out foundPackageVersion)) {
            return false;
          }

          return string.Equals(foundPackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(foundPackageVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
        })
        .OrderByDescending((directoryName) => {
          return directoryName;
        })
        .ToArray();

      foreach (string packageDirectoryName in packageDirectories) {
        NugetDeliveredDll[] candidates = this.TryFindDllsInExistingPackageDirectory(packageDirectoryName);

        if (candidates.Length == 0) {
          continue;
        }

        return candidates
          .Select((candidate) => {
            string subPath = this.GetPackageRelativeLibraryPath(packageDirectoryName, candidate.AssemblyFullName);
            string mappedAssemblyFullName = Path.Combine(targetPackageDirectoryName, subPath);

            return new NugetDeliveredDll(
              mappedAssemblyFullName,
              candidate.TargetFramework
            );
          })
          .ToArray();
      }

      return new NugetDeliveredDll[0];
    }

    /// <summary>
    /// Gets the package-relative library path, for example lib\net48\SomePackage.dll.
    /// </summary>
    private string GetPackageRelativeLibraryPath(string packageDirectoryName, string assemblyFullName) {
      string relativePath = Path.GetRelativePath(packageDirectoryName, assemblyFullName);
      return relativePath.Replace("/", "\\");
    }














    /// <summary>
    /// Tries to find the best matching assembly candidate for a NuGet package.
    /// </summary>
    private NugetDeliveredDll TryFindBestNugetLibraryCandidate(
      string packagesFullDirectoryName,
      DependencyInfo dependency,
      string preferredAssemblyFileName
    ) {
      NugetDeliveredDll[] candidates = this.TryFindOrAsumeConcreteDllsForNugetDependency(
        packagesFullDirectoryName,
        dependency
      );

      NugetDeliveredDll preferredCandidate = candidates
        .FirstOrDefault((candidate) => {
          return string.Equals(
            Path.GetFileName(candidate.AssemblyFullName),
            preferredAssemblyFileName,
            StringComparison.OrdinalIgnoreCase
          );
        });

      if (preferredCandidate != null) {
        return preferredCandidate;
      }

      return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Tries to find the best package directory for local packages-folder and centralized NuGet cache layouts.
    /// </summary>
    private string TryFindBestPackageDirectory(
      string packagesFullDirectoryName,
      string packageId,
      string packageVersion
    ) {
      string localPackagesDirectoryName = Path.Combine(
        packagesFullDirectoryName,
        packageId + "." + packageVersion
      );

      if (Directory.Exists(localPackagesDirectoryName)) {
        return localPackagesDirectoryName;
      }

      string centralPackagesDirectoryName = Path.Combine(
        packagesFullDirectoryName,
        packageId.ToLowerInvariant(),
        packageVersion
      );

      if (Directory.Exists(centralPackagesDirectoryName)) {
        return centralPackagesDirectoryName;
      }

      if (!Directory.Exists(packagesFullDirectoryName)) {
        return null;
      }

      string[] possibleDirectories = Directory
        .GetDirectories(packagesFullDirectoryName, packageId + ".*")
        .OrderByDescending((directoryName) => {
          return directoryName;
        })
        .ToArray();

      return possibleDirectories.FirstOrDefault();
    }

    /// <summary>
    /// Tries to find the most compatible lib target framework directory for the current .NET Framework project.
    /// </summary>
    private string TryFindBestFrameworkDirectory(string libDirectoryName) {
      Version projectVersion = this.GetDotNetVersion();

      string[] frameworkDirectories = Directory
        .GetDirectories(libDirectoryName)
        .Where((directoryName) => {
          return Path.GetFileName(directoryName).StartsWith("net", StringComparison.OrdinalIgnoreCase);
        })
        .ToArray();

      string bestDirectoryName = null;
      Version bestVersion = null;

      foreach (string frameworkDirectoryName in frameworkDirectories) {
        string frameworkName = Path.GetFileName(frameworkDirectoryName);

        if (frameworkName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        if (frameworkName.StartsWith("netcore", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        Version frameworkVersion = this.TryParseLegacyNugetFrameworkFolderVersion(frameworkName);

        if (frameworkVersion == null) {
          continue;
        }

        if (frameworkVersion > projectVersion) {
          continue;
        }

        if (bestVersion == null || frameworkVersion > bestVersion) {
          bestVersion = frameworkVersion;
          bestDirectoryName = frameworkDirectoryName;
        }
      }

      return bestDirectoryName;
    }

    /// <summary>
    /// Parses legacy NuGet lib folder names like net40, net45, net48 or net481.
    /// </summary>
    private Version TryParseLegacyNugetFrameworkFolderVersion(string frameworkName) {
      Match match = Regex.Match(
        frameworkName,
        @"^net(\d+)$",
        RegexOptions.IgnoreCase
      );

      if (!match.Success) {
        return null;
      }

      string versionText = match.Groups[1].Value;

      if (versionText.Length == 2) {
        return new Version(
          Int32.Parse(versionText.Substring(0, 1)),
          Int32.Parse(versionText.Substring(1, 1))
        );
      }

      if (versionText.Length == 3) {
        return new Version(
          Int32.Parse(versionText.Substring(0, 1)),
          Int32.Parse(versionText.Substring(1, 1)),
          Int32.Parse(versionText.Substring(2, 1))
        );
      }

      return null;
    }

    /// <summary>
    /// Gets a conservative legacy target framework folder name for fallback HintPath generation.
    /// </summary>
    private string GetLegacyNugetFrameworkFolderName() {
      Version version = this.GetDotNetVersion();

      if (version.Build > 0) {
        return "net" + version.Major.ToString() + version.Minor.ToString() + version.Build.ToString();
      }

      return "net" + version.Major.ToString() + version.Minor.ToString();
    }

    /// <summary>
    /// Tries to read NuGet package information from a Reference HintPath.
    /// </summary>
    private NetFxProjFileReferenceEntry TryReadReferenceEntriesRelatedToNuget(
      string projectDirectoryName,
      string packagesFullDirectoryName,
      string hintPath,
      bool initializeTargetFrameworkInfo
    ) {

      if (string.IsNullOrWhiteSpace(hintPath)) {
        return null;
      }

      string hintFullName = Path.GetFullPath(Path.Combine(projectDirectoryName, hintPath));
      string[] pathParts = hintFullName.Split(
        new char[] {
      Path.DirectorySeparatorChar,
      Path.AltDirectorySeparatorChar
        },
        StringSplitOptions.RemoveEmptyEntries
      );

      for (int index = 0; index < pathParts.Length; index++) {
        if (!string.Equals(pathParts[index], "lib", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        if (index < 1) {
          continue;
        }

        if (index + 2 >= pathParts.Length) {
          continue;
        }

        string targetFramework = null;
        if (initializeTargetFrameworkInfo) {
          targetFramework = pathParts[index + 1];
        }
        string assemblyFileName = pathParts[pathParts.Length - 1];

        // Central NuGet cache:
        // {packageId}\{version}\lib\{framework}\{assembly}.dll
        if (index >= 2) {
          string possibleVersion = pathParts[index - 1];
          string possiblePackageId = pathParts[index - 2];

          if (this.LooksLikeNugetVersion(possibleVersion)) {
            return new NetFxProjFileReferenceEntry(
              possiblePackageId,
              possibleVersion,
              targetFramework,
              assemblyFileName
            );
          }
        }

        // Classic packages folder:
        // {packageId}.{version}\lib\{framework}\{assembly}.dll
        string packageFolderName = pathParts[index - 1];
        string packageId = null;
        string packageVersion = null;

        if (this.TrySplitPackageFolderName(packageFolderName, out packageId, out packageVersion)) {
          return new NetFxProjFileReferenceEntry(
            packageId,
            packageVersion,
            targetFramework,
            assemblyFileName
          );
        }
      }

      return null;
    }

    /// <summary>
    /// Determines whether the given text looks like a NuGet package version.
    /// </summary>
    private bool LooksLikeNugetVersion(string value) {
      if (string.IsNullOrWhiteSpace(value)) {
        return false;
      }

      return Regex.IsMatch(
        value,
        @"^[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:\.[0-9]+)?(?:[-A-Za-z0-9.]+)?$"
      );
    }

    /// <summary>
    /// Splits a classic packages-folder name like Package.Id.1.2.3 into package id and version.
    /// </summary>
    /// <summary>
    /// Splits a classic NuGet package folder name into package id and package version.
    /// Supports package versions with three or four numeric parts and optional pre-release suffixes.
    /// </summary>
    private bool TrySplitPackageFolderName(
      string packageFolderName,
      out string packageId,
      out string packageVersion
    ) {
      packageId = null;
      packageVersion = null;

      if (string.IsNullOrWhiteSpace(packageFolderName)) {
        return false;
      }

      int bestVersionStartIndex = -1;
      int bestVersionEndIndex = -1;
      int length = packageFolderName.Length;

      for (int index = 0; index < length; index++) {
        if (packageFolderName[index] != '.') {
          continue;
        }

        int versionStartIndex = index + 1;

        if (versionStartIndex >= length) {
          continue;
        }

        int cursor = versionStartIndex;
        int numericPartCount = 0;
        bool isValidCandidate = true;

        while (cursor < length) {
          int numberStartIndex = cursor;

          while (cursor < length && Char.IsDigit(packageFolderName[cursor])) {
            cursor++;
          }

          if (numberStartIndex == cursor) {
            isValidCandidate = false;
            break;
          }

          numericPartCount++;

          if (numericPartCount == 4) {
            bestVersionStartIndex = versionStartIndex;
            bestVersionEndIndex = cursor;
            break;
          }

          if (cursor >= length) {
            if (numericPartCount >= 3) {
              bestVersionStartIndex = versionStartIndex;
              bestVersionEndIndex = cursor;
            }

            break;
          }

          if (packageFolderName[cursor] == '.') {
            cursor++;
            continue;
          }

          if (packageFolderName[cursor] == '-') {
            if (numericPartCount >= 3) {
              bestVersionStartIndex = versionStartIndex;
              bestVersionEndIndex = length;
            }
            else {
              isValidCandidate = false;
            }

            break;
          }

          isValidCandidate = false;
          break;
        }

        if (bestVersionStartIndex >= 0) {
          break;
        }

        if (!isValidCandidate) {
          continue;
        }
      }

      if (bestVersionStartIndex < 0) {
        return false;
      }

      packageId = packageFolderName.Substring(0, bestVersionStartIndex - 1);
      packageVersion = packageFolderName.Substring(
        bestVersionStartIndex,
        bestVersionEndIndex - bestVersionStartIndex
      );

      return true;
    }

    /// <summary>
    /// Reads an attribute value by local name.
    /// </summary>
    private string ReadAttributeValue(XElement element, string localName) {
      XAttribute attribute = element
        .Attributes()
        .FirstOrDefault((candidate) => {
          return string.Equals(candidate.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
        });

      if (attribute == null) {
        return null;
      }

      return attribute.Value;
    }

    /// <summary>
    /// Converts an absolute path to a project-relative path.
    /// </summary>
    private string GetRelativeProjectPath(string projectDirectoryName, string fullName) {
      string relativePath = Path.GetRelativePath(projectDirectoryName, fullName);
      return relativePath.Replace("/", "\\");
    }

    /// <summary>
    /// Saves the XML document while preserving existing whitespace as far as possible.
    /// </summary>
    private void SaveXmlDocument(XDocument document) {
      XmlWriterSettings settings = new XmlWriterSettings();
      settings.Encoding = FileIoHelper.DetectFileEncoding(_FileFullName);
      settings.Indent = false;
      settings.OmitXmlDeclaration = document.Declaration == null;

      using (XmlWriter writer = XmlWriter.Create(this._FileFullName, settings)) {
        document.Save(writer);
      }
    }

    /// <summary>
    /// Holds parsed NuGet reference information from a legacy HintPath.
    /// </summary>
    private sealed class NetFxProjFileReferenceEntry {

      private string _PackageId;
      private string _PackageVersion;
      private string _TargetFramework;
      private string _AssemblyFileName;

      public NetFxProjFileReferenceEntry(
        string packageId,
        string packageVersion,
        string targetFramework,
        string assemblyFileName
      ) {
        _PackageId = packageId;
        _PackageVersion = packageVersion;
        _TargetFramework = targetFramework;
        _AssemblyFileName = assemblyFileName;
      }

      public string PackageId {
        get {
          return _PackageId;
        }
      }

      public string PackageVersion {
        get {
          return _PackageVersion;
        }
      }

      public string TargetFramework {
        get {
          return _TargetFramework;
        }
      }

      public string AssemblyFileName {
        get {
          return _AssemblyFileName;
        }
      }

    }

    /// <summary>
    /// Holds a candidate assembly found inside a NuGet package lib folder.
    /// </summary>
    private sealed class NugetDeliveredDll {

      private string _AssemblyFullName;
      private string _TargetFramework;

      public NugetDeliveredDll(string assemblyFullName, string targetFramework) {
        _AssemblyFullName = assemblyFullName;
        _TargetFramework = targetFramework;
      }

      public string AssemblyFullName {
        get {
          return _AssemblyFullName;
        }
      }

      public string TargetFramework {
        get {
          return _TargetFramework;
        }
      }

    }















  }

}
