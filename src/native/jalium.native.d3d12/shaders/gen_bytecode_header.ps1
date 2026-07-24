param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'compiled'),
    [string]$HeaderPath = (Join-Path $PSScriptRoot '..\include\d3d12_shader_bytecode.h')
)

$shaders = @(
    'sdf_rect.vs', 'sdf_rect.ps',
    'bitmap_text.vs', 'bitmap_text.ps', 'bitmap_text_smooth.ps',
    'bitmap_quad.vs', 'bitmap_quad.ps',
    'custom_effect.vs',
    'transition_quad.ps', 'desktop_backdrop.ps', 'snapshot_backdrop.ps',
    'color_matrix.ps', 'emboss.ps',
    'triangle.vs', 'triangle.ps',
    'gaussian_blur.cs'
)

function Format-ArrayBody([byte[]]$bytes) {
    $sb = [System.Text.StringBuilder]::new()
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($i % 16 -eq 0) { [void]$sb.Append('    ') }
        [void]$sb.Append(('0x{0:x2}' -f $bytes[$i]))
        if ($i -lt $bytes.Length - 1) { [void]$sb.Append(',') }
        if (($i + 1) % 16 -eq 0) {
            [void]$sb.AppendLine()
        }
        elseif ($i -lt $bytes.Length - 1) {
            [void]$sb.Append(' ')
        }
    }
    if ($bytes.Length % 16 -ne 0) { [void]$sb.AppendLine() }
    return $sb.ToString().TrimEnd("`r", "`n")
}

if (-not (Test-Path $HeaderPath)) {
    throw "Header not found: $HeaderPath"
}

$originalBytes = [System.IO.File]::ReadAllBytes($HeaderPath)
$hadBom = $originalBytes.Length -ge 3 `
    -and $originalBytes[0] -eq 0xEF `
    -and $originalBytes[1] -eq 0xBB `
    -and $originalBytes[2] -eq 0xBF
$text = [System.IO.File]::ReadAllText($HeaderPath)
$usesCrlf = $text.Contains("`r`n")
$updated = 0

foreach ($name in $shaders) {
    $csoFile = Join-Path $OutDir "$name.cso"
    if (-not (Test-Path $csoFile)) {
        Write-Warning "Missing: $csoFile - skipping"
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($csoFile)
    $varName = 'k' + ($name -replace '\.', '_')
    $pattern =
        '(?s)(static const unsigned int ' +
        [regex]::Escape("${varName}Size") +
        ' = )\d+(;\r?\nstatic const unsigned char ' +
        [regex]::Escape($varName) +
        '\[\] = \{\r?\n)(.*?)(\r?\n\};)'
    $regex = [regex]::new($pattern)
    $match = $regex.Match($text)
    if (-not $match.Success) {
        throw "Marker for $varName not found in $HeaderPath"
    }

    $embeddedHex = [regex]::Matches(
        $match.Groups[3].Value,
        '0x([0-9a-fA-F]{2})')
    $same = $embeddedHex.Count -eq $bytes.Length
    if ($same) {
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            if ([Convert]::ToByte(
                    $embeddedHex[$i].Groups[1].Value,
                    16) -ne $bytes[$i]) {
                $same = $false
                break
            }
        }
    }
    if ($same) {
        continue
    }

    $body = Format-ArrayBody $bytes
    $body = $body -replace "`r`n", "`n"
    if ($usesCrlf) {
        $body = $body -replace "`n", "`r`n"
    }

    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
        param($currentMatch)
        return $currentMatch.Groups[1].Value +
            $bytes.Length +
            $currentMatch.Groups[2].Value +
            $body +
            $currentMatch.Groups[4].Value
    }
    $text = $regex.Replace($text, $evaluator, 1)
    $updated++
}

if ($updated -gt 0) {
    $encoding = New-Object System.Text.UTF8Encoding($hadBom)
    [System.IO.File]::WriteAllText($HeaderPath, $text, $encoding)
}

Write-Host "Updated $updated shader array(s) in $HeaderPath"
