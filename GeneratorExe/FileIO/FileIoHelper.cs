using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FileIO {

  internal static class FileIoHelper {

    /// <summary>
    /// Detects the current file encoding by inspecting the byte order mark.
    /// Falls back to Encoding.Default to preserve legacy Visual Studio file behavior.
    /// </summary>
    public static Encoding DetectFileEncoding(string fileFullName) {
      byte[] bytes = File.ReadAllBytes(fileFullName);

      if (bytes.Length >= 3) {
        if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
          return new UTF8Encoding(true);
        }
      }

      if (bytes.Length >= 2) {
        if (bytes[0] == 0xFF && bytes[1] == 0xFE) {
          return Encoding.Unicode;
        }

        if (bytes[0] == 0xFE && bytes[1] == 0xFF) {
          return Encoding.BigEndianUnicode;
        }
      }

      return Encoding.Default;
    }

    public static string Replace(string input, string regexSearch, string replacement, ref int matchCount) {
      var col = Console.ForegroundColor;
      Console.WriteLine($"  Searching for '" + regexSearch + "' ...");
      var matches = Regex.Matches(input, regexSearch);
      foreach (Match match in matches) {

        if(match.Value == replacement) {
          Console.ForegroundColor = ConsoleColor.White;
          Console.WriteLine($"      Keeping   '" + match.Value + "' (no change)");
        }
        else {
          Console.ForegroundColor = ConsoleColor.Magenta;
          Console.WriteLine($"      Replacing '" + match.Value + "'");
          Console.ForegroundColor = ConsoleColor.Yellow;
          Console.WriteLine($"             by '" + replacement + "'");
        }

        matchCount++;
      }
      Console.ForegroundColor = col;
      return Regex.Replace(input, regexSearch, replacement);
    }

    public static bool WriteFile(
      string fileFullName, string rawContent,
      bool retryOnWriteProtect = true, Encoding specialEncoding = null
    ) {

      try {

        if(specialEncoding == null) {
          //specialEncoding = DetectFileEncoding(fileFullName);
          specialEncoding = new UTF8Encoding(true);
        }

        using (FileStream fs = new FileStream(fileFullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) {
          using (StreamWriter sw = new StreamWriter(fs, specialEncoding)) {
            sw.Write(rawContent);
            sw.Flush();
          }
        }
        var col = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"File '{fileFullName}' sucessfully written!");
        Console.ForegroundColor = col;
        return true;
      }
      catch (Exception ex) {
        if (retryOnWriteProtect && ex.GetType() == typeof(UnauthorizedAccessException)) {
          try {
            FileAttributes attributes = File.GetAttributes(fileFullName);
            if((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly) {
              File.SetAttributes(fileFullName, attributes & ~FileAttributes.ReadOnly);
              Thread.Sleep(100);
              return WriteFile(fileFullName, rawContent, false);
            }
          }
          catch { 
          }
        }
        var col = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: File '{fileFullName}' could not be written!");
        Console.ForegroundColor = col;
      }

      return false;
    }

  }

}
