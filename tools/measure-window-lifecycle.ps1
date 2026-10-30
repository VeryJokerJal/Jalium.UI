[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProbePath,

    [string]$OutputDirectory,

    [switch]$NativeTitleBar,

    [ValidateRange(1, 100)]
    [int]$RebuildCycles = 10,

    [ValidateRange(1, 120)]
    [int]$InitialWarmupSeconds = 10,

    [ValidateRange(1, 120)]
    [int]$InitialStableSeconds = 10,

    [ValidateRange(1, 120)]
    [int]$PhaseWarmupSeconds = 5,

    [ValidateRange(1, 120)]
    [int]$PhaseStableSeconds = 5,

    [ValidateRange(100, 5000)]
    [int]$IntervalMilliseconds = 500,

    [ValidateRange(10, 1000)]
    [int]$WindowResponseTimeoutMilliseconds = 100,

    [ValidateRange(1, 600)]
    [int]$StartupTimeoutSeconds = 60,

    [ValidateRange(1, 120)]
    [int]$ScheduledExitGraceSeconds = 15,

    [ValidateRange(0, 7200)]
    [int]$OverallTimeoutSeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-Mebibytes {
    param([double]$Bytes)

    return [Math]::Round($Bytes / 1MB, 3)
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

function ConvertTo-PowerShellLiteral {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    return "'" + $Value.Replace("'", "''") + "'"
}

function ConvertTo-CommandLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Arguments
    )

    $parts = New-Object 'System.Collections.Generic.List[string]'
    [void]$parts.Add((ConvertTo-ProcessArgument -Argument $Executable))
    foreach ($argument in $Arguments) {
        [void]$parts.Add((ConvertTo-ProcessArgument -Argument $argument))
    }
    return $parts -join ' '
}

function Write-CsvOrEmpty {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.ICollection]$Rows,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ($Rows.Count -gt 0) {
        $Rows | Export-Csv -LiteralPath $Path -NoTypeInformation -Encoding UTF8
    }
    else {
        Set-Content -LiteralPath $Path -Value '' -Encoding UTF8
    }
}

function Assert-WithinDeadline {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Stopwatch]$Clock,

        [Parameter(Mandatory = $true)]
        [long]$DeadlineMilliseconds,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Clock.ElapsedMilliseconds -ge $DeadlineMilliseconds) {
        throw "Overall lifecycle deadline expired while $Context. Elapsed=$($Clock.ElapsedMilliseconds)ms Deadline=${DeadlineMilliseconds}ms."
    }
}

function Wait-UntilLifecycleTarget {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Stopwatch]$LifecycleClock,

        [Parameter(Mandatory = $true)]
        [long]$TargetMilliseconds,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Stopwatch]$OverallClock,

        [Parameter(Mandatory = $true)]
        [long]$DeadlineMilliseconds
    )

    while ($true) {
        if ($Process.HasExited) {
            throw "Probe process $($Process.Id) exited before scheduled lifecycle sample ${TargetMilliseconds}ms. ExitCode=$($Process.ExitCode)."
        }

        Assert-WithinDeadline -Clock $OverallClock -DeadlineMilliseconds $DeadlineMilliseconds -Context "waiting for lifecycle sample ${TargetMilliseconds}ms"

        $remaining = $TargetMilliseconds - $LifecycleClock.ElapsedMilliseconds
        if ($remaining -le 0) {
            return
        }

        $sleepMilliseconds = [int][Math]::Min(100, $remaining)
        Start-Sleep -Milliseconds $sleepMilliseconds
    }
}

function Get-TaskText {
    param(
        [object]$Task,
        [string]$StreamName,
        [int]$TimeoutMilliseconds = 5000
    )

    if ($null -eq $Task) {
        return ''
    }

    try {
        [void]$Task.Wait($TimeoutMilliseconds)
        if (-not $Task.IsCompleted) {
            return "<$StreamName read did not complete within ${TimeoutMilliseconds}ms>"
        }
        return [string]$Task.Result
    }
    catch {
        return "<$StreamName read failed: $($_.Exception.Message)>"
    }
}

function New-HashRow {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Role,

        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    $rootWithSeparator = $Root.TrimEnd([char[]]'\/') + [System.IO.Path]::DirectorySeparatorChar
    if ($File.FullName.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        $relativePath = $File.FullName.Substring($rootWithSeparator.Length)
    }
    else {
        $relativePath = $File.Name
    }

    return [pscustomobject][ordered]@{
        Role = $Role
        RelativePath = $relativePath
        FullPath = $File.FullName
        LengthBytes = $File.Length
        LastWriteTimeUtc = $File.LastWriteTimeUtc.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        Sha256 = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash
    }
}

function Write-InputHashManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,

        [Parameter(Mandatory = $true)]
        [string]$ProbePath,

        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $rows = New-Object 'System.Collections.Generic.List[object]'
    $scriptItem = Get-Item -LiteralPath $ScriptPath
    $probeItem = Get-Item -LiteralPath $ProbePath
    $payloadRoot = Split-Path -Parent $ProbePath
    [void]$rows.Add((New-HashRow -Role 'measurement-script' -Root (Split-Path -Parent $ScriptPath) -File $scriptItem))
    [void]$rows.Add((New-HashRow -Role 'probe-entrypoint' -Root $payloadRoot -File $probeItem))

    $outputPrefix = $OutputDirectory.TrimEnd([char[]]'\/') + [System.IO.Path]::DirectorySeparatorChar
    $payloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force | Sort-Object FullName)
    foreach ($file in $payloadFiles) {
        if ($file.FullName -ieq $probeItem.FullName) {
            continue
        }
        if ($file.FullName.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        [void]$rows.Add((New-HashRow -Role 'probe-payload' -Root $payloadRoot -File $file))
    }

    $rows | Export-Csv -LiteralPath $Destination -NoTypeInformation -Encoding UTF8
    return $rows
}

function Write-ArtifactHashManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $rows = New-Object 'System.Collections.Generic.List[object]'
    $files = @(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse -Force | Sort-Object FullName)
    foreach ($file in $files) {
        if ($file.FullName -ieq $Destination) {
            continue
        }
        [void]$rows.Add((New-HashRow -Role 'measurement-evidence' -Root $OutputDirectory -File $file))
    }

    if ($rows.Count -gt 0) {
        $rows | Export-Csv -LiteralPath $Destination -NoTypeInformation -Encoding UTF8
    }
}

