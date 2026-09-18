param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$extensions = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @(
    '.c', '.cc', '.cpp', '.h', '.hpp',
    '.hlsl', '.hlsli', '.ps1', '.cso', '.spv')) {
    [void]$extensions.Add($extension)
}

$projects = @(
    'src/native/jalium.native.d3d12/jalium.native.d3d12.vcxproj',
    'src/native/jalium.native.d3d12/jalium.native.d3d12.static.vcxproj',
    'src/native/jalium.native.vulkan/jalium.native.vulkan.vcxproj',
    'src/native/jalium.native.vulkan/jalium.native.vulkan.static.vcxproj'
)

$msbuild = Get-Command msbuild -ErrorAction Stop
$failed = $false
foreach ($relativeProject in $projects) {
    $project = [IO.Path]::GetFullPath((Join-Path $repoRoot $relativeProject))
    $componentDir = Split-Path $project -Parent
    $jsonText = & $msbuild.Source $project "/p:Configuration=$Configuration" "/p:Platform=$Platform" `
        '/getItem:ClCompile;ClInclude;None' /nologo | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed while evaluating $relativeProject (exit $LASTEXITCODE)."
    }

    $evaluation = $jsonText | ConvertFrom-Json
    $included = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($itemType in @('ClCompile', 'ClInclude', 'None')) {
        foreach ($item in @($evaluation.Items.$itemType)) {
            if ($item.FullPath) {
                [void]$included.Add([IO.Path]::GetFullPath($item.FullPath))
            }
        }
    }

    $missing = @(
        Get-ChildItem $componentDir -Recurse -File |
            Where-Object { $extensions.Contains($_.Extension) -and
                           -not $included.Contains([IO.Path]::GetFullPath($_.FullName)) } |
            ForEach-Object { [IO.Path]::GetRelativePath($componentDir, $_.FullName) } |
            Sort-Object
    )

    if ($missing.Count -eq 0) {
        Write-Host "[vcxproj] OK $relativeProject"
        continue
    }

    $failed = $true
    Write-Error -ErrorAction Continue `
        "[vcxproj] $relativeProject is missing $($missing.Count) code/shader item(s):"
    foreach ($path in $missing) {
        Write-Error -ErrorAction Continue "  $path"
    }
}

if ($failed) {
    exit 1
}

Write-Host '[vcxproj] All D3D12/Vulkan hand-authored project files are complete.'
