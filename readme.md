# KornSW VersioningUtil

Automates semantic versioning (for .net Solutions) by parsing the changelog.md file and patching AssemblyInfo and/or project files.

Normally, you would **change the version numbers manually** before integrating (push, check-in) your changes. This is OK as long as you're working alone.
When working in a team, this will raise **merge conflicts**.

Solution: Instead of directly defining a new version number manually, just document your changes in the **changelog.md**, using
the **keywords "Breaking Change" or "New Feature"**. 
KornSW VersioningUtil will parse changelog.md, **calculate a new version number** and **automaticall apply it to relevant targets**.

# Installation

- Build this solution and fetch **kvu.exe** from bin.
  - The exe is monolithic (doesn't need any neighbour DLLs) and doesn't need any installation.
- Put it into your **repository**, sub directory **"vers"**
- Integrate an execution of kvu.exe into your build pipeline (after fetching sources, but before compiling sources).

# Get Started

Make sure there is no **changelog.md** file existing yet.
Directly run **kvu.exe** (locally) in your **vers** directory, this will create a properly formatted **changelog.md**.

## Editing changelog.md

You'll see...

    ## Upcoming Changes
    
    *(none)*

...replace the line `*(none)*` by one (or many) of these:

    - Description of a change that is backward compatible 
       
    - New Feature: Description of a new feature 
    
    - Breaking Change: Description of a breaking change

- Make sure you don't edit anything else (this would break the syntax).

- Make sure you use exactly the prefixes `New Feature:` or `Breaking Change:`

## Applying the new version number

Run **kvu.exe** again. This time it will detect the existing **changelog.md**.
Depending on your entries, a new version number will be calculated and inserted into changelog.md as new headline. 
Your entries will be moved under the new headline.
A new **versioninfo.json** file will be created. It contains metadata for follow-up tools, that can patch the new version number into relevant places.

# Functionality In Depth

## Anatomy

changelog.md is divided into three sections:

|Section |Source                                   |Remarks                                                     |
|--------|-----------------------------------------|------------------------------------------------------------|
|Head    |`# Change log`<br>`This file contains...`| Leave this unchanged.                                      |
|Incoming|`## Upcoming Changes`                    | Add your entries here. Will be parsed and moved by kvu.exe.|
|History |`## v x.x.x`                             | Leave this unchanged. Will be added by kvu.exe.            |

## Behaviour

The sections are detected by their headlines. The incoming section will evoke the following actions:

### Interpretation

- Any content except `*(none)*` will lead to an increase of the **patch** version
- Any occurance of `New Feature:` will instead lead to an increase of the **minor** version 
- Any occurance of `Breaking Change:` will instead lead to an increase of the **major** version 

### Reformatting

- Below the incoming section, a new history headline will be inserted (containing the new version number).
- The content of the incoming section will be moved to the new history section.
- `*(none)*` will be put into the incoming section.

### Metadata Extraction

The **versioninfo.json** file will be updated.





# Why this tool exists

Modern .NET projects look simple from the outside. A solution contains projects, projects reference packages, packages have versions, and somewhere a NuGet restore turns that into a buildable result.

In practice, anyone who has maintained real-world .NET repositories for more than a few years knows that this is only the clean marketing version of the story.

A serious .NET codebase rarely consists of one homogeneous project style. It often contains SDK-style .NET projects, old .NET Framework projects, `packages.config` files, `.nuspec` files, custom package folders, generated hint paths, project-specific conventions, legacy Visual Studio behavior, and sometimes years of accumulated migration history. Some projects use `<PackageReference>`. Others still use assembly references plus `HintPath`. Some packages are described in `.nuspec` dependency groups. Some are framework-specific. Some are intentionally abstract. Some repositories use local package folders. Others use the global NuGet cache. Some were restored by Visual Studio, some by `nuget.exe`, some by `dotnet restore`, and some by build infrastructure that no longer exists.

This tool exists because those realities are not edge cases. They are the normal state of long-lived enterprise and infrastructure repositories.

## The problem with relying on moving targets

The official .NET and NuGet tooling is powerful, but it is also a moving target.

Over the years, Microsoft has changed project formats, restore behavior, SDK assumptions, package resolution rules, command-line tooling, target framework naming, and the preferred way of expressing dependencies. Many of these changes are reasonable in isolation. But for a repository that must survive across multiple generations of .NET, they create a practical maintenance problem.

A tool that depends too heavily on `dotnet`, `nuget.exe`, MSBuild object models, Visual Studio internals, or SDK-specific behavior inherits all of that volatility.

That means a simple operation such as "update this package version" can suddenly depend on:

- which SDK is installed,
- which Visual Studio version is present,
- whether the project is SDK-style or legacy-style,
- whether packages are restored locally or globally,
- whether the restore logic still understands the old project shape,
- whether a command-line tool still supports the exact scenario,
- whether the repository contains custom NuGet configuration,
- whether the target framework is interpreted the same way as before.

For short-lived projects this may be acceptable. For long-lived repositories it is not.

This tool takes a different approach: it reads and writes the actual files.

## The core idea

The core idea is simple:

A project dependency is not primarily a command-line operation. It is information.

At its most basic level, the relevant fact is:

> Project or package metadata references package X in version Y for target framework Z.

That fact may be represented in different ways:

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

or:

```xml
<package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
```

or:

```xml
<Reference Include="Newtonsoft.Json">
  <HintPath>..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll</HintPath>
</Reference>
```

or:

```xml
<dependencies>
  <group targetFramework="net8.0">
    <dependency id="Newtonsoft.Json" version="13.0.3" />
  </group>
</dependencies>
```

The representation changes. The semantic meaning stays the same.

This tool is built around that distinction. It does not try to delegate responsibility to external tooling whenever possible. Instead, it models the semantic information internally and then maps that information to the correct physical file format.

That makes the tool more predictable, easier to debug, and easier to extend.

## Supported artifact types

The tool works directly with the files that actually define package and version information.

It understands Visual Studio project files, including SDK-style projects and legacy .NET Framework projects. It understands `packages.config` files used by older .NET Framework projects. It understands `.nuspec` files, including flat dependency declarations and framework-specific dependency groups.

Each file type is treated as an accessor over a shared internal dependency model.

This means the tool can operate consistently across very different project styles while still respecting the unique rules of each file format.

## Why manual XML handling is intentional

At first glance, manually handling XML may sound like a step backwards. But in this case, it is a deliberate design decision.

The goal is not to invent a new XML parser or to perform fragile string replacements. The goal is to avoid high-level dependencies that impose assumptions about how modern .NET projects are supposed to look.

The tool uses the built-in .NET XML APIs to read and write project metadata without requiring external libraries. It avoids unnecessary NuGet or MSBuild dependencies because those dependencies would themselves become part of the moving target problem.

The result is a standalone tool that can be compiled and used without relying on a specific external package management implementation.

## Minimal invasive writing

A central design goal is that the tool should not destroy the structure of existing files.

Project files and `.nuspec` files often contain manual comments, carefully ordered nodes, custom metadata, special build conditions, or formatting that exists for a reason. A dependency update tool must not casually rewrite the whole document just because one package version changed.

Therefore, the tool tries to update only the specific nodes that represent the dependency being changed. Existing comments, unrelated nodes, custom metadata, item group layout, and manual ordering should remain untouched as far as reasonably possible.

This is especially important for legacy projects. In old `.csproj` and `.vbproj` files, a package dependency may not be represented as a clean package reference. It may be represented as an assembly reference with nested metadata, hint paths, private flags, build conditions, and other project-specific details. Rebuilding such entries from scratch is dangerous. Updating them in place is safer.

## Legacy .NET Framework is a first-class scenario

Many tools treat old .NET Framework projects as a compatibility burden. This tool treats them as a first-class use case.

That matters because many valuable repositories still contain .NET Framework projects. They may be stable, business-critical, and expensive to migrate. They still need package updates, version management, and consistent metadata.

In legacy projects, package information is often split across multiple places:

- `packages.config` contains the NuGet package identity and version.
- The project file contains assembly references.
- The project file also contains `HintPath` entries that point into a package folder.
- The package folder may be local, solution-relative, custom-configured, or inferred from existing references.

A robust tool must understand that `packages.config` and the project file are related but not identical.

The design used here treats `packages.config` as the leading dependency declaration for .NET Framework projects. After it is updated, the project file can be synchronized so that assembly references and hint paths match the desired package state.

This allows the repository to be repaired into a consistent state instead of merely editing one file and hoping the rest still works.

## SDK-style projects are handled differently

Modern SDK-style projects usually express dependencies using `PackageReference`.

That is a cleaner model, but it still needs careful handling. `PackageReference` entries may be spread across multiple `ItemGroup` nodes. The version may appear as an attribute or as a nested element. Project files may contain comments, conditions, custom build logic, or manually sorted references.

The tool therefore searches through all relevant item groups and updates existing package references in place. New references, when allowed by the requested operation, are inserted into an appropriate item group instead of blindly rebuilding the project file.

The important part is that SDK-style projects are not treated like legacy projects. They have their own file semantics and therefore their own update strategy.

## NuSpec dependency groups

`.nuspec` files introduce another important scenario: framework-specific dependency groups.

A package may depend on one version of a dependency for `net8.0` and another version for `net10.0`. A flat dependency list cannot represent that accurately. NuGet therefore allows dependency groups with a `targetFramework` attribute.

The tool supports this model explicitly.

If a `.nuspec` file contains dependency groups, framework-specific dependencies are routed into the matching group. Dependencies without a specific framework are treated as wildcard dependencies and applied to all relevant groups. This avoids ambiguous duplication between root-level dependencies and group-level dependencies.

The rule is simple:

Once dependency groups are used, grouped dependencies become authoritative.

That prevents a package from accidentally containing the same dependency both globally and framework-specifically with potentially conflicting versions.

## The internal model matters

The long-term strength of this tool is not that it can update one XML node. The strength is that it gradually builds a stable internal model around dependency information.

Instead of letting every file format define its own isolated behavior, the tool uses concepts like:

- package id,
- version constraint,
- target framework,
- project format,
- package source layout,
- dependency scope.

The accessors translate between physical file formats and this internal representation.

This makes the design extensible. If another format needs to be supported later, such as `Directory.Packages.props`, central package management, lock files, or custom repository metadata, it can be added as another accessor without rewriting the whole tool.

## Why this can become long-lived

A tool becomes long-lived when its core assumptions are stable.

The external shape of .NET tooling changes frequently. The semantic idea of a project depending on a package version changes much more slowly.

This tool is built around the stable part.

If Microsoft changes command-line behavior, this tool is not immediately broken. If a new SDK changes restore behavior, this tool still understands the files. If a repository contains both old and new project styles, this tool can handle them directly. If a specific edge case appears, the corresponding parser or writer can be adjusted without throwing away the overall design.

That is the main advantage.

The tool is not trying to predict every future .NET convention. It is designed so that future conventions can be added locally and explicitly.

## A repair tool, not just an update tool

The practical value goes beyond version updates.

Real repositories often contain inconsistent states:

- `packages.config` says one version, but the project file points to another.
- Hint paths point to old package folders.
- A package was updated manually but references were not synchronized.
- A project contains stale assembly references.
- Different frameworks inside a `.nuspec` file drift apart.
- Package folders follow different layout conventions.
- Some projects were migrated while others were not.

Official tooling often assumes the project is already in a clean state. This tool is more useful precisely when the repository is not clean.

It can act as a repair layer for dependency metadata.

That is an important distinction. A simple version bump script changes text. A repository repair tool understands the relationship between files.

## No hidden magic

Another design goal is transparency.

When official tools perform package operations, they may trigger restore logic, project evaluation, SDK resolution, MSBuild imports, target execution, and environment-dependent behavior. That is powerful, but it can also make failures hard to understand.

This tool should behave more directly.

It reads files. It interprets known structures. It writes specific changes. If something fails, the failure should be close to the actual file content that caused it.

That makes debugging easier. It also makes the tool more suitable for automation, because its behavior is less dependent on the environment in which it runs.

## Designed for extension

The tool is intentionally practical rather than theoretical.

There will always be strange project files. There will always be repositories with slightly unusual conventions. There will always be packages that do not follow the simplest layout.

The design accepts that.

Instead of depending on a black-box implementation, the tool keeps the logic close enough to be adapted. If a new package folder layout appears, path detection can be extended. If a new target framework pattern appears, framework parsing can be updated. If another dependency declaration style becomes relevant, another accessor can be added.

This is not a weakness. It is the reason the tool can survive in real repositories.

## Philosophy

The philosophy behind the tool can be summarized as follows:

Do not ask external tools to guess what should happen when the required information is already present in the repository.

Do not rewrite complete project files when a targeted update is enough.

Do not treat old project formats as second-class citizens.

Do not hide dependency semantics behind command-line behavior that may change with the next SDK.

Do not assume a repository is clean before the tool runs.

Model the meaning first. Then update the file representation carefully.

## Conclusion

This tool exists because long-lived .NET repositories need something more stable than whatever the current official command-line experience happens to support.

It is meant for mixed environments, legacy projects, modern projects, NuSpec metadata, framework-specific dependency rules, and repositories that have accumulated real-world complexity over time.

Its value is not only in updating versions. Its value is in making dependency metadata explicit, inspectable, repairable, and independent of fragile external tooling assumptions.

That makes it especially useful for infrastructure work, release automation, repository modernization, and long-term maintenance of .NET codebases that cannot simply be recreated every time the ecosystem changes.

The result is a small but important kind of tool: one that understands the files that define the build instead of merely invoking another tool and hoping the environment agrees.