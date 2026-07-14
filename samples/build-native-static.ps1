<#
.SYNOPSIS
    Build the Jalium.UI native .static.lib archives required by the NativeAOT
    publish path (samples/Jalium.UI.AotDemo).

.DESCRIPTION
    NativeAOT publish runs through the dotnet CLI's embedded MSBuild, which
    cannot import VCTargetsPath and therefore cannot build a .vcxproj. This
    script bridges that gap: it locates the full Visual Studio MSBuild.exe
    via vswhere, then builds jalium.native.aot.static.vcxproj — which pulls
    in core / d3d12 / software via project references.

    Output lands in src/native/bin/native-static/<Configuration>/.

.PARAMETER Configuration
    Debug or Release (default Release).

.PARAMETER Platform
    Always x64 today.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File samples\build-native-static.ps1 -Configuration Release
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..')
$nativeRoot = Join-Path $repoRoot 'src\native'
$aotProj    = Join-Path $nativeRoot 'jalium.native.aot\jalium.native.aot.static.vcxproj'

if (-not (Test-Path $aotProj)) {
    throw "Cannot find $aotProj — repository layout changed?"
}

# -- Locate MSBuild.exe -----------------------------------------------------
$vswhereCandidates = @(
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
) | Where-Object { Test-Path $_ }

if (-not $vswhereCandidates) {
    throw "vswhere.exe not found. Install Visual Studio Installer or add MSBuild.exe to PATH manually."
}

$vswhere = $vswhereCandidates[0]

$msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) {
    throw "vswhere did not return an MSBuild.exe path. Install the 'MSBuild' component for any Visual Studio instance."
}

Write-Host "MSBuild      : $msbuild"
Write-Host "Configuration: $Configuration|$Platform"
Write-Host "Project      : $aotProj"
Write-Host ''

# -- Build ------------------------------------------------------------------
& $msbuild $aotProj `
    "-t:Build" `
    "-p:Configuration=$Configuration" `
    "-p:Platform=$Platform" `
    "-p:SolutionDir=$nativeRoot\" `
    "-v:minimal" `
    "-nologo"

if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE"
}

# jalium.native.browser.static is packed by Jalium.UI.Build but is not in the
# aot project's reference closure; build it too so this script produces the
# complete packaged set (and refreshes the provenance stamp coherently).
$browserProj = Join-Path $nativeRoot 'jalium.native.browser\jalium.native.browser.static.vcxproj'
if (Test-Path $browserProj) {
    & $msbuild $browserProj `
        "-t:Build" `
        "-p:Configuration=$Configuration" `
        "-p:Platform=$Platform" `
        "-p:SolutionDir=$nativeRoot\" `
        "-v:minimal" `
        "-nologo"

    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE (jalium.native.browser.static)"
    }
}

$outDir = Join-Path $nativeRoot "bin\native-static\$Configuration"
Write-Host ''
Write-Host "Static archives in $outDir :"
Get-ChildItem -Path $outDir -Filter '*.lib' | Format-Table Name, Length, LastWriteTime
