# Compiles the BUILT-IN GPU-RT effect shaders to SPIR-V (DXC) and SURGICALLY
# replaces ONLY the three arrays inside ../include/vulkan_effect_builtin_shaders.h:
#   kEffectBuiltinCustomShaderVsSpv   <- custom_shader_effect.vs.hlsl (vs_6_0)
#   kEffectBuiltinColorMatrixPsSpv    <- color_matrix_effect.ps.hlsl  (ps_6_0)
#   kEffectBuiltinEmbossPsSpv         <- emboss_effect.ps.hlsl        (ps_6_0)
#
# CRITICAL: these must byte-compatibly match the SPIR-V the runtime compiler
# (VulkanShaderCompiler::Compile) would have produced, because the same
# descriptor-set layout (EnsureCustomShaderBase: b0 UBO / t0 sampled image / s0
# sampler at kBShift=0 / kTShift=16 / kSShift=32) binds them. So we pass the
# EXACT same DXC flags the runtime path uses:
#   -fvk-t-shift 16 0  -fvk-s-shift 32 0  -fvk-u-shift 48 0  -Zpc  (-O3 in release)
# (see src/vulkan_shader_compiler.cpp and include/vulkan_shader_compiler.h).
#
# Same splice-in-place pattern as gen_bitmap_quad_spv.ps1 — the header is not
# fully regenerated, only the three arrays are replaced, everything else is left
# byte-for-byte intact.

param(
    [string]$ShaderSrcDir = $PSScriptRoot,
    [string]$HeaderPath   = (Join-Path $PSScriptRoot '..\include\vulkan_effect_builtin_shaders.h')
)

$ErrorActionPreference = 'Stop'

$dxc = $null
if ($env:VULKAN_SDK -and (Test-Path (Join-Path $env:VULKAN_SDK 'Bin\dxc.exe'))) {
    $dxc = Join-Path $env:VULKAN_SDK 'Bin\dxc.exe'
} else {
    $cmd = Get-Command dxc -ErrorAction SilentlyContinue
    if ($cmd) { $dxc = $cmd.Source }
}
if (-not $dxc) { throw "dxc.exe not found (set VULKAN_SDK or put dxc on PATH)" }

function Compile-Spv([string]$src, [string]$profile) {
    $tmp = [System.IO.Path]::GetTempFileName() + '.spv'
    # Register-class shifts + column-major matrices, matching
    # VulkanShaderCompiler::Compile exactly so the runtime descriptor layout binds.
    & $dxc -spirv -T $profile -E main `
        -fvk-t-shift 16 0 -fvk-s-shift 32 0 -fvk-u-shift 48 0 `
        -Zpc -O3 $src -Fo $tmp | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dxc failed for $src" }
    $bytes = [System.IO.File]::ReadAllBytes($tmp)
    Remove-Item $tmp -Force
    if (($bytes.Length % 4) -ne 0) { throw "$src SPIR-V length not multiple of 4" }
    return ,$bytes
}

function Format-ArrayBody([byte[]]$bytes) {
    $sb = [System.Text.StringBuilder]::new()
    $words = $bytes.Length / 4
    $line = '    '
    for ($w = 0; $w -lt $words; $w++) {
        $i = $w * 4
        $word = [uint32]$bytes[$i] -bor ([uint32]$bytes[$i+1] -shl 8) -bor ([uint32]$bytes[$i+2] -shl 16) -bor ([uint32]$bytes[$i+3] -shl 24)
        $line += ('0x{0:x8}u,' -f $word)
        if ((($w + 1) % 8) -eq 0) { [void]$sb.AppendLine($line); $line = '    ' } else { $line += ' ' }
    }
    if ($line.Trim().Length -gt 0) { [void]$sb.AppendLine($line.TrimEnd()) }
    return $sb.ToString().TrimEnd("`r","`n")
}

function Splice-Array([string]$text, [string]$sym, [string]$body) {
    $pattern = "(?s)(inline constexpr uint32_t " + [regex]::Escape($sym) + "\[\] = \{\r?\n).*?(\r?\n\};)"
    $re = [regex]::new($pattern)
    if (-not $re.IsMatch($text)) { throw "marker for $sym not found in header" }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
        param($m)
        return $m.Groups[1].Value + $body + $m.Groups[2].Value
    }
    return $re.Replace($text, $evaluator, 1)
}

$vs = Compile-Spv (Join-Path $ShaderSrcDir 'custom_shader_effect.vs.hlsl') 'vs_6_0'
$cm = Compile-Spv (Join-Path $ShaderSrcDir 'color_matrix_effect.ps.hlsl') 'ps_6_0'
$em = Compile-Spv (Join-Path $ShaderSrcDir 'emboss_effect.ps.hlsl') 'ps_6_0'
$og = Compile-Spv (Join-Path $ShaderSrcDir 'outer_glow_effect.ps.hlsl') 'ps_6_0'

$vsBody = Format-ArrayBody $vs
$cmBody = Format-ArrayBody $cm
$emBody = Format-ArrayBody $em
$ogBody = Format-ArrayBody $og

$origBytes = [System.IO.File]::ReadAllBytes($HeaderPath)
$hadBom = ($origBytes.Length -ge 3 -and $origBytes[0] -eq 0xEF -and $origBytes[1] -eq 0xBB -and $origBytes[2] -eq 0xBF)
$text = [System.IO.File]::ReadAllText($HeaderPath)
$usesCrlf = $text.Contains("`r`n")

$text = Splice-Array $text 'kEffectBuiltinCustomShaderVsSpv' $vsBody
$text = Splice-Array $text 'kEffectBuiltinColorMatrixPsSpv'  $cmBody
$text = Splice-Array $text 'kEffectBuiltinEmbossPsSpv'       $emBody
$text = Splice-Array $text 'kEffectBuiltinOuterGlowPsSpv'    $ogBody

$text = $text -replace "`r`n", "`n"
if ($usesCrlf) { $text = $text -replace "`n", "`r`n" }
$enc = New-Object System.Text.UTF8Encoding($hadBom)
[System.IO.File]::WriteAllText($HeaderPath, $text, $enc)
Write-Host ("Spliced built-in effect SPIR-V into {0}  (vs={1}B color_matrix={2}B emboss={3}B outer_glow={4}B)" -f $HeaderPath, $vs.Length, $cm.Length, $em.Length, $og.Length)
