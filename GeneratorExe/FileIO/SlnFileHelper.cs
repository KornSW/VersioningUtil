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

  public static class SlnFileHelper {

    private const string _RegexRepoPath = "key=\\\"repositoryPath\\\" value=\\\"[a-zA-Z0-9.\\-\\\\)\\(\\*]*\\\"";

    public static string GetPackagesFolderFullPath(string solutionFile) {
      string folder = Path.GetDirectoryName(solutionFile.Replace("\\\\","\\"));

      string nugetConfigFile = Path.Combine(folder, "nuget.config");
      if (!File.Exists(nugetConfigFile)) {
        nugetConfigFile = Path.Combine(folder, ".vs", "nuget.config");
      }

      if (File.Exists(nugetConfigFile)) {
        string rawContent = File.ReadAllText(nugetConfigFile, Encoding.Default);
        var matchVers = Regex.Matches(rawContent, _RegexRepoPath).FirstOrDefault();
        if (matchVers != null) {
          string configValue = matchVers.Value.Substring(28, matchVers.Value.Length - 29);
          return Path.GetFullPath(Path.Combine(folder, configValue.Replace("\\\\", "\\")));
        }
      }
      return Path.Combine(folder, "packages");
    }

    private const string _ProjFilePath = "\"[a-zA-Z0-9.\\-\\\\)\\(\\*]*(\\.csproj|\\.vbproj)\"";

    public static string[] GetProjectFileFullNames(string solutionFile) {
      string folder = Path.GetDirectoryName(solutionFile.Replace("\\\\", "\\"));
      List<string> projectFiles = new List<string>();
      if (File.Exists(solutionFile)) {
        string rawContent = File.ReadAllText(solutionFile, Encoding.Default);
        foreach (Match match in Regex.Matches(rawContent, _ProjFilePath).OfType<Match>()) {
          string projectFileName = match.Value.Substring(1, match.Value.Length - 2); // remove quotes
          projectFiles.Add(Path.GetFullPath(Path.Combine(folder, projectFileName)));
        }
      }
      return projectFiles.ToArray();
    }

  }

}
