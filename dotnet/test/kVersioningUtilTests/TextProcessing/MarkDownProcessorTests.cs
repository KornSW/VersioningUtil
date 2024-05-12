using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Versioning.TextProcessing {

  [TestClass]
  public class MarkDownProcessorTests {

    [TestMethod]
    public void ProcessMarkdownAndCreateNewVersion_VariousTestPatterns_ProduceExpectedResults() {

      List<string> allLines = new List<string>();

      allLines.Add(Conventions.startMarker);

      MarkDownProcessor.ProcessMarkdownAndCreateNewVersion(allLines);

// [0] "# Change log"  
// [1] "## Upcoming Changes" 
// [2] ""  
// [3] "*(none)*"  
// [4] ""  
// [5] ""  
// [6] ""  
// [7] "## v 0.1.0"  
// [8] "released **2024-05-12**, including:" 
// [9] " - new revision without significant changes" 
// [10]  ""  
// [11]  ""  
// [12]  ""  

// changeGrade "fix" 
// currentFix  0 int
// currentMajor  0 int
// currentMinor  1 int
// currentVersion  "0.1.0" 
// currentVersionWithSuffix  "0.1.0" 
// preReleaseSuffix  ""  
// previousVersion "0.0.0" 
// versionDateInfo "2024-05-12"  
// versionNotes  "- new revision without significant changes\r\n"  
// versionTimeInfo "17:36:09"  

    }

  }
}