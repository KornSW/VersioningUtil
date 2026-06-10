using FileIO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Utils;

namespace Versioning { 

  [TestClass]
  public class DependencyUpdateHelperTests {

    #region " Helper "

    private static DependencyInfo Entry(
      string targetPackageId, string dedicatedToTargetFramework, int major
    ) {
      return new DependencyInfo {
        TargetPackageId = targetPackageId,
        DedicatedToTargetFramework = dedicatedToTargetFramework,
        TargetPackageVersionConstraint = new VersionContraint(major.ToString() + ".0.0")
      };
    }

    internal static void AssertI(
      DependencyInfo[] dependencies, int index,
      string targetPackageId, string dedicatedToTargetFramework, int major
    ) { 
      Assert.IsTrue(index < dependencies.Length, $"Index {index} is out of range for dependencies of length {dependencies.Length}");
      Assert.AreEqual(targetPackageId, dependencies[index].TargetPackageId, $"TargetPackageId mismatch at index {index}");
      Assert.AreEqual(dedicatedToTargetFramework, dependencies[index].DedicatedToTargetFramework, $"DedicatedToTargetFramework mismatch at index {index}");
      Assert.AreEqual(major.ToString() + ".0.0", dependencies[index].TargetPackageVersionConstraint.ConstraintPattern, $"TargetPackageVersionConstraint mismatch at index {index}");
    }

    #endregion

    private static IEnumerable<DependencyInfo> CurrentPackages1() {
      yield return Entry("GlobalPkg", null, 1);
      yield return Entry("AnotherGlobalPkg", null, 1);
      yield return Entry("Net8Pkg", "net8.0", 1);
      yield return Entry("Net10Pkg", "net10.0", 1);
      yield return Entry("DoublePkg", "net8.0", 1);
      yield return Entry("DoublePkg", "net10.0", 1);
    }

    private static IEnumerable<DependencyInfo> IncommingPackages_GlobalOnly() {
      yield return Entry("GlobalPkg", null, 2);
      yield return Entry("DoublePkg", null, 2);
      yield return Entry("NewOneGlobal", null, 2);
      yield return Entry("NewOne8", "net8.0", 2);
      yield return Entry("NewOne10", "net10.0", 2);
      yield return Entry("NewOneDbl", "net8.0", 2);
      yield return Entry("NewOneDbl", "net10.0", 2);
    }

    [TestMethod()]
    public void DependencyUpdateTest_Unscoped() {
      DependencyInfo[] result = null;

      DependencyUpdateHelper helper = new DependencyUpdateHelper(
        CurrentPackages1, (newPackages) => result = newPackages.ToArray()
      );
      helper.SkipSorting = true;

      helper.WritePackageDependencies(
        IncommingPackages_GlobalOnly().ToArray(),
        addNew: true, updateExisiting: true, deleteOthers: true,
        allowDowngrade: false, onlyForTargetFramework: null
      );

      Assert.IsNotNull(result);
      Assert.AreEqual(8, result.Length);
      AssertI(result, 0, "GlobalPkg", null, 2);
      //~REM~(result, ~, "AnotherGlobalPkg", null, 2);
      //~REM~(result, ~, "Net8Pkg", "net8.0", 2);
      //~REM~(result, ~, "Net10Pkg", "net10.0", 2);
      AssertI(result, 1, "DoublePkg", "net8.0", 2);//non-specified fx should have acted as wildcard 
      AssertI(result, 2, "DoublePkg", "net10.0", 2);//non-specified fx should have acted as wildcard 
      AssertI(result, 3, "NewOneGlobal", null, 2);  //new entries added as-given
      AssertI(result, 4, "NewOne8", "net8.0", 2);   //new entries added as-given
      AssertI(result, 5, "NewOne10", "net10.0", 2); //new entries added as-given
      AssertI(result, 6, "NewOneDbl", "net8.0", 2);  //new entries added as-given
      AssertI(result, 7, "NewOneDbl", "net10.0", 2); //new entries added as-given
    }

    [TestMethod()]
    public void DependencyUpdateTest_Scoped() {
      DependencyInfo[] result = null;

      DependencyUpdateHelper helper = new DependencyUpdateHelper(
        CurrentPackages1, (newPackages) => result = newPackages.ToArray()
      );
      helper.SkipSorting = true;

      helper.WritePackageDependencies(
        IncommingPackages_GlobalOnly().ToArray(),
        addNew: true, updateExisiting: true, deleteOthers: true,
        allowDowngrade: false, onlyForTargetFramework: "net8.0"
      );

      Assert.IsNotNull(result);
      Assert.AreEqual(9, result.Length);
      AssertI(result, 0, "GlobalPkg", null, 1);//MUST NOT BE TOUCHED as it is out of scope
      AssertI(result, 1, "AnotherGlobalPkg", null, 1); //MUST NOT BE TOUCHED as it is out of scope
      //~REM~(result, ~, "Net8Pkg", "net8.0", 2);
      AssertI(result, 2, "Net10Pkg", "net10.0", 1);//MUST NOT BE TOUCHED as it is out of scope
      AssertI(result, 3, "DoublePkg", "net10.0", 1);//MUST NOT BE TOUCHED as it is out of scope
      AssertI(result, 4, "DoublePkg", "net8.0", 2);//non-specified fx should have acted as wildcard 
      AssertI(result, 5, "GlobalPkg", "net8.0", 2); //ADDED 'in-scope' because of explicit wish!
      AssertI(result, 6, "NewOneGlobal", "net8.0", 2);  //ADDED 'in-scope' because of explicit wish!
      AssertI(result, 7, "NewOne8", "net8.0", 2);   //new entries added as-given
      //~SKP~(result, ~, "NewOne10", "net10.0", 2); //MUST NOT BE ADDED as it is out of scope
      AssertI(result, 8, "NewOneDbl", "net8.0", 2);  //new entries added as-given
      //~SKP~(result, ~, "NewOneDbl", "net10.0", 2); //MUST NOT BE ADDED as it is out of scope

    }

  }

}
