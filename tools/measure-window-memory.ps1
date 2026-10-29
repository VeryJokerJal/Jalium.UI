[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProbePath,

    [string]$OutputDirectory,

    [ValidateRange(1, 100)]
    [int]$Runs = 3,

    [ValidateRange(1, 64)]
    [int]$WindowCount = 1,

    [ValidateRange(0, 64)]
    [int]$ExpectedWindowCount = 0,

    [switch]$NativeTitleBar,

    # Use the complete-theme minimal host without MemoryProbe's internal
    # logging/module enumeration. Readiness and all samples stay external.
    [switch]$MinimalApplication,

    [string[]]$ProbeArgument = @(),

    [ValidateRange(1, 600)]
    [int]$StartupTimeoutSeconds = 60,

    [ValidateRange(0, 600)]
    [int]$StabilizationSeconds = 10,

    [ValidateRange(1, 3600)]
    [int]$MeasurementSeconds = 20,

    [ValidateRange(100, 10000)]
    [int]$IntervalMilliseconds = 500,

    [ValidateRange(10, 1000)]
    [int]$WindowResponseTimeoutMilliseconds = 100,

    # Explicit feature scenarios may initialize a renderer during warmup.
    # Zero keeps the same timeout as every other phase. Stable samples always
    # retain WindowResponseTimeoutMilliseconds, including feature scenarios.
    [ValidateScript({ $_ -eq 0 -or ($_ -ge 10 -and $_ -le 1000) })]
    [int]$StabilizationResponseTimeoutMilliseconds = 0,

    [ValidateRange(1, 2147483647)]
    [long]$MemoryBudgetBytes = 20000000,

    [switch]$RequireBudget
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($StabilizationResponseTimeoutMilliseconds -eq 0) {
    $StabilizationResponseTimeoutMilliseconds = $WindowResponseTimeoutMilliseconds
}

if (-not (Test-Path -LiteralPath $ProbePath -PathType Leaf)) {
    throw "Probe executable was not found: '$ProbePath'."
}

$ProbePath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ProbePath).Path)
if ($MinimalApplication -and
    ($WindowCount -ne 1 -or $ExpectedWindowCount -gt 1 -or $NativeTitleBar -or
     $ProbeArgument.Count -ne 0 -or
     [System.IO.Path]::GetFileNameWithoutExtension($ProbePath) -ne 'Jalium.UI.MinimalWindowProbe')) {
    throw 'MinimalApplication requires Jalium.UI.MinimalWindowProbe, one default window, and no additional arguments.'
}
if ($ExpectedWindowCount -eq 0) {
    $ExpectedWindowCount = $WindowCount
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\empty-window-memory\measure-$timestamp"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)

$conflictingOutputs = @(Get-ChildItem -LiteralPath $OutputDirectory -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^(run-\d+\.(csv|stdout\.log|stderr\.log)|summary\.csv|aggregate\.csv|all-samples\.csv)$' })
if ($conflictingOutputs.Count -gt 0) {
    throw "Output directory already contains measurement files: '$OutputDirectory'. Choose a fresh directory."
}

if (-not ([System.Management.Automation.PSTypeName]'JaliumMemoryProbe.NativeWindowInspector').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace JaliumMemoryProbe
{
    public sealed class WindowInspection
    {
        public IntPtr[] Handles { get; set; }
        public string[] Titles { get; set; }
        public int VisibleCount { get; set; }
        public int ResponsiveCount { get; set; }
    }

    public static class NativeWindowInspector
    {
        private const uint WM_NULL = 0x0000;
        private const uint WM_CLOSE = 0x0010;
        private const uint SMTO_BLOCK = 0x0001;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeoutMilliseconds,
            out IntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

        public static WindowInspection Inspect(int processId, uint responseTimeoutMilliseconds)
        {
            List<IntPtr> handles = EnumerateVisibleWindows(processId);
            List<string> titles = new List<string>(handles.Count);
            int responsiveCount = 0;

            foreach (IntPtr handle in handles)
            {
                titles.Add(ReadTitle(handle));
                IntPtr ignored;
                IntPtr result = SendMessageTimeout(
                    handle,
                    WM_NULL,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    SMTO_BLOCK | SMTO_ABORTIFHUNG,
                    responseTimeoutMilliseconds,
                    out ignored);
                if (result != IntPtr.Zero)
                {
                    responsiveCount++;
                }
            }

            return new WindowInspection
            {
                Handles = handles.ToArray(),
                Titles = titles.ToArray(),
                VisibleCount = handles.Count,
                ResponsiveCount = responsiveCount
            };
        }

        public static int RequestClose(int processId)
        {
            List<IntPtr> handles = EnumerateVisibleWindows(processId);
            int posted = 0;
            foreach (IntPtr handle in handles)
            {
                if (PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                {
                    posted++;
                }
            }
            return posted;
        }

        private static List<IntPtr> EnumerateVisibleWindows(int processId)
        {
            List<IntPtr> handles = new List<IntPtr>();
            EnumWindowsProc callback = delegate(IntPtr window, IntPtr parameter)
            {
                uint ownerProcessId;
                GetWindowThreadProcessId(window, out ownerProcessId);
                if (ownerProcessId == (uint)processId && IsWindowVisible(window))
                {
                    handles.Add(window);
                }
                return true;
            };

            if (!EnumWindows(callback, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed.");
            }

            return handles;
        }

        private static string ReadTitle(IntPtr window)
        {
            int length = GetWindowTextLength(window);
            StringBuilder text = new StringBuilder(Math.Max(1, length + 1));
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }
    }
}
'@
}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Argument
    )

    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = New-Object System.Text.StringBuilder
    $slash = [char]92
    $quote = [char]34
    [void]$builder.Append($quote)
    $slashCount = 0

    for ($index = 0; $index -lt $Argument.Length; $index++) {
        $character = $Argument[$index]
        if ($character -eq $slash) {
            $slashCount++
            continue
        }

        if ($character -eq $quote) {
            [void]$builder.Append($slash, ($slashCount * 2) + 1)
            [void]$builder.Append($quote)
            $slashCount = 0
            continue
        }

        if ($slashCount -gt 0) {
            [void]$builder.Append($slash, $slashCount)
            $slashCount = 0
        }
        [void]$builder.Append($character)
    }

    if ($slashCount -gt 0) {
        [void]$builder.Append($slash, $slashCount * 2)
    }
    [void]$builder.Append($quote)
    return $builder.ToString()
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [ValidateRange(0, 100)]
        [double]$Percentile
    )

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return $null
    }

    [double[]]$sorted = @($Values | Sort-Object)
    $rank = [Math]::Ceiling(($Percentile / 100.0) * $sorted.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($sorted.Count - 1, [int]$rank))
    return $sorted[$index]
}

