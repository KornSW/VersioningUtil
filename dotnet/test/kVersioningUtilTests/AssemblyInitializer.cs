using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace Logging {

  [TestClass]
  public class AssemblyInitializer {

    [AssemblyInitialize]
    public static void InitializeAssembly(TestContext testContext) {
    
    }

  }

}
