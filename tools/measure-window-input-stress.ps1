[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProbePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 1000)][int]$EventRate = 125,
    [ValidateRange(2, 120)][int]$ActiveSeconds = 8,
    [ValidateRange(0, 10000)][int]$EventsPerPhase = 0,
    [switch]$Features,
    [switch]$Trace,
    [switch]$Cursor,
    [switch]$LifetimeDiagnostics,
    [ValidateRange(0, 8192)][int]$InitialWidth = 0,
    [ValidateRange(0, 8192)][int]$InitialHeight = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProbePath = (Resolve-Path -LiteralPath $ProbePath).Path
if ([IO.Path]::GetFileName($ProbePath) -ne 'Jalium.UI.MemoryProbe.exe') {
    throw 'Select the locally built Jalium.UI.MemoryProbe.exe.'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new evidence directory.' }
[void](New-Item -ItemType Directory -Path $OutputDirectory)

if (-not ('JaliumInputStress.Driver' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace JaliumInputStress {
    public static class Driver {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr SendMessageTimeoutW(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
        [DllImport("user32.dll", SetLastError=true)] private static extern bool SetWindowPos(IntPtr h, IntPtr z, int x, int y, int w, int t, uint f);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out Rect r);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref Point p);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor,ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a,uint b,bool attach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        public static Point CursorPosition() { Point p; if(!GetCursorPos(out p)) throw new Win32Exception(); return p; }
        public static void RestoreCursor(Point p) { SetCursorPos(p.X,p.Y); }
        public static Rect PlaceForCursorInput(IntPtr h) {
            Rect bounds=Outer(h);
            var info=new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
            if(!GetMonitorInfoW(MonitorFromWindow(h,2),ref info)) throw new Win32Exception();
            int width=bounds.Right-bounds.Left, height=bounds.Bottom-bounds.Top;
            if(width>info.Work.Right-info.Work.Left || height>info.Work.Bottom-info.Work.Top)
                throw new InvalidOperationException("The cursor trajectory requires the complete probe window to fit inside its monitor work area.");
            int x=info.Work.Left+Math.Min(24,(info.Work.Right-info.Work.Left-width)/2);
            int y=info.Work.Top+Math.Min(24,(info.Work.Bottom-info.Work.Top-height)/2);
            if(!SetWindowPos(h,IntPtr.Zero,x,y,0,0,0x0015)) throw new Win32Exception();
            return Outer(h);
        }
        public static void Activate(IntPtr h) {
            uint ignored; uint foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);
            uint currentThread=GetCurrentThreadId(); bool attached=false;
            try {
                if(foregroundThread!=0 && foregroundThread!=currentThread) attached=AttachThreadInput(currentThread,foregroundThread,true);
                SetForegroundWindow(h);
                if(GetForegroundWindow()!=h) throw new InvalidOperationException("Cannot activate the owned probe for OS cursor input.");
            } finally { if(attached) AttachThreadInput(currentThread,foregroundThread,false); }
        }
        public static void MoveCursor(IntPtr h,int x,int y) {
            if(GetForegroundWindow()!=h) throw new InvalidOperationException("Probe lost foreground; no cursor movement sent.");
            Point p=new Point {X=x,Y=y};
            if(!ClientToScreen(h,ref p) || !SetCursorPos(p.X,p.Y)) throw new Win32Exception();
        }
        public static Rect Client(IntPtr h) { Rect r; if(!GetClientRect(h,out r)) throw new Win32Exception(); return r; }
        public static Rect Outer(IntPtr h) { Rect r; if(!GetWindowRect(h,out r)) throw new Win32Exception(); return r; }
        public static void Message(IntPtr h,uint message,long w,long l) {
            IntPtr result;
            if(SendMessageTimeoutW(h,message,new IntPtr(w),new IntPtr(l),3,1000,out result)==IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Owned window did not process stress message.");
        }
        public static void Move(IntPtr h,int x,int y) { Message(h,0x0200,0,((long)(y & 65535)<<16)|(uint)(x & 65535)); }
        public static void Resize(IntPtr h,int width,int height) {
            if(!SetWindowPos(h,IntPtr.Zero,0,0,width,height,0x0016)) throw new Win32Exception();
        }
    }
}
'@
}

$samples = [Collections.Generic.List[object]]::new()
$stages = [Collections.Generic.List[object]]::new()
$owned = $null
$clock = [Diagnostics.Stopwatch]::new()
$initialHash = (Get-FileHash -LiteralPath $ProbePath).Hash
$failure = $null
$inSizing = $false
$finalDimensionsPassed = $false
$exitCode = $null
$handle = [IntPtr]::Zero
$originalCursor = $null
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $ProbePath
$startInfo.WorkingDirectory = Split-Path -Parent $ProbePath
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.Arguments = if ($Features) { '--action 0:feature' } else { '' }
if ($LifetimeDiagnostics) {
    $activeBudget = if ($EventsPerPhase -gt 0) { [Math]::Max($ActiveSeconds, $EventsPerPhase*0.2) } else { $ActiveSeconds }
    $startInfo.Arguments += ' --lifetime-diagnostics --exit-after-ms ' + [string](($activeBudget*4+90)*1000)
}
$startInfo.EnvironmentVariables['JALIUM_WORKING_SET_TRIM'] = 'off'
$startInfo.EnvironmentVariables['JALIUM_HOVER_TRACE'] = if ($Trace) { '1' } else { '0' }
$startInfo.EnvironmentVariables['JALIUM_HOVER_TRACE_FILE'] = Join-Path $OutputDirectory 'hover-trace.log'
[pscustomobject]@{
    ProbePath=$ProbePath; ProbeSha256=$initialHash; ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
    EventRate=$EventRate; ActiveSeconds=$ActiveSeconds; Features=[bool]$Features; Trace=[bool]$Trace
    EventsPerPhase=$EventsPerPhase
    OsCursorInput=[bool]$Cursor; LifetimeDiagnostics=[bool]$LifetimeDiagnostics
    InitialWidth=$InitialWidth; InitialHeight=$InitialHeight
    StartedUtc=[DateTime]::UtcNow.ToString('o'); ProcessorCount=[Environment]::ProcessorCount
    InputKind=if ($Cursor) { 'SetCursorPos generates OS mouse movement, guarded by owned foreground HWND. Actual WM_SIZE from SetWindowPos; synthetic enter/exit sizing brackets.' } else { 'Targeted WM_MOUSEMOVE through real WndProc; SetWindowPos generates actual WM_SIZE. Synthetic enter/exit sizing brackets. Message-only hover may conflict with the physical cursor; not a physical-hover acceptance run.' }
    CpuMetric='OneCorePercent = CPU time delta / wall time delta * 100; normalized CPU additionally divided by logical processor count.'
    Backend=$env:JALIUM_RENDER_BACKEND; Engine=$env:JALIUM_RENDERING_ENGINE
} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'environment.json')

