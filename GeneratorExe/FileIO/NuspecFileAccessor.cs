using System;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Utils;
using Versioning;
using System.Xml;
using System.Xml.Linq;

namespace FileIO {

  public class NuspecFileAccessor : IVersionContainer {

    private string _FileFullName;

    public NuspecFileAccessor(string fileFullName) {
      _FileFullName = Path.GetFullPath(fileFullName);
      if (!File.Exists(fileFullName)) {
        throw new FileNotFoundException("Could not find File: " + fileFullName);
      }
    }

    public VersionInfo ReadVersion() {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      var versionInfo = new VersionInfo();

      var matchVers = Regex.Matches(rawContent, _RegexSearch).FirstOrDefault();
      if (matchVers != null) {
        versionInfo.currentVersionWithSuffix = matchVers.Value.Substring(9, matchVers.Value.Length - 19);
      }

      versionInfo.CurrentVersionWithSuffix2CurrentVersionAndPrereleaseSuffx(true);

      Console.WriteLine($"Loaded Version '{versionInfo.currentVersionWithSuffix}' from '{_FileFullName}'");
      return versionInfo;
    }

    /*
     * <?xml version="1.0"?>
       <package>
         <metadata>
           <version>2.0.6-FF-FF</version> 
     */

    private string _RegexSearch = "<version>[a-zA-Z0-9.\\-\\*]*</version>";

    public void WriteVersion(VersionInfo versionInfo) {
      string rawContent = File.ReadAllText(_FileFullName, Encoding.Default);
      string versionToWrite = versionInfo.currentVersionWithSuffix;

      versionToWrite = versionToWrite.Replace("*","0");

      Console.WriteLine($"Writing Version '{versionToWrite}' into '{_FileFullName}'");

      int matchCount = 0;
      rawContent = FileIoHelper.Replace(
        rawContent, _RegexSearch, $"<version>{versionToWrite}</version>", ref matchCount
      );

      Console.WriteLine($"  Processed {matchCount} matches...");
      if (matchCount > 0) {
        FileIoHelper.WriteFile(_FileFullName, rawContent);
      }
    }

    public void WritePackageDependencies(
      DependencyInfo[] packageDependencies,
      bool addNew, bool updateExisiting, bool deleteOthers,
      string onlyForTargetFramework
    ) {

      DependencyUpdateHelper updateHelper = new DependencyUpdateHelper(
         () => ReadPackageDependencies(true), (deps) => OverwriteAllPackageDependencies(deps.ToArray())
      );

      updateHelper.WritePackageDependencies(
        packageDependencies, addNew, updateExisiting, deleteOthers, onlyForTargetFramework
      );

    }

    public void OverwriteAllPackageDependencies(DependencyInfo[] newDependencies) {

      XDocument document = XDocument.Load(this._FileFullName, LoadOptions.PreserveWhitespace);
      XElement dependenciesElement = this.GetOrCreateDependenciesElement(document);

      bool fileChanged = this.OverwriteNuspecDependencies(dependenciesElement, newDependencies);

      if (fileChanged) {
        this.SaveXmlDocument(document);
      }

      // HIER ÄNDERN //

      //string rawContent = File.ReadAllText(_FileFullName, Encoding.UTF8);

      //bool fileChanged = false;
      //foreach (var packageDependency in newDependencies) {
      //  int matchCount = 0;
      //  rawContent = FileIoHelper.Replace(
      //    rawContent,
      //    $"<dependency id=\"{packageDependency.TargetPackageId}\" version=\"[a-zA-Z0-9.,)(\\[\\]\\-\\*]*\"",
      //    $"<dependency id=\"{packageDependency.TargetPackageId}\" version=\"{packageDependency.TargetPackageVersionConstraint}\"", ref matchCount
      //  );
      //  if (matchCount > 0) {
      //    Console.WriteLine($"  Processed ref version of {matchCount} matches for \"{packageDependency.TargetPackageId}\" to \"{packageDependency.TargetPackageVersionConstraint}\"");
      //    fileChanged = true;
      //  }
      //}

      //if (fileChanged) {
      //  FileIoHelper.WriteFile(_FileFullName, rawContent);
      //}

    }