function New-ProbeLaunch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ProbeArguments
    )

    $fileName = $ExecutablePath
    $arguments = New-Object 'System.Collections.Generic.List[string]'

    if ([System.IO.Path]::GetExtension($ExecutablePath) -ieq '.dll') {
        $dotnetCommand = Get-Command dotnet -ErrorAction Stop | Select-Object -First 1
        $fileName = $dotnetCommand.Source
        [void]$arguments.Add($ExecutablePath)
    }
    foreach ($argument in $ProbeArguments) {
        [void]$arguments.Add($argument)
    }

    [string[]]$argumentArray = $arguments.ToArray()
    $quotedArguments = @($argumentArray | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ })
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $fileName
    $startInfo.Arguments = $quotedArguments -join ' '
    $startInfo.WorkingDirectory = Split-Path -Parent $ExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables['JALIUM_WORKING_SET_TRIM'] = 'off'
    [void]$startInfo.EnvironmentVariables.Remove('JALIUM_RENDER_BACKEND')

    return [pscustomobject]@{
        StartInfo = $startInfo
        FileName = $fileName
        Arguments = $argumentArray
        CommandLine = ConvertTo-CommandLine -Executable $fileName -Arguments $argumentArray
    }
}

function Get-LifecycleSample {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Stopwatch]$LifecycleClock,

        [Parameter(Mandatory = $true)]
        [object]$Stage,

        [Parameter(Mandatory = $true)]
        [int]$SampleIndex,

        [Parameter(Mandatory = $true)]
        [long]$ScheduledElapsedMilliseconds,

        [Parameter(Mandatory = $true)]
        [int]$ResponseTimeoutMilliseconds
    )

    if ($Process.HasExited) {
        throw "Probe process $($Process.Id) exited before stage '$($Stage.Stage)' sample $SampleIndex. ExitCode=$($Process.ExitCode)."
    }

    $inspectionClock = [System.Diagnostics.Stopwatch]::StartNew()
    $inspection = [JaliumWindowLifecycle.NativeWindowInspector]::Inspect(
        $Process.Id,
        [uint32]$ResponseTimeoutMilliseconds)
    $inspectionClock.Stop()

    $Process.Refresh()
    $actualElapsedMilliseconds = $LifecycleClock.ElapsedMilliseconds
    $stageOffsetMilliseconds = $ScheduledElapsedMilliseconds - [long]$Stage.StartMilliseconds
    $stable = $stageOffsetMilliseconds -ge [long]$Stage.WarmupMilliseconds
    $windowCheckPassed =
        $inspection.VisibleCount -eq [int]$Stage.ExpectedWindowCount -and
        $inspection.ResponsiveCount -eq [int]$Stage.ExpectedWindowCount

    $formattedHandles = @($inspection.Handles | ForEach-Object {
        '0x{0:x}' -f $_.ToInt64()
    }) -join ';'
    $formattedTitles = @($inspection.Titles | ForEach-Object {
        ($_ -replace '[\r\n;]', ' ')
    }) -join ';'

    return [pscustomobject][ordered]@{
        SampleIndex = $SampleIndex
        TimestampUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        ProcessId = $Process.Id
        ScheduledElapsedMilliseconds = $ScheduledElapsedMilliseconds
        ActualElapsedMilliseconds = $actualElapsedMilliseconds
        ScheduleLagMilliseconds = $actualElapsedMilliseconds - $ScheduledElapsedMilliseconds
        StageIndex = $Stage.StageIndex
        Stage = $Stage.Stage
        StageAction = $Stage.Action
        StageOffsetMilliseconds = $stageOffsetMilliseconds
        SampleKind = $(if ($stable) { 'stable' } else { 'warmup' })
        StablePhaseSample = [bool]$stable
        ExpectedWindowCount = $Stage.ExpectedWindowCount
        VisibleWindowCount = $inspection.VisibleCount
        ResponsiveWindowCount = $inspection.ResponsiveCount
        WindowCheckPassed = [bool]$windowCheckPassed
        InspectionDurationMilliseconds = $inspectionClock.ElapsedMilliseconds
        WindowHandles = $formattedHandles
        WindowTitles = $formattedTitles
        WorkingSetBytes = $Process.WorkingSet64
        WorkingSetMiB = ConvertTo-Mebibytes $Process.WorkingSet64
        PrivateMemoryBytes = $Process.PrivateMemorySize64
        PrivateMemoryMiB = ConvertTo-Mebibytes $Process.PrivateMemorySize64
        PeakWorkingSetBytes = $Process.PeakWorkingSet64
        PeakWorkingSetMiB = ConvertTo-Mebibytes $Process.PeakWorkingSet64
        ThreadCount = $Process.Threads.Count
        HandleCount = $Process.HandleCount
    }
}