try {
    $owned = [Diagnostics.Process]::Start($startInfo)
    $stdout = $owned.StandardOutput.ReadToEndAsync()
    $stderr = $owned.StandardError.ReadToEndAsync()
    $readyClock = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($owned.HasExited) { throw 'Probe exited before showing its window.' }
        $owned.Refresh()
        $handle = $owned.MainWindowHandle
        if ($handle -ne [IntPtr]::Zero -and [JaliumInputStress.Driver]::IsWindowVisible($handle)) { break }
        if ($readyClock.Elapsed.TotalSeconds -gt 30) { throw 'Probe window was not ready within 30 seconds.' }
        Start-Sleep -Milliseconds 50
    } while ($true)
    $ownerId = [uint32]0
    [void][JaliumInputStress.Driver]::GetWindowThreadProcessId($handle,[ref]$ownerId)
    if ($ownerId -ne $owned.Id) { throw 'Unexpected window owner.' }
    if ($InitialWidth -gt 0 -or $InitialHeight -gt 0) {
        if ($InitialWidth -le 0 -or $InitialHeight -le 0) { throw 'Specify both initial dimensions.' }
        [JaliumInputStress.Driver]::Resize($handle,$InitialWidth,$InitialHeight)
    }
    if ($Cursor) {
        $originalCursor=[JaliumInputStress.Driver]::CursorPosition()
        [JaliumInputStress.Driver]::PlaceForCursorInput($handle) | ConvertTo-Json |
            Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'cursor-window-placement.json')
        [JaliumInputStress.Driver]::Activate($handle)
    }
    $original = [JaliumInputStress.Driver]::Outer($handle)
    $originalWidth = $original.Right-$original.Left
    $originalHeight = $original.Bottom-$original.Top
    $clock.Start()
    $owned.Refresh()
    $previousCpu = $owned.TotalProcessorTime.TotalMilliseconds
    $previousSampleMs = 0.0
    foreach ($phase in @('startup-idle','mouse-blank','mouse-caption','after-mouse','live-resize','after-resize','resize-repeat','final-idle')) {
        $active = $phase -in @('mouse-blank','mouse-caption','live-resize','resize-repeat')
        $seconds = if ($active) { $ActiveSeconds } elseif ($phase -eq 'startup-idle' -or $phase -eq 'final-idle') { 10 } else { 5 }
        $resize = $phase -in @('live-resize','resize-repeat')
        if ($resize) { [JaliumInputStress.Driver]::Message($handle,0x0231,0,0); $inSizing=$true }
        $stageStart = $clock.Elapsed.TotalMilliseconds
        $owned.Refresh()
        $stageCpuStart = $owned.TotalProcessorTime.TotalMilliseconds
        $nextInput = $stageStart
        $nextSample = $stageStart
        $sent = 0
        $client = [JaliumInputStress.Driver]::Client($handle)
        while (($active -and $EventsPerPhase -gt 0 -and $sent -lt $EventsPerPhase) -or
               ((-not $active -or $EventsPerPhase -eq 0) -and $clock.Elapsed.TotalMilliseconds -lt $stageStart+$seconds*1000)) {
            if ($owned.HasExited) { throw "Probe exited during $phase." }
            if ($EventsPerPhase -gt 0 -and $clock.Elapsed.TotalMilliseconds-$stageStart -gt [Math]::Max(60000,$EventsPerPhase*200)) {
                throw "The fixed input sequence exceeded its bounded duration during $phase."
            }
            $now = $clock.Elapsed.TotalMilliseconds
            if ($active -and $now -ge $nextInput) {
                if ($resize) {
                    $offset = [int](90*(1+[Math]::Sin($sent*0.09)))
                    [JaliumInputStress.Driver]::Resize($handle,$originalWidth+$offset,$originalHeight+[int]($offset*0.6))
                } elseif ($phase -eq 'mouse-caption') {
                    $x = $client.Right-25-([int]($sent/8)%3)*46
                    if ($Cursor) { [JaliumInputStress.Driver]::MoveCursor($handle,$x,18) }
                    else { [JaliumInputStress.Driver]::Move($handle,$x,18) }
                } else {
                    $x=50+($sent*7)%([Math]::Max(1,$client.Right-100))
                    $y=120+($sent*3)%([Math]::Max(1,$client.Bottom-180))
                    if ($Cursor) { [JaliumInputStress.Driver]::MoveCursor($handle,$x,$y) }
                    else { [JaliumInputStress.Driver]::Move($handle,$x,$y) }
                }
                $sent++
                # Never catch up by flooding an unbounded queue after a slow call.
                $nextInput = [Math]::Max($nextInput+1000.0/$EventRate,$clock.Elapsed.TotalMilliseconds)
            }
            if ($now -ge $nextSample) {
                $owned.Refresh()
                $sampleMs = $clock.Elapsed.TotalMilliseconds
                $cpuMs = $owned.TotalProcessorTime.TotalMilliseconds
                $oneCore = if ($sampleMs -gt $previousSampleMs) { 100*($cpuMs-$previousCpu)/($sampleMs-$previousSampleMs) } else { 0 }
                $responsive=$true
                try { [JaliumInputStress.Driver]::Message($handle,0,0,0) } catch { $responsive=$false }
                $rect = [JaliumInputStress.Driver]::Client($handle)
                $samples.Add([pscustomobject]@{
                    Phase=$phase; ElapsedMs=[Math]::Round($sampleMs,1); PhaseMs=[Math]::Round($sampleMs-$stageStart,1)
                    ProcessId=$owned.Id; Sent=$sent; CpuTotalMs=$cpuMs; OneCorePercent=[Math]::Round($oneCore,3)
                    NormalizedCpuPercent=[Math]::Round($oneCore/[Environment]::ProcessorCount,3)
                    WorkingSetBytes=$owned.WorkingSet64; PrivateBytes=$owned.PrivateMemorySize64
                    Threads=$owned.Threads.Count; Handles=$owned.HandleCount; Responding=$responsive
                    ClientWidth=$rect.Right; ClientHeight=$rect.Bottom
                })
                $previousCpu=$cpuMs; $previousSampleMs=$sampleMs; $nextSample=$sampleMs+250
            }
            Start-Sleep -Milliseconds 1
        }
        if ($resize) {
            [JaliumInputStress.Driver]::Resize($handle,$originalWidth,$originalHeight)
            [JaliumInputStress.Driver]::Message($handle,0x0232,0,0)
            $inSizing=$false
        }
        $owned.Refresh()
        $stages.Add([pscustomobject]@{Phase=$phase; StartMs=$stageStart; EndMs=$clock.Elapsed.TotalMilliseconds; Sent=$sent; CpuMs=$owned.TotalProcessorTime.TotalMilliseconds-$stageCpuStart})
    }
    $finalRect=[JaliumInputStress.Driver]::Outer($handle)
    $finalDimensionsPassed=($finalRect.Right-$finalRect.Left)-eq $originalWidth -and ($finalRect.Bottom-$finalRect.Top)-eq $originalHeight
    if (-not $finalDimensionsPassed) { throw 'The final outer window size differs from its original size.' }
}
catch { $failure=$_.Exception.ToString() }
finally {
    if ($null -ne $owned) {
        try {
            if (-not $owned.HasExited) {
                if ($inSizing) { [JaliumInputStress.Driver]::Message($handle,0x0232,0,0) }
                [void]$owned.CloseMainWindow()
                if (-not $owned.WaitForExit(5000)) { $owned.Kill(); [void]$owned.WaitForExit(5000); if (-not $failure) { $failure='Owned probe required forced termination.' } }
            }
            $exitCode=$owned.ExitCode
            [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.log'),$stdout.GetAwaiter().GetResult())
            [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.log'),$stderr.GetAwaiter().GetResult())
        } finally { $owned.Dispose() }
    }
    if ($null -ne $originalCursor) { [JaliumInputStress.Driver]::RestoreCursor($originalCursor) }
    $samples | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'samples.csv')
    $stages | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'stages.csv')
    $summary=@(foreach($stage in $stages) {
        $rows=@($samples | Where-Object Phase -eq $stage.Phase)
        [pscustomobject]@{
            Phase=$stage.Phase; Sent=$stage.Sent; DurationMs=[Math]::Round($stage.EndMs-$stage.StartMs,1)
            CpuMilliseconds=$stage.CpuMs
            CpuMillisecondsPerInput=if ($stage.Sent -gt 0) { [Math]::Round($stage.CpuMs/$stage.Sent,6) } else { 0 }
            CpuOneCoreMeanPercent=[Math]::Round(($rows | Measure-Object OneCorePercent -Average).Average,3)
            WorkingSetMaxMiB=[Math]::Round(($rows | Measure-Object WorkingSetBytes -Maximum).Maximum/1MB,3)
            PrivateMaxMiB=[Math]::Round(($rows | Measure-Object PrivateBytes -Maximum).Maximum/1MB,3)
            PrivateFinalMiB=[Math]::Round([long]$rows[-1].PrivateBytes/1MB,3)
            ThreadsMax=($rows | Measure-Object Threads -Maximum).Maximum
            HandlesMax=($rows | Measure-Object Handles -Maximum).Maximum
            ResponseFailures=@($rows | Where-Object Responding -eq $false).Count
        }
    })
    $summary | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'summary.csv')
    $responseFailures=@($samples | Where-Object Responding -eq $false).Count
    $passed= -not $failure -and $exitCode -eq 0 -and $finalDimensionsPassed -and $responseFailures -eq 0 -and (Get-FileHash -LiteralPath $ProbePath).Hash -eq $initialHash
    [pscustomobject]@{Passed=$passed; Failure=$failure; ExitCode=$exitCode; FinalDimensionsPassed=$finalDimensionsPassed; DiagnosticOnly=$true; BudgetEvidence=$false} |
        ConvertTo-Json -Depth 3 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'result.json')
    $summary | Format-Table -AutoSize
}
if (-not $passed) { throw "Input/resize stress failed: $failure" }
