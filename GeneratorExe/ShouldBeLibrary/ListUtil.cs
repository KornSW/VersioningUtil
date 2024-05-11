using System;
using System.Collections.Generic;
using System.Linq;

namespace Versioning.ShouldBeLibrary {

  public  class ListUtil {

    public  static int FindIndex(List<string> list, string searchString, int startAt = 0) {
      for (int i = startAt; i < list.Count(); i++) {
        if (list[i].Contains(searchString, StringComparison.InvariantCultureIgnoreCase)) {
          return i;
        }
      }
      return -1;
    }
  }
}