    private string _RegexSearchDep = "<dependency id=\"[a-zA-Z0-9.\\-\\*]*\" version=\"[a-zA-Z0-9.,)(\\[\\]\\-\\*]*\"";
    public DependencyInfo[] ReadPackageDependencies(bool includeFrameworkInfo) {
      XDocument document = XDocument.Load(this._FileFullName, LoadOptions.PreserveWhitespace);
      XElement dependenciesElement = this.GetDependenciesElement(document);

      if (dependenciesElement == null) {
        return new DependencyInfo[0];
      }

      return this.ReadNuspecDependencies(dependenciesElement, includeFrameworkInfo);



      //string rawContent = File.ReadAllText(_FileFullName, Encoding.UTF8);


      //// HIER ÄNDERN //

      //return Regex.Matches(rawContent, _RegexSearchDep).Select((m) => {
      //  string[] slt = m.Value.Split('"');
      //  return new DependencyInfo(slt[1], slt[3]);
      //}).ToArray();
    }

    /// <summary>
    /// Reads all NuSpec dependencies, including framework-specific dependency groups.
    /// </summary>
    private DependencyInfo[] ReadNuspecDependencies(XElement dependenciesElement, bool includeFrameworkInfo) {
      XNamespace xmlNamespace = dependenciesElement.Name.Namespace;
      XName dependencyName = xmlNamespace + "dependency";
      XName groupName = xmlNamespace + "group";

      DependencyInfo[] rootDependencies = dependenciesElement
        .Elements(dependencyName)
        .Select((dependencyElement) => {
          return this.ReadNuspecDependencyElement(dependencyElement, null, includeFrameworkInfo);
        })
        .Where((dependencyInfo) => {
          return dependencyInfo != null;
        })
        .ToArray();

      DependencyInfo[] groupedDependencies = dependenciesElement
        .Elements(groupName)
        .SelectMany((groupElement) => {
          string targetFramework = this.ReadAttributeValue(groupElement, "targetFramework");

          return groupElement
            .Elements(dependencyName)
            .Select((dependencyElement) => {
              return this.ReadNuspecDependencyElement(dependencyElement, targetFramework, includeFrameworkInfo);
            });
        })
        .Where((dependencyInfo) => {
          return dependencyInfo != null;
        })
        .ToArray();

      return rootDependencies
        .Concat(groupedDependencies)
        .ToArray();
    }

    /// <summary>
    /// Overwrites NuSpec dependencies while preserving the existing root/group structure as far as possible.
    /// </summary>
    private bool OverwriteNuspecDependencies(XElement dependenciesElement, DependencyInfo[] newDependencies) {
      XNamespace xmlNamespace = dependenciesElement.Name.Namespace;
      XName dependencyName = xmlNamespace + "dependency";
      XName groupName = xmlNamespace + "group";

      XElement[] groupElements = dependenciesElement
        .Elements(groupName)
        .ToArray();

      bool hasGroups = groupElements.Length > 0;
      bool hasFrameworkSpecificInput = newDependencies.Any((dependencyInfo) => {
        return dependencyInfo != null && !string.IsNullOrWhiteSpace(dependencyInfo.DedicatedToTargetFramework);
      });

      bool useGroups = hasGroups || hasFrameworkSpecificInput;
      bool fileChanged = false;

      if (useGroups) {
        XElement[] rootDependencyElements = dependenciesElement
          .Elements(dependencyName)
          .ToArray();

        foreach (XElement rootDependencyElement in rootDependencyElements) {
          rootDependencyElement.Remove();
          fileChanged = true;
        }

        string[] requiredFrameworks = this.GetRequiredNuspecTargetFrameworks(groupElements, newDependencies);

        foreach (string targetFramework in requiredFrameworks) {
          XElement groupElement = this.GetOrCreateNuspecDependencyGroup(dependenciesElement, targetFramework);
          DependencyInfo[] dependenciesForGroup = this.GetDependenciesForNuspecGroup(newDependencies, targetFramework);

          if (this.OverwriteNuspecDependencyElementsInContainer(groupElement, dependenciesForGroup)) {
            fileChanged = true;
          }
        }
      }
      else {
        if (this.OverwriteNuspecDependencyElementsInContainer(dependenciesElement, newDependencies)) {
          fileChanged = true;
        }
      }

      return fileChanged;
    }

    /// <summary>
    /// Reads one dependency XML element into a dependency info object.
    /// </summary>
    private DependencyInfo ReadNuspecDependencyElement(
      XElement dependencyElement,
      string targetFramework,
      bool includeFrameworkInfo
    ) {
      string packageId = this.ReadAttributeValue(dependencyElement, "id");
      string version = this.ReadAttributeValue(dependencyElement, "version");

      if (string.IsNullOrWhiteSpace(packageId)) {
        return null;
      }

      if (string.IsNullOrWhiteSpace(version)) {
        return null;
      }

      DependencyInfo dependencyInfo = new DependencyInfo(packageId, version);

      if (includeFrameworkInfo && !string.IsNullOrWhiteSpace(targetFramework)) {
        dependencyInfo.DedicatedToTargetFramework = targetFramework;
      }

      return dependencyInfo;
    }