function New-StageSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Stage,

        [Parameter(Mandatory = $true)]
        [object[]]$StableRows,

        [object]$PreviousSummary,

        [object]$InitialSummary
    )

    $expectedSamples = [int]$Stage.ExpectedStableSamples
    $sampleCountPassed = $StableRows.Count -eq $expectedSamples
    $windowFailures = @($StableRows | Where-Object { -not $_.WindowCheckPassed }).Count
    $validationPassed = $sampleCountPassed -and $windowFailures -eq 0

    if ($StableRows.Count -eq 0) {
        return [pscustomobject][ordered]@{
            StageIndex = $Stage.StageIndex
            Stage = $Stage.Stage
            Action = $Stage.Action
            ExpectedWindowCount = $Stage.ExpectedWindowCount
            ScheduledStartMilliseconds = $Stage.StartMilliseconds
            WarmupMilliseconds = $Stage.WarmupMilliseconds
            StableStartMilliseconds = $Stage.StableStartMilliseconds
            ScheduledEndMilliseconds = $Stage.EndMilliseconds
            ExpectedStableSamples = $expectedSamples
            StableSamples = 0
            ExactStableSampleCountPassed = $sampleCountPassed
            StableWindowValidationFailures = $windowFailures
            StableValidationPassed = $validationPassed
        }
    }

    $orderedRows = @($StableRows | Sort-Object ScheduledElapsedMilliseconds)
    $first = $orderedRows[0]
    $last = $orderedRows[$orderedRows.Count - 1]
    $workingSet = $orderedRows | Measure-Object -Property WorkingSetBytes -Average -Minimum -Maximum
    $privateMemory = $orderedRows | Measure-Object -Property PrivateMemoryBytes -Average -Minimum -Maximum
    $peakWorkingSet = $orderedRows | Measure-Object -Property PeakWorkingSetBytes -Maximum
    $threads = $orderedRows | Measure-Object -Property ThreadCount -Average -Minimum -Maximum
    $handles = $orderedRows | Measure-Object -Property HandleCount -Average -Minimum -Maximum
    $visible = $orderedRows | Measure-Object -Property VisibleWindowCount -Minimum -Maximum
    $responsive = $orderedRows | Measure-Object -Property ResponsiveWindowCount -Minimum -Maximum
    $scheduleLag = $orderedRows | Measure-Object -Property ScheduleLagMilliseconds -Average -Maximum
    $inspectionDuration = $orderedRows | Measure-Object -Property InspectionDurationMilliseconds -Average -Maximum

    $workingSetMeanBytes = [long][Math]::Round([double]$workingSet.Average)
    $privateMemoryMeanBytes = [long][Math]::Round([double]$privateMemory.Average)
    $peakWorkingSetMaximumBytes = [long]$peakWorkingSet.Maximum
    $threadCountMean = [Math]::Round([double]$threads.Average, 3)
    $handleCountMean = [Math]::Round([double]$handles.Average, 3)

    $workingSetMeanPreviousDelta = $null
    $privateMemoryMeanPreviousDelta = $null
    $peakWorkingSetPreviousDelta = $null
    $threadCountMeanPreviousDelta = $null
    $handleCountMeanPreviousDelta = $null
    if ($null -ne $PreviousSummary) {
        $workingSetMeanPreviousDelta = $workingSetMeanBytes - [long]$PreviousSummary.WorkingSetMeanBytes
        $privateMemoryMeanPreviousDelta = $privateMemoryMeanBytes - [long]$PreviousSummary.PrivateMemoryMeanBytes
        $peakWorkingSetPreviousDelta = $peakWorkingSetMaximumBytes - [long]$PreviousSummary.PeakWorkingSetMaximumBytes
        $threadCountMeanPreviousDelta = [Math]::Round($threadCountMean - [double]$PreviousSummary.ThreadCountMean, 3)
        $handleCountMeanPreviousDelta = [Math]::Round($handleCountMean - [double]$PreviousSummary.HandleCountMean, 3)
    }

    $workingSetMeanInitialDelta = 0L
    $privateMemoryMeanInitialDelta = 0L
    $peakWorkingSetInitialDelta = 0L
    $threadCountMeanInitialDelta = 0.0
    $handleCountMeanInitialDelta = 0.0
    if ($null -ne $InitialSummary) {
        $workingSetMeanInitialDelta = $workingSetMeanBytes - [long]$InitialSummary.WorkingSetMeanBytes
        $privateMemoryMeanInitialDelta = $privateMemoryMeanBytes - [long]$InitialSummary.PrivateMemoryMeanBytes
        $peakWorkingSetInitialDelta = $peakWorkingSetMaximumBytes - [long]$InitialSummary.PeakWorkingSetMaximumBytes
        $threadCountMeanInitialDelta = [Math]::Round($threadCountMean - [double]$InitialSummary.ThreadCountMean, 3)
        $handleCountMeanInitialDelta = [Math]::Round($handleCountMean - [double]$InitialSummary.HandleCountMean, 3)
    }

    return [pscustomobject][ordered]@{
        StageIndex = $Stage.StageIndex
        Stage = $Stage.Stage
        Action = $Stage.Action
        ExpectedWindowCount = $Stage.ExpectedWindowCount
        ScheduledStartMilliseconds = $Stage.StartMilliseconds
        WarmupMilliseconds = $Stage.WarmupMilliseconds
        StableStartMilliseconds = $Stage.StableStartMilliseconds
        ScheduledEndMilliseconds = $Stage.EndMilliseconds
        ExpectedStableSamples = $expectedSamples
        StableSamples = $StableRows.Count
        ExactStableSampleCountPassed = $sampleCountPassed
        StableWindowValidationFailures = $windowFailures
        StableValidationPassed = $validationPassed
        VisibleWindowMinimum = [int]$visible.Minimum
        VisibleWindowMaximum = [int]$visible.Maximum
        ResponsiveWindowMinimum = [int]$responsive.Minimum
        ResponsiveWindowMaximum = [int]$responsive.Maximum
        WorkingSetMeanBytes = $workingSetMeanBytes
        WorkingSetMeanMiB = ConvertTo-Mebibytes $workingSetMeanBytes
        WorkingSetMinimumBytes = [long]$workingSet.Minimum
        WorkingSetMinimumMiB = ConvertTo-Mebibytes $workingSet.Minimum
        WorkingSetMaximumBytes = [long]$workingSet.Maximum
        WorkingSetMaximumMiB = ConvertTo-Mebibytes $workingSet.Maximum
        PrivateMemoryMeanBytes = $privateMemoryMeanBytes
        PrivateMemoryMeanMiB = ConvertTo-Mebibytes $privateMemoryMeanBytes
        PrivateMemoryMinimumBytes = [long]$privateMemory.Minimum
        PrivateMemoryMinimumMiB = ConvertTo-Mebibytes $privateMemory.Minimum
        PrivateMemoryMaximumBytes = [long]$privateMemory.Maximum
        PrivateMemoryMaximumMiB = ConvertTo-Mebibytes $privateMemory.Maximum
        PeakWorkingSetMaximumBytes = $peakWorkingSetMaximumBytes
        PeakWorkingSetMaximumMiB = ConvertTo-Mebibytes $peakWorkingSetMaximumBytes
        ThreadCountMean = $threadCountMean
        ThreadCountMinimum = [int]$threads.Minimum
        ThreadCountMaximum = [int]$threads.Maximum
        HandleCountMean = $handleCountMean
        HandleCountMinimum = [int]$handles.Minimum
        HandleCountMaximum = [int]$handles.Maximum
        WorkingSetStableDriftBytes = [long]$last.WorkingSetBytes - [long]$first.WorkingSetBytes
        PrivateMemoryStableDriftBytes = [long]$last.PrivateMemoryBytes - [long]$first.PrivateMemoryBytes
        PeakWorkingSetStableDriftBytes = [long]$last.PeakWorkingSetBytes - [long]$first.PeakWorkingSetBytes
        ThreadCountStableDrift = [int]$last.ThreadCount - [int]$first.ThreadCount
        HandleCountStableDrift = [int]$last.HandleCount - [int]$first.HandleCount
        WorkingSetMeanDeltaFromPreviousStageBytes = $workingSetMeanPreviousDelta
        PrivateMemoryMeanDeltaFromPreviousStageBytes = $privateMemoryMeanPreviousDelta
        PeakWorkingSetMaximumDeltaFromPreviousStageBytes = $peakWorkingSetPreviousDelta
        ThreadCountMeanDeltaFromPreviousStage = $threadCountMeanPreviousDelta
        HandleCountMeanDeltaFromPreviousStage = $handleCountMeanPreviousDelta
        WorkingSetMeanDeltaFromInitialStageBytes = $workingSetMeanInitialDelta
        PrivateMemoryMeanDeltaFromInitialStageBytes = $privateMemoryMeanInitialDelta
        PeakWorkingSetMaximumDeltaFromInitialStageBytes = $peakWorkingSetInitialDelta
        ThreadCountMeanDeltaFromInitialStage = $threadCountMeanInitialDelta
        HandleCountMeanDeltaFromInitialStage = $handleCountMeanInitialDelta
        ScheduleLagMeanMilliseconds = [Math]::Round([double]$scheduleLag.Average, 3)
        ScheduleLagMaximumMilliseconds = [long]$scheduleLag.Maximum
        InspectionDurationMeanMilliseconds = [Math]::Round([double]$inspectionDuration.Average, 3)
        InspectionDurationMaximumMilliseconds = [long]$inspectionDuration.Maximum
    }
}

