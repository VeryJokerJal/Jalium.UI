[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProbePath,

    [string]$OutputDirectory,

    [ValidateRange(30, 1000)]
    [int]$Rounds = 30,

    [ValidateRange(0, 14400)]
    [int]$OverallTimeoutSeconds = 0,

    [switch]$NativeTitleBar
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

function ConvertTo-CommandLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $parts = New-Object 'System.Collections.Generic.List[string]'
    [void]$parts.Add((ConvertTo-ProcessArgument -Argument $Executable))
    foreach ($argument in $Arguments) {
        [void]$parts.Add((ConvertTo-ProcessArgument -Argument $argument))
    }
    return $parts -join ' '
}

function ConvertFrom-ProbeKeyValueMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Line,

        [Parameter(Mandatory = $true)]
        [string]$Prefix
    )

    if (-not $Line.StartsWith($Prefix, [StringComparison]::Ordinal)) {
        throw "Marker does not start with '$Prefix': $Line"
    }

    $properties = [ordered]@{}
    $payload = $Line.Substring($Prefix.Length).Trim()
    foreach ($token in $payload.Split([char[]]' ', [StringSplitOptions]::RemoveEmptyEntries)) {
        $separator = $token.IndexOf('=')
        if ($separator -le 0) {
            continue
        }

        $name = $token.Substring(0, $separator)
        $value = $token.Substring($separator + 1)
        $properties[$name] = $value
    }

    return [pscustomobject]$properties
}

function Get-AsyncText {
    param(
        [object]$Task,
        [string]$StreamName
    )

    if ($null -eq $Task) {
        return ''
    }

    try {
        if (-not $Task.Wait(5000)) {
            return "<$StreamName capture did not complete within 5000ms>"
        }
        return [string]$Task.Result
    }
    catch {
        return "<$StreamName capture failed: $($_.Exception.Message)>"
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

    $rootPrefix = $Root.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    $relativePath = if ($File.FullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $File.FullName.Substring($rootPrefix.Length)
    }
    else {
        $File.Name
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
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,

        [Parameter(Mandatory = $true)]
        [string]$ProbePath,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $rows = New-Object 'System.Collections.Generic.List[object]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    $scriptItem = Get-Item -LiteralPath $ScriptPath
    [void]$rows.Add((New-HashRow -Role 'measurement-script' -Root (Split-Path -Parent $ScriptPath) -File $scriptItem))
    [void]$seen.Add($scriptItem.FullName)

    foreach ($relativeSource in @(
        'tests\Jalium.UI.MemoryProbe\Program.cs',
        'tests\Jalium.UI.MemoryProbe\Program.InputLifecycle.cs'
    )) {
        $sourcePath = Join-Path $RepositoryRoot $relativeSource
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            $sourceItem = Get-Item -LiteralPath $sourcePath
            if ($seen.Add($sourceItem.FullName)) {
                [void]$rows.Add((New-HashRow -Role 'probe-source' -Root $RepositoryRoot -File $sourceItem))
            }
        }
    }

    $payloadRoot = Split-Path -Parent $ProbePath
    $evidenceRoot = Split-Path -Parent $Destination
    $evidencePrefix = $evidenceRoot.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force | Sort-Object FullName)) {
        if ($file.FullName.StartsWith($evidencePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($seen.Add($file.FullName)) {
            $role = if ($file.FullName -ieq $ProbePath) { 'probe-entrypoint' } else { 'probe-payload' }
            [void]$rows.Add((New-HashRow -Role $role -Root $payloadRoot -File $file))
        }
    }

    $rows | Export-Csv -LiteralPath $Destination -NoTypeInformation -Encoding UTF8
}

function Write-ArtifactHashManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EvidenceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $rows = New-Object 'System.Collections.Generic.List[object]'
    foreach ($file in @(Get-ChildItem -LiteralPath $EvidenceDirectory -File -Recurse -Force | Sort-Object FullName)) {
        if ($file.FullName -ieq $Destination) {
            continue
        }
        [void]$rows.Add((New-HashRow -Role 'measurement-evidence' -Root $EvidenceDirectory -File $file))
    }

    $rows | Export-Csv -LiteralPath $Destination -NoTypeInformation -Encoding UTF8
}

