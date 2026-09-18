[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProbePath,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProbePath = (Resolve-Path -LiteralPath $ProbePath).Path
if ([IO.Path]::GetFileName($ProbePath) -ne 'Jalium.UI.StartupAllocationProbe.exe') {
    throw 'This diagnostic requires Jalium.UI.StartupAllocationProbe.exe.'
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Choose a new output directory so previous diagnostic evidence is preserved.'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $OutputDirectory)
$imageHash = (Get-FileHash -LiteralPath $ProbePath -Algorithm SHA256).Hash

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StartupLayerWindowCheck
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);
}
'@

$variants = @(
    @{ Name = 'native-shell-only'; Argument = '--native-shell-only' },
    @{ Name = 'application-native-shell'; Argument = '--application-native-shell' },
    @{ Name = 'framework-window'; Argument = $null }
)
$summaries = [Collections.Generic.List[object]]::new()
$oldTrim = [Environment]::GetEnvironmentVariable('JALIUM_WORKING_SET_TRIM', 'Process')
try {
    [Environment]::SetEnvironmentVariable('JALIUM_WORKING_SET_TRIM', 'off', 'Process')
    foreach ($variant in $variants) {
        if ((Get-FileHash -LiteralPath $ProbePath -Algorithm SHA256).Hash -ne $imageHash) {
            throw 'The executable changed between diagnostic modes. Freeze the build before measuring.'
        }
        $stdout = Join-Path $OutputDirectory ($variant.Name + '.stdout.log')
        $stderr = Join-Path $OutputDirectory ($variant.Name + '.stderr.log')
        $start = @{
            FilePath = $ProbePath
            PassThru = $true
            RedirectStandardOutput = $stdout
            RedirectStandardError = $stderr
        }
        if ($variant.Argument) { $start.ArgumentList = $variant.Argument }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process @start
        # Keep the original process handle open so Windows PowerShell can read
        # its exit code after the independently timed window has closed.
        $null = $process.Handle
        $samples = [Collections.Generic.List[object]]::new()
        $firstVisibleMs = $null
        try {
            while (-not $process.HasExited) {
                if ($watch.ElapsedMilliseconds -gt 30000) {
                    [void]$process.CloseMainWindow()
                    throw "Diagnostic '$($variant.Name)' did not exit at its ten-second timer."
                }
                $process.Refresh()
                if ($process.HasExited) { break }
                $handle = $process.MainWindowHandle
                $visible = $handle -ne [IntPtr]::Zero -and [StartupLayerWindowCheck]::IsWindowVisible($handle)
                if ($visible -and $null -eq $firstVisibleMs) {
                    $firstVisibleMs = $watch.ElapsedMilliseconds
                }
                $visibleElapsedMs = if ($null -eq $firstVisibleMs) { -1 } else { $watch.ElapsedMilliseconds - $firstVisibleMs }
                $samples.Add([pscustomobject]@{
                    Mode = $variant.Name
                    ProcessId = $process.Id
                    ElapsedMs = $watch.ElapsedMilliseconds
                    VisibleElapsedMs = $visibleElapsedMs
                    WorkingSetBytes = $process.WorkingSet64
                    PrivateBytes = $process.PrivateMemorySize64
                    Visible = $visible
                    Responding = $process.Responding
                    InDiagnosticWindow = $visibleElapsedMs -ge 2000 -and $visibleElapsedMs -le 8500
                })
                Start-Sleep -Milliseconds 250
            }
            $process.WaitForExit()
            $samples | Export-Csv -LiteralPath (Join-Path $OutputDirectory ($variant.Name + '.samples.csv')) -NoTypeInformation
            $stable = @($samples | Where-Object InDiagnosticWindow)
            $outText = [IO.File]::ReadAllText($stdout)
            $valid = $process.ExitCode -eq 0 -and $stable.Count -ge 16 -and
                @($stable | Where-Object { -not $_.Visible -or -not $_.Responding }).Count -eq 0 -and
                $outText.Contains('stage=stable_10s')
            $summaries.Add([pscustomobject]@{
                Mode = $variant.Name
                DiagnosticOnly = $true
                BudgetAcceptance = $false
                SameExecutableSha256 = $imageHash
                ExitCode = $process.ExitCode
                Valid = $valid
                Samples = $stable.Count
                WorkingSetMeanMiB = [math]::Round(($stable | Measure-Object WorkingSetBytes -Average).Average / 1MB, 3)
                WorkingSetMaxMiB = [math]::Round(($stable | Measure-Object WorkingSetBytes -Maximum).Maximum / 1MB, 3)
                PrivateMaxMiB = [math]::Round(($stable | Measure-Object PrivateBytes -Maximum).Maximum / 1MB, 3)
            })
        }
        finally {
            $process.Dispose()
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('JALIUM_WORKING_SET_TRIM', $oldTrim, 'Process')
}
$finalImageHash = (Get-FileHash -LiteralPath $ProbePath -Algorithm SHA256).Hash
if ($finalImageHash -ne $imageHash) {
    throw 'The executable changed during diagnostics. This run is not a valid comparison.'
}
$summaries | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation
$summaries | Format-Table Mode, Valid, Samples, WorkingSetMeanMiB, WorkingSetMaxMiB, PrivateMaxMiB -AutoSize
if (@($summaries | Where-Object { -not $_.Valid }).Count) {
    throw 'At least one diagnostic mode failed. See its stdout, stderr and sample CSV.'
}
