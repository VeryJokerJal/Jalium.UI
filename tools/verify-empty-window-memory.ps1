[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$NativeOutputRoot,

    [ValidateRange(1, 100)]
    [int]$Runs = 3,

    [ValidateRange(1, 300)]
    [int]$RebuildCycles = 300,

    [ValidateRange(1, 2147483647)]
    [long]$MemoryBudgetBytes = 20000000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($renderingOverride in @('JALIUM_RENDER_BACKEND', 'JALIUM_GPU_PREFERENCE', 'JALIUM_RENDERING_ENGINE')) {
    if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($renderingOverride, 'Process'))) {
        throw "Unset $renderingOverride before measuring the default empty-window configuration."
    }
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Choose a new evidence directory; existing results will not be overwritten: $OutputDirectory"
}
if ($NativeOutputRoot) {
    $NativeOutputRoot = (Resolve-Path -LiteralPath $NativeOutputRoot).Path
}
[void](New-Item -ItemType Directory -Path $OutputDirectory)
$scenarioResults = [Collections.Generic.List[object]]::new()
$publishResults = [Collections.Generic.List[object]]::new()

function Save-ScenarioResult {
    param([string]$Name, [bool]$Passed, [string]$Failure)
    $scenarioResults.Add([pscustomobject]@{
        Scenario = $Name
        ExecutionPassed = $Passed
        Failure = $Failure
        FinishedUtc = [DateTime]::UtcNow.ToString('o')
    })
    $scenarioResults | Export-Csv -NoTypeInformation -Encoding UTF8 `
        -LiteralPath (Join-Path $OutputDirectory 'scenario-results.csv')
}

function Invoke-MemoryScenario {
    param([string]$Name, [string]$ScriptName, [hashtable]$Parameters)
    $scenarioOutput = Join-Path $OutputDirectory $Name
    $Parameters['OutputDirectory'] = $scenarioOutput
    $Parameters | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 `
        -LiteralPath (Join-Path $OutputDirectory ($Name + '.arguments.json'))
    try {
        & (Join-Path $PSScriptRoot $ScriptName) @Parameters *> (Join-Path $OutputDirectory ($Name + '.log'))
        Save-ScenarioResult $Name $true ''
        Write-Output "$Name measured: $scenarioOutput"
    }
    catch {
        $_ | Out-String | Add-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory ($Name + '.log'))
        Save-ScenarioResult $Name $false $_.Exception.Message
        Write-Output "$Name failed; evidence retained: $scenarioOutput"
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    $environment = [ordered]@{
        StartedUtc = [DateTime]::UtcNow.ToString('o')
        Repository = $repositoryRoot
        NativeOutputRoot = $NativeOutputRoot
        BudgetBytes = $MemoryBudgetBytes
        Runs = $Runs
        RebuildDiagnosticCycles = $RebuildCycles
        LifecycleRebuildCycles = 10
        StabilizationSeconds = 10
        MeasurementSeconds = 20
        IntervalMilliseconds = 500
        GlobalizationUseNls = $env:DOTNET_SYSTEM_GLOBALIZATION_USENLS
        GlobalizationInvariant = $env:DOTNET_SYSTEM_GLOBALIZATION_INVARIANT
        RenderBackend = $env:JALIUM_RENDER_BACKEND
        GpuPreference = $env:JALIUM_GPU_PREFERENCE
        RenderingEngine = $env:JALIUM_RENDERING_ENGINE
        GcConserveMemory = $env:DOTNET_GCConserveMemory
        GcRegionSize = $env:DOTNET_GCRegionSize
        GcConcurrent = $env:DOTNET_gcConcurrent
        MeasurementKind = 'Fresh processes, default custom title bar, 800x600, complete theme, Content=null; no forced GC or working-set trimming.'
    }
    $environment | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 `
        -LiteralPath (Join-Path $OutputDirectory 'environment.json')
    & dotnet --info | Out-File -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'dotnet-info.txt')

    foreach ($variant in @('aot', 'release', 'debug')) {
        $configuration = if ($variant -eq 'debug') { 'Debug' } else { 'Release' }
        $payload = Join-Path $OutputDirectory $variant
        $buildRoot = Join-Path $OutputDirectory ($variant + '-build')
        $publishArguments = @(
            'publish', 'tests/Jalium.UI.MemoryProbe/Jalium.UI.MemoryProbe.csproj',
            '-c', $configuration, '-r', 'win-x64', "-p:JaliumBuildRoot=$buildRoot",
            '-p:UseSharedCompilation=false', '-m:1', '-nodeReuse:false', '-o', $payload
        )
        if ($NativeOutputRoot) { $publishArguments += "-p:JaliumNativeOutputRoot=$NativeOutputRoot" }
        if ($variant -eq 'aot') { $publishArguments += '-p:PublishProfile=LowMemory' }
        else { $publishArguments += @('-p:PublishAot=false', '--self-contained', 'false') }
        $publishArguments | ConvertTo-Json | Set-Content -Encoding UTF8 `
            -LiteralPath (Join-Path $OutputDirectory ("publish-$variant.arguments.json"))
        $publishStarted = [DateTime]::UtcNow
        # Windows PowerShell can turn native stderr into terminating errors even
        # when the native exit code is zero. Preserve both streams and use the
        # actual dotnet exit code for the build result.
        $savedErrorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & dotnet @publishArguments *> (Join-Path $OutputDirectory ("publish-$variant.log"))
            $publishExit = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $savedErrorPreference }
        $publishResults.Add([pscustomobject]@{
            Variant = $variant
            ExitCode = $publishExit
            StartedUtc = $publishStarted.ToString('o')
            FinishedUtc = [DateTime]::UtcNow.ToString('o')
            Payload = $payload
        })
        $publishResults | Export-Csv -NoTypeInformation -Encoding UTF8 `
            -LiteralPath (Join-Path $OutputDirectory 'publish-results.csv')
        if ($publishExit -ne 0) { throw "The $variant build failed with exit code $publishExit. See publish-$variant.log." }
        Get-ChildItem -LiteralPath $payload -File | Get-FileHash -Algorithm SHA256 |
            Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory ("$variant-hashes.csv"))
        Write-Output "$variant published: $payload"
    }

    foreach ($variant in @('aot', 'release', 'debug')) {
        Invoke-MemoryScenario "$variant-cold" 'measure-window-memory.ps1' @{
            ProbePath = Join-Path $OutputDirectory "$variant\Jalium.UI.MemoryProbe.exe"
            Runs = $Runs; StabilizationSeconds = 10; MeasurementSeconds = 20
            IntervalMilliseconds = 500; MemoryBudgetBytes = $MemoryBudgetBytes
        }
    }
    $aotProbe = Join-Path $OutputDirectory 'aot\Jalium.UI.MemoryProbe.exe'
    foreach ($windowCount in @(2, 4)) {
        Invoke-MemoryScenario "aot-$windowCount-windows" 'measure-window-memory.ps1' @{
            ProbePath = $aotProbe; Runs = 1; WindowCount = $windowCount
            StabilizationSeconds = 10; MeasurementSeconds = 20
            IntervalMilliseconds = 500; MemoryBudgetBytes = $MemoryBudgetBytes
        }
    }
    Invoke-MemoryScenario 'aot-lifecycle' 'measure-window-lifecycle.ps1' @{
        ProbePath = $aotProbe; RebuildCycles = 10
    }
    Invoke-MemoryScenario 'aot-rebuilds' 'measure-window-rebuilds.ps1' @{
        ProbePath = $aotProbe; Cycles = $RebuildCycles; CycleMilliseconds = 500
    }

    $summary = [Collections.Generic.List[object]]::new()
    foreach ($name in @('aot-cold', 'release-cold', 'debug-cold', 'aot-2-windows', 'aot-4-windows')) {
        $aggregatePath = Join-Path $OutputDirectory "$name\aggregate.csv"
        if (Test-Path -LiteralPath $aggregatePath) {
            $aggregate = Import-Csv -LiteralPath $aggregatePath
            $summary.Add([pscustomobject]@{
                Scenario = $name
                Samples = [int]$aggregate.MeasurementSamples
                WorkingSetMeanMiB = $aggregate.WorkingSetMeanMiB
                WorkingSetMaximumBytes = [long]$aggregate.WorkingSetMaximumBytes
                PrivateMemoryMaximumBytes = [long]$aggregate.PrivateMemoryMaximumBytes
                BelowWorkingSetBudget = [long]$aggregate.WorkingSetMaximumBytes -lt $MemoryBudgetBytes
                WindowValidationFailures = [int]$aggregate.WindowValidationFailures
            })
        }
    }
    $summary | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'memory-summary.csv')
    $summary | Format-Table -AutoSize
    $aotCold = @($summary | Where-Object Scenario -eq 'aot-cold')
    $executionPassed = $scenarioResults.Count -eq 7 -and @($scenarioResults | Where-Object { -not $_.ExecutionPassed }).Count -eq 0
    $budgetPassed = $aotCold.Count -eq 1 -and $aotCold[0].BelowWorkingSetBudget
    [pscustomobject]@{
        ExecutionPassed = $executionPassed
        NativeAotWorkingSetBudgetPassed = $budgetPassed
        MemoryValidationPassed = $executionPassed -and $budgetPassed
        BudgetBytes = $MemoryBudgetBytes
        Note = 'Execution success is independent of the memory budget. This runner covers memory and window lifecycle; run functional regressions and real input separately.'
    } | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'acceptance.json')
    if (-not $executionPassed) { throw "One or more measurements failed. See scenario-results.csv in $OutputDirectory" }
    if (-not $budgetPassed) { throw "The single-window working set still exceeds $MemoryBudgetBytes bytes. See memory-summary.csv in $OutputDirectory" }
}
finally { Pop-Location }