function Get-MemoryTrend {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Rows,

        [Parameter(Mandatory = $true)]
        [string]$Property
    )

    if ($Rows.Count -eq 0) {
        return $null
    }

    $ordered = @($Rows | Sort-Object { [int]$_.round })
    $values = @($ordered | ForEach-Object { [double]($_.$Property) })
    $measure = $values | Measure-Object -Minimum -Maximum -Average
    $first = [long]$values[0]
    $last = [long]$values[$values.Count - 1]

    $meanX = ($ordered | Measure-Object -Property round -Average).Average
    $meanY = ($values | Measure-Object -Average).Average
    $numerator = 0.0
    $denominator = 0.0
    for ($index = 0; $index -lt $ordered.Count; $index++) {
        $dx = [double]$ordered[$index].round - [double]$meanX
        $dy = [double]$values[$index] - [double]$meanY
        $numerator += $dx * $dy
        $denominator += $dx * $dx
    }
    $slope = if ($denominator -eq 0.0) { 0.0 } else { $numerator / $denominator }

    return [pscustomobject][ordered]@{
        Samples = $ordered.Count
        FirstBytes = $first
        LastBytes = $last
        DeltaBytes = $last - $first
        MinimumBytes = [long]$measure.Minimum
        MaximumBytes = [long]$measure.Maximum
        AverageBytes = [long][Math]::Round([double]$measure.Average)
        LeastSquaresBytesPerRound = [Math]::Round($slope, 3)
    }
}

function Add-ValidationError {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[string]]$Errors,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    [void]$Errors.Add($Message)
}

if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
    throw 'measure-input-lifecycle.ps1 requires Windows because the probe uses real HWND windows and WM_MOUSEMOVE.'
}

$scriptPath = [IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $scriptPath) '..'))

if (-not [IO.Path]::IsPathRooted($ProbePath)) {
    $ProbePath = Join-Path $repositoryRoot $ProbePath
}
$ProbePath = [IO.Path]::GetFullPath($ProbePath)
if (-not (Test-Path -LiteralPath $ProbePath -PathType Leaf)) {
    throw "Probe entrypoint does not exist: $ProbePath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $evidenceParent = Join-Path (Split-Path -Parent $repositoryRoot) 'Jalium.UI-memory-evidence'
    $OutputDirectory = Join-Path $evidenceParent 'input-lifecycle-20260917'
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Refusing to overwrite existing input lifecycle evidence directory: $OutputDirectory"
}

$outputParent = Split-Path -Parent $OutputDirectory
if (-not (Test-Path -LiteralPath $outputParent)) {
    [void](New-Item -ItemType Directory -Path $outputParent -Force)
}
[void](New-Item -ItemType Directory -Path $OutputDirectory)

$copiedScriptPath = Join-Path $OutputDirectory 'measure-input-lifecycle.ps1'
$stdoutPath = Join-Path $OutputDirectory 'probe.stdout.log'
$stderrPath = Join-Path $OutputDirectory 'probe.stderr.log'
$metricsPath = Join-Path $OutputDirectory 'input-lifecycle-metrics.csv'
$summaryJsonPath = Join-Path $OutputDirectory 'input-lifecycle-summary.json'
$summaryTextPath = Join-Path $OutputDirectory 'input-lifecycle-summary.txt'
$commandPath = Join-Path $OutputDirectory 'probe-command.txt'
$environmentPath = Join-Path $OutputDirectory 'environment.json'
$gitRevisionPath = Join-Path $OutputDirectory 'git-revision.txt'
$gitStatusPath = Join-Path $OutputDirectory 'git-status.txt'
$inputHashesPath = Join-Path $OutputDirectory 'input-hashes.csv'
$artifactHashesPath = Join-Path $OutputDirectory 'artifact-hashes.csv'
$failurePath = Join-Path $OutputDirectory 'failure.txt'

Copy-Item -LiteralPath $scriptPath -Destination $copiedScriptPath
Write-InputHashManifest `
    -RepositoryRoot $repositoryRoot `
    -ScriptPath $scriptPath `
    -ProbePath $ProbePath `
    -Destination $inputHashesPath

$gitRevision = '<unavailable>'
$gitStatus = '<unavailable>'
try {
    $gitRevisionOutput = @(& git -C $repositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -eq 0) {
        $gitRevision = ($gitRevisionOutput -join [Environment]::NewLine).Trim()
    }
    $gitStatusOutput = @(& git -C $repositoryRoot status --short)
    if ($LASTEXITCODE -eq 0) {
        $gitStatus = $gitStatusOutput -join [Environment]::NewLine
    }
}
catch {
    $gitStatus = "<git metadata failed: $($_.Exception.Message)>"
}
[IO.File]::WriteAllText($gitRevisionPath, $gitRevision + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText($gitStatusPath, $gitStatus + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))