    /// <summary>
    /// Updates dependency elements inside one concrete NuSpec dependency container.
    /// </summary>
    private bool OverwriteNuspecDependencyElementsInContainer(
      XElement containerElement,
      DependencyInfo[] dependencies
    ) {
      XNamespace xmlNamespace = containerElement.Name.Namespace;
      XName dependencyName = xmlNamespace + "dependency";

      bool fileChanged = false;

      XElement[] existingDependencyElements = containerElement
        .Elements(dependencyName)
        .ToArray();

      foreach (XElement dependencyElement in existingDependencyElements) {
        string packageId = this.ReadAttributeValue(dependencyElement, "id");

        DependencyInfo matchingDependency = dependencies
          .FirstOrDefault((dependencyInfo) => {
            return dependencyInfo != null &&
              string.Equals(
                dependencyInfo.TargetPackageId,
                packageId,
                StringComparison.OrdinalIgnoreCase
              );
          });

        if (matchingDependency == null) {
          dependencyElement.Remove();
          fileChanged = true;
        }
        else {
          string version = matchingDependency.TargetPackageVersionConstraint.ToString();

          XAttribute versionAttribute = dependencyElement.Attribute("version");

          if (versionAttribute == null) {
            dependencyElement.SetAttributeValue("version", version);
            fileChanged = true;
          }
          else if (!string.Equals(versionAttribute.Value, version, StringComparison.Ordinal)) {
            versionAttribute.Value = version;
            fileChanged = true;
          }
        }
      }

      foreach (DependencyInfo dependencyInfo in dependencies) {
        bool alreadyExists = containerElement
          .Elements(dependencyName)
          .Any((dependencyElement) => {
            string packageId = this.ReadAttributeValue(dependencyElement, "id");

            return string.Equals(
              packageId,
              dependencyInfo.TargetPackageId,
              StringComparison.OrdinalIgnoreCase
            );
          });

        if (!alreadyExists) {
          XElement dependencyElement = new XElement(
            dependencyName,
            new XAttribute("id", dependencyInfo.TargetPackageId),
            new XAttribute("version", dependencyInfo.TargetPackageVersionConstraint.ToString())
          );

          this.AddElementWithNuspecIndent(containerElement, dependencyElement);
          fileChanged = true;
        }
      }

      return fileChanged;
    }