function Write-StageReport {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.ICollection]$Summaries,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [bool]$UseNativeTitleBar,

        [Parameter(Mandatory = $true)]
        [int]$RebuildCycles
    )

    $lines = New-Object 'System.Collections.Generic.List[string]'
    [void]$lines.Add('# Window lifecycle measurement')
    [void]$lines.Add('')
    [void]$lines.Add(('Title bar: {0}; rebuild cycles: {1}.' -f $(if ($UseNativeTitleBar) { 'native' } else { 'default custom' }), $RebuildCycles))
    [void]$lines.Add('')
    [void]$lines.Add('| Stage | Windows | Stable samples | WS mean MiB | Private mean MiB | Peak WS MiB | Threads mean | Handles mean | WS delta vs previous MiB | Validation |')
    [void]$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|---|')
    foreach ($summary in $Summaries) {
        $deltaMiB = if ($null -eq $summary.WorkingSetMeanDeltaFromPreviousStageBytes) {
            ''
        }
        else {
            ConvertTo-Mebibytes ([double]$summary.WorkingSetMeanDeltaFromPreviousStageBytes)
        }
        [void]$lines.Add((
            '| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |' -f
            $summary.Stage,
            $summary.ExpectedWindowCount,
            $summary.StableSamples,
            $summary.WorkingSetMeanMiB,
            $summary.PrivateMemoryMeanMiB,
            $summary.PeakWorkingSetMaximumMiB,
            $summary.ThreadCountMean,
            $summary.HandleCountMean,
            $deltaMiB,
            $(if ($summary.StableValidationPassed) { 'passed' } else { 'FAILED' })))
    }
    [void]$lines.Add('')
    [void]$lines.Add('Warmup intervals are wait-only. Every row in `raw-samples.csv` is a 500 ms stable-window sample and requires the exact expected visible and responsive window count.')
    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

if (-not (Test-Path -LiteralPath $ProbePath -PathType Leaf)) {
    throw "Probe executable was not found: '$ProbePath'."
}

$ProbePath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ProbePath).Path)
$scriptPath = [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff', [Globalization.CultureInfo]::InvariantCulture)
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\empty-window-memory\lifecycle-$timestamp"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output directory already exists; lifecycle evidence requires a fresh directory: '$OutputDirectory'."
}

$outputParent = Split-Path -Parent $OutputDirectory
if (-not (Test-Path -LiteralPath $outputParent -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $outputParent -Force)
}
[void](New-Item -ItemType Directory -Path $OutputDirectory)

$initialWarmupMilliseconds = [long]$InitialWarmupSeconds * 1000L
$initialStableMilliseconds = [long]$InitialStableSeconds * 1000L
$phaseWarmupMilliseconds = [long]$PhaseWarmupSeconds * 1000L
$phaseStableMilliseconds = [long]$PhaseStableSeconds * 1000L
$initialDurationMilliseconds = $initialWarmupMilliseconds + $initialStableMilliseconds
$phaseDurationMilliseconds = $phaseWarmupMilliseconds + $phaseStableMilliseconds

foreach ($duration in @(
    $initialWarmupMilliseconds,
    $initialStableMilliseconds,
    $phaseWarmupMilliseconds,
    $phaseStableMilliseconds)) {
    if (($duration % $IntervalMilliseconds) -ne 0) {
        throw "Every warmup and stable duration must be exactly divisible by IntervalMilliseconds=$IntervalMilliseconds. Invalid duration=${duration}ms."
    }
}

$phases = New-Object 'System.Collections.Generic.List[object]'
$probeArguments = New-Object 'System.Collections.Generic.List[string]'
[void]$probeArguments.Add('--windows')
[void]$probeArguments.Add('1')
if ($NativeTitleBar) {
    [void]$probeArguments.Add('--native-titlebar')
}

$stageIndex = 0
$cursorMilliseconds = 0L
[void]$phases.Add([pscustomobject][ordered]@{
    StageIndex = $stageIndex
    Stage = 'initial-one'
    Action = 'startup:1'
    ExpectedWindowCount = 1
    StartMilliseconds = $cursorMilliseconds
    WarmupMilliseconds = $initialWarmupMilliseconds
    StableStartMilliseconds = $cursorMilliseconds + $initialWarmupMilliseconds
    StableDurationMilliseconds = $initialStableMilliseconds
    EndMilliseconds = $cursorMilliseconds + $initialDurationMilliseconds
    ExpectedStableSamples = [int]($initialStableMilliseconds / $IntervalMilliseconds)
})
$cursorMilliseconds += $initialDurationMilliseconds