$probeArguments = New-Object 'System.Collections.Generic.List[string]'
[void]$probeArguments.Add('--input-lifecycle-rounds')
[void]$probeArguments.Add($Rounds.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($NativeTitleBar) {
    [void]$probeArguments.Add('--native-titlebar')
}

$executable = $ProbePath
$processArguments = New-Object 'System.Collections.Generic.List[string]'
if ([IO.Path]::GetExtension($ProbePath) -ieq '.dll') {
    $dotnet = Get-Command dotnet -ErrorAction Stop | Select-Object -First 1
    $executable = $dotnet.Source
    [void]$processArguments.Add($ProbePath)
}
foreach ($argument in $probeArguments) {
    [void]$processArguments.Add($argument)
}

[string[]]$argumentArray = $processArguments.ToArray()
$commandLine = ConvertTo-CommandLine -Executable $executable -Arguments $argumentArray
if ($OverallTimeoutSeconds -eq 0) {
    $OverallTimeoutSeconds = [Math]::Max(180, ($Rounds * 12) + 120)
}

$commandText = @(
    $commandLine
    "OverallTimeoutSeconds=$OverallTimeoutSeconds"
    'JALIUM_WORKING_SET_TRIM=off'
    'WindowVisibility=normal'
    'ForcedGc=false'
    'WorkingSetTrim=false'
) -join [Environment]::NewLine
[IO.File]::WriteAllText($commandPath, $commandText + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))

$environment = [pscustomobject][ordered]@{
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    MachineName = [Environment]::MachineName
    OperatingSystem = [Environment]::OSVersion.VersionString
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    RepositoryRoot = $repositoryRoot
    ProbePath = $ProbePath
    EvidenceDirectory = $OutputDirectory
    Rounds = $Rounds
    NativeTitleBar = [bool]$NativeTitleBar
    OverallTimeoutSeconds = $OverallTimeoutSeconds
    WorkingSetTrim = 'off'
    ForcedGc = $false
    HiddenMeasurementWindow = $false
    InputKind = 'synthetic client WM_MOUSEMOVE sent only to the probe HWND'
    SeparatePhysicalInputTools = @('measure-window-input-stress.ps1', 'measure-window-drag-stress.ps1')
}
$environment | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $environmentPath -Encoding UTF8

$startInfo = New-Object Diagnostics.ProcessStartInfo
$startInfo.FileName = $executable
$startInfo.Arguments = (@($argumentArray | ForEach-Object { ConvertTo-ProcessArgument -Argument $_ })) -join ' '
$startInfo.WorkingDirectory = Split-Path -Parent $ProbePath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['JALIUM_WORKING_SET_TRIM'] = 'off'
[void]$startInfo.EnvironmentVariables.Remove('JALIUM_RENDER_BACKEND')

$probeProcess = $null
$stdoutTask = $null
$stderrTask = $null
$stdout = ''
$stderr = ''
$probeExitCode = $null
$timedOut = $false
$runFailure = $null
$runClock = [Diagnostics.Stopwatch]::StartNew()

