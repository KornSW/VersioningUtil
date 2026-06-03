using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Versioning;

namespace FileIO {

  public static class DotnetExeCaller {

    public static string[] ListPackages(string netCoreProjectName) {

        string rawOutput = RunCommandline(
          "C:\\Program Files\\dotnet\\dotnet.exe",
           $"dotnet list \"{netCoreProjectName}\" package"
        );
        var packageNames = new List<string>();

        using (var reader = new StringReader(rawOutput)) {
          string line = reader.ReadLine();
          while (!string.IsNullOrWhiteSpace(line)) {
            line = line.Trim();
            if (line.StartsWith(">")) {
              line = line.Substring(1).TrimStart();
              line = line.Substring(0, line.IndexOf(" "));
              packageNames.Add(line);
            }
            line = reader.ReadLine();
          }
        }

        return packageNames.ToArray();
    }

    public static void InstallPackage(string projFileFullName, string solutionWorkdir, string packageId, string targetVersion = null) {

      if (string.IsNullOrWhiteSpace(targetVersion)) {
        Console.WriteLine(RunCommandline("C:\\Program Files\\dotnet\\dotnet.exe",
          $"add \"{projFileFullName}\" package {packageId} ", solutionWorkdir)//--package-directory \"{packagesFolder}\" ")
        );

      }
      else {
        Console.WriteLine(RunCommandline("C:\\Program Files\\dotnet\\dotnet.exe",
          $"add \"{projFileFullName}\" package {packageId} --version {targetVersion} ", solutionWorkdir)//--package-directory \"{packagesFolder}\" ")
        );
        //--configfile \"{nugetConfig}\"


      }

    }

    private static string RunCommandline(string executableFilename, string arguments, string workDir = default) {

      System.Diagnostics.Process p = new System.Diagnostics.Process();

      p.StartInfo.FileName = executableFilename;
      if (!string.IsNullOrWhiteSpace(workDir)) {
        p.StartInfo.WorkingDirectory = workDir;
      }
      p.StartInfo.UseShellExecute = false;
      p.StartInfo.Arguments = arguments;
      p.StartInfo.CreateNoWindow = true;
      p.StartInfo.RedirectStandardOutput = true;
      p.StartInfo.RedirectStandardError = true;
      p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
      p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;

      p.EnableRaisingEvents = true;

      Console.WriteLine($"Executing: {executableFilename} {arguments}");

      p.Start();

      string output = p.StandardOutput.ReadToEnd();
      string error = p.StandardError.ReadToEnd();

      if (!string.IsNullOrWhiteSpace(error)) {
        throw new Exception("ERROR from StdErr: " + error);
      }

      return output;
    }

  }

}