$stageIndex++
[void]$probeArguments.Add('--action')
[void]$probeArguments.Add(('{0}:create:1' -f $cursorMilliseconds))
[void]$phases.Add([pscustomobject][ordered]@{
    StageIndex = $stageIndex
    Stage = 'two'
    Action = 'create:1'
    ExpectedWindowCount = 2
    StartMilliseconds = $cursorMilliseconds
    WarmupMilliseconds = $phaseWarmupMilliseconds
    StableStartMilliseconds = $cursorMilliseconds + $phaseWarmupMilliseconds
    StableDurationMilliseconds = $phaseStableMilliseconds
    EndMilliseconds = $cursorMilliseconds + $phaseDurationMilliseconds
    ExpectedStableSamples = [int]($phaseStableMilliseconds / $IntervalMilliseconds)
})
$cursorMilliseconds += $phaseDurationMilliseconds

$stageIndex++
[void]$probeArguments.Add('--action')
[void]$probeArguments.Add(('{0}:create:2' -f $cursorMilliseconds))
[void]$phases.Add([pscustomobject][ordered]@{
    StageIndex = $stageIndex
    Stage = 'four'
    Action = 'create:2'
    ExpectedWindowCount = 4
    StartMilliseconds = $cursorMilliseconds
    WarmupMilliseconds = $phaseWarmupMilliseconds
    StableStartMilliseconds = $cursorMilliseconds + $phaseWarmupMilliseconds
    StableDurationMilliseconds = $phaseStableMilliseconds
    EndMilliseconds = $cursorMilliseconds + $phaseDurationMilliseconds
    ExpectedStableSamples = [int]($phaseStableMilliseconds / $IntervalMilliseconds)
})
$cursorMilliseconds += $phaseDurationMilliseconds

$stageIndex++
[void]$probeArguments.Add('--action')
[void]$probeArguments.Add(('{0}:close:2' -f $cursorMilliseconds))
[void]$phases.Add([pscustomobject][ordered]@{
    StageIndex = $stageIndex
    Stage = 'partial-close-two'
    Action = 'close:2'
    ExpectedWindowCount = 2
    StartMilliseconds = $cursorMilliseconds
    WarmupMilliseconds = $phaseWarmupMilliseconds
    StableStartMilliseconds = $cursorMilliseconds + $phaseWarmupMilliseconds
    StableDurationMilliseconds = $phaseStableMilliseconds
    EndMilliseconds = $cursorMilliseconds + $phaseDurationMilliseconds
    ExpectedStableSamples = [int]($phaseStableMilliseconds / $IntervalMilliseconds)
})
$cursorMilliseconds += $phaseDurationMilliseconds

for ($cycle = 1; $cycle -le $RebuildCycles; $cycle++) {
    $stageIndex++
    [void]$probeArguments.Add('--action')
    [void]$probeArguments.Add(('{0}:rebuild:2' -f $cursorMilliseconds))
    [void]$phases.Add([pscustomobject][ordered]@{
        StageIndex = $stageIndex
        Stage = ('rebuild-two-{0:d2}' -f $cycle)
        Action = 'rebuild:2'
        ExpectedWindowCount = 2
        StartMilliseconds = $cursorMilliseconds
        WarmupMilliseconds = $phaseWarmupMilliseconds
        StableStartMilliseconds = $cursorMilliseconds + $phaseWarmupMilliseconds
        StableDurationMilliseconds = $phaseStableMilliseconds
        EndMilliseconds = $cursorMilliseconds + $phaseDurationMilliseconds
        ExpectedStableSamples = [int]($phaseStableMilliseconds / $IntervalMilliseconds)
    })
    $cursorMilliseconds += $phaseDurationMilliseconds
}

$lifecycleDurationMilliseconds = $cursorMilliseconds
if ($lifecycleDurationMilliseconds -gt [int]::MaxValue) {
    throw "Lifecycle duration exceeds the MemoryProbe action scheduler range: ${lifecycleDurationMilliseconds}ms."
}
[void]$probeArguments.Add('--exit-after-ms')
[void]$probeArguments.Add(([string]$lifecycleDurationMilliseconds))

$minimumOverallTimeoutSeconds =
    $StartupTimeoutSeconds +
    [int][Math]::Ceiling($lifecycleDurationMilliseconds / 1000.0) +
    $ScheduledExitGraceSeconds +
    5
if ($OverallTimeoutSeconds -eq 0) {
    $OverallTimeoutSeconds = $minimumOverallTimeoutSeconds
}
elseif ($OverallTimeoutSeconds -lt $minimumOverallTimeoutSeconds) {
    throw "OverallTimeoutSeconds=$OverallTimeoutSeconds is too short for the configured startup, lifecycle, and scheduled-exit grace. Minimum=$minimumOverallTimeoutSeconds."
}
$deadlineMilliseconds = [long]$OverallTimeoutSeconds * 1000L

if (-not ([System.Management.Automation.PSTypeName]'JaliumWindowLifecycle.NativeWindowInspector').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace JaliumWindowLifecycle
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

[string[]]$probeArgumentArray = $probeArguments.ToArray()
$launch = New-ProbeLaunch -ExecutablePath $ProbePath -ProbeArguments $probeArgumentArray

$powershellCommand = Get-Command powershell.exe -ErrorAction Stop | Select-Object -First 1
$measurementArguments = New-Object 'System.Collections.Generic.List[string]'
[void]$measurementArguments.Add('-NoProfile')
[void]$measurementArguments.Add('-ExecutionPolicy')
[void]$measurementArguments.Add('Bypass')
[void]$measurementArguments.Add('-File')
[void]$measurementArguments.Add($scriptPath)
[void]$measurementArguments.Add('-ProbePath')
[void]$measurementArguments.Add($ProbePath)
[void]$measurementArguments.Add('-OutputDirectory')
[void]$measurementArguments.Add($OutputDirectory)
if ($NativeTitleBar) {
    [void]$measurementArguments.Add('-NativeTitleBar')
}
foreach ($pair in @(
    @('-RebuildCycles', [string]$RebuildCycles),
    @('-InitialWarmupSeconds', [string]$InitialWarmupSeconds),
    @('-InitialStableSeconds', [string]$InitialStableSeconds),
    @('-PhaseWarmupSeconds', [string]$PhaseWarmupSeconds),
    @('-PhaseStableSeconds', [string]$PhaseStableSeconds),
    @('-IntervalMilliseconds', [string]$IntervalMilliseconds),
    @('-WindowResponseTimeoutMilliseconds', [string]$WindowResponseTimeoutMilliseconds),
    @('-StartupTimeoutSeconds', [string]$StartupTimeoutSeconds),
    @('-ScheduledExitGraceSeconds', [string]$ScheduledExitGraceSeconds),
    @('-OverallTimeoutSeconds', [string]$OverallTimeoutSeconds))) {
    [void]$measurementArguments.Add($pair[0])
    [void]$measurementArguments.Add($pair[1])
}
[string[]]$measurementArgumentArray = $measurementArguments.ToArray()
$measurementCommandLine = ConvertTo-CommandLine -Executable $powershellCommand.Source -Arguments $measurementArgumentArray

$commands = [ordered]@{
    MeasurementExecutable = $powershellCommand.Source
    MeasurementArguments = $measurementArgumentArray
    MeasurementCommandLine = $measurementCommandLine
    ProbeExecutable = $launch.FileName
    ProbeArguments = $launch.Arguments
    ProbeCommandLine = $launch.CommandLine
}
$commands | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'commands.json') -Encoding UTF8
@(
    '# Exact measurement invocation'
    $measurementCommandLine
    ''
    '# Exact child probe invocation'
    $launch.CommandLine
) | Set-Content -LiteralPath (Join-Path $OutputDirectory 'commands.txt') -Encoding UTF8