try {
    $probeProcess = New-Object Diagnostics.Process
    $probeProcess.StartInfo = $startInfo
    if (-not $probeProcess.Start()) {
        throw 'Process.Start returned false for the input lifecycle probe.'
    }

    $stdoutTask = $probeProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $probeProcess.StandardError.ReadToEndAsync()

    while (-not $probeProcess.WaitForExit(250)) {
        if ($runClock.Elapsed.TotalSeconds -ge $OverallTimeoutSeconds) {
            $timedOut = $true
            throw "Input lifecycle probe exceeded the overall timeout of $OverallTimeoutSeconds seconds."
        }
    }

    $probeProcess.WaitForExit()
    $probeExitCode = $probeProcess.ExitCode
}
catch {
    $runFailure = $_
}
finally {
    if ($null -ne $probeProcess) {
        try {
            if (-not $probeProcess.HasExited) {
                # Kill only the exact Process instance this script started. The
                # harness never enumerates or terminates unrelated dotnet/probe jobs.
                $probeProcess.Kill()
                [void]$probeProcess.WaitForExit(5000)
            }
            if ($probeProcess.HasExited -and $null -eq $probeExitCode) {
                $probeExitCode = $probeProcess.ExitCode
            }
        }
        catch {
            if ($null -eq $runFailure) {
                $runFailure = $_
            }
        }
    }

    $stdout = Get-AsyncText -Task $stdoutTask -StreamName 'stdout'
    $stderr = Get-AsyncText -Task $stderrTask -StreamName 'stderr'
    [IO.File]::WriteAllText($stdoutPath, $stdout, (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText($stderrPath, $stderr, (New-Object Text.UTF8Encoding($false)))
}

$metricPrefix = '## PROBE INPUT_LIFECYCLE_METRIC '
$okPrefix = '## PROBE INPUT_LIFECYCLE_OK '
$startPrefix = '## PROBE INPUT_LIFECYCLE_START '
$metricRows = New-Object 'System.Collections.Generic.List[object]'
$okMarker = $null
$startMarker = $null
$sawFailureMarker = $false

foreach ($line in ($stdout -split '\r?\n')) {
    if ($line.StartsWith($metricPrefix, [StringComparison]::Ordinal)) {
        [void]$metricRows.Add((ConvertFrom-ProbeKeyValueMarker -Line $line -Prefix $metricPrefix))
    }
    elseif ($line.StartsWith($okPrefix, [StringComparison]::Ordinal)) {
        $okMarker = ConvertFrom-ProbeKeyValueMarker -Line $line -Prefix $okPrefix
    }
    elseif ($line.StartsWith($startPrefix, [StringComparison]::Ordinal)) {
        $startMarker = ConvertFrom-ProbeKeyValueMarker -Line $line -Prefix $startPrefix
    }
    elseif ($line.StartsWith('## PROBE INPUT_LIFECYCLE_FAILED ', [StringComparison]::Ordinal) -or
            $line.StartsWith('## PROBE ERROR ', [StringComparison]::Ordinal)) {
        $sawFailureMarker = $true
    }
}

if ($metricRows.Count -gt 0) {
    $metricRows | Export-Csv -LiteralPath $metricsPath -NoTypeInformation -Encoding UTF8
}
else {
    [IO.File]::WriteAllText($metricsPath, '', (New-Object Text.UTF8Encoding($false)))
}

$validationErrors = New-Object 'System.Collections.Generic.List[string]'
if ($null -ne $runFailure) {
    Add-ValidationError -Errors $validationErrors -Message $runFailure.Exception.Message
}
if ($timedOut) {
    Add-ValidationError -Errors $validationErrors -Message 'The process exceeded the bounded overall timeout.'
}
if ($probeExitCode -ne 0) {
    Add-ValidationError -Errors $validationErrors -Message "Probe exit code was $probeExitCode; expected 0."
}
if ($sawFailureMarker) {
    Add-ValidationError -Errors $validationErrors -Message 'Probe output contains an INPUT_LIFECYCLE_FAILED or PROBE ERROR marker.'
}
if ($null -eq $startMarker) {
    Add-ValidationError -Errors $validationErrors -Message 'Missing INPUT_LIFECYCLE_START marker.'
}
if ($null -eq $okMarker) {
    Add-ValidationError -Errors $validationErrors -Message 'Missing INPUT_LIFECYCLE_OK marker.'
}

$expectedMetricRows = $Rounds * 2
if ($metricRows.Count -ne $expectedMetricRows) {
    Add-ValidationError -Errors $validationErrors -Message (
        "Metric row count was $($metricRows.Count); expected exactly $expectedMetricRows (round + quiet for each round).")
}

$expectedInputPerRound = 3 * 24
$expectedShownPerRound = 1 + (3 * 4)
$initialRenderingSubscribers = if ($null -ne $startMarker) { [int]$startMarker.rendering } else { $null }
$initialCompositionSubscribers = if ($null -ne $startMarker) { [int]$startMarker.compositionSubscribers } else { $null }
$settledRenderingSubscribers = if ($null -ne $okMarker) { [int]$okMarker.rendering } else { $null }
$settledCompositionSubscribers = if ($null -ne $okMarker) { [int]$okMarker.compositionSubscribers } else { $null }
for ($round = 1; $round -le $Rounds; $round++) {
    $roundRows = @($metricRows | Where-Object { [int]$_.round -eq $round })
    $activeRows = @($roundRows | Where-Object { $_.stage -eq 'round' })
    $quietRows = @($roundRows | Where-Object { $_.stage -eq 'quiet' })
    if ($activeRows.Count -ne 1 -or $quietRows.Count -ne 1) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round has active=$($activeRows.Count), quiet=$($quietRows.Count); expected one of each.")
        continue
    }

    $active = $activeRows[0]
    $quiet = $quietRows[0]
    if ([int]$active.mainFrameStarting -ne 1 -or [int]$active.tempFrameStarting -ne 1) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round active FrameStarting counts were main=$($active.mainFrameStarting), temp=$($active.tempFrameStarting).")
    }
    if ([int]$quiet.mainFrameStarting -ne 1 -or [int]$quiet.tempFrameStarting -ne 0) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round quiet FrameStarting counts were main=$($quiet.mainFrameStarting), temp=$($quiet.tempFrameStarting).")
    }
    if ([int]$quiet.renderableWindows -ne 1) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round quiet rendering-window count was $($quiet.renderableWindows); expected 1.")
    }
    if ([int]$active.shown -ne $expectedShownPerRound -or [int]$quiet.shown -ne $expectedShownPerRound) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round Shown callbacks were active=$($active.shown), quiet=$($quiet.shown); expected $expectedShownPerRound.")
    }
    if ([int]$active.sizeChanged -ne 12 -or [int]$quiet.sizeChanged -ne 12) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round controlled SizeChanged callbacks were active=$($active.sizeChanged), quiet=$($quiet.sizeChanged); expected 12.")
    }
    if ([int]$active.closed -ne 0 -or [int]$quiet.closed -ne 1) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round Closed callbacks were active=$($active.closed), quiet=$($quiet.closed); expected 0 then 1.")
    }
    if ([int]$active.openWindows -ne 2 -or [int]$quiet.openWindows -ne 1) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round open-window counts were active=$($active.openWindows), quiet=$($quiet.openWindows); expected 2 then 1.")
    }
    if ([int]$active.shownWindows -ne 2 -or [int]$quiet.shownWindows -ne 1) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round shown-window counts were active=$($active.shownWindows), quiet=$($quiet.shownWindows); expected 2 then 1.")
    }
    $requiredQuietMilliseconds = if ($round -eq $Rounds) { 2000 } else { 600 }
    if ([long]$quiet.quietElapsedMs -lt $requiredQuietMilliseconds) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round quiet observation was $($quiet.quietElapsedMs)ms; expected at least ${requiredQuietMilliseconds}ms.")
    }

    $expectedRendering = if ($round -lt 5) { $initialRenderingSubscribers } else { $settledRenderingSubscribers }
    $expectedComposition = if ($round -lt 5) { $initialCompositionSubscribers } else { $settledCompositionSubscribers }
    if ($null -ne $expectedRendering -and [int]$quiet.rendering -ne [int]$expectedRendering) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round quiet Rendering delegate count was $($quiet.rendering); expected $expectedRendering.")
    }
    if ($null -ne $expectedComposition -and [int]$quiet.compositionSubscribers -ne [int]$expectedComposition) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Round $round quiet CompositionTarget subscriber count was $($quiet.compositionSubscribers); expected $expectedComposition.")
    }
    foreach ($property in @('previewMouseMove', 'mouseMove', 'pointerMove', 'pointerMoved', 'syntheticMessages')) {
        if ([int]$active.$property -ne $expectedInputPerRound -or [int]$quiet.$property -ne $expectedInputPerRound) {
            Add-ValidationError -Errors $validationErrors -Message (
                "Round $round $property was active=$($active.$property), quiet=$($quiet.$property); expected $expectedInputPerRound.")
        }
    }
}

