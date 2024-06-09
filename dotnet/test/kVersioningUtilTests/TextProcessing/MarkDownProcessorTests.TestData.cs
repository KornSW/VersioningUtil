using System;

namespace Versioning.TextProcessing {

  public partial class MarkDownProcessorTests {

    private const string V010Expected = (
@"# Change log
## Upcoming Changes

*(none)*



## v 0.1.0
released **2024-02-01**, including:
 - new revision without significant changes



");

    private const string V011Expected = (
@"# Change log
## Upcoming Changes

*(none)*



## v 0.1.1
released **2024-02-01**, including:
 - new revision without significant changes



## v 0.1.0
released **2024-02-01**, including:
 - new revision without significant changes



");

  }

}
