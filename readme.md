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