    /// <summary>
    /// Gets all target frameworks that need a NuSpec dependency group.
    /// </summary>
    private string[] GetRequiredNuspecTargetFrameworks(XElement[] existingGroupElements, DependencyInfo[] newDependencies) {
      string[] existingFrameworks = existingGroupElements
        .Select((groupElement) => {
          return this.ReadAttributeValue(groupElement, "targetFramework");
        })
        .Where((targetFramework) => {
          return !string.IsNullOrWhiteSpace(targetFramework);
        })
        .ToArray();

      string[] dependencyFrameworks = newDependencies
        .Where((dependencyInfo) => {
          return dependencyInfo != null && !string.IsNullOrWhiteSpace(dependencyInfo.DedicatedToTargetFramework);
        })
        .Select((dependencyInfo) => {
          return dependencyInfo.DedicatedToTargetFramework.Trim();
        })
        .ToArray();

      return existingFrameworks
        .Concat(dependencyFrameworks)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    /// <summary>
    /// Gets the dependencies that belong into one concrete NuSpec dependency group.
    /// Dependencies without a dedicated target framework act as wildcard dependencies.
    /// </summary>
    private DependencyInfo[] GetDependenciesForNuspecGroup(DependencyInfo[] newDependencies, string targetFramework) {
      return newDependencies
        .Where((dependencyInfo) => {
          if (dependencyInfo == null) {
            return false;
          }

          if (string.IsNullOrWhiteSpace(dependencyInfo.DedicatedToTargetFramework)) {
            return true;
          }

          return string.Equals(
            dependencyInfo.DedicatedToTargetFramework.Trim(),
            targetFramework,
            StringComparison.OrdinalIgnoreCase
          );
        })
        .ToArray();
    }

    /// <summary>
    /// Gets or creates a NuSpec dependency group for the given target framework.
    /// </summary>
    private XElement GetOrCreateNuspecDependencyGroup(XElement dependenciesElement, string targetFramework) {
      XNamespace xmlNamespace = dependenciesElement.Name.Namespace;
      XName groupName = xmlNamespace + "group";

      XElement groupElement = dependenciesElement
        .Elements(groupName)
        .FirstOrDefault((candidate) => {
          string candidateTargetFramework = this.ReadAttributeValue(candidate, "targetFramework");

          return string.Equals(
            candidateTargetFramework,
            targetFramework,
            StringComparison.OrdinalIgnoreCase
          );
        });

      if (groupElement != null) {
        return groupElement;
      }

      groupElement = new XElement(
        groupName,
        new XAttribute("targetFramework", targetFramework)
      );

      this.AddElementWithNuspecIndent(dependenciesElement, groupElement);
      return groupElement;
    }

    /// <summary>
    /// Gets the NuSpec metadata dependencies element if it already exists.
    /// </summary>
    private XElement GetDependenciesElement(XDocument document) {
      XElement rootElement = document.Root;

      if (rootElement == null) {
        return null;
      }

      XNamespace xmlNamespace = rootElement.Name.Namespace;
      XName metadataName = xmlNamespace + "metadata";
      XName dependenciesName = xmlNamespace + "dependencies";

      XElement metadataElement = rootElement.Element(metadataName);

      if (metadataElement == null) {
        return null;
      }

      return metadataElement.Element(dependenciesName);
    }

    /// <summary>
    /// Gets or creates the NuSpec metadata dependencies element.
    /// </summary>
    private XElement GetOrCreateDependenciesElement(XDocument document) {
      XElement rootElement = document.Root;

      if (rootElement == null) {
        throw new InvalidDataException("The NuSpec file does not contain a root element.");
      }

      XNamespace xmlNamespace = rootElement.Name.Namespace;
      XName metadataName = xmlNamespace + "metadata";
      XName dependenciesName = xmlNamespace + "dependencies";

      XElement metadataElement = rootElement.Element(metadataName);

      if (metadataElement == null) {
        throw new InvalidDataException("The NuSpec file does not contain a metadata element.");
      }

      XElement dependenciesElement = metadataElement.Element(dependenciesName);

      if (dependenciesElement == null) {
        dependenciesElement = new XElement(dependenciesName);

        metadataElement.Add(
          new XText(Environment.NewLine + "    "),
          dependenciesElement,
          new XText(Environment.NewLine + "  ")
        );
      }

      return dependenciesElement;
    }

    /// <summary>
    /// Adds an XML element using the normal NuSpec indentation.
    /// </summary>
    private void AddElementWithNuspecIndent(XElement containerElement, XElement childElement) {
      containerElement.Add(
        new XText(Environment.NewLine + "      "),
        childElement,
        new XText(Environment.NewLine + "    ")
      );
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

  }

  /// Provides read and write access to the NuGet package specification file (*.nuspec) of a project.
  ///
  /// The accessor is responsible for extracting and maintaining package metadata,
  /// version information and dependency declarations while preserving the original
  /// document structure as much as possible.
  ///
  /// General responsibilities:
  ///
  /// - Reads package metadata from the nuspec file, including version information,
  ///   dependency declarations and additional package-related metadata.
  /// - Converts dependency declarations from XML into DependencyInfo instances,
  ///   allowing the remainder of the versioning infrastructure to work against a
  ///   normalized in-memory representation.
  /// - Writes modified dependency information back into the nuspec file while
  ///   preserving unrelated XML nodes, custom metadata and manual formatting
  ///   whenever possible.
  /// - Serves as an abstraction layer between higher-level tooling and the physical
  ///   nuspec file representation.
  /// - Allows dependency synchronization logic to work independently of the actual
  ///   XML layout chosen within the nuspec file.
  /// - Can be used by packaging tools, build tooling, version management utilities,
  ///   release automation pipelines and validation processes.
  ///
  /// Dependency handling:
  ///
  /// NuGet allows dependencies to be declared in multiple ways:
  ///
  /// 1. Flat dependency list:
  ///
  ///   <dependencies>
  ///     <dependency id="PackageA" version="1.0.0" />
  ///     <dependency id="PackageB" version="2.0.0" />
  ///   </dependencies>
  ///
  /// 2. Framework-specific dependency groups:
  ///
  ///   <dependencies>
  ///     <group targetFramework=".NETFramework4.8">
  ///       <dependency id="PackageA" version="1.0.0" />
  ///     </group>
  ///     <group targetFramework="net8.0">
  ///       <dependency id="PackageA" version="2.0.0" />
  ///     </group>
  ///   </dependencies>
  ///
  /// The accessor supports both formats transparently.
  ///
  /// Reading dependencies:
  ///
  /// - Dependencies located directly below the <dependencies> node are treated as
  ///   framework-independent dependencies.
  /// - Dependencies located inside a <group> node are treated as framework-specific
  ///   dependencies.
  /// - When framework information is requested, the corresponding targetFramework
  ///   attribute value is stored in DependencyInfo.DedicatedToTargetFramework.
  /// - The caller therefore receives a flat DependencyInfo collection regardless of
  ///   the original XML layout.
  ///
  /// Writing dependencies:
  ///
  /// - Existing dependency entries are updated in-place whenever possible.
  /// - Existing XML structure should be preserved as far as possible.
  /// - Unrelated XML nodes, metadata sections and comments must remain untouched.
  /// - Dependency updates are performed against the existing dependency container
  ///   rather than rebuilding the entire nuspec document.
  ///
  /// Framework-specific dependency routing:
  ///
  /// - If the nuspec already contains framework-specific dependency groups, those
  ///   groups are considered authoritative.
  /// - Dependencies carrying a DedicatedToTargetFramework value are routed into
  ///   the matching group.
  /// - If a matching group does not exist, it may be created automatically.
  /// - Dependencies without a DedicatedToTargetFramework value are treated as
  ///   framework-independent wildcard dependencies.
  ///
  /// Wildcard dependency behavior:
  ///
  /// - When no dependency groups exist, wildcard dependencies are written as
  ///   normal root-level dependencies below the <dependencies> node.
  /// - When dependency groups exist, wildcard dependencies are replicated into
  ///   every relevant framework group.
  /// - This guarantees consistent dependency versions across all framework-specific
  ///   package variants while avoiding ambiguity between root-level and grouped
  ///   dependency declarations.
  ///
  /// Duplicate avoidance:
  ///
  /// - A dependency should never exist both as a framework-specific dependency and
  ///   simultaneously as a root-level dependency.
  /// - Once framework-specific groups are used, dependency management should occur
  ///   exclusively inside those groups.
  /// - Root-level dependency declarations are therefore treated as obsolete in
  ///   grouped scenarios and are removed or ignored as necessary.
  /// - This prevents version conflicts and ambiguity during package restore.
  ///
  /// Framework targeting:
  ///
  /// - Multiple framework-specific dependency definitions may coexist within the
  ///   same nuspec file.
  /// - Different versions of the same package may therefore be associated with
  ///   different target frameworks.
  /// - Example:
  ///
  ///   DependencyInfo(
  ///     Package = "Foo",
  ///     Version = "1.0.0",
  ///     Framework = "net8.0"
  ///   )
  ///
  ///   DependencyInfo(
  ///     Package = "Foo",
  ///     Version = "2.0.0",
  ///     Framework = "net10.0"
  ///   )
  ///
  ///   will be routed into the corresponding target framework groups.
  ///
  /// Version handling:
  ///
  /// - Dependency version constraints are preserved and written using the
  ///   VersionContraint representation used throughout the versioning subsystem.
  /// - Exact versions, minimum versions and version ranges are supported.
  /// - Existing version constraints are updated without modifying unrelated
  ///   dependency metadata.
  ///
  /// Structural preservation:
  ///
  /// - XML namespaces must be preserved.
  /// - Existing metadata elements must remain unchanged.
  /// - Existing dependency group ordering should remain stable.
  /// - Existing comments should remain untouched whenever possible.
  /// - Manual formatting should be disturbed as little as practical.
  /// - The accessor must never perform a destructive rewrite of the entire
  ///   nuspec file solely to update dependency information.
  ///
  /// Intended usage scenarios:
  ///
  /// - Package version management.
  /// - Dependency synchronization between projects and packages.
  /// - Automated release pipelines.
  /// - Build and packaging automation.
  /// - Package validation tooling.
  /// - Migration of dependency versions across multiple frameworks.
  /// - Inspection and reporting of package metadata.
  /// - Generation and maintenance of NuGet package specifications.
  ///
  /// The overall design goal is to provide a deterministic and minimally invasive
  /// manipulation layer for nuspec files while supporting both legacy and modern
  /// dependency declaration styles used by NuGet.


}