function ConvertTo-Mebibytes {
    param([double]$Bytes)
    return [Math]::Round($Bytes / 1MB, 3)
}

function New-ProbeStartInfo {
    param(
        [string]$ExecutablePath,
        [int]$InitialWindowCount,
        [bool]$UseNativeTitleBar,
        [bool]$UseMinimalApplication,
        [string[]]$AdditionalArguments
    )

    $arguments = New-Object 'System.Collections.Generic.List[string]'
    $fileName = $ExecutablePath

    if ([System.IO.Path]::GetExtension($ExecutablePath) -ieq '.dll') {
        $dotnetCommand = Get-Command dotnet -ErrorAction Stop
        $fileName = $dotnetCommand.Source
        [void]$arguments.Add($ExecutablePath)
    }

    if (-not $UseMinimalApplication) {
        [void]$arguments.Add('--windows')
        [void]$arguments.Add($InitialWindowCount.ToString([Globalization.CultureInfo]::InvariantCulture))
    }
    if ($UseNativeTitleBar) {
        [void]$arguments.Add('--native-titlebar')
    }
    foreach ($argument in $AdditionalArguments) {
        [void]$arguments.Add($argument)
    }

    $quotedArguments = @($arguments | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ })
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $fileName
    $startInfo.Arguments = $quotedArguments -join ' '
    $startInfo.WorkingDirectory = Split-Path -Parent $ExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables['JALIUM_WORKING_SET_TRIM'] = 'off'

    return $startInfo
}

