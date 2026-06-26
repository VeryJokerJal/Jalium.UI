# Compiles solid_rect.{vert,frag}.hlsl to SPIR-V (DXC) and SURGICALLY replaces
# ONLY the kSolidRectVertexShaderSpv / kSolidRectFragmentShaderSpv arrays inside
# ../include/vulkan_embedded_shaders.h. That header aggregates many shaders
# (frame-composite, etc.) that are NOT regenerated here, so we must NOT rewrite
# the whole file — we splice the two arrays in place and leave everything else
# byte-for-byte intact.
#
# Both shaders use [[vk::push_constant]] (DXC-only syntax), so DXC -spirv is the
# only valid compiler here (FXC would reject the attribute).

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

# Emit the array BODY only (the indented 0x.... lines), matching the existing
# 8-words-per-line, '0x%08xu,' formatting already used in the header.
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

# Replace the body between 'inline constexpr uint32_t <sym>[] = {' and the next '};'.
function Splice-Array([string]$text, [string]$sym, [string]$body) {
    $pattern = "(?s)(inline constexpr uint32_t " + [regex]::Escape($sym) + "\[\] = \{\r?\n).*?(\r?\n\};)"
    $re = [regex]::new($pattern)
    if (-not $re.IsMatch($text)) { throw "marker for $sym not found in header" }
    # Use a MatchEvaluator so '$' / backslashes in $body are NOT treated as
    # substitution tokens.
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
        param($m)
        return $m.Groups[1].Value + $body + $m.Groups[2].Value
    }
    return $re.Replace($text, $evaluator, 1)
}

$fs = Compile-Spv (Join-Path $ShaderSrcDir 'solid_rect.frag.hlsl') 'ps_6_0'
$vs = Compile-Spv (Join-Path $ShaderSrcDir 'solid_rect.vert.hlsl') 'vs_6_0'

$fsBody = Format-ArrayBody $fs
$vsBody = Format-ArrayBody $vs

$origBytes = [System.IO.File]::ReadAllBytes($HeaderPath)
$hadBom = ($origBytes.Length -ge 3 -and $origBytes[0] -eq 0xEF -and $origBytes[1] -eq 0xBB -and $origBytes[2] -eq 0xBF)
$text = [System.IO.File]::ReadAllText($HeaderPath)
$usesCrlf = $text.Contains("`r`n")

$text = Splice-Array $text 'kSolidRectFragmentShaderSpv' $fsBody
$text = Splice-Array $text 'kSolidRectVertexShaderSpv'   $vsBody

# Normalise the spliced-in bodies (StringBuilder.AppendLine emits CRLF) back to
# the file's original newline convention so the diff stays confined to the two
# arrays, and preserve the original UTF-8 BOM presence.
$text = $text -replace "`r`n", "`n"
if ($usesCrlf) { $text = $text -replace "`n", "`r`n" }
$enc = New-Object System.Text.UTF8Encoding($hadBom)
[System.IO.File]::WriteAllText($HeaderPath, $text, $enc)
Write-Host ("Spliced kSolidRect frag+vert SPIR-V into {0}  (frag={1}B vert={2}B)" -f $HeaderPath, $fs.Length, $vs.Length)
