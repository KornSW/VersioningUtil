$repo = "KornSW/VersioningUtil"
$localToolsDir = "C:\kvu"
mkdir $localToolsDir 

Write-Host Determining latest release
$releases = "https://api.github.com/repos/$repo/releases"
$tag = (Invoke-WebRequest $releases | ConvertFrom-Json)[0].tag_name

Write-Host Dowloading latest release
Invoke-WebRequest "https://github.com/$repo/releases/download/$tag/kvu.exe"                 -Out "$localToolsDir\kvu.exe"
Invoke-WebRequest "https://github.com/$repo/releases/download/$tag/kvu.deps.json"           -Out "$localToolsDir\kvu.deps.json"
Invoke-WebRequest "https://github.com/$repo/releases/download/$tag/kvu.dll"                 -Out "$localToolsDir\kvu.dll"
Invoke-WebRequest "https://github.com/$repo/releases/download/$tag/kvu.pdb"                 -Out "$localToolsDir\kvu.pdb"
Invoke-WebRequest "https://github.com/$repo/releases/download/$tag/kvu.runtimeconfig.json"  -Out "$localToolsDir\kvu.runtimeconfig.json"
Invoke-WebRequest "https://github.com/$repo/releases/download/$tag/kvu.exe"                 -Out "$localToolsDir\kvu.xml"

Write-Host Registering to PATH
$oldpath = (Get-ItemProperty -Path 'Registry::HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Session Manager\Environment' -Name PATH).path
$newpath = "$localToolsDir;$oldpath" 
Set-ItemProperty -Path 'Registry::HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Session Manager\Environment' -Name PATH -Value $newpath
Write-Host "##vso[task.prependpath]$localToolsDir"

