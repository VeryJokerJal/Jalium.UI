[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MeasurementDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$MeasurementDirectory = (Resolve-Path -LiteralPath $MeasurementDirectory).Path
$samples = @(Import-Csv -LiteralPath (Join-Path $MeasurementDirectory 'process-samples.csv'))
$run = Get-Content -LiteralPath (Join-Path $MeasurementDirectory 'run-status.json') -Raw | ConvertFrom-Json
if ($samples.Count -eq 0) { throw 'The measurement contains no process samples.' }

$gcRows = [Collections.Generic.List[object]]::new()
foreach ($line in [IO.File]::ReadLines((Join-Path $MeasurementDirectory 'stdout.log'))) {
    if ($line -notmatch 'LIFETIME reason=') { continue }
    $row = [ordered]@{}
    foreach ($match in [regex]::Matches($line, '(?<name>\w+)=(?<value>\d+)')) {
        $row[$match.Groups['name'].Value] = [long]$match.Groups['value'].Value
    }
    $gcRows.Add([pscustomobject]$row)
}

function Get-SeriesStatistics {
    param([object[]]$Rows, [string]$Property)
    $numbers = @($Rows | ForEach-Object { [double]$_.$Property } | Sort-Object)
    $middle = [int][math]::Floor($numbers.Count / 2)
    $median = if (($numbers.Count % 2) -eq 0) {
        ($numbers[$middle - 1] + $numbers[$middle]) / 2
    }
    else { $numbers[$middle] }
    return [pscustomobject]@{
        Minimum = $numbers[0]
        Mean = ($numbers | Measure-Object -Average).Average
        Median = $median
        Maximum = $numbers[-1]
    }
}

# Report every post-startup sample in four chronological bins. Fixed bins avoid
# selecting only the low points of the GC cycle when checking for retained growth.
$active = @($samples | Where-Object { [long]$_.ElapsedMs -ge 10000 })
$blocks = [Collections.Generic.List[object]]::new()
if ($active.Count -gt 0) {
    for ($block = 0; $block -lt 4; $block++) {
        $first = [int][math]::Floor($active.Count * $block / 4)
        $end = [int][math]::Floor($active.Count * ($block + 1) / 4)
        if ($end -le $first) { continue }
        $rows = @($active[$first..($end - 1)])
        $blocks.Add([pscustomobject]@{
            Block = $block + 1
            FirstElapsedMilliseconds = [long]$rows[0].ElapsedMs
            LastElapsedMilliseconds = [long]$rows[-1].ElapsedMs
            Samples = $rows.Count
            WorkingSetBytes = Get-SeriesStatistics $rows 'WorkingSetBytes'
            PrivateBytes = Get-SeriesStatistics $rows 'PrivateBytes'
            ThreadCount = Get-SeriesStatistics $rows 'Threads'
            HandleCount = Get-SeriesStatistics $rows 'Handles'
        })
    }
}

$transitions = [Collections.Generic.List[object]]::new()
for ($index = 1; $index -lt $gcRows.Count; $index++) {
    $before = $gcRows[$index - 1]
    $after = $gcRows[$index]
    if ($after.lastGcIndex -eq $before.lastGcIndex) { continue }
    $transitions.Add([pscustomobject]@{
        ElapsedMilliseconds = $after.elapsedMs
        GcIndex = $after.lastGcIndex
        Gen0 = $after.gen0
        Gen1 = $after.gen1
        Gen2 = $after.gen2
        ClosedWindowsAliveBefore = $before.closedAlive
        ClosedWindowsAliveAfter = $after.closedAlive
        ManagedBytesBefore = $before.managedBytes
        ManagedBytesAfter = $after.managedBytes
        HeapBytesAfterLastGc = $after.lastGcHeapBytes
        CommittedBytesAfterLastGc = $after.lastGcCommittedBytes
    })
}
$transitions | Export-Csv -NoTypeInformation -Encoding UTF8 `
    -LiteralPath (Join-Path $MeasurementDirectory 'gc-transitions.csv')

$summary = [pscustomobject]@{
    DiagnosticOnly = $true
    BudgetAcceptance = $false
    ExecutionPassed = [bool]$run.Passed
    CyclesRequested = $run.CyclesRequested
    CyclesCompleted = $run.CyclesCompleted
    Samples = $samples.Count
    WorkingSetBytes = Get-SeriesStatistics $samples 'WorkingSetBytes'
    PrivateBytes = Get-SeriesStatistics $samples 'PrivateBytes'
    Blocks = $blocks.ToArray()
    GcTransitionsObserved = $transitions.Count
    FinalSample = $samples[-1]
    FinalGc = if ($gcRows.Count) { $gcRows[$gcRows.Count - 1] } else { $null }
    Note = 'Natural GC only. Window and heap counts describe this bounded run; they are not a proof of absence of every possible leak.'
}
$summary | ConvertTo-Json -Depth 7 | Set-Content -Encoding UTF8 `
    -LiteralPath (Join-Path $MeasurementDirectory 'analysis.json')
$blocks | Select-Object Block, Samples, FirstElapsedMilliseconds, LastElapsedMilliseconds,
    @{Name='WorkingSetMeanMiB';Expression={[math]::Round($_.WorkingSetBytes.Mean / 1MB, 3)}},
    @{Name='PrivateMeanMiB';Expression={[math]::Round($_.PrivateBytes.Mean / 1MB, 3)}},
    @{Name='ThreadsMean';Expression={[math]::Round($_.ThreadCount.Mean, 2)}},
    @{Name='HandlesMean';Expression={[math]::Round($_.HandleCount.Mean, 2)}} |
    Format-Table -AutoSize