function Get-ProbeSample {
    param(
        [System.Diagnostics.Process]$Process,
        [System.Diagnostics.Stopwatch]$RunClock,
        [int]$RunNumber,
        [string]$Phase,
        [int]$ExpectedWindows,
        [int]$ResponseTimeoutMilliseconds
    )

    if ($Process.HasExited) {
        throw "Probe process $($Process.Id) exited before a '$Phase' sample could be collected. ExitCode=$($Process.ExitCode)."
    }

    $Process.Refresh()
    $inspectionClock = [Diagnostics.Stopwatch]::StartNew()
    $inspection = [JaliumMemoryProbe.NativeWindowInspector]::Inspect(
        $Process.Id,
        [uint32]$ResponseTimeoutMilliseconds)
    $inspectionClock.Stop()

    $threadCount = $Process.Threads.Count
    $handleCount = $Process.HandleCount
    $workingSetBytes = $Process.WorkingSet64
    $privateBytes = $Process.PrivateMemorySize64
    $peakWorkingSetBytes = $Process.PeakWorkingSet64
    $windowCheckPassed =
        $inspection.VisibleCount -eq $ExpectedWindows -and
        $inspection.ResponsiveCount -eq $ExpectedWindows

    $formattedHandles = @($inspection.Handles | ForEach-Object {
        '0x{0:x}' -f $_.ToInt64()
    }) -join ';'
    $formattedTitles = @($inspection.Titles | ForEach-Object {
        ($_ -replace '[\r\n;]', ' ')
    }) -join ';'

    return [pscustomobject][ordered]@{
        Run = $RunNumber
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        ElapsedMilliseconds = $RunClock.ElapsedMilliseconds
        Phase = $Phase
        ProcessId = $Process.Id
        WorkingSetBytes = $workingSetBytes
        WorkingSetMiB = ConvertTo-Mebibytes $workingSetBytes
        PrivateMemoryBytes = $privateBytes
        PrivateMemoryMiB = ConvertTo-Mebibytes $privateBytes
        PeakWorkingSetBytes = $peakWorkingSetBytes
        PeakWorkingSetMiB = ConvertTo-Mebibytes $peakWorkingSetBytes
        ThreadCount = $threadCount
        HandleCount = $handleCount
        ExpectedWindowCount = $ExpectedWindows
        VisibleWindowCount = $inspection.VisibleCount
        ResponsiveWindowCount = $inspection.ResponsiveCount
        WindowCheckPassed = $windowCheckPassed
        WindowResponseTimeoutMilliseconds = $ResponseTimeoutMilliseconds
        WindowInspectionMilliseconds = $inspectionClock.Elapsed.TotalMilliseconds
        WindowHandles = $formattedHandles
        WindowTitles = $formattedTitles
    }
}

function Add-SamplePhase {
    param(
        [System.Collections.Generic.List[object]]$Rows,
        [System.Diagnostics.Process]$Process,
        [System.Diagnostics.Stopwatch]$RunClock,
        [int]$RunNumber,
        [string]$Phase,
        [int]$DurationSeconds,
        [int]$IntervalMilliseconds,
        [int]$ExpectedWindows,
        [int]$ResponseTimeoutMilliseconds
    )

    $phaseClock = [System.Diagnostics.Stopwatch]::StartNew()
    $lastIndex = [int][Math]::Floor(($DurationSeconds * 1000.0) / $IntervalMilliseconds)
    $validationFailures = 0

    for ($sampleIndex = 0; $sampleIndex -le $lastIndex; $sampleIndex++) {
        $targetMilliseconds = $sampleIndex * $IntervalMilliseconds
        $remainingMilliseconds = $targetMilliseconds - $phaseClock.ElapsedMilliseconds
        if ($remainingMilliseconds -gt 0) {
            Start-Sleep -Milliseconds $remainingMilliseconds
        }

        $sample = Get-ProbeSample `
            -Process $Process `
            -RunClock $RunClock `
            -RunNumber $RunNumber `
            -Phase $Phase `
            -ExpectedWindows $ExpectedWindows `
            -ResponseTimeoutMilliseconds $ResponseTimeoutMilliseconds
        [void]$Rows.Add($sample)
        if (-not $sample.WindowCheckPassed) {
            $validationFailures++
        }
    }

    return $validationFailures
}

