param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (git status --porcelain)
{
    throw 'Commit or discard existing changes before creating a release.'
}
if (git tag --list "v$Version")
{
    throw "Tag v$Version already exists."
}

$propsPath = Join-Path $root 'Directory.Build.props'
$props = Get-Content $propsPath -Raw
$props = $props -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
$props = $props -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
$props = $props -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
Set-Content $propsPath $props -NoNewline

$plistPath = Join-Path $root 'portable\Info.plist'
$plist = Get-Content $plistPath -Raw
$plist = $plist -replace '(<key>CFBundleShortVersionString</key>\s*<string>)[^<]+', "`${1}$Version"
$plist = $plist -replace '(<key>CFBundleVersion</key>\s*<string>)[^<]+', "`${1}$Version"
Set-Content $plistPath $plist -NoNewline

dotnet build MultibandCore.csproj --configuration Release
dotnet build portable/BitsPleaseYTM6.Portable.csproj --configuration Release

git add Directory.Build.props portable/Info.plist
git commit -m "Release v$Version"
git tag -a "v$Version" -m "BitsPleaseYT M12 v$Version"
git push origin main
git push origin "v$Version"
