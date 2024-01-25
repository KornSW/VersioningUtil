using Microsoft.VisualBasic;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Versioning {

  [DebuggerDisplay("{TargetPackageId} {TargetPackageVersionConstraint}")]
  public class DependencyInfo {

    public DependencyInfo() {
    }
    public DependencyInfo(string targetPackageId, string targetPackageVersionConstraint) {
      this.TargetPackageId = targetPackageId;
      this.TargetPackageVersionConstraint = new VersionContraint(targetPackageVersionConstraint);
    }

    public String TargetPackageId { get; set; } = null;

    public VersionContraint TargetPackageVersionConstraint { get; set; } = null;

  }

  [DebuggerDisplay("{ConstraintPattern}")]
  public class VersionContraint { 
  
    public VersionContraint(string constraintPattern) {
      _ConstraintPattern = constraintPattern;
    }

    public VersionContraint(string minVersion, string maxVersion) {
      _ConstraintPattern = $"[{minVersion},{maxVersion}]";
    }

    public void SetVersionShouldBeGreaterThan(string version, bool preserveMaxVersion = false) {
      VersionContraint.Parse(
        _ConstraintPattern,
        out bool minIncluided, out string minVersion, out bool maxIncluided, out string maxVersion
      );

      minIncluided = false;
      minVersion = version;

      if (!preserveMaxVersion) {
        maxVersion = null;
        maxIncluided = false;
      }

      _ConstraintPattern = VersionContraint.GenerateConstraintPattern(
        minIncluided, minVersion, maxIncluided, maxVersion
      );
    }

    public void SetVersionShouldBeExact(string version) {
      _ConstraintPattern = VersionContraint.GenerateConstraintPattern(
        true, version, true, null
      );
    }

    public void SetVersionShouldBeGreaterThanOrEqual(string version, bool preserveMaxVersion = false) {
      VersionContraint.Parse(
        _ConstraintPattern,
        out bool minIncluided, out string minVersion, out bool maxIncluided, out string maxVersion
      );

      minIncluided = true;
      minVersion = version;

      if (!preserveMaxVersion) {
        maxVersion = null;
        maxIncluided = false;
      }

      _ConstraintPattern = VersionContraint.GenerateConstraintPattern(
        minIncluided, minVersion, maxIncluided, maxVersion
      );
    }

    public void SetVersionShouldBeLessThan(string version, bool preserveMinVersion = false) {
      VersionContraint.Parse(
        _ConstraintPattern,
        out bool minIncluided, out string minVersion, out bool maxIncluided, out string maxVersion
      );

      maxIncluided = false;
      maxVersion = version;

      if (!preserveMinVersion) {
        minVersion = null;
        minIncluided = false;
      }

      _ConstraintPattern = VersionContraint.GenerateConstraintPattern(
        minIncluided, minVersion, maxIncluided, maxVersion
      );
    }

    public void SetVersionShouldBeLessThanOrEqual(string version, bool preserveMinVersion = false) {
      VersionContraint.Parse(
        _ConstraintPattern,
        out bool minIncluided, out string minVersion, out bool maxIncluided, out string maxVersion
      );

      maxIncluided = true;
      maxVersion = version;

      if (!preserveMinVersion) {
        minVersion = null;
        minIncluided = false;
      }

      _ConstraintPattern = VersionContraint.GenerateConstraintPattern(
        minIncluided, minVersion, maxIncluided, maxVersion
      );
    }

    public void SetVersionShouldBeInNonBreakingRange(string version) {
      string[] vt = version.Split('-');
      string[] v = vt[0].Split('.');
      Int32.TryParse(v[0], out Int32 major);
      string semver = major.ToString();
      if (v.Length > 1) {
        semver = semver + "." + v[1];
        if (v.Length > 2) {
          semver = semver + "." + v[2];
        }
      }
      else {
        semver = semver + ".0";
      }
      if (vt.Length > 1) {
        semver = semver + "-" + vt[1];
      }
      _ConstraintPattern = VersionContraint.GenerateConstraintPattern(
        true, semver, false, $"{major + 1}.0"
      );
    }

    private string _ConstraintPattern = null;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string ConstraintPattern {
      get {
        return _ConstraintPattern;
      }
    }

    public override string ToString() {
      return this.ConstraintPattern;
    }

    public string ToString(bool cleanMinVersion) {
      if (cleanMinVersion) {
        return this.ConstraintPattern;
      }
      else {
        var clean = this.ConstraintPattern.Replace("[","").Replace("]", "").Replace("(", "").Replace(")", "").Split(',');
        if (clean.Length == 1 || clean[0] != string.Empty) {
          return clean[0];
        }
        return clean[1];
      }
    }

    private static string GenerateConstraintPattern(
      bool minIncluided, string minVersion,
      bool maxIncluided, string maxVersion
    ) {

      // please read: https://learn.microsoft.com/en-us/nuget/concepts/package-versioning?tabs=semver20sort#version-ranges
      // to understand this crazy-shit!

      if (string.IsNullOrWhiteSpace(maxVersion) ) {
        if (maxIncluided) {
          if (minIncluided) {
            //"Exact version match"
            return $"[{minVersion}]";
          }
          else {
            //INVALID (1.0] will be fixed to (,1.0] by asuming "Maximum version, inclusive"
            return $"(,{minVersion}]";
          }
        }
        else {
          if (minIncluided) {
            // "Minimum version, inclusive" >> no braces neccessary, can return 1.0 instead of [1.0,)
            return minVersion;
          }
          else {
            //"Minimum version, exclusive" (1.0,)
            return $"({minVersion},)"; 
          } 
        }   
      }
      else { //we have a ","
        if (minVersion == string.Empty) {
          if (maxIncluided) {
            //"Maximum version, inclusive" (,1.0]
            return $"(,{maxVersion}]";
          }
          else {
            //"Maximum version, exclusive" (,1.0)
            return $"(,{maxVersion})";
          }
        }
        else {
          if (maxIncluided) {
            if (minIncluided) {
              //"Exact range, inclusive" [1.0,2.0]
              return $"[{minVersion},{maxVersion}]";
            }
            else {
              //UNDOCUMENTED: "Mixed exclusive minimum and exclusive inclusive version" [1.0,2.0)  	
              return $"({minVersion},{maxVersion}]";
            }
          }
          else {
            if (minIncluided) {
              //"Mixed inclusive minimum and exclusive maximum version" [1.0,2.0) 	
              return $"[{minVersion},{maxVersion})";
            }
            else {
              //"Exact range, exclusive" (1.0,2.0)
              return $"({minVersion},{maxVersion})";
            }
          }
        }
      }
    }

    private static void Parse(
      string constraintPattern,
      out bool minIncluided, out string minVersion,
      out bool maxIncluided, out string maxVersion
    ) {

      minIncluided = true;
      if (constraintPattern.StartsWith("(")) {
        constraintPattern = constraintPattern.Substring(1);
        minIncluided = false;
      }
      else if (constraintPattern.StartsWith("[")) {
        constraintPattern = constraintPattern.Substring(1);
      }

      maxIncluided = true;
      if (constraintPattern.EndsWith(")")) {
        constraintPattern = constraintPattern.Substring(0, constraintPattern.Length - 1);
        maxIncluided = false;
      }
      else if (constraintPattern.EndsWith("]")) {
        constraintPattern = constraintPattern.Substring(0, constraintPattern.Length - 1);
      }

      int spltIdx = constraintPattern.IndexOf(',');
      if (spltIdx < 0) {
        minVersion = constraintPattern;
        maxVersion = null;
      }
      else {
        minVersion = constraintPattern.Substring(0, spltIdx);
        maxVersion = constraintPattern.Substring(spltIdx + 1);
      }

    }

  }

 }