function New-RunSummary {
    param(
        [int]$RunNumber,
        [object[]]$Rows,
        [string]$Status,
        [string]$Failure,
        [object]$ExitCode,
        [string]$StartedUtc,
        [string]$CompletedUtc
    )

    $measurementRows = @($Rows | Where-Object { $_.Phase -eq 'measurement' })
    if ($measurementRows.Count -eq 0) {
        return [pscustomobject][ordered]@{
            Run = $RunNumber
            Status = $Status
            Failure = $Failure
            ExitCode = $ExitCode
            StartedUtc = $StartedUtc
            CompletedUtc = $CompletedUtc
            MeasurementSamples = 0
            MemoryBudgetBytes = $MemoryBudgetBytes
            WorkingSetBelowBudget = $false
            PrivateMemoryBelowBudget = $false
            WorkingSetMeanMiB = $null
            WorkingSetMinimumMiB = $null
            WorkingSetMaximumMiB = $null
            WorkingSetP50MiB = $null
            WorkingSetP95MiB = $null
            PrivateMemoryMeanMiB = $null
            PrivateMemoryMinimumMiB = $null
            PrivateMemoryMaximumMiB = $null
            PrivateMemoryP50MiB = $null
            PrivateMemoryP95MiB = $null
            PeakWorkingSetMaximumMiB = $null
            ThreadCountMean = $null
            ThreadCountMaximum = $null
            HandleCountMean = $null
            HandleCountMaximum = $null
            WindowValidationFailures = $null
        }
    }

    $workingSet = $measurementRows | Measure-Object -Property WorkingSetBytes -Average -Minimum -Maximum
    $privateMemory = $measurementRows | Measure-Object -Property PrivateMemoryBytes -Average -Minimum -Maximum
    $peakWorkingSet = $measurementRows | Measure-Object -Property PeakWorkingSetBytes -Maximum
    $threads = $measurementRows | Measure-Object -Property ThreadCount -Average -Maximum
    $handles = $measurementRows | Measure-Object -Property HandleCount -Average -Maximum
    [double[]]$workingSetValues = @($measurementRows | ForEach-Object { [double]$_.WorkingSetBytes })
    [double[]]$privateValues = @($measurementRows | ForEach-Object { [double]$_.PrivateMemoryBytes })

    return [pscustomobject][ordered]@{
        Run = $RunNumber
        Status = $Status
        Failure = $Failure
        ExitCode = $ExitCode
        StartedUtc = $StartedUtc
        CompletedUtc = $CompletedUtc
        MeasurementSamples = $measurementRows.Count
        MemoryBudgetBytes = $MemoryBudgetBytes
        WorkingSetBelowBudget = ($workingSet.Maximum -lt $MemoryBudgetBytes)
        PrivateMemoryBelowBudget = ($privateMemory.Maximum -lt $MemoryBudgetBytes)
        WorkingSetMeanMiB = ConvertTo-Mebibytes $workingSet.Average
        WorkingSetMinimumMiB = ConvertTo-Mebibytes $workingSet.Minimum
        WorkingSetMaximumMiB = ConvertTo-Mebibytes $workingSet.Maximum
        WorkingSetP50MiB = ConvertTo-Mebibytes (Get-Percentile -Values $workingSetValues -Percentile 50)
        WorkingSetP95MiB = ConvertTo-Mebibytes (Get-Percentile -Values $workingSetValues -Percentile 95)
        PrivateMemoryMeanMiB = ConvertTo-Mebibytes $privateMemory.Average
        PrivateMemoryMinimumMiB = ConvertTo-Mebibytes $privateMemory.Minimum
        PrivateMemoryMaximumMiB = ConvertTo-Mebibytes $privateMemory.Maximum
        PrivateMemoryP50MiB = ConvertTo-Mebibytes (Get-Percentile -Values $privateValues -Percentile 50)
        PrivateMemoryP95MiB = ConvertTo-Mebibytes (Get-Percentile -Values $privateValues -Percentile 95)
        PeakWorkingSetMaximumMiB = ConvertTo-Mebibytes $peakWorkingSet.Maximum
        ThreadCountMean = [Math]::Round([double]$threads.Average, 3)
        ThreadCountMaximum = $threads.Maximum
        HandleCountMean = [Math]::Round([double]$handles.Average, 3)
        HandleCountMaximum = $handles.Maximum
        WindowValidationFailures = @($measurementRows | Where-Object { -not $_.WindowCheckPassed }).Count
    }
}

