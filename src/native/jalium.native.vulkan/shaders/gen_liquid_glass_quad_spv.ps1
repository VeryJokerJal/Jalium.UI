# Compiles liquid_glass_quad.{vert,frag}.hlsl to SPIR-V (DXC) and SURGICALLY
# replaces BOTH the kLiquidGlassQuadVertexShaderSpv and
# kLiquidGlassQuadFragmentShaderSpv arrays inside
# ../include/vulkan_embedded_shaders.h (splice pattern of gen_solid_rect_spv.ps1).
# The vertex shader is re-emitted here because the LiquidGlass rewrite changed it
# to expand the quad by padding and pass on-screen pixel coords to the FS.

param(
    [string]$ShaderSrcDir = $PSScriptRoot,
    [string]$HeaderPath   = (Join-Path $PSScriptRoot '..\include\vulkan_embedded_shaders.h')
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
    & $dxc -spirv -T $profile -E main -O3 $src -Fo $tmp | Out-Null
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

$vs = Compile-Spv (Join-Path $ShaderSrcDir 'liquid_glass_quad.vert.hlsl') 'vs_6_0'
$vsBody = Format-ArrayBody $vs
$fs = Compile-Spv (Join-Path $ShaderSrcDir 'liquid_glass_quad.frag.hlsl') 'ps_6_0'
$fsBody = Format-ArrayBody $fs

$origBytes = [System.IO.File]::ReadAllBytes($HeaderPath)
$hadBom = ($origBytes.Length -ge 3 -and $origBytes[0] -eq 0xEF -and $origBytes[1] -eq 0xBB -and $origBytes[2] -eq 0xBF)
$text = [System.IO.File]::ReadAllText($HeaderPath)
$usesCrlf = $text.Contains("`r`n")

$text = Splice-Array $text 'kLiquidGlassQuadVertexShaderSpv' $vsBody
$text = Splice-Array $text 'kLiquidGlassQuadFragmentShaderSpv' $fsBody

$text = $text -replace "`r`n", "`n"
if ($usesCrlf) { $text = $text -replace "`n", "`r`n" }
$enc = New-Object System.Text.UTF8Encoding($hadBom)
[System.IO.File]::WriteAllText($HeaderPath, $text, $enc)
Write-Host ("Spliced kLiquidGlassQuad vert+frag SPIR-V into {0}  (vert={1}B frag={2}B)" -f $HeaderPath, $vs.Length, $fs.Length)
