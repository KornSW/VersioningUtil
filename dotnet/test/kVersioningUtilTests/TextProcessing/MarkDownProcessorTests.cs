using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Versioning.ShouldBeLibrary;

namespace Versioning.TextProcessing {

  [TestClass]
  public partial class MarkDownProcessorTests {

    [ClassInitialize]
    public static void InitializeOnceBeforeAllTests(TestContext context) {
      MarkDownProcessor._OnGetTimeStamp = () => new DateTime(2024, 2, 1, 15, 16, 17);
    }

    [TestMethod]
    public void ProcessMarkdownAndCreateNewVersion_VariousTestPatterns_ProduceExpectedResults() {

      List<string> allLines = new List<string>();

      string actualText;

      VersionInfo actualVersionInfo;

      // Starting with empty text (except an existing start marker) => should create Version 0.1.0

      allLines.Add(Conventions.startMarker);

      actualVersionInfo = MarkDownProcessor.ProcessMarkdownAndCreateNewVersion(allLines);

      actualText = ListUtil.ToText(allLines);

      Assert.AreEqual(V010Expected, actualText);

      Assert.AreEqual("fix", actualVersionInfo.changeGrade);
      Assert.AreEqual(0, actualVersionInfo.currentMajor);
      Assert.AreEqual(1, actualVersionInfo.currentMinor);
      Assert.AreEqual(0, actualVersionInfo.currentFix);
      Assert.AreEqual("0.1.0", actualVersionInfo.currentVersion);
      Assert.AreEqual("0.1.0", actualVersionInfo.currentVersionWithSuffix);
      Assert.AreEqual("", actualVersionInfo.preReleaseSuffix);
      Assert.AreEqual("0.0.0", actualVersionInfo.previousVersion);
      Assert.AreEqual("2024-02-01", actualVersionInfo.versionDateInfo);
      Assert.AreEqual("- new revision without significant changes\r\n", actualVersionInfo.versionNotes);

      // Don't add any specific info, just recycle => should create Version 0.1.1

      actualVersionInfo = MarkDownProcessor.ProcessMarkdownAndCreateNewVersion(allLines);

      actualText = ListUtil.ToText(allLines);

      Assert.AreEqual(V011Expected, actualText);

      Assert.AreEqual("fix", actualVersionInfo.changeGrade);
      Assert.AreEqual(0, actualVersionInfo.currentMajor);
      Assert.AreEqual(1, actualVersionInfo.currentMinor);
      Assert.AreEqual(1, actualVersionInfo.currentFix);
      Assert.AreEqual("0.1.1", actualVersionInfo.currentVersion);
      Assert.AreEqual("0.1.1", actualVersionInfo.currentVersionWithSuffix);
      Assert.AreEqual("", actualVersionInfo.preReleaseSuffix);
      Assert.AreEqual("0.1.0", actualVersionInfo.previousVersion);
      Assert.AreEqual("2024-02-01", actualVersionInfo.versionDateInfo);
      Assert.AreEqual("- new revision without significant changes\r\n", actualVersionInfo.versionNotes);

    }

  }
}