$probeHash = (Get-FileHash -LiteralPath $ProbePath -Algorithm SHA256).Hash
$dotnetVersion = try {
    (& dotnet --version | Out-String).Trim()
}
catch {
    '<unavailable: {0}>' -f $_.Exception.Message
}

$metadata = [ordered]@{
    SchemaVersion = 3
    MeasurementKind = 'fresh process start; operating-system file/page caches were not cleared'
    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    ScriptPath = $MyInvocation.MyCommand.Path
    ProbePath = $ProbePath
    ProbeSha256 = $probeHash
    ProbeLengthBytes = (Get-Item -LiteralPath $ProbePath).Length
    ProbeArguments = $(if ($MinimalApplication) { @() } else { @('--windows', $WindowCount) + @($(if ($NativeTitleBar) { '--native-titlebar' })) + $ProbeArgument })
    MinimalApplication = [bool]$MinimalApplication
    Runs = $Runs
    WindowCount = $WindowCount
    ExpectedWindowCount = $ExpectedWindowCount
    NativeTitleBar = [bool]$NativeTitleBar
    StartupTimeoutSeconds = $StartupTimeoutSeconds
    StabilizationSeconds = $StabilizationSeconds
    MeasurementSeconds = $MeasurementSeconds
    IntervalMilliseconds = $IntervalMilliseconds
    WindowResponseTimeoutMilliseconds = $WindowResponseTimeoutMilliseconds
    StabilizationResponseTimeoutMilliseconds = $StabilizationResponseTimeoutMilliseconds
    MemoryBudgetBytes = $MemoryBudgetBytes
    BudgetMetric = 'maximum working set across every stable measurement sample'
    DisplayUnit = 'MiB = 1048576 bytes; default budget = 20000000 bytes'
    WorkingSetTrimEnvironment = 'off'
    MachineName = [Environment]::MachineName
    UserName = [Environment]::UserName
    OperatingSystem = [Environment]::OSVersion.VersionString
    Is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
    Is64BitMeasurementHost = [Environment]::Is64BitProcess
    ProcessorIdentifier = $env:PROCESSOR_IDENTIFIER
    ProcessorCount = [Environment]::ProcessorCount
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    ClrVersion = [Environment]::Version.ToString()
    DotnetSdkVersion = $dotnetVersion
}

$metadata | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.json') -Encoding UTF8

