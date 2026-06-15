using System;
using System.Linq;

/// <summary>
/// Resolves how one dependency scope should be mapped from a source container to a target container.
/// </summary>
public class DependencyScopeTransferResolver {

  private readonly bool _SourceCanRepresentScopes;
  private readonly bool _SourceUsesScopes;
  private readonly bool _TargetCanRepresentScopes;
  private readonly bool _TargetUsesScopes;
  private readonly string[] _TargetScopes;

  public DependencyScopeTransferResolver(
    bool sourceCanRepresentScopes,
    bool sourceUsesScopes,
    bool targetCanRepresentScopes,
    bool targetUsesScopes,
    string[] targetScopes
  ) {
    _SourceCanRepresentScopes = sourceCanRepresentScopes;
    _SourceUsesScopes = sourceUsesScopes;
    _TargetCanRepresentScopes = targetCanRepresentScopes;
    _TargetUsesScopes = targetUsesScopes;

    if (targetScopes == null) {
      _TargetScopes = new string[0];
    }
    else {
      _TargetScopes = targetScopes;
    }
  }

  public DependencyScopeTransferResult Resolve(
  string sourceScope,
  string configuredTargetScope
) {
    string sourceScopeNormalized = this.NormalizeScope(sourceScope);
    string configuredTargetScopeNormalized = this.NormalizeScope(configuredTargetScope);

    bool hasConcreteTargetScope = this.IsConcreteScope(configuredTargetScopeNormalized);

    if (_SourceUsesScopes && string.IsNullOrWhiteSpace(sourceScopeNormalized)) {
      return DependencyScopeTransferResult.Fail(
         "Source uses scopes, but the provided dependency has no scope."
      );
    }

    if (_TargetUsesScopes && string.Equals(configuredTargetScopeNormalized, "", StringComparison.OrdinalIgnoreCase)) {
      return DependencyScopeTransferResult.Fail(
         "Target uses dependency scopes, but root is requested as target."
      );
    }

    if (hasConcreteTargetScope && !_TargetUsesScopes) {
      return DependencyScopeTransferResult.Fail(
        "A concrete dependency scope was requested, but the target does not use dependency scopes."
      );
    }

    if (hasConcreteTargetScope && !_TargetScopes.Contains(configuredTargetScopeNormalized, StringComparer.OrdinalIgnoreCase)) {
      return DependencyScopeTransferResult.Fail(
        "A concrete dependency scope was requested, but the target does not contain this dependency scope: " + configuredTargetScopeNormalized
      );
    }

    if (string.Equals(configuredTargetScopeNormalized, "", StringComparison.OrdinalIgnoreCase) && _TargetUsesScopes) {
      configuredTargetScopeNormalized = "*";
      //return DependencyScopeTransferResult.Fail(
      //  "Root-level dependency output was requested, but the target currently uses dependency scopes."
      //);
    }

    if (string.Equals(configuredTargetScopeNormalized, "=", StringComparison.OrdinalIgnoreCase)) {

      if (!_TargetUsesScopes) {

        if (_SourceUsesScopes) {

          return DependencyScopeTransferResult.Fail(
           "Source-scope-preserving dependency output was requested, but the target not using scopes."
         );

        }
        else {

          //in this case "=" was ment to say 'from root to root'
          configuredTargetScopeNormalized = "";
        }

      }

      //if (_TargetUsesScopes && _SourceUsesScopes && string.IsNullOrWhiteSpace(sourceScopeNormalized)) {
      if (_TargetUsesScopes && string.IsNullOrWhiteSpace(sourceScopeNormalized)) {
        return DependencyScopeTransferResult.Fail(
          "Source-scope-preserving dependency output was requested, but the source dependency has no scope."
        );
      }

    }

    string effectiveMode = configuredTargetScopeNormalized;

    if (effectiveMode == null) {
      effectiveMode = this.ResolveAutoMode(sourceScopeNormalized);
    }

    if (!_TargetUsesScopes) {
      return DependencyScopeTransferResult.Success(new string[] { null });
    }

    if (this.IsConcreteScope(effectiveMode)) {
      return DependencyScopeTransferResult.Success(new string[] { effectiveMode });
    }

    if (string.Equals(effectiveMode, "*", StringComparison.OrdinalIgnoreCase)) {
      if (_SourceUsesScopes && !string.IsNullOrWhiteSpace(sourceScopeNormalized)) {
        return DependencyScopeTransferResult.Success(new string[] { sourceScopeNormalized });
      }

      return DependencyScopeTransferResult.Success(_TargetScopes);
    }

    if (string.Equals(effectiveMode, "=", StringComparison.OrdinalIgnoreCase)) {
      if (!string.IsNullOrWhiteSpace(sourceScopeNormalized)) {
        return DependencyScopeTransferResult.Success(new string[] { sourceScopeNormalized });
      }

      return DependencyScopeTransferResult.Success(_TargetScopes);
    }

    return DependencyScopeTransferResult.Success(new string[] { null });
  }

