# ============================================================================
# Jalium D3D12 / Vulkan rendering-parity one-shot driver.
#
#   .\run_parity.ps1 [-Configuration Release] [-SceneFilter <substring>]
#                    [-OutRoot out\parity] [-SkipBuild]
#
# Pipeline:
#   1. dotnet build tests/Jalium.UI.ParityHarness   (native DLLs ride along
#      via the Interop project's Content items - rebuild native first with
#      `cmake --build src/native/build_x64_vs18 --config Release` when the
#      readback ABI or a backend changed)
#   2. run the harness with JALIUM_RENDER_BACKEND-independent explicit args:
#        harness d3d12  <OutRoot>\d3d12
#        harness vulkan <OutRoot>\vulkan
#      Exit 2 = the backend reported readback NOT_SUPPORTED -> a SKIP marker
#      is written; the run is reported but NOT treated as a failure.
#      Exit 3 = PARTIAL: some scenes dumped, others faulted (each faulted scene
#      leaves a `<scene>.FAILED.txt` marker). The run still diffs the rest.
#   3. python tools/parity_diff.py <d3d12Dir> <vulkanDir>
#      (skipped with a clear notice when either side SKIPped)
#
# Exit code: 0 on success (including the vulkan-SKIP / PARTIAL cases), 1 on any
# hard failure (build, d3d12 render, or diff FAIL verdict).
# ============================================================================
param(
    [string]$Configuration = "Release",
    [string]$SceneFilter = "",
    [string]$OutRoot = "out\parity",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$harnessProj = Join-Path $repoRoot "tests\Jalium.UI.ParityHarness\Jalium.UI.ParityHarness.csproj"
$harnessExe = Join-Path $repoRoot "tests\Jalium.UI.ParityHarness\bin\$Configuration\net10.0-windows\Jalium.UI.ParityHarness.exe"
$diffTool = Join-Path $repoRoot "tools\parity_diff.py"
$outD3d12 = Join-Path $repoRoot (Join-Path $OutRoot "d3d12")
$outVulkan = Join-Path $repoRoot (Join-Path $OutRoot "vulkan")

# -- 1. Build ----------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "== Building parity harness ($Configuration) ==" -ForegroundColor Cyan
    # Defender file-lock flakes (CS2012/MSB3491) - retry up to 3 times, 60 s apart.
    $built = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        dotnet build $harnessProj -c $Configuration -m:1 --nologo -v q
        if ($LASTEXITCODE -eq 0) { $built = $true; break }
        if ($attempt -lt 3) {
            Write-Warning "build attempt $attempt failed (exit $LASTEXITCODE) - retrying in 60 s"
            Start-Sleep -Seconds 60
        }
    }
    if (-not $built) { Write-Error "harness build failed after 3 attempts"; exit 1 }
}
if (-not (Test-Path $harnessExe)) { Write-Error "harness exe not found: $harnessExe"; exit 1 }

# -- 2. Render both halves ---------------------------------------------------
# C-gamma: the GPU offscreen effect RT is now the DEFAULT Vulkan path (it samples
# the TRUE isolated element, at D3D12 parity, instead of the legacy pass-through
# that composited the element back UNMODIFIED). So the effect scenes take the GPU
# path with NO env set, and the vulkan half is a SINGLE pass exactly like d3d12 -
# no more per-process flag latching, no more two-pass scoping. The old two-pass
# dance existed only because the GPU path used to be opt-in behind
# JALIUM_VK_EFFECT_GPU_RT=1; that flag now survives solely as a kill-switch
# (JALIUM_VK_EFFECT_GPU_RT=0 forces the legacy approximation) and this script no
# longer sets it either way. Non-effect frames never touch the offscreen RT (the
# backend scans each frame's stream for an offscreen marker before allocating), so
# a single default-ON process reproduces the non-effect baseline bit-for-bit.

