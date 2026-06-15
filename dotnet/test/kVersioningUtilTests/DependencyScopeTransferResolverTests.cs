using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace Versioning.Tests {

  [TestClass]
  public class DependencyScopeTransferResolverTests {

    [DataTestMethod]
    [DataRow(false, false, true, true, null, "=")]
    [DataRow(true, false, false, false, null, "net8.0")]
    [DataRow(true, true, false, false, "net8.0", "=")]
    [DataRow(true, true, false, false, "net8.0", "net8.0")]
    [DataRow(true, true, true, true, "net8.0", "")]
    [DataRow(false, false, false, false, "net8.0", "net8.0")]
    [DataRow(true, false, true, true, null, "=")]
    [DataRow(true, false, true, true, null, "")]
    public void Resolve_ShouldFailForInvalidScopeTransfers(
      bool sourceCanRepresentScopes,
      bool sourceUsesScopes,
      bool targetCanRepresentScopes,
      bool targetUsesScopes,
      string sourceScope,
      string configuredTargetScope
    ) {

      DependencyScopeTransferResolver resolver = this.CreateResolver(
        sourceCanRepresentScopes,
        sourceUsesScopes,
        targetCanRepresentScopes,
        targetUsesScopes
      );

      DependencyScopeTransferResult result = resolver.Resolve(
        sourceScope,
        configuredTargetScope
      );

      Assert.IsFalse(result.IsSuccess);
      Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));

    }


    [DataTestMethod]
    [DataRow(false, false, false, false, null, null, null)]
    [DataRow(false, false, false, false, null, "", null)]
    [DataRow(false, false, false, false, null, "*", null)]
    [DataRow(false, false, false, false, "net8.0", "=", null)]
    [DataRow(false, false, true, true, "net8.0", null, "net8.0")]
    [DataRow(false, false, true, true, "net8.0", "=", "net8.0")]
    [DataRow(false, false, true, true, "net8.0", "*", "net8.0;net10.0")]
    [DataRow(false, false, true, true, "net8.0", "net10.0", "net10.0")]
    [DataRow(true, false, true, true, null, null, "net8.0;net10.0")]
    [DataRow(true, false, true, true, null, "*", "net8.0;net10.0")]
    [DataRow(true, true, true, true, "net8.0", null, "net8.0")]
    [DataRow(true, true, true, true, "net8.0", "=", "net8.0")]
    [DataRow(true, true, true, true, "net8.0", "*", "net8.0")]
    [DataRow(true, true, true, true, "net8.0", "net10.0", "net10.0")]
    [DataRow(true, true, false, false, "net8.0", null, null)]
    [DataRow(true, true, false, false, "net8.0", "", null)]
    [DataRow(true, true, false, false, "net10.0", "*", null)]
    public void Resolve_ShouldReturnExpectedTargetScopes(
      bool sourceCanRepresentScopes,
      bool sourceUsesScopes,
      bool targetCanRepresentScopes,
      bool targetUsesScopes,
      string sourceScope,
      string configuredTargetScope,
      string expectedTargetScopesJoined
    ) {

      DependencyScopeTransferResolver resolver = this.CreateResolver(
        sourceCanRepresentScopes,
        sourceUsesScopes,
        targetCanRepresentScopes,
        targetUsesScopes
      );

      DependencyScopeTransferResult result = resolver.Resolve(
        sourceScope,
        configuredTargetScope
      );

      Assert.IsTrue(result.IsSuccess, result.ErrorMessage);

      string[] expectedTargetScopes = this.SplitScopes(expectedTargetScopesJoined);

      CollectionAssert.AreEqual(
        expectedTargetScopes,
        result.TargetScopes
      );
    }

    private DependencyScopeTransferResolver CreateResolver(
      bool sourceCanRepresentScopes,
      bool sourceUsesScopes,
      bool targetCanRepresentScopes,
      bool targetUsesScopes
    ) {

      return new DependencyScopeTransferResolver(
        sourceCanRepresentScopes,
        sourceUsesScopes,
        targetCanRepresentScopes,
        targetUsesScopes,
        new string[] {
          "net8.0",
          "net10.0"
        }
      );

    }

    private string[] SplitScopes(string joinedScopes) {

      if (joinedScopes == null) {
        return new string[] {
          null
        };
      }

      if (joinedScopes == string.Empty) {
        return new string[0];
      }

      return joinedScopes.Split(';').ToArray();
    }

  }

}