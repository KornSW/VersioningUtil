using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Versioning;

namespace FileIO {

  public class VersionInfoJsonFileAccessor : IVersionContainer {

    private string _FileFullName;
    private JsonSerializerOptions _JsonOpt = new JsonSerializerOptions();
  
    public VersionInfoJsonFileAccessor(string fileFullName) {
      _FileFullName = fileFullName;
      _JsonOpt.PropertyNameCaseInsensitive = true;
      _JsonOpt.IncludeFields = true;
      _JsonOpt.WriteIndented = true;
    }

    public VersionInfo ReadVersion() {
      string rawJson = File.ReadAllText(_FileFullName, Encoding.Default);
      return JsonSerializer.Deserialize<VersionInfo>(rawJson,_JsonOpt);
    }

    public void WriteVersion(VersionInfo versionInfo) {
      string rawJson = JsonSerializer.Serialize(this, _JsonOpt);
      using (FileStream fs = new FileStream(_FileFullName, FileMode.Create, FileAccess.Write)) {
        using (StreamWriter sw = new StreamWriter(fs, Encoding.Default)) {
          sw.WriteLine(rawJson);
          sw.Flush();
        }
      }
    }

  }

}
