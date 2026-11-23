# Builds artifacts/WinTabs-<version>-setup.exe.
#   ./installer/build.ps1                 version from Directory.Build.props
#   ./installer/build.ps1 -Version 1.2.0  e.g. from a v1.2.0 git tag
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Version) {
    $Version = ([xml](Get-Content "$root/Directory.Build.props")).Project.PropertyGroup.Version
}
# Inno Setup and Windows file versions take numbers only: "0.1.0-alpha" -> "0.1.0".
$numericVersion = ($Version -split '[-+]')[0]

# Self-contained single file: users do not need .NET installed.
$publish = "$root/artifacts/publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root/wintabs/wintabs.csproj" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version -p:DebugType=none -o $publish
if ($LASTEXITCODE) { throw "dotnet publish failed" }

# Inno Setup compiler, pinned and downloaded once (same package on CI and locally).
$innoVersion = '6.7.3'
$inno = "$root/artifacts/tools/innosetup-$innoVersion"
if (-not (Test-Path "$inno/tools/ISCC.exe")) {
    $package = "$root/artifacts/tools/innosetup.zip"
    New-Item -ItemType Directory -Force (Split-Path $package) | Out-Null
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$innoVersion/tools.innosetup.$innoVersion.nupkg" -OutFile $package
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($package, $inno)
    Remove-Item $package
}

& "$inno/tools/ISCC.exe" /Qp "/DAppVersion=$numericVersion" "/DSourceDir=$publish" "$PSScriptRoot/WinTabs.iss"
if ($LASTEXITCODE) { throw "Inno Setup failed" }
