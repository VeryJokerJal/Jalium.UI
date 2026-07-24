# Compiles triangle_fill_vc.{vert,frag}.hlsl to SPIR-V with DXC and
# surgically replaces only the two arrays in the dedicated embedded header.
param(
    [string]$ShaderSrcDir = $PSScriptRoot,
    [string]$HeaderPath = (Join-Path $PSScriptRoot '..\include\vulkan_triangle_vc_shaders.h')
)

$ErrorActionPreference = 'Stop'

$dxc = $null
if ($env:VULKAN_SDK -and (Test-Path (Join-Path $env:VULKAN_SDK 'Bin\dxc.exe'))) {
    $dxc = Join-Path $env:VULKAN_SDK 'Bin\dxc.exe'
} else {
    $command = Get-Command dxc -ErrorAction SilentlyContinue
    if ($command) { $dxc = $command.Source }
}
if (-not $dxc) { throw 'dxc.exe not found (set VULKAN_SDK or put dxc on PATH)' }

function Compile-Spv([string]$Source, [string]$Profile) {
    $temporaryPath = [System.IO.Path]::GetTempFileName() + '.spv'
    try {
        & $dxc -spirv -T $Profile -E main -O3 $Source -Fo $temporaryPath | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "dxc failed for $Source" }
        $bytes = [System.IO.File]::ReadAllBytes($temporaryPath)
        if (($bytes.Length % 4) -ne 0) {
            throw "$Source SPIR-V length is not a multiple of 4"
        }
        return ,$bytes
    } finally {
        Remove-Item $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function Format-ArrayBody([byte[]]$Bytes) {
    $builder = [System.Text.StringBuilder]::new()
    $wordCount = $Bytes.Length / 4
    $line = '    '
    for ($wordIndex = 0; $wordIndex -lt $wordCount; $wordIndex++) {
        $byteIndex = $wordIndex * 4
        $word = [uint32]$Bytes[$byteIndex] -bor
            ([uint32]$Bytes[$byteIndex + 1] -shl 8) -bor
            ([uint32]$Bytes[$byteIndex + 2] -shl 16) -bor
            ([uint32]$Bytes[$byteIndex + 3] -shl 24)
        $line += ('0x{0:x8}u,' -f $word)
        if ((($wordIndex + 1) % 8) -eq 0) {
            [void]$builder.AppendLine($line)
            $line = '    '
        } else {
            $line += ' '
        }
    }
    if ($line.Trim().Length -gt 0) {
        [void]$builder.AppendLine($line.TrimEnd())
    }
    return $builder.ToString().TrimEnd([char[]]@(13, 10))
}

function Splice-Array([string]$Text, [string]$Symbol, [string]$Body) {
    $pattern = '(?s)(inline constexpr uint32_t ' +
        [regex]::Escape($Symbol) + '\[\] = \{\r?\n).*?(\r?\n\};)'
    $regex = [regex]::new($pattern)
    if (-not $regex.IsMatch($Text)) {
        throw "marker for $Symbol not found in header"
    }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        return $match.Groups[1].Value + $Body + $match.Groups[2].Value
    }
    return $regex.Replace($Text, $evaluator, 1)
}

$fragmentBytes = Compile-Spv (Join-Path $ShaderSrcDir 'triangle_fill_vc.frag.hlsl') 'ps_6_0'
$vertexBytes = Compile-Spv (Join-Path $ShaderSrcDir 'triangle_fill_vc.vert.hlsl') 'vs_6_0'

$originalBytes = [System.IO.File]::ReadAllBytes($HeaderPath)
$hadBom = $originalBytes.Length -ge 3 -and
    $originalBytes[0] -eq 0xEF -and
    $originalBytes[1] -eq 0xBB -and
    $originalBytes[2] -eq 0xBF
$text = [System.IO.File]::ReadAllText($HeaderPath)
$crlf = [string]::Concat([char]13, [char]10)
$lf = [string][char]10
$usesCrlf = $text.Contains($crlf)

$text = Splice-Array $text 'kTriangleFillVcFragShaderSpv' (Format-ArrayBody $fragmentBytes)
$text = Splice-Array $text 'kTriangleFillVcVertShaderSpv' (Format-ArrayBody $vertexBytes)

$text = $text.Replace($crlf, $lf)
if ($usesCrlf) { $text = $text.Replace($lf, $crlf) }
$encoding = [System.Text.UTF8Encoding]::new($hadBom)
[System.IO.File]::WriteAllText($HeaderPath, $text, $encoding)

Write-Host ("Spliced triangle_fill_vc frag+vert SPIR-V into {0} (frag={1}B vert={2}B)" -f
    $HeaderPath, $fragmentBytes.Length, $vertexBytes.Length)