$inputHashes = Write-InputHashManifest `
    -ScriptPath $scriptPath `
    -ProbePath $ProbePath `
    -OutputDirectory $OutputDirectory `
    -Destination (Join-Path $OutputDirectory 'input-hashes.csv')

$dotnetInfo = try {
    (& dotnet --info | Out-String).TrimEnd()
}
catch {
    '<dotnet --info unavailable: {0}>' -f $_.Exception.Message
}
$dotnetInfo | Set-Content -LiteralPath (Join-Path $OutputDirectory 'dotnet-info.txt') -Encoding UTF8

$environment = [ordered]@{
    SchemaVersion = 1
    MeasurementKind = 'visible window lifecycle: 1 -> 2 -> 4 -> close to 2 -> repeated rebuild of 2'
    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    ScriptPath = $scriptPath
    ScriptSha256 = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
    ProbePath = $ProbePath
    ProbeSha256 = (Get-FileHash -LiteralPath $ProbePath -Algorithm SHA256).Hash
    ProbeLengthBytes = (Get-Item -LiteralPath $ProbePath).Length
    ProbePayloadRoot = Split-Path -Parent $ProbePath
    OutputDirectory = $OutputDirectory
    TitleBar = $(if ($NativeTitleBar) { 'native' } else { 'default-custom' })
    NativeTitleBar = [bool]$NativeTitleBar
    RebuildCycles = $RebuildCycles
    IntervalMilliseconds = $IntervalMilliseconds
    InitialWarmupSeconds = $InitialWarmupSeconds
    InitialStableSeconds = $InitialStableSeconds
    PhaseWarmupSeconds = $PhaseWarmupSeconds
    PhaseStableSeconds = $PhaseStableSeconds
    StartupTimeoutSeconds = $StartupTimeoutSeconds
    ScheduledExitGraceSeconds = $ScheduledExitGraceSeconds
    OverallTimeoutSeconds = $OverallTimeoutSeconds
    LifecycleDurationMilliseconds = $lifecycleDurationMilliseconds
    ExpectedRawSamples = [int](($initialStableMilliseconds + (($phases.Count - 1) * $phaseStableMilliseconds)) / $IntervalMilliseconds)
    ExpectedStableSamples = [int](($initialStableMilliseconds + (($phases.Count - 1) * $phaseStableMilliseconds)) / $IntervalMilliseconds)
    WindowResponseTimeoutMilliseconds = $WindowResponseTimeoutMilliseconds
    PhasePlan = $phases.ToArray()
    WorkingSetTrimming = $false
    ForcedGarbageCollection = $false
    WarmupSamplesRecorded = $false
    ChildProcessWindowsRemainVisible = $true
    ChildProcessCreateNoWindow = $false
    ChildEnvironmentOverrides = [ordered]@{
        JALIUM_WORKING_SET_TRIM = 'off'
        JALIUM_RENDER_BACKEND = '<removed: framework default>'
    }
    InheritedRelevantEnvironment = [ordered]@{
        JALIUM_WORKING_SET_TRIM = $env:JALIUM_WORKING_SET_TRIM
        JALIUM_RENDER_BACKEND = $env:JALIUM_RENDER_BACKEND
        DOTNET_ROOT = $env:DOTNET_ROOT
        DOTNET_ROOT_X64 = $env:DOTNET_ROOT_X64
        PROCESSOR_IDENTIFIER = $env:PROCESSOR_IDENTIFIER
        NUMBER_OF_PROCESSORS = $env:NUMBER_OF_PROCESSORS
    }
    MachineName = [Environment]::MachineName
    UserName = [Environment]::UserName
    OperatingSystem = [Environment]::OSVersion.VersionString
    Is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
    Is64BitMeasurementHost = [Environment]::Is64BitProcess
    ProcessorCount = [Environment]::ProcessorCount
    CurrentCulture = [Globalization.CultureInfo]::CurrentCulture.Name
    CurrentUICulture = [Globalization.CultureInfo]::CurrentUICulture.Name
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    PowerShellEdition = $(if ($PSVersionTable.ContainsKey('PSEdition')) { $PSVersionTable.PSEdition } else { 'Desktop' })
    ClrVersion = [Environment]::Version.ToString()
    MeasurementHostProcessId = $PID
}
$environment | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.json') -Encoding UTF8
@(
    'CreatedUtc={0}' -f $environment.CreatedUtc
    'ScriptPath={0}' -f $environment.ScriptPath
    'ScriptSha256={0}' -f $environment.ScriptSha256
    'ProbePath={0}' -f $environment.ProbePath
    'ProbeSha256={0}' -f $environment.ProbeSha256
    'TitleBar={0}' -f $environment.TitleBar
    'RebuildCycles={0}' -f $RebuildCycles
    'IntervalMilliseconds={0}' -f $IntervalMilliseconds
    'LifecycleDurationMilliseconds={0}' -f $lifecycleDurationMilliseconds
    'OverallTimeoutSeconds={0}' -f $OverallTimeoutSeconds
    'JALIUM_WORKING_SET_TRIM=off'
    'JALIUM_RENDER_BACKEND=<removed: framework default>'
    'ForcedGarbageCollection=False'
    'WorkingSetTrimming=False'
    'ChildProcessWindowsRemainVisible=True'
    'OperatingSystem={0}' -f $environment.OperatingSystem
    'PowerShellVersion={0}' -f $environment.PowerShellVersion
) | Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.txt') -Encoding UTF8

