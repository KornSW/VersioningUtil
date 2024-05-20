using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Versioning.ShouldBeLibrary {

  public class ListUtil {

    public static int FindRowIndex(List<string> list, string searchString, int startAt = 0) {
      for (int i = startAt; i < list.Count; i++) {
        if (list[i].Contains(searchString, StringComparison.InvariantCultureIgnoreCase)) {
          return i;
        }
      }
      return -1;
    }

    public static List<string> CreateFromText(string text) {
      return text.Split(new[] { "\r\n" }, StringSplitOptions.None).ToList(); // HACK: LinQ
    }

    public static string ToText(List<string>allLines) {
      StringBuilder builder = new StringBuilder(1024);
      foreach (string line in allLines) {
        builder.AppendLine(line);
      }
      return builder.ToString();
    }

  }
}