# Run the harness once. $filter narrows to a scene-name substring ('' = all
# selected by the outer -SceneFilter). JALIUM_VK_EFFECT_GPU_RT is deliberately
# left UNSET (default ON) so the run measures the shipping default path.
function Invoke-HarnessPass([string]$backend, [string]$outDir, [string]$filter) {
    if (Test-Path $outDir) { Remove-Item -Recurse -Force -Confirm:$false $outDir }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $harnessArgs = @($backend, $outDir)
    if ($filter -ne "") { $harnessArgs += $filter }

    # 2>&1 folds the harness's own stderr (per-scene "FAIL scene=..." lines) into
    # the success stream so it does NOT surface as a terminating NativeCommandError
    # under $ErrorActionPreference='Stop'. Out-Host keeps the log visible without
    # leaking pipeline objects into the return value.
    & $harnessExe @harnessArgs 2>&1 | Out-Host
    return $LASTEXITCODE
}

function Invoke-Half([string]$backend, [string]$outDir) {
    Write-Host "== Rendering $backend -> $outDir ==" -ForegroundColor Cyan
    return Invoke-HarnessPass $backend $outDir $SceneFilter
}

# Harness exit codes: 0 = all selected scenes dumped, 2 = readback NOT_SUPPORTED
# (SKIP marker), 3 = PARTIAL (some scenes dumped, others faulted; each faulted
# scene leaves a `<scene>.FAILED.txt` marker). We diff on 0 OR 3 so the baseline
# still compares every scene that rendered on BOTH halves.
$d3dExit = Invoke-Half "d3d12" $outD3d12
if ($d3dExit -eq 2) {
    Write-Warning "d3d12 half SKIPped (readback NOT_SUPPORTED) - unexpected on Windows; investigate."
} elseif ($d3dExit -eq 3) {
    Write-Warning "d3d12 half PARTIAL - some scenes faulted (see .FAILED.txt markers in $outD3d12). Diffing the rest."
} elseif ($d3dExit -ne 0) {
    Write-Error "d3d12 half failed (exit $d3dExit)"; exit 1
}

$vkExit = Invoke-Half "vulkan" $outVulkan
if ($vkExit -eq 2) {
    Write-Host ""
    Write-Host "NOTE: vulkan half SKIPped -- the Vulkan backend has not implemented the" -ForegroundColor Yellow
    Write-Host "      readback ABI yet (RequestReadback returned NOT_SUPPORTED)." -ForegroundColor Yellow
    Write-Host "      This is EXPECTED until the Vulkan capture path lands; it is NOT a failure." -ForegroundColor Yellow
    Write-Host "      Marker: $outVulkan\SKIP.readback-not-supported" -ForegroundColor Yellow
} elseif ($vkExit -eq 3) {
    Write-Host ""
    Write-Host "NOTE: vulkan half PARTIAL -- some scenes faulted (see .FAILED.txt markers" -ForegroundColor Yellow
    Write-Host "      in $outVulkan). The scenes that DID render are still diffed below." -ForegroundColor Yellow
} elseif ($vkExit -ne 0) {
    Write-Error "vulkan half failed (exit $vkExit)"; exit 1
}

# -- 3. Diff -----------------------------------------------------------------
# Diff whenever BOTH halves produced frames (exit 0 or 3). A SKIP (exit 2) on
# either side means one backend produced nothing - nothing to compare.
$d3dOk = ($d3dExit -eq 0 -or $d3dExit -eq 3)
$vkOk = ($vkExit -eq 0 -or $vkExit -eq 3)
$anyPartial = ($d3dExit -eq 3 -or $vkExit -eq 3)
if ($d3dOk -and $vkOk) {
    Write-Host "== Diffing ==" -ForegroundColor Cyan
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) { $python = Get-Command python3 -ErrorAction SilentlyContinue }
    if ($null -eq $python) { Write-Error "python not found on PATH - cannot diff"; exit 1 }
    & $python.Source $diffTool $outD3d12 $outVulkan
    $diffExit = $LASTEXITCODE
    Write-Host ""
    Write-Host "report: $outVulkan\parity_report.md" -ForegroundColor Green
    if ($anyPartial) {
        Write-Host "NOTE: one or more halves were PARTIAL; scenes with a .FAILED.txt marker on" -ForegroundColor Yellow
        Write-Host "      a side are absent from that side and show under the diff's Faulted list." -ForegroundColor Yellow
    }
    if ($diffExit -ne 0) { exit 1 }
} else {
    Write-Host ""
    Write-Host "Diff skipped: only one backend produced frames." -ForegroundColor Yellow
    Write-Host "d3d12 frames: $outD3d12" -ForegroundColor Green
}

exit 0