$rows = New-Object 'System.Collections.Generic.List[object]'
$summaries = New-Object 'System.Collections.Generic.List[object]'
$markerValidation = New-Object 'System.Collections.Generic.List[object]'
$process = $null
$processStarted = $false
$stdoutTask = $null
$stderrTask = $null
$stdout = ''
$stderr = ''
$streamsCaptured = $false
$status = 'Failed'
$failure = ''
$exitCode = $null
$cleanupAction = 'none'
$startedUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
$completedUtc = $null
$overallClock = [System.Diagnostics.Stopwatch]::StartNew()
$lifecycleClock = $null
$previousSummary = $null
$initialSummary = $null
$sampleIndex = 0

try {
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $launch.StartInfo
    if (-not $process.Start()) {
        throw "Process.Start returned false for '$ProbePath'."
    }
    $processStarted = $true
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $startupClock = [System.Diagnostics.Stopwatch]::StartNew()
    $ready = $false
    while ($startupClock.Elapsed.TotalSeconds -lt $StartupTimeoutSeconds) {
        Assert-WithinDeadline -Clock $overallClock -DeadlineMilliseconds $deadlineMilliseconds -Context 'waiting for the initial responsive window'
        if ($process.HasExited) {
            throw "Probe exited before the initial window became responsive. ExitCode=$($process.ExitCode)."
        }

        $inspection = [JaliumWindowLifecycle.NativeWindowInspector]::Inspect(
            $process.Id,
            [uint32]$WindowResponseTimeoutMilliseconds)
        if ($inspection.VisibleCount -eq 1 -and $inspection.ResponsiveCount -eq 1) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 50
    }
    if (-not $ready) {
        throw "Probe did not expose exactly one responsive visible window within $StartupTimeoutSeconds second(s)."
    }

    $lifecycleClock = [System.Diagnostics.Stopwatch]::StartNew()
    foreach ($stage in $phases) {
        for (
            $targetMilliseconds = [long]$stage.StableStartMilliseconds;
            $targetMilliseconds -lt [long]$stage.EndMilliseconds;
            $targetMilliseconds += $IntervalMilliseconds) {

            Wait-UntilLifecycleTarget `
                -Process $process `
                -LifecycleClock $lifecycleClock `
                -TargetMilliseconds $targetMilliseconds `
                -OverallClock $overallClock `
                -DeadlineMilliseconds $deadlineMilliseconds

            $sample = Get-LifecycleSample `
                -Process $process `
                -LifecycleClock $lifecycleClock `
                -Stage $stage `
                -SampleIndex $sampleIndex `
                -ScheduledElapsedMilliseconds $targetMilliseconds `
                -ResponseTimeoutMilliseconds $WindowResponseTimeoutMilliseconds
            [void]$rows.Add($sample)
            $sampleIndex++
        }

        Write-CsvOrEmpty -Rows $rows -Path (Join-Path $OutputDirectory 'raw-samples.csv')
        $stableRows = @($rows | Where-Object {
            $_.StageIndex -eq $stage.StageIndex -and $_.StablePhaseSample
        })
        $summary = New-StageSummary `
            -Stage $stage `
            -StableRows $stableRows `
            -PreviousSummary $previousSummary `
            -InitialSummary $initialSummary
        [void]$summaries.Add($summary)
        Write-CsvOrEmpty -Rows $summaries -Path (Join-Path $OutputDirectory 'stage-summary.csv')
        $summaries | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'stage-summary.json') -Encoding UTF8
        Write-StageReport `
            -Summaries $summaries `
            -Path (Join-Path $OutputDirectory 'stage-summary.md') `
            -UseNativeTitleBar ([bool]$NativeTitleBar) `
            -RebuildCycles $RebuildCycles

        if ($null -eq $initialSummary) {
            $initialSummary = $summary
        }
        $previousSummary = $summary

        if (-not $summary.ExactStableSampleCountPassed) {
            throw "Stage '$($stage.Stage)' recorded $($summary.StableSamples) stable sample(s); expected exactly $($summary.ExpectedStableSamples)."
        }
        if ($summary.StableWindowValidationFailures -ne 0) {
            throw "Stage '$($stage.Stage)' had $($summary.StableWindowValidationFailures) stable sample(s) with an unexpected visible or responsive window count."
        }
    }

    $expectedRawSamples = [int](($initialStableMilliseconds + (($phases.Count - 1) * $phaseStableMilliseconds)) / $IntervalMilliseconds)
    if ($rows.Count -ne $expectedRawSamples) {
        throw "Raw sample count mismatch: recorded $($rows.Count), expected exactly $expectedRawSamples."
    }

    $scheduledExitClock = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not $process.HasExited) {
        Assert-WithinDeadline -Clock $overallClock -DeadlineMilliseconds $deadlineMilliseconds -Context 'waiting for the scheduled probe exit'
        if ($scheduledExitClock.Elapsed.TotalSeconds -ge $ScheduledExitGraceSeconds) {
            throw "Probe did not exit within $ScheduledExitGraceSeconds second(s) of its scheduled lifecycle exit."
        }
        [void]$process.WaitForExit(100)
    }

    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) {
        throw "Probe exited with code $exitCode."
    }

    $stdout = Get-TaskText -Task $stdoutTask -StreamName 'stdout'
    $stderr = Get-TaskText -Task $stderrTask -StreamName 'stderr'
    $streamsCaptured = $true

    $expectedTitleBarMarker = if ($NativeTitleBar) { 'titlebar=native' } else { 'titlebar=default-custom' }
    $expectedWindowLifecycleMarkers = @(
        [pscustomobject]@{ Marker = '## PROBE START '; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = $expectedTitleBarMarker; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE THEME loaded=true'; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE NATIVE_GUARD_OK'; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE READY '; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE ACTION create requested=1 windows=2'; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE ACTION create requested=2 windows=4'; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE ACTION close requested=2 closed=2 windows=2'; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE ACTION rebuild old=2 replacement=2 windows=2'; ExpectedCount = $RebuildCycles },
        [pscustomobject]@{ Marker = '## PROBE WINDOW_SHOWN '; ExpectedCount = (4 + (2 * $RebuildCycles)) },
        [pscustomobject]@{ Marker = '## PROBE WINDOW_CLOSED '; ExpectedCount = (4 + (2 * $RebuildCycles)) },
        [pscustomobject]@{ Marker = '## PROBE ACTION exit '; ExpectedCount = 1 },
        [pscustomobject]@{ Marker = '## PROBE EXIT code=0'; ExpectedCount = 1 }
    )
    foreach ($requirement in $expectedWindowLifecycleMarkers) {
        $actualCount = [regex]::Matches(
            $stdout,
            [regex]::Escape([string]$requirement.Marker)).Count
        $passed = $actualCount -eq [int]$requirement.ExpectedCount
        [void]$markerValidation.Add([pscustomobject][ordered]@{
            Marker = $requirement.Marker
            ExpectedCount = $requirement.ExpectedCount
            ActualCount = $actualCount
            Passed = $passed
        })
    }
    Write-CsvOrEmpty -Rows $markerValidation -Path (Join-Path $OutputDirectory 'marker-validation.csv')
    $markerValidation | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'marker-validation.json') -Encoding UTF8

    $markerFailures = @($markerValidation | Where-Object { -not $_.Passed })
    if ($markerFailures.Count -gt 0) {
        $details = @($markerFailures | ForEach-Object {
            "'$($_.Marker)' expected=$($_.ExpectedCount) actual=$($_.ActualCount)"
        }) -join '; '
        throw "Probe marker validation failed: $details"
    }

    $status = 'Passed'
}
catch {
    $failure = $_.Exception.ToString()
}
finally {
    if ($null -ne $process -and $processStarted) {
        if (-not $process.HasExited) {
            try {
                $posted = [JaliumWindowLifecycle.NativeWindowInspector]::RequestClose($process.Id)
                $cleanupAction = "WM_CLOSE posted to $posted visible window(s) owned by process $($process.Id)"
                [void]$process.WaitForExit(5000)
            }
            catch {
                $cleanupAction = "WM_CLOSE cleanup failed for owned process $($process.Id): $($_.Exception.Message)"
            }
        }

        if (-not $process.HasExited) {
            try {
                $ownedProcessId = $process.Id
                $process.Kill()
                [void]$process.WaitForExit(5000)
                $cleanupAction = "$cleanupAction; killed owned process $ownedProcessId"
            }
            catch {
                $cleanupAction = "$cleanupAction; kill failed for owned process $($process.Id): $($_.Exception.Message)"
            }
        }

        if ($process.HasExited -and $null -eq $exitCode) {
            $exitCode = $process.ExitCode
        }
    }

    if (-not $streamsCaptured) {
        $stdout = Get-TaskText -Task $stdoutTask -StreamName 'stdout'
        $stderr = Get-TaskText -Task $stderrTask -StreamName 'stderr'
        $streamsCaptured = $true
    }

    $stdout | Set-Content -LiteralPath (Join-Path $OutputDirectory 'stdout.log') -Encoding UTF8
    $stderr | Set-Content -LiteralPath (Join-Path $OutputDirectory 'stderr.log') -Encoding UTF8
    Write-CsvOrEmpty -Rows $rows -Path (Join-Path $OutputDirectory 'raw-samples.csv')
    Write-CsvOrEmpty -Rows $summaries -Path (Join-Path $OutputDirectory 'stage-summary.csv')
    if ($summaries.Count -gt 0) {
        $summaries | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'stage-summary.json') -Encoding UTF8
        Write-StageReport `
            -Summaries $summaries `
            -Path (Join-Path $OutputDirectory 'stage-summary.md') `
            -UseNativeTitleBar ([bool]$NativeTitleBar) `
            -RebuildCycles $RebuildCycles
    }
    else {
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'stage-summary.json') -Value '[]' -Encoding UTF8
    }
    Write-CsvOrEmpty -Rows $markerValidation -Path (Join-Path $OutputDirectory 'marker-validation.csv')
    if ($markerValidation.Count -gt 0) {
        $markerValidation | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'marker-validation.json') -Encoding UTF8
    }
    else {
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'marker-validation.json') -Value '[]' -Encoding UTF8
    }

    $overallClock.Stop()
    $completedUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    $runStatus = [ordered]@{
        Status = $status
        Failure = $failure
        StartedUtc = $startedUtc
        CompletedUtc = $completedUtc
        ElapsedMilliseconds = $overallClock.ElapsedMilliseconds
        ProbeProcessId = $(if ($null -ne $process -and $processStarted) { $process.Id } else { $null })
        ProbeExitCode = $exitCode
        CleanupAction = $cleanupAction
        OutputDirectory = $OutputDirectory
        RawSamples = $rows.Count
        StageSummaries = $summaries.Count
        StableValidationFailures = @($summaries | Where-Object { -not $_.StableValidationPassed }).Count
        MarkerValidationFailures = @($markerValidation | Where-Object { -not $_.Passed }).Count
        OverallTimeoutSeconds = $OverallTimeoutSeconds
        LifecycleDurationMilliseconds = $lifecycleDurationMilliseconds
    }
    $runStatus | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'run-status.json') -Encoding UTF8
    @(
        'Status={0}' -f $status
        'Failure={0}' -f ($failure -replace '[\r\n]+', ' ')
        'StartedUtc={0}' -f $startedUtc
        'CompletedUtc={0}' -f $completedUtc
        'ElapsedMilliseconds={0}' -f $overallClock.ElapsedMilliseconds
        'ProbeProcessId={0}' -f $runStatus.ProbeProcessId
        'ProbeExitCode={0}' -f $exitCode
        'CleanupAction={0}' -f $cleanupAction
        'RawSamples={0}' -f $rows.Count
        'StageSummaries={0}' -f $summaries.Count
    ) | Set-Content -LiteralPath (Join-Path $OutputDirectory 'run-status.txt') -Encoding UTF8

    try {
        Write-ArtifactHashManifest `
            -OutputDirectory $OutputDirectory `
            -Destination (Join-Path $OutputDirectory 'artifact-hashes.csv')
    }
    catch {
        $_.Exception.ToString() | Set-Content -LiteralPath (Join-Path $OutputDirectory 'artifact-hash-error.log') -Encoding UTF8
    }

    if ($null -ne $process) {
        $process.Dispose()
    }
}

if ($status -ne 'Passed') {
    throw ("Window lifecycle measurement failed. Evidence: '$OutputDirectory'." +
        [Environment]::NewLine + $failure)
}

Write-Output "Window lifecycle measurement complete: $OutputDirectory"
$summaries | Format-Table Stage, ExpectedWindowCount, StableSamples, WorkingSetMeanMiB, PrivateMemoryMeanMiB, ThreadCountMean, HandleCountMean, StableValidationPassed -AutoSize
