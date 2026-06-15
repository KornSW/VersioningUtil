using FileIO;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace Versioning {

  public partial class KVersioningHelper {

    /// <summary>
    /// Pre-processes NuSpec files by reading exactly one KVU configuration from XML comments.
    /// </summary>
    public void ProcessNuspecFiles(
      string targetFilesToProcess,
      string metaDataSourceFile = "versioninfo.json"
    ) {
      IVersionContainer versionSource = this.InitializeVersionContainerByFileType(metaDataSourceFile);
      VersionInfo versionInfo = versionSource.ReadVersion();

      string[] nuspecFileFullNames = this.ListFiles(targetFilesToProcess, expandSolutionFiles: false);

      foreach (string nuspecFileFullName in nuspecFileFullNames) {

        if (!nuspecFileFullName.EndsWith(".nuspec", StringComparison.CurrentCultureIgnoreCase)) {
          continue;
        }

        Console.WriteLine($"Pre-processing NuSpec '{nuspecFileFullName}'...");

        KvuNuspecPreProcessorConfiguration configuration = this.ReadSingleKvuConfigurationFromNuspec(nuspecFileFullName);
        NuspecFileAccessor nuspecAccessor = new NuspecFileAccessor(nuspecFileFullName);

        nuspecAccessor.WriteVersion(versionInfo);

        this.ApplyKvuDependencySync(
          nuspecFileFullName,
          nuspecAccessor,
          configuration
        );

        this.ApplyKvuVersioningTwins(
          nuspecAccessor,
          configuration,
          versionInfo
        );
      }
    }

    /// <summary>
    /// Applies dependency synchronization from all configured sources.
    /// </summary>
    private void ApplyKvuDependencySync(
      string nuspecFileFullName,
      NuspecFileAccessor nuspecAccessor,
      KvuNuspecPreProcessorConfiguration configuration
    ) {
      if (configuration.DependencySync == null) {
        return;
      }

      if (configuration.DependencySync.Sources == null) {
        return;
      }

      bool targetNuspecHasGroups = this.NuspecHasDependencyGroups(nuspecFileFullName);
      string nuspecDirectoryName = Path.GetDirectoryName(nuspecFileFullName);

      foreach (KvuDependencySyncSourceConfiguration source in configuration.DependencySync.Sources) {
        if (source == null) {
          continue;
        }

        this.ApplyKvuDependencySyncSource(
          nuspecDirectoryName,
          nuspecAccessor,
          targetNuspecHasGroups,
          configuration,
          source
        );
      }
    }

    /// <summary>
    /// Applies dependency synchronization from one configured source.
    /// </summary>
    private void ApplyKvuDependencySyncSource(
      string nuspecDirectoryName,
      NuspecFileAccessor nuspecAccessor,
      bool targetNuspecHasGroups,
      KvuNuspecPreProcessorConfiguration configuration,
      KvuDependencySyncSourceConfiguration source
    ) {
      if (string.IsNullOrWhiteSpace(source.Origin)) {
        throw new InvalidDataException("The KVU dependencySync source contains no origin.");
      }

      string sourceFullName = Path.GetFullPath(
        Path.Combine(nuspecDirectoryName, source.Origin)
      );

      if (!File.Exists(sourceFullName)) {
        throw new FileNotFoundException(
          $"KVU source '{source.Origin}' resolved to '{sourceFullName}' but the file does not exist."
        );
      }

      IVersionContainer sourceContainer = this.InitializeVersionContainerByFileType(sourceFullName);

      string[] versioningTwins = this.NormalizeStringArray(configuration.VersioningTwins);

      string resolvedTargetFrameworkMode = this.ResolveKvuTargetFrameworkMode(
        source.TargetFramework,
        targetNuspecHasGroups
      );

      bool addMissing = this.ResolveKvuAddMissing(
        configuration.DependencySync,
        source
      );

      bool updateExisting = this.ResolveKvuUpdateExisting(
        configuration.DependencySync,
        source
      );

      bool allowDowngrades = this.ResolveKvuAllowDowngrades(
        configuration.DependencySync,
        source
      );

      bool removeOrphaned = this.ResolveKvuRemoveOrphaned(
        configuration.DependencySync,
        source
      );

      string constraintType = this.ResolveKvuConstraintType(
        configuration.DependencySync,
        source
      );

      string onlyForTargetFramework = this.ResolveKvuOnlyForTargetFramework(
        resolvedTargetFrameworkMode
      );

      DependencyInfo[] sourceDependencies = sourceContainer
        .ReadPackageDependencies(true)
        .Where((dependency) => {
          return dependency != null;
        })
        .Where((dependency) => {
          return !this.ContainsPackageId(versioningTwins, dependency.TargetPackageId);
        })
        .Where((dependency) => {
          return this.IsAllowedByKvuLists(
            dependency.TargetPackageId,
            configuration.DependencySync.Whitelist,
            configuration.DependencySync.Blacklist,
            source.Whitelist,
            source.Blacklist
          );
        })
        .Select((dependency) => {
          return this.CloneKvuDependencyForTargetFramework(
            dependency,
            resolvedTargetFrameworkMode,
            targetNuspecHasGroups
          );
        })
        .ToArray();

      this.ApplyKvuConstraintType(
        sourceDependencies,
        constraintType
      );

      nuspecAccessor.WritePackageDependencies(
        sourceDependencies,
        addMissing,
        updateExisting,
        removeOrphaned,
        allowDowngrades,
        onlyForTargetFramework
      );
    }

    /// <summary>
    /// Applies versioning twin dependencies after dependency synchronization.
    /// </summary>
    private void ApplyKvuVersioningTwins(
      NuspecFileAccessor nuspecAccessor,
      KvuNuspecPreProcessorConfiguration configuration,
      VersionInfo versionInfo
    ) {
      string[] versioningTwins = this.NormalizeStringArray(configuration.VersioningTwins);

      if (versioningTwins.Length == 0) {
        return;
      }

      DependencyInfo[] twinDependencies = versioningTwins
        .Select((packageId) => {
          return new DependencyInfo(packageId, "[" + versionInfo.currentVersionWithSuffix + "]");
        })
        .ToArray();

      nuspecAccessor.WritePackageDependencies(
        twinDependencies,
        true,
        true,
        false,
        true,
        null
      );
    }

    /// <summary>
    /// Resolves the target framework mode for one KVU source.
    /// </summary>
    private string ResolveKvuTargetFrameworkMode(
      string configuredTargetFramework,
      bool targetNuspecHasGroups
    ) {
      if (!string.IsNullOrWhiteSpace(configuredTargetFramework)) {
        return configuredTargetFramework.Trim();
      }

      if (targetNuspecHasGroups) {
        return "=";
      }

      return "";
    }

    /// <summary>
    /// Clones a dependency and applies the resolved target framework mode.
    /// </summary>
    private DependencyInfo CloneKvuDependencyForTargetFramework(
      DependencyInfo sourceDependency,
      string targetFrameworkMode,
      bool targetNuspecHasGroups
    ) {
      DependencyInfo clone = new DependencyInfo {
        TargetPackageId = sourceDependency.TargetPackageId,
        TargetPackageVersionConstraint = sourceDependency.TargetPackageVersionConstraint,
        DedicatedToTargetFramework = sourceDependency.DedicatedToTargetFramework
      };

      if (string.IsNullOrWhiteSpace(targetFrameworkMode)) {
        clone.DedicatedToTargetFramework = null;
        return clone;
      }

      if (string.Equals(targetFrameworkMode, "*", StringComparison.OrdinalIgnoreCase)) {
        clone.DedicatedToTargetFramework = null;
        return clone;
      }

      if (string.Equals(targetFrameworkMode, "=", StringComparison.OrdinalIgnoreCase)) {
        if (targetNuspecHasGroups && string.IsNullOrWhiteSpace(sourceDependency.DedicatedToTargetFramework)) {
          clone.DedicatedToTargetFramework = null;
          return clone;
        }

        clone.DedicatedToTargetFramework = sourceDependency.DedicatedToTargetFramework;
        return clone;
      }

      clone.DedicatedToTargetFramework = targetFrameworkMode;
      return clone;
    }

    /// <summary>
    /// Resolves the WritePackageDependencies framework filter.
    /// </summary>
    private string ResolveKvuOnlyForTargetFramework(string targetFrameworkMode) {
      if (string.IsNullOrWhiteSpace(targetFrameworkMode)) {
        return null;
      }

      if (string.Equals(targetFrameworkMode, "*", StringComparison.OrdinalIgnoreCase)) {
        return null;
      }

      if (string.Equals(targetFrameworkMode, "=", StringComparison.OrdinalIgnoreCase)) {
        return null;
      }

      return targetFrameworkMode;
    }

    /// <summary>
    /// Applies the configured constraint type using the existing version constraint methods.
    /// </summary>
    private void ApplyKvuConstraintType(
      DependencyInfo[] dependencies,
      string constraintType
    ) {
      foreach (DependencyInfo dependency in dependencies) {
        string cleanVersion = dependency.TargetPackageVersionConstraint.ToString(true);

        if (string.Equals(constraintType, "EXACT", StringComparison.OrdinalIgnoreCase)) {
          dependency.TargetPackageVersionConstraint.SetVersionShouldBeExact(cleanVersion);
        }
        else if (string.Equals(constraintType, "MIN", StringComparison.OrdinalIgnoreCase)) {
          dependency.TargetPackageVersionConstraint.SetVersionShouldBeGreaterThanOrEqual(cleanVersion);
        }
        else if (string.Equals(constraintType, "KEEP", StringComparison.OrdinalIgnoreCase)) {
        }
        else {
          dependency.TargetPackageVersionConstraint.SetVersionShouldBeInNonBreakingRange(cleanVersion);
        }
      }
    }

    /// <summary>
    /// Checks whether the package id passes global and source-specific white/blacklist rules.
    /// </summary>
    private bool IsAllowedByKvuLists(
      string packageId,
      string[] globalWhitelist,
      string[] globalBlacklist,
      string[] sourceWhitelist,
      string[] sourceBlacklist
    ) {
      if (!this.IsAllowedByWhitelist(packageId, globalWhitelist)) {
        return false;
      }

      if (!this.IsAllowedByWhitelist(packageId, sourceWhitelist)) {
        return false;
      }

      if (this.IsDeniedByBlacklist(packageId, globalBlacklist)) {
        return false;
      }

      if (this.IsDeniedByBlacklist(packageId, sourceBlacklist)) {
        return false;
      }

      return true;
    }

    /// <summary>
    /// Checks a whitelist. An empty whitelist allows everything.
    /// </summary>
    private bool IsAllowedByWhitelist(string packageId, string[] whitelist) {
      string[] normalizedWhitelist = this.NormalizeStringArray(whitelist);

      if (normalizedWhitelist.Length == 0) {
        return true;
      }

      return normalizedWhitelist.Any((pattern) => {
        return this.MatchesWildcardMask(packageId, pattern);
      });
    }

    /// <summary>
    /// Checks a blacklist. An empty blacklist denies nothing.
    /// </summary>
    private bool IsDeniedByBlacklist(string packageId, string[] blacklist) {
      string[] normalizedBlacklist = this.NormalizeStringArray(blacklist);

      return normalizedBlacklist.Any((pattern) => {
        return this.MatchesWildcardMask(packageId, pattern);
      });
    }

    /// <summary>
    /// Reads exactly one KVU configuration from XML comments of a NuSpec file.
    /// </summary>
    private KvuNuspecPreProcessorConfiguration ReadSingleKvuConfigurationFromNuspec(string nuspecFileFullName) {
      XDocument document = XDocument.Load(nuspecFileFullName, LoadOptions.PreserveWhitespace);

      KvuNuspecPreProcessorConfiguration[] configurations = document
        .DescendantNodes()
        .OfType<XComment>()
        .Select((comment) => {
          return this.TryReadKvuConfigurationFromComment(comment.Value);
        })
        .Where((configuration) => {
          return configuration != null;
        })
        .ToArray();

      if (configurations.Length == 0) {
        throw new InvalidDataException("The NuSpec file contains no KVU pre-processor configuration.");
      }

      if (configurations.Length > 1) {
        throw new InvalidDataException("The NuSpec file contains more than one KVU pre-processor configuration.");
      }

      return configurations[0];
    }

    /// <summary>
    /// Tries to read one KVU configuration from a single XML comment.
    /// </summary>
    private KvuNuspecPreProcessorConfiguration TryReadKvuConfigurationFromComment(string commentText) {
      if (string.IsNullOrWhiteSpace(commentText)) {
        return null;
      }

      string jsonText = this.ExtractJsonObjectFromComment(commentText);

      if (string.IsNullOrWhiteSpace(jsonText)) {
        return null;
      }

      try {
        JsonDocumentOptions documentOptions = new JsonDocumentOptions();
        documentOptions.AllowTrailingCommas = true;
        documentOptions.CommentHandling = JsonCommentHandling.Skip;

        using (JsonDocument jsonDocument = JsonDocument.Parse(jsonText, documentOptions)) {
          JsonElement rootElement = jsonDocument.RootElement;

          if (rootElement.ValueKind != JsonValueKind.Object) {
            return null;
          }

          JsonElement kvuElement;

          if (!this.TryGetJsonPropertyIgnoreCase(rootElement, "KVU", out kvuElement)) {
            kvuElement = rootElement;
          }

          JsonSerializerOptions serializerOptions = new JsonSerializerOptions();
          serializerOptions.PropertyNameCaseInsensitive = true;
          serializerOptions.AllowTrailingCommas = true;
          serializerOptions.ReadCommentHandling = JsonCommentHandling.Skip;

          KvuNuspecPreProcessorConfiguration configuration =
            JsonSerializer.Deserialize<KvuNuspecPreProcessorConfiguration>(
              kvuElement.GetRawText(),
              serializerOptions
            );

          return configuration;
        }
      }
      catch (JsonException ex) {
        throw new Exception("Found a \"KVU\": { ... json structure but failed to parse it: " + ex.Message);
      }
    }

    /// <summary>
    /// Extracts the first full JSON object from an XML comment.
    /// </summary>
    private string ExtractJsonObjectFromComment(string commentText) {
      int firstBraceIndex = commentText.IndexOf('{');

      if (firstBraceIndex < 0) {
        return null;
      }

      int depth = 0;
      bool insideString = false;
      bool escaped = false;

      for (int index = firstBraceIndex; index < commentText.Length; index++) {
        char currentChar = commentText[index];

        if (insideString) {
          if (escaped) {
            escaped = false;
          }
          else if (currentChar == '\\') {
            escaped = true;
          }
          else if (currentChar == '"') {
            insideString = false;
          }

          continue;
        }

        if (currentChar == '"') {
          insideString = true;
          continue;
        }

        if (currentChar == '{') {
          depth++;
        }
        else if (currentChar == '}') {
          depth--;

          if (depth == 0) {
            return commentText.Substring(firstBraceIndex, index - firstBraceIndex + 1);
          }
        }
      }

      return null;
    }

    /// <summary>
    /// Tries to get a JSON property by name using case-insensitive matching.
    /// </summary>
    private bool TryGetJsonPropertyIgnoreCase(
      JsonElement rootElement,
      string propertyName,
      out JsonElement propertyElement
    ) {
      foreach (JsonProperty property in rootElement.EnumerateObject()) {
        if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
          propertyElement = property.Value;
          return true;
        }
      }

      propertyElement = default;
      return false;
    }

    /// <summary>
    /// Determines whether the NuSpec file currently uses dependency groups.
    /// </summary>
    private bool NuspecHasDependencyGroups(string nuspecFileFullName) {
      XDocument document = XDocument.Load(nuspecFileFullName, LoadOptions.PreserveWhitespace);

      XElement rootElement = document.Root;

      if (rootElement == null) {
        return false;
      }

      XNamespace xmlNamespace = rootElement.Name.Namespace;
      XName metadataName = xmlNamespace + "metadata";
      XName dependenciesName = xmlNamespace + "dependencies";
      XName groupName = xmlNamespace + "group";

      XElement metadataElement = rootElement.Element(metadataName);

      if (metadataElement == null) {
        return false;
      }

      XElement dependenciesElement = metadataElement.Element(dependenciesName);

      if (dependenciesElement == null) {
        return false;
      }

      return dependenciesElement.Elements(groupName).Any();
    }

    /// <summary>
    /// Checks whether a package id is contained in a package id array.
    /// </summary>
    private bool ContainsPackageId(string[] packageIds, string packageId) {
      return packageIds.Any((candidate) => {
        return string.Equals(candidate, packageId, StringComparison.OrdinalIgnoreCase);
      });
    }

    /// <summary>
    /// Normalizes a string array by removing empty entries.
    /// </summary>
    private string[] NormalizeStringArray(string[] values) {
      if (values == null) {
        return new string[0];
      }

      return values
        .Where((value) => {
          return !string.IsNullOrWhiteSpace(value);
        })
        .Select((value) => {
          return value.Trim();
        })
        .ToArray();
    }

    /// <summary>
    /// Resolves the effective add-missing flag.
    /// </summary>
    private bool ResolveKvuAddMissing(
      KvuDependencySyncConfiguration globalConfiguration,
      KvuDependencySyncSourceConfiguration sourceConfiguration
    ) {
      if (sourceConfiguration.AddMissing.HasValue) {
        return sourceConfiguration.AddMissing.Value;
      }

      return globalConfiguration.AddMissing;
    }

    /// <summary>
    /// Resolves the effective update-existing flag.
    /// </summary>
    private bool ResolveKvuUpdateExisting(
      KvuDependencySyncConfiguration globalConfiguration,
      KvuDependencySyncSourceConfiguration sourceConfiguration
    ) {
      if (sourceConfiguration.UpdateExisting.HasValue) {
        return sourceConfiguration.UpdateExisting.Value;
      }

      return globalConfiguration.UpdateExisting;
    }

    /// <summary>
    /// Resolves the effective allow-downgrades flag.
    /// </summary>
    private bool ResolveKvuAllowDowngrades(
      KvuDependencySyncConfiguration globalConfiguration,
      KvuDependencySyncSourceConfiguration sourceConfiguration
    ) {
      if (sourceConfiguration.AllowDowngrades.HasValue) {
        return sourceConfiguration.AllowDowngrades.Value;
      }

      return globalConfiguration.AllowDowngrades;
    }

    /// <summary>
    /// Resolves the effective remove-orphaned flag.
    /// </summary>
    private bool ResolveKvuRemoveOrphaned(
      KvuDependencySyncConfiguration globalConfiguration,
      KvuDependencySyncSourceConfiguration sourceConfiguration
    ) {
      if (sourceConfiguration.RemoveOrphaned.HasValue) {
        return sourceConfiguration.RemoveOrphaned.Value;
      }

      return globalConfiguration.RemoveOrphaned;
    }

    /// <summary>
    /// Resolves the effective constraint type.
    /// </summary>
    private string ResolveKvuConstraintType(
      KvuDependencySyncConfiguration globalConfiguration,
      KvuDependencySyncSourceConfiguration sourceConfiguration
    ) {
      if (!string.IsNullOrWhiteSpace(sourceConfiguration.ConstraintLevel)) {
        return sourceConfiguration.ConstraintLevel;
      }

      if (!string.IsNullOrWhiteSpace(globalConfiguration.ConstraintLevel)) {
        return globalConfiguration.ConstraintLevel;
      }

      return "KEEP";
    }

  }

  /// <summary>
  /// Root configuration for KVU NuSpec pre-processing.
  /// </summary>
  public class KvuNuspecPreProcessorConfiguration {

    public string[] VersioningTwins { get; set; } = new string[0];

    public KvuDependencySyncConfiguration DependencySync { get; set; } = null;

  }

  /// <summary>
  /// Global dependency synchronization configuration.
  /// </summary>
  public class KvuDependencySyncConfiguration {

    public bool AddMissing { get; set; } = true;

    public bool UpdateExisting { get; set; } = true;

    public bool AllowDowngrades { get; set; } = true;

    public bool RemoveOrphaned { get; set; } = false;

    public string ConstraintLevel { get; set; } = "KEEP";

    public string[] Whitelist { get; set; } = new string[0];

    public string[] Blacklist { get; set; } = new string[0];

    public KvuDependencySyncSourceConfiguration[] Sources { get; set; } = new KvuDependencySyncSourceConfiguration[0];

  }

  /// <summary>
  /// Source-specific dependency synchronization configuration.
  /// </summary>
  public class KvuDependencySyncSourceConfiguration {

    public string Origin { get; set; } = null;

    public bool? AddMissing { get; set; } = null;

    public bool? UpdateExisting { get; set; } = null;

    public bool? AllowDowngrades { get; set; } = null;

    public bool? RemoveOrphaned { get; set; } = null;

    public string TargetFramework { get; set; } = null;

    public string ConstraintLevel { get; set; } = null;

    public string[] Whitelist { get; set; } = new string[0];

    public string[] Blacklist { get; set; } = new string[0];

  }

}
