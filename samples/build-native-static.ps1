<#
.SYNOPSIS
    Build the Jalium.UI native .static.lib archives required by the NativeAOT
    publish path (samples/Jalium.UI.AotDemo).

.DESCRIPTION
    NativeAOT publish runs through the dotnet CLI's embedded MSBuild, which
    cannot import VCTargetsPath and therefore cannot build a .vcxproj. This
    script bridges that gap: it locates the full Visual Studio MSBuild.exe
    via vswhere, then builds the default NativeAOT closure plus the optional
    browser and Vulkan archives shipped by Jalium.UI.Build. It also stages the
    static WebView2 loader beside those archives.

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

$repoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..')
$nativeRoot  = Join-Path $repoRoot 'src\native'
$aotProj     = Join-Path $nativeRoot 'jalium.native.aot\jalium.native.aot.static.vcxproj'
$browserProj = Join-Path $nativeRoot 'jalium.native.browser\jalium.native.browser.static.vcxproj'
$vulkanProj  = Join-Path $nativeRoot 'jalium.native.vulkan\jalium.native.vulkan.static.vcxproj'
$webView2Lib = Join-Path $nativeRoot 'jalium.native.browser\third_party\webview2\lib\x64\WebView2LoaderStatic.lib'

foreach ($requiredInput in @($aotProj, $browserProj, $vulkanProj, $webView2Lib)) {
    if (-not (Test-Path -LiteralPath $requiredInput -PathType Leaf)) {
        throw "Cannot find $requiredInput - repository layout or native prerequisites changed."
    }
}

# -- Locate MSBuild.exe -----------------------------------------------------
$vswhereCandidates = @(
    @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    ) | Where-Object { Test-Path $_ }
)

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
Write-Host "Projects     : $aotProj, $browserProj, $vulkanProj"
Write-Host ''

# -- Build ------------------------------------------------------------------
foreach ($project in @($aotProj, $browserProj, $vulkanProj)) {
    & $msbuild $project `
        "-t:Build" `
        "-p:Configuration=$Configuration" `
        "-p:Platform=$Platform" `
        "-p:SolutionDir=$nativeRoot\" `
        "-v:minimal" `
        "-nologo"

    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE ($project)"
    }
}

$outDir = Join-Path $nativeRoot "bin\native-static\$Configuration"
Copy-Item -LiteralPath $webView2Lib -Destination (Join-Path $outDir 'WebView2LoaderStatic.lib') -Force

$expectedLibraries = @(
    'jalium.native.aot.static.lib',
    'jalium.native.browser.static.lib',
    'jalium.native.core.static.lib',
    'jalium.native.d3d12.static.lib',
    'jalium.native.media.core.static.lib',
    'jalium.native.media.windows.static.lib',
    'jalium.native.platform.static.lib',
    'jalium.native.software.static.lib',
    'jalium.native.vulkan.static.lib',
    'WebView2LoaderStatic.lib'
)
$missingLibraries = @($expectedLibraries | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $outDir $_) -PathType Leaf)
})
if ($missingLibraries.Count -ne 0) {
    throw "NativeAOT asset build is incomplete. Missing: $($missingLibraries -join ', ')"
}

Write-Host ''
Write-Host "Static archives in $outDir :"
Get-ChildItem -LiteralPath $outDir -Filter '*.lib' | Sort-Object Name | Format-Table Name, Length, LastWriteTime