@(
    'MeasurementKind={0}' -f $metadata.MeasurementKind
    'CreatedUtc={0}' -f $metadata.CreatedUtc
    'ProbePath={0}' -f $metadata.ProbePath
    'ProbeSha256={0}' -f $metadata.ProbeSha256
    'Runs={0}' -f $Runs
    'WindowCount={0}' -f $WindowCount
    'ExpectedWindowCount={0}' -f $ExpectedWindowCount
    'NativeTitleBar={0}' -f [bool]$NativeTitleBar
    'StabilizationSeconds={0}' -f $StabilizationSeconds
    'MeasurementSeconds={0}' -f $MeasurementSeconds
    'IntervalMilliseconds={0}' -f $IntervalMilliseconds
    'MemoryBudgetBytes={0}' -f $MemoryBudgetBytes
    'BudgetMetric={0}' -f $metadata.BudgetMetric
    'JALIUM_WORKING_SET_TRIM=off'
    'OperatingSystem={0}' -f $metadata.OperatingSystem
    'PowerShellVersion={0}' -f $metadata.PowerShellVersion
    'DotnetSdkVersion={0}' -f $metadata.DotnetSdkVersion
) | Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.txt') -Encoding UTF8

$allRows = New-Object 'System.Collections.Generic.List[object]'
$summaries = New-Object 'System.Collections.Generic.List[object]'
$failedRuns = New-Object 'System.Collections.Generic.List[string]'

