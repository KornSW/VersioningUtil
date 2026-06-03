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

  public static class NugetExeCaller {

    public static void UpdatePackage(string projFileFullName, string solutionWorkdir, string packageId, string targetVersion = null) {


      //Slnfile
      //GetPackagesFolderFullPath




      //if (string.IsNullOrWhiteSpace(targetVersion)) {
      //  Console.WriteLine(RunCommandline("C:\\GIT\\BCGer\\Build\\nuget_6.7.0.exe",
      //    $"update \"{projFileFullName}\" -id {packageId} -RepositoryPath \"{packagesFolder}\"  -Verbosity detailed", solutionWorkdir)//-ConfigFile \"{nugetConfig}\"
      //  );
      //}
      //else {
      //  Console.WriteLine(RunCommandline("C:\\GIT\\BCGer\\Build\\nuget_6.7.0.exe",
      //    $"update \"{projFileFullName}\" -id {packageId} -Version {targetVersion} -RepositoryPath \"{packagesFolder}\" -Verbosity detailed", solutionWorkdir)
      //  );
      //}
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