  ///// <summary>
  ///// Resolves the target scopes for one dependency.
  ///// </summary>
  //public DependencyScopeTransferResult Resolve(
  //  string sourceScope,
  //  string configuredTargetScope
  //) {
  //  string mode = configuredTargetScope;

  //  if (mode == null) {
  //    mode = this.ResolveAutoMode(sourceScope);
  //  }

  //  if (mode == "") {
  //    return this.ResolveRootMode();
  //  }

  //  if (mode == "*") {
  //    return this.ResolveAllTargetScopesMode(sourceScope);
  //  }

  //  if (mode == "=") {
  //    return this.ResolveSameAsSourceMode(sourceScope);
  //  }

  //  return this.ResolveConcreteScopeMode(mode);
  //}

  /// <summary>
  /// Resolves the automatic mode from endpoint capabilities.
  /// </summary>
  private string ResolveAutoMode(string sourceScope) {
    if (!_TargetUsesScopes) {
      return "";
    }

    if (_SourceUsesScopes) {
      return "=";
    }

    if (string.IsNullOrWhiteSpace(sourceScope)) {
      return "*";
    }

    return "=";
  }

  /// <summary>
  /// Resolves root-level output.
  /// </summary>
  private DependencyScopeTransferResult ResolveRootMode() {
    if (_TargetUsesScopes) {
      return DependencyScopeTransferResult.Fail(
        "Root-level dependency output was requested, but the target currently uses dependency scopes."
      );
    }

    return DependencyScopeTransferResult.Success(new string[] { null });
  }

  /// <summary>
  /// Resolves all-target-scopes output.
  /// </summary>
  private DependencyScopeTransferResult ResolveAllTargetScopesMode(string sourceScope) {
    if (!_TargetUsesScopes) {
      return DependencyScopeTransferResult.Success(new string[] { null });
    }

    return DependencyScopeTransferResult.Success(_TargetScopes);
  }

  /// <summary>
  /// Resolves source-scope-preserving output.
  /// </summary>
  private DependencyScopeTransferResult ResolveSameAsSourceMode(string sourceScope) {
    if (!_TargetUsesScopes) {
      if (_SourceUsesScopes) {
        return DependencyScopeTransferResult.Fail(
          "Source-scoped dependency output was requested, but the target does not use dependency scopes."
        );
      }

      return DependencyScopeTransferResult.Success(new string[] { null });
    }

    if (string.IsNullOrWhiteSpace(sourceScope)) {
      return DependencyScopeTransferResult.Fail(
        "Source-scope-preserving dependency output was requested, but the source dependency has no scope."
      );
    }

    return DependencyScopeTransferResult.Success(new string[] { sourceScope });
  }

  /// <summary>
  /// Resolves output to one concrete target scope.
  /// </summary>
  private DependencyScopeTransferResult ResolveConcreteScopeMode(string targetScope) {
    if (!_TargetUsesScopes) {
      return DependencyScopeTransferResult.Fail(
        "A concrete dependency scope was requested, but the target does not use dependency scopes."
      );
    }

    return DependencyScopeTransferResult.Success(new string[] { targetScope });
  }

  private string NormalizeScope(string scope) {
    if (scope == null) {
      return null;
    }

    return scope.Trim();
  }

  private bool IsConcreteScope(string scope) {
    if (string.IsNullOrWhiteSpace(scope)) {
      return false;
    }

    if (string.Equals(scope, "*", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    if (string.Equals(scope, "=", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    return true;
  }

}