$expectedFeatureCycles = [Math]::Floor($Rounds / 5)
if ($null -ne $okMarker) {
    if ([int]$okMarker.rounds -ne $Rounds) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Summary rounds=$($okMarker.rounds); expected $Rounds.")
    }
    if ([int]$okMarker.featureCycles -ne $expectedFeatureCycles -or [int]$okMarker.featureCycles -lt 6) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Summary featureCycles=$($okMarker.featureCycles); expected $expectedFeatureCycles and at least 6.")
    }
    if ([int]$okMarker.inputBatches -ne ($Rounds * 3)) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Summary inputBatches=$($okMarker.inputBatches); expected $($Rounds * 3).")
    }
    if ([int]$okMarker.syntheticMessages -ne ($Rounds * $expectedInputPerRound)) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Summary syntheticMessages=$($okMarker.syntheticMessages); expected $($Rounds * $expectedInputPerRound).")
    }
    if ([int]$okMarker.resizePresents -ne $Rounds) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Summary resizePresents=$($okMarker.resizePresents); expected $Rounds.")
    }
    if ($null -ne $startMarker -and [int]$okMarker.frameStarting -ne [int]$startMarker.frameStarting) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Summary FrameStarting count was $($okMarker.frameStarting); initial count was $($startMarker.frameStarting).")
    }
    if ([int]$okMarker.renderableWindows -ne 1) {
        Add-ValidationError -Errors $validationErrors -Message (
            "Summary renderable-window count was $($okMarker.renderableWindows); expected 1.")
    }
}