for ($runNumber = 1; $runNumber -le $Runs; $runNumber++) {
    $runLabel = 'run-{0:d2}' -f $runNumber
    $rows = New-Object 'System.Collections.Generic.List[object]'
    $process = $null
    $stdoutTask = $null
    $stderrTask = $null
    $stdout = ''
    $stderr = ''
    $status = 'Failed'
    $failure = ''
    $exitCode = $null
    $startedUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    $runClock = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $startInfo = New-ProbeStartInfo `
            -ExecutablePath $ProbePath `
            -InitialWindowCount $WindowCount `
            -UseNativeTitleBar ([bool]$NativeTitleBar) `
            -UseMinimalApplication ([bool]$MinimalApplication) `
            -AdditionalArguments $ProbeArgument

        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Process.Start returned false for '$ProbePath'."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        $startupClock = [System.Diagnostics.Stopwatch]::StartNew()
        $startupSampleIndex = 0
        $ready = $false
        while ($startupClock.Elapsed.TotalSeconds -lt $StartupTimeoutSeconds) {
            $targetMilliseconds = $startupSampleIndex * $IntervalMilliseconds
            $remainingMilliseconds = $targetMilliseconds - $startupClock.ElapsedMilliseconds
            if ($remainingMilliseconds -gt 0) {
                Start-Sleep -Milliseconds $remainingMilliseconds
            }

            $sample = Get-ProbeSample `
                -Process $process `
                -RunClock $runClock `
                -RunNumber $runNumber `
                -Phase 'startup' `
                -ExpectedWindows $ExpectedWindowCount `
                -ResponseTimeoutMilliseconds $WindowResponseTimeoutMilliseconds
            [void]$rows.Add($sample)
            if ($sample.WindowCheckPassed) {
                $ready = $true
                break
            }

            $startupSampleIndex++
        }

        if (-not $ready) {
            throw "Probe did not expose exactly $ExpectedWindowCount responsive visible window(s) within $StartupTimeoutSeconds second(s)."
        }

        $stabilizationFailures = Add-SamplePhase `
            -Rows $rows `
            -Process $process `
            -RunClock $runClock `
            -RunNumber $runNumber `
            -Phase 'stabilization' `
            -DurationSeconds $StabilizationSeconds `
            -IntervalMilliseconds $IntervalMilliseconds `
            -ExpectedWindows $ExpectedWindowCount `
            -ResponseTimeoutMilliseconds $StabilizationResponseTimeoutMilliseconds

        $measurementFailures = Add-SamplePhase `
            -Rows $rows `
            -Process $process `
            -RunClock $runClock `
            -RunNumber $runNumber `
            -Phase 'measurement' `
            -DurationSeconds $MeasurementSeconds `
            -IntervalMilliseconds $IntervalMilliseconds `
            -ExpectedWindows $ExpectedWindowCount `
            -ResponseTimeoutMilliseconds $WindowResponseTimeoutMilliseconds

        if (($stabilizationFailures + $measurementFailures) -ne 0) {
            throw "Window validation failed for $stabilizationFailures stabilization sample(s) and $measurementFailures measurement sample(s)."
        }

        # Inspect modules only after the last sample: even a read-only remote
        # module enumeration must not affect the measured startup/steady state.
        # The minimal host validates the complete theme before showing a HWND;
        # this external check supplies its native-payload provenance separately.
        $nativeModules = @($process.Modules | Where-Object { $_.ModuleName -like 'jalium.native.*' } |
            ForEach-Object {
                $modulePath = [IO.Path]::GetFullPath($_.FileName)
                [pscustomobject]@{
                    ModuleName = $_.ModuleName
                    Path = $modulePath
                    Sha256 = (Get-FileHash -LiteralPath $modulePath -Algorithm SHA256).Hash
                    MatchesPayloadDirectory = [string]::Equals(
                        (Split-Path -Parent $modulePath), (Split-Path -Parent $ProbePath),
                        [StringComparison]::OrdinalIgnoreCase)
                }
            })
        $nativeModules | Export-Csv -NoTypeInformation -Encoding UTF8 `
            -LiteralPath (Join-Path $OutputDirectory "$runLabel.native-modules.csv")
        if ($nativeModules.Count -lt 2 -or
            @($nativeModules | Where-Object { -not $_.MatchesPayloadDirectory }).Count -ne 0) {
            throw 'Loaded native modules did not match the selected complete local payload.'
        }

        [void][JaliumMemoryProbe.NativeWindowInspector]::RequestClose($process.Id)
        if (-not $process.WaitForExit(10000)) {
            throw "Probe did not exit within 10 seconds after WM_CLOSE was posted."
        }

        $exitCode = $process.ExitCode
        if ($exitCode -ne 0) {
            throw "Probe exited with code $exitCode."
        }

        $status = 'Passed'
    }
    catch {
        $failure = $_.Exception.ToString()
    }
    finally {
        if ($null -ne $process) {
            if (-not $process.HasExited) {
                try {
                    [void][JaliumMemoryProbe.NativeWindowInspector]::RequestClose($process.Id)
                    [void]$process.WaitForExit(5000)
                }
                catch {
                }
            }

            if (-not $process.HasExited) {
                try {
                    $process.Kill()
                    [void]$process.WaitForExit(5000)
                }
                catch {
                }
            }

            if ($process.HasExited -and $null -eq $exitCode) {
                $exitCode = $process.ExitCode
            }
        }

        if ($null -ne $stdoutTask) {
            try {
                [void]$stdoutTask.Wait(5000)
                if ($stdoutTask.IsCompleted) {
                    $stdout = $stdoutTask.Result
                }
            }
            catch {
                $stdout = "<stdout read failed: $($_.Exception.Message)>"
            }
        }
        if ($null -ne $stderrTask) {
            try {
                [void]$stderrTask.Wait(5000)
                if ($stderrTask.IsCompleted) {
                    $stderr = $stderrTask.Result
                }
            }
            catch {
                $stderr = "<stderr read failed: $($_.Exception.Message)>"
            }
        }

        $stdout | Set-Content -LiteralPath (Join-Path $OutputDirectory "$runLabel.stdout.log") -Encoding UTF8
        $stderr | Set-Content -LiteralPath (Join-Path $OutputDirectory "$runLabel.stderr.log") -Encoding UTF8

        if ($rows.Count -gt 0) {
            $rows | Export-Csv -LiteralPath (Join-Path $OutputDirectory "$runLabel.csv") -NoTypeInformation -Encoding UTF8
            foreach ($row in $rows) {
                [void]$allRows.Add($row)
            }
        }
        else {
            Set-Content -LiteralPath (Join-Path $OutputDirectory "$runLabel.csv") -Value '' -Encoding UTF8
        }

        if ($status -ne 'Passed' -and -not [string]::IsNullOrWhiteSpace($stderr)) {
            $failure = $failure + [Environment]::NewLine + 'Probe stderr:' + [Environment]::NewLine + $stderr.Trim()
        }

        $completedUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        $summary = New-RunSummary `
            -RunNumber $runNumber `
            -Rows ($rows.ToArray()) `
            -Status $status `
            -Failure $failure `
            -ExitCode $exitCode `
            -StartedUtc $startedUtc `
            -CompletedUtc $completedUtc
        [void]$summaries.Add($summary)
        $summaries | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding UTF8

        if ($status -ne 'Passed') {
            [void]$failedRuns.Add("Run ${runNumber}: $failure")
        }

        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

if ($allRows.Count -gt 0) {
    $allRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'all-samples.csv') -NoTypeInformation -Encoding UTF8
}

$allMeasurementRows = @($allRows | Where-Object { $_.Phase -eq 'measurement' })
if ($allMeasurementRows.Count -gt 0) {
    $workingSet = $allMeasurementRows | Measure-Object -Property WorkingSetBytes -Average -Minimum -Maximum
    $privateMemory = $allMeasurementRows | Measure-Object -Property PrivateMemoryBytes -Average -Minimum -Maximum
    [double[]]$workingSetValues = @($allMeasurementRows | ForEach-Object { [double]$_.WorkingSetBytes })
    [double[]]$privateValues = @($allMeasurementRows | ForEach-Object { [double]$_.PrivateMemoryBytes })

    $aggregate = [pscustomobject][ordered]@{
        MeasurementKind = $metadata.MeasurementKind
        RunsRequested = $Runs
        RunsPassed = @($summaries | Where-Object { $_.Status -eq 'Passed' }).Count
        MeasurementSamples = $allMeasurementRows.Count
        MemoryBudgetBytes = $MemoryBudgetBytes
        WorkingSetBelowBudget = ($workingSet.Maximum -lt $MemoryBudgetBytes)
        PrivateMemoryBelowBudget = ($privateMemory.Maximum -lt $MemoryBudgetBytes)
        WorkingSetMaximumBytes = [long]$workingSet.Maximum
        PrivateMemoryMaximumBytes = [long]$privateMemory.Maximum
        WorkingSetMeanMiB = ConvertTo-Mebibytes $workingSet.Average
        WorkingSetMinimumMiB = ConvertTo-Mebibytes $workingSet.Minimum
        WorkingSetMaximumMiB = ConvertTo-Mebibytes $workingSet.Maximum
        WorkingSetP50MiB = ConvertTo-Mebibytes (Get-Percentile -Values $workingSetValues -Percentile 50)
        WorkingSetP95MiB = ConvertTo-Mebibytes (Get-Percentile -Values $workingSetValues -Percentile 95)
        PrivateMemoryMeanMiB = ConvertTo-Mebibytes $privateMemory.Average
        PrivateMemoryMinimumMiB = ConvertTo-Mebibytes $privateMemory.Minimum
        PrivateMemoryMaximumMiB = ConvertTo-Mebibytes $privateMemory.Maximum
        PrivateMemoryP50MiB = ConvertTo-Mebibytes (Get-Percentile -Values $privateValues -Percentile 50)
        PrivateMemoryP95MiB = ConvertTo-Mebibytes (Get-Percentile -Values $privateValues -Percentile 95)
        WindowValidationFailures = @($allMeasurementRows | Where-Object { -not $_.WindowCheckPassed }).Count
    }
    $aggregate | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'aggregate.csv') -NoTypeInformation -Encoding UTF8
}

if ($failedRuns.Count -gt 0) {
    throw ("One or more fresh-process runs failed. Evidence: '$OutputDirectory'." +
        [Environment]::NewLine + ($failedRuns -join [Environment]::NewLine))
}

Write-Output "Measurement complete: $OutputDirectory"
$summaries | Format-Table Run, Status, WorkingSetMaximumMiB, PrivateMemoryMaximumMiB, WorkingSetBelowBudget -AutoSize

if ($RequireBudget -and ($allMeasurementRows.Count -eq 0 -or -not $aggregate.WorkingSetBelowBudget)) {
    throw "The stable working set exceeded the strict $MemoryBudgetBytes-byte budget. Evidence: '$OutputDirectory'."
}
