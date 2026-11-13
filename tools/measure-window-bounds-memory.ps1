[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ProbePath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(5,120)][int]$ActiveSeconds=5
)

# This measures programmatic window changes. It does not simulate mouse input
# or enter the native drag loop, and its results must be labelled accordingly.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$ProbePath=(Resolve-Path -LiteralPath $ProbePath).Path
if([IO.Path]::GetFileName($ProbePath) -ne 'Jalium.UI.MinimalWindowProbe.exe') {
    throw 'Select the locally built, complete-theme MinimalWindowProbe.'
}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new evidence directory.'}
[void](New-Item -ItemType Directory -Path $OutputDirectory)

if(-not ('JaliumOwnedBounds' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class JaliumOwnedBounds {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr h,IntPtr z,int x,int y,int w,int t,uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeoutW(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint t,out IntPtr result);
    public static Rect Inspect(IntPtr h,int processId) {
        uint id;
        if(GetWindowThreadProcessId(h,out id)==0 || id!=(uint)processId || !IsWindowVisible(h))
            throw new InvalidOperationException("The selected visible window does not belong to the owned probe.");
        Rect r;
        if(!GetWindowRect(h,out r)) throw new InvalidOperationException("Cannot read owned window bounds.");
        return r;
    }
    public static void Set(IntPtr h,int processId,int x,int y,int width,int height) {
        Inspect(h,processId);
        // SWP_NOZORDER | SWP_NOACTIVATE: do not change foreground focus.
        if(!SetWindowPos(h,IntPtr.Zero,x,y,width,height,0x14))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public static bool Responds(IntPtr h,int processId) {
        Inspect(h,processId);
        IntPtr result;
        return SendMessageTimeoutW(h,0,IntPtr.Zero,IntPtr.Zero,3,500,out result)!=IntPtr.Zero;
    }
}
'@
}

$rows=[Collections.Generic.List[object]]::new()
$stages=[Collections.Generic.List[object]]::new()
$owned=$null
$stdout=$null
$stderr=$null
$failure=$null
$exitCode=$null
$forcedTermination=$false
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=$ProbePath
$start.WorkingDirectory=Split-Path -Parent $ProbePath
$start.UseShellExecute=$false
$start.RedirectStandardOutput=$true
$start.RedirectStandardError=$true
$start.EnvironmentVariables['JALIUM_WORKING_SET_TRIM']='off'
[pscustomobject]@{
    ProbePath=$ProbePath;ProbeSha256=(Get-FileHash -LiteralPath $ProbePath).Hash
    ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
    InputKind='Programmatic SetWindowPos on the owned HWND; no global input, no synthetic enter/exit sizing messages; not a mouse-drag benchmark'
    WarmupSeconds=5;IdleSeconds=10;ActiveSeconds=$ActiveSeconds;IntervalMilliseconds=250
    GcConserveMemory=$env:DOTNET_GCConserveMemory;GcConcurrent=$env:DOTNET_gcConcurrent
    GcGen0MaxBudget=$env:DOTNET_GCGen0MaxBudget;GcRegionSize=$env:DOTNET_GCRegionSize
    Backend=$env:JALIUM_RENDER_BACKEND
    ForcedGc=$false;WorkingSetTrim=$false;StartedUtc=[DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'environment.json')
try {
    $owned=[Diagnostics.Process]::Start($start)
    $stdout=$owned.StandardOutput.ReadToEndAsync()
    $stderr=$owned.StandardError.ReadToEndAsync()
    $ready=[Diagnostics.Stopwatch]::StartNew()
    do {
        $owned.Refresh()
        if($owned.HasExited){throw 'Probe exited before showing its window.'}
        $windowHandle=$owned.MainWindowHandle
        if($windowHandle -ne [IntPtr]::Zero){break}
        if($ready.Elapsed.TotalSeconds -gt 30){throw 'Probe startup timed out.'}
        Start-Sleep -Milliseconds 50
    } while($true)
    $initial=[JaliumOwnedBounds]::Inspect($windowHandle,$owned.Id)
    $width=$initial.Right-$initial.Left
    $height=$initial.Bottom-$initial.Top
    foreach($phase in @('warmup','idle-before','move-window','resize-window','idle-after')) {
        $active=$phase -in @('move-window','resize-window')
        $seconds=if($active){$ActiveSeconds}elseif($phase -eq 'warmup'){5}else{10}
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $nextSample=0.0
        $nextUpdate=0.0
        $updates=0
        $owned.Refresh()
        $cpuStart=$owned.TotalProcessorTime.TotalMilliseconds
        while($clock.Elapsed.TotalSeconds -lt $seconds) {
            $elapsed=$clock.Elapsed.TotalMilliseconds
            if($active -and $elapsed -ge $nextUpdate) {
                $delta=[int][Math]::Round(60*[Math]::Sin($elapsed/280.0))
                if($phase -eq 'move-window') {
                    [JaliumOwnedBounds]::Set($windowHandle,$owned.Id,$initial.Left,$initial.Top+$delta,$width,$height)
                } else {
                    [JaliumOwnedBounds]::Set($windowHandle,$owned.Id,$initial.Left,$initial.Top-$delta,$width,$height+$delta)
                }
                $updates++
                $nextUpdate=$clock.Elapsed.TotalMilliseconds+16
            }
            if($elapsed -ge $nextSample) {
                $owned.Refresh()
                $bounds=[JaliumOwnedBounds]::Inspect($windowHandle,$owned.Id)
                $rows.Add([pscustomobject]@{
                    Phase=$phase;PhaseMs=$clock.Elapsed.TotalMilliseconds;ProcessId=$owned.Id
                    PrivateBytes=$owned.PrivateMemorySize64;WorkingSetBytes=$owned.WorkingSet64
                    CpuMs=$owned.TotalProcessorTime.TotalMilliseconds
                    Left=$bounds.Left;Top=$bounds.Top;Width=$bounds.Right-$bounds.Left;Height=$bounds.Bottom-$bounds.Top
                    Responding=[JaliumOwnedBounds]::Responds($windowHandle,$owned.Id)
                })
                $nextSample=$clock.Elapsed.TotalMilliseconds+250
            }
            Start-Sleep -Milliseconds 5
        }
        if($active){[JaliumOwnedBounds]::Set($windowHandle,$owned.Id,$initial.Left,$initial.Top,$width,$height)}
        $bounds=[JaliumOwnedBounds]::Inspect($windowHandle,$owned.Id)
        if($bounds.Left -ne $initial.Left -or $bounds.Top -ne $initial.Top -or $bounds.Right -ne $initial.Right -or $bounds.Bottom -ne $initial.Bottom) {
            throw "Window bounds did not restore after $phase."
        }
        $owned.Refresh()
        $stages.Add([pscustomobject]@{Phase=$phase;Updates=$updates;DurationMs=$clock.Elapsed.TotalMilliseconds;CpuMs=$owned.TotalProcessorTime.TotalMilliseconds-$cpuStart})
    }
    if(@($rows | Where-Object { -not $_.Responding }).Count -gt 0){throw 'An unresponsive sample was observed.'}
    $nativeModules=@($owned.Modules | Where-Object ModuleName -Like 'jalium.native.*' | ForEach-Object {
        [pscustomobject]@{Name=$_.ModuleName;Path=$_.FileName;Sha256=(Get-FileHash -LiteralPath $_.FileName).Hash;
            MatchesPayloadDirectory=[string]::Equals((Split-Path -Parent $_.FileName),(Split-Path -Parent $ProbePath),[StringComparison]::OrdinalIgnoreCase)}
    })
    $nativeModules | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'native-modules.csv')
    if($nativeModules.Count -lt 2 -or @($nativeModules | Where-Object { -not $_.MatchesPayloadDirectory }).Count -gt 0){throw 'Native modules did not match the local payload.'}
} catch {
    $failure=$_.Exception.ToString()
} finally {
    if($null -ne $owned) {
        try {
            if(-not $owned.HasExited) {
                [void]$owned.CloseMainWindow()
                if(-not $owned.WaitForExit(5000)){$owned.Kill();$forcedTermination=$true;[void]$owned.WaitForExit(5000)}
            }
            $exitCode=$owned.ExitCode
            if($null -ne $stdout){[IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.log'),$stdout.GetAwaiter().GetResult())}
            if($null -ne $stderr){[IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.log'),$stderr.GetAwaiter().GetResult())}
        } finally {$owned.Dispose()}
    }
    $rows | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'samples.csv')
    $summary=@(foreach($stage in $stages) {
        $samples=@($rows | Where-Object Phase -eq $stage.Phase)
        $private=$samples | Measure-Object PrivateBytes -Average -Maximum
        $working=$samples | Measure-Object WorkingSetBytes -Average -Maximum
        [pscustomobject]@{
            Phase=$stage.Phase;Samples=$samples.Count;Updates=$stage.Updates
            PrivateMeanMiB=[Math]::Round($private.Average/1MB,3);PrivateMaxMiB=[Math]::Round($private.Maximum/1MB,3)
            WorkingSetMeanMiB=[Math]::Round($working.Average/1MB,3);WorkingSetMaxMiB=[Math]::Round($working.Maximum/1MB,3)
            CpuOneCorePercent=[Math]::Round(100*$stage.CpuMs/$stage.DurationMs,3)
        }
    })
    $summary | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'summary.csv')
    $passed=$null -eq $failure -and $exitCode -eq 0 -and -not $forcedTermination -and $stages.Count -eq 5
    [pscustomobject]@{Passed=$passed;ExitCode=$exitCode;Failure=$failure;ForcedTermination=$forcedTermination;RealMouseInput=$false} |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'result.json')
}
$summary | Format-Table -AutoSize
if(-not $passed){throw "Bounds measurement failed: $failure"}
