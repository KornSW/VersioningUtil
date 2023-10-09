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

    public static string Replace(string input, string regexSearch, string replacement, ref int matchCount) {
      var col = Console.ForegroundColor;
      Console.WriteLine($"  Searching for '" + regexSearch + "' ...");
      var matches = Regex.Matches(input, regexSearch);
      foreach (Match match in matches) {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"      Replacing '" + match.Value + "'");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"             by '" + replacement + "'");
        matchCount++;
      }
      Console.ForegroundColor = col;
      return Regex.Replace(input, regexSearch, replacement);
    }

    public static bool WriteFile(string fileFullName, string rawContent,bool retryOnWriteProtect = true) {
      try {
        using (FileStream fs = new FileStream(fileFullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) {
          using (StreamWriter sw = new StreamWriter(fs, Encoding.Default)) {
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