$activeMetricRows = @($metricRows | Where-Object { $_.stage -eq 'round' })
$quietMetricRows = @($metricRows | Where-Object { $_.stage -eq 'quiet' })
$memoryTrend = [pscustomobject][ordered]@{
    ActiveWorkingSet = Get-MemoryTrend -Rows $activeMetricRows -Property 'workingSetBytes'
    QuietWorkingSet = Get-MemoryTrend -Rows $quietMetricRows -Property 'workingSetBytes'
    ActivePrivateMemory = Get-MemoryTrend -Rows $activeMetricRows -Property 'privateBytes'
    QuietPrivateMemory = Get-MemoryTrend -Rows $quietMetricRows -Property 'privateBytes'
    ActiveManagedMemory = Get-MemoryTrend -Rows $activeMetricRows -Property 'managedBytes'
    QuietManagedMemory = Get-MemoryTrend -Rows $quietMetricRows -Property 'managedBytes'
    Interpretation = 'Descriptive only. Natural GC produces sawtooth trends; no monotonic-decrease or memory-threshold assertion is applied.'
}

$summary = [pscustomobject][ordered]@{
    SchemaVersion = 1
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    ValidationPassed = ($validationErrors.Count -eq 0)
    ValidationErrors = @($validationErrors)
    ProbeExitCode = $probeExitCode
    TimedOut = $timedOut
    ElapsedMilliseconds = $runClock.ElapsedMilliseconds
    RequestedRounds = $Rounds
    ExpectedFeatureCycles = $expectedFeatureCycles
    MetricRows = $metricRows.Count
    StartMarker = $startMarker
    CompletionMarker = $okMarker
    MemoryTrend = $memoryTrend
    MeasurementPolicy = [pscustomobject][ordered]@{
        ForcedGc = $false
        WorkingSetTrim = $false
        HiddenMeasurementWindow = $false
        MemoryTrendIsPassFailCriterion = $false
        ListenerAndCallbackCountsArePassFailCriteria = $true
        InputKind = 'synthetic client WM_MOUSEMOVE sent only to the probe HWND'
        PhysicalInputAndDragAreMeasuredSeparately = $true
    }
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

$summaryLines = New-Object 'System.Collections.Generic.List[string]'
[void]$summaryLines.Add("ValidationPassed=$($summary.ValidationPassed)")
[void]$summaryLines.Add("ProbeExitCode=$probeExitCode")
[void]$summaryLines.Add("Rounds=$Rounds")
[void]$summaryLines.Add("MetricRows=$($metricRows.Count)/$expectedMetricRows")
[void]$summaryLines.Add("FeatureCycles=$(if ($null -ne $okMarker) { $okMarker.featureCycles } else { '<missing>' })/$expectedFeatureCycles")
[void]$summaryLines.Add("ResizePresents=$(if ($null -ne $okMarker) { $okMarker.resizePresents } else { '<missing>' })/$Rounds")
[void]$summaryLines.Add("ElapsedMilliseconds=$($runClock.ElapsedMilliseconds)")
[void]$summaryLines.Add('MemoryPolicy=descriptive only; natural GC sawtooth is retained and no monotonic or fixed-memory threshold is asserted')
[void]$summaryLines.Add('InputKind=synthetic client WM_MOUSEMOVE sent only to the probe HWND; physical high-frequency input and drag use separate tools')
if ($validationErrors.Count -gt 0) {
    [void]$summaryLines.Add('ValidationErrors:')
    foreach ($validationError in $validationErrors) {
        [void]$summaryLines.Add("- $validationError")
    }
}
$summaryLines | Set-Content -LiteralPath $summaryTextPath -Encoding UTF8

if ($validationErrors.Count -gt 0) {
    $failureText = @(
        'Input lifecycle measurement failed validation.'
        $validationErrors
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText($failurePath, $failureText + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

Write-ArtifactHashManifest -EvidenceDirectory $OutputDirectory -Destination $artifactHashesPath

if ($validationErrors.Count -gt 0) {
    throw "Input lifecycle evidence was captured but validation failed. See '$failurePath'."
}

Write-Host "Input lifecycle evidence: $OutputDirectory"
