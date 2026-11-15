[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProbePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 300)][int]$Cycles = 300,
    [ValidateRange(500, 10000)][int]$CycleMilliseconds = 500
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProbePath = (Resolve-Path -LiteralPath $ProbePath).Path
if ([IO.Path]::GetFileName($ProbePath) -ne 'Jalium.UI.MemoryProbe.exe') {
    throw 'Select the locally built Jalium.UI.MemoryProbe.exe.'
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Choose a new output directory to preserve existing evidence.'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $OutputDirectory)
$initialHash = (Get-FileHash -LiteralPath $ProbePath -Algorithm SHA256).Hash
$exitDelay = 10000 + $Cycles * $CycleMilliseconds + 10000
$arguments = [Collections.Generic.List[string]]::new()
$arguments.AddRange([string[]]@('--windows', '2', '--lifetime-diagnostics', '--exit-after-ms', [string]$exitDelay))
for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
    $arguments.Add('--action')
    $arguments.Add(('{0}:rebuild:2' -f (10000 + $cycle * $CycleMilliseconds)))
}
$arguments.ToArray() | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'arguments.json') -Encoding UTF8
$stdoutPath = Join-Path $OutputDirectory 'stdout.log'
$stderrPath = Join-Path $OutputDirectory 'stderr.log'
$watch = [Diagnostics.Stopwatch]::StartNew()
$process = $null
$samples = [Collections.Generic.List[object]]::new()
try {
    $process = Start-Process -FilePath $ProbePath -ArgumentList $arguments.ToArray() -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $null = $process.Handle
    while (-not $process.HasExited) {
        if ($watch.ElapsedMilliseconds -gt $exitDelay + 60000) {
            [void]$process.CloseMainWindow()
            throw 'The rebuild diagnostic exceeded its scheduled exit grace period.'
        }
        $process.Refresh()
        if ($process.HasExited) { break }
        $samples.Add([pscustomobject]@{
            ElapsedMs = $watch.ElapsedMilliseconds
            WorkingSetBytes = $process.WorkingSet64
            PrivateBytes = $process.PrivateMemorySize64
            Threads = $process.Threads.Count
            Handles = $process.HandleCount
            Responding = $process.Responding
            CpuTotalMilliseconds = $process.TotalProcessorTime.TotalMilliseconds
            CpuUserMilliseconds = $process.UserProcessorTime.TotalMilliseconds
            CpuKernelMilliseconds = $process.PrivilegedProcessorTime.TotalMilliseconds
        })
        Start-Sleep -Milliseconds 500
    }
    $process.WaitForExit()
    $samples | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'process-samples.csv') -NoTypeInformation
    $stdout = [IO.File]::ReadAllText($stdoutPath)
    $stderr = [IO.File]::ReadAllText($stderrPath)
    $completed = [regex]::Matches($stdout, 'ACTION rebuild old=2 replacement=2 windows=2').Count
    $finalHash = (Get-FileHash -LiteralPath $ProbePath -Algorithm SHA256).Hash
    $result = [pscustomobject]@{
        DiagnosticOnly = $true
        BudgetAcceptance = $false
        ProbeSha256 = $initialHash
        SameExecutableAtExit = $initialHash -eq $finalHash
        CyclesRequested = $Cycles
        CyclesCompleted = $completed
        ExitCode = $process.ExitCode
        HasPassiveGcCounters = $stdout.Contains('LIFETIME reason=')
        StderrBytes = (Get-Item -LiteralPath $stderrPath).Length
        Passed = $process.ExitCode -eq 0 -and $completed -eq $Cycles -and
            $initialHash -eq $finalHash -and $stderr.Length -eq 0 -and
            $stdout.Contains('LIFETIME reason=') -and $stdout.Contains('THEME loaded=true') -and
            $stdout.Contains('NATIVE_GUARD_OK')
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'run-status.json') -Encoding UTF8
    $result | Format-List
    if (-not $result.Passed) {
        throw 'The rebuild diagnostic failed. Inspect its process log and run-status.json.'
    }
}
catch {
    if ($samples.Count -gt 0) {
        $samples | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'process-samples.csv') -NoTypeInformation
    }
    throw
}
finally {
    if ($null -ne $process) {
        $cleanupAction = 'none'
        $cleanupFailure = $null
        try {
            if (-not $process.HasExited) {
                # Only clean up the process created by this invocation. Never
                # search by process name: another session may own a probe too.
                $cleanupAction = 'close'
                [void]$process.CloseMainWindow()
                if (-not $process.WaitForExit(5000)) {
                    $cleanupAction = 'kill-after-close-timeout'
                    $process.Kill()
                    if (-not $process.WaitForExit(5000)) {
                        throw 'The owned probe did not exit after cleanup.'
                    }
                }
            }
        }
        catch { $cleanupFailure = $_.Exception.Message }
        finally {
            [pscustomobject]@{
                ProcessId = $process.Id
                Action = $cleanupAction
                Failure = $cleanupFailure
            } | ConvertTo-Json | Set-Content -Encoding UTF8 `
                -LiteralPath (Join-Path $OutputDirectory 'cleanup.json')
            $process.Dispose()
        }
        if ($cleanupFailure) { throw $cleanupFailure }
    }
}
