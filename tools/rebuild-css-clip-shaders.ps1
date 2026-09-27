param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\css-clip-native\shaders'),
    [string]$FxcPath
)
$ErrorActionPreference = 'Stop'
$cssRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cssVulkanShaders = Join-Path $cssRoot 'src\native\jalium.native.vulkan\shaders'
$cssVulkanHeader = Join-Path $cssRoot 'src\native\jalium.native.vulkan\include\vulkan_embedded_shaders.h'
$cssDxc = (Get-Command dxc -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& python (Join-Path $PSScriptRoot 'generate-elliptical-clip-shader.py')
if ($LASTEXITCODE -ne 0) { throw 'Clip HLSL source embedding failed.' }

if (-not $FxcPath) {
    $cssSdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $FxcPath = Get-ChildItem -LiteralPath $cssSdkBin -Filter fxc.exe -Recurse |
        Where-Object FullName -Match '\\x64\\fxc\.exe$' | Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $FxcPath) { throw 'FXC was not found; pass -FxcPath from the Windows SDK.' }
$cssD3dShaders = Join-Path $cssRoot 'src\native\jalium.native.d3d12\shaders'
& $FxcPath /nologo /T vs_5_1 /O3 /E main /Fo (Join-Path $OutputDirectory 'sdf_rect.vs.cso') (Join-Path $cssD3dShaders 'sdf_rect.vs.hlsl')
if ($LASTEXITCODE -ne 0) { throw 'FXC failed for sdf_rect.vs.' }
foreach ($cssShader in @('sdf_rect.ps', 'bitmap_text.ps', 'bitmap_text_smooth.ps', 'bitmap_quad.ps', 'triangle.ps')) {
    & $FxcPath /nologo /T ps_5_1 /O3 /E main /Fo (Join-Path $OutputDirectory ($cssShader + '.cso')) (Join-Path $cssD3dShaders ($cssShader + '.hlsl'))
    if ($LASTEXITCODE -ne 0) { throw "FXC failed for $cssShader." }
}
& (Join-Path $cssD3dShaders 'gen_bytecode_header.ps1') -OutDir $OutputDirectory

foreach ($cssGenerator in @('solid_rect', 'bitmap_quad', 'bitmap_premul', 'bitmap_lcd', 'text_glyph',
    'triangle_fill_vc', 'impeller_solid_fill', 'vello_composite', 'blur_quad', 'backdrop_quad',
    'liquid_glass_quad', 'transition_quad', 'ink_composite')) {
    & (Join-Path $cssVulkanShaders ('gen_' + $cssGenerator + '_spv.ps1'))
}

# The simple triangle pipeline shares the aggregate header but has no dedicated generator.
$cssSpv = Join-Path $OutputDirectory 'triangle_fill.frag.spv'
& $cssDxc -spirv -T ps_6_0 -E main -O3 (Join-Path $cssVulkanShaders 'triangle_fill.frag.hlsl') -Fo $cssSpv
if ($LASTEXITCODE -ne 0) { throw 'Triangle clip shader compilation failed.' }
$cssBytes = [IO.File]::ReadAllBytes($cssSpv)
$cssBody = [Text.StringBuilder]::new()
for ($cssOffset = 0; $cssOffset -lt $cssBytes.Length; $cssOffset += 4) {
    if ($cssOffset % 32 -eq 0) { [void]$cssBody.Append('    ') }
    [void]$cssBody.Append(('0x{0:x8}u,' -f [BitConverter]::ToUInt32($cssBytes, $cssOffset)))
    if ($cssOffset % 32 -eq 28) { [void]$cssBody.AppendLine() } else { [void]$cssBody.Append(' ') }
}
$cssText = [IO.File]::ReadAllText($cssVulkanHeader)
$cssPattern = [regex]::new('(?s)(inline constexpr uint32_t kTriangleFillFragmentShaderSpv\[\] = \{\r?\n).*?(\r?\n\};)')
if ($cssPattern.Matches($cssText).Count -ne 1) { throw 'Triangle shader array marker is missing or ambiguous.' }
$cssText = $cssPattern.Replace($cssText, [Text.RegularExpressions.MatchEvaluator]{param($match)
    $match.Groups[1].Value + $cssBody.ToString().TrimEnd() + $match.Groups[2].Value
}, 1)
[IO.File]::WriteAllText($cssVulkanHeader, $cssText, [Text.UTF8Encoding]::new($false))
