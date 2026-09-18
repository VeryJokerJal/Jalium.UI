[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ProbePath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(5,120)][int]$PhaseSeconds=15,
    [switch]$Trace
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$ProbePath=(Resolve-Path -LiteralPath $ProbePath).Path
if([IO.Path]::GetFileName($ProbePath) -ne 'Jalium.UI.MemoryProbe.exe'){throw 'Select a locally built MemoryProbe.'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory.'}
[void](New-Item -ItemType Directory -Path $OutputDirectory)
if(-not ('OwnedWindowDragStress' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
public static class OwnedWindowDragStress {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct GuiThreadInfo {
        public uint Size,Flags;
        public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;
        public Rect CaretRect;
    }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h,ref Point p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] input,int size);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll",SetLastError=true)] static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWaitableTimerExW(IntPtr attributes,string name,uint flags,uint access);
    [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr state,bool resume);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr z,int x,int y,int w,int t,uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr h,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeoutW(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint t,out IntPtr result);
    public sealed class Sample {
        public string Phase; public double PhaseMs,CpuMs,WorkingSetBytes,PrivateBytes;
        public int Sent,Threads,Handles,Left,Top,Width,Height; public bool Responding;
    }
    public sealed class PhaseResult {
        public string Phase; public double DurationMs,CpuMs,ActualMovesPerSecond;
        public int RequestedRate,Sent,FinalPositionCorrections,ConfirmedDragStarts,ConfirmedDragEnds; public bool FinalDimensionsPassed; public Rect FinalRect;
    }
    public sealed class Result {
        public List<Sample> Samples=new List<Sample>();
        public List<PhaseResult> Phases=new List<PhaseResult>();
        public bool Passed; public string Failure; public Rect InitialRect;
    }
    static void CheckOwnedForeground(IntPtr h,int expectedPid) {
        uint pid; GetWindowThreadProcessId(h,out pid);
        if(pid!=(uint)expectedPid || GetForegroundWindow()!=h)
            throw new InvalidOperationException("Owned probe lost foreground; no further input sent.");
    }
    static void Button(bool down) {
        var input=new Input {Type=0,Mouse=new MouseInput {Flags=down?2u:4u}};
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))!=1)
            throw new InvalidOperationException("SendInput did not accept the mouse button event.");
    }
    static void WaitForSizingState(IntPtr h,int processId,bool active) {
        uint pid; uint thread=GetWindowThreadProcessId(h,out pid);
        var clock=Stopwatch.StartNew();
        var info=new GuiThreadInfo {Size=(uint)Marshal.SizeOf(typeof(GuiThreadInfo))};
        do {
            CheckOwnedForeground(h,processId);
            if(!GetGUIThreadInfo(thread,ref info))
                throw new InvalidOperationException("Cannot inspect the owned GUI thread's sizing state.");
            bool sizing=(info.Flags&2u)!=0;
            if(active?sizing && info.MoveSize==h:!sizing) return;
            Thread.Sleep(5);
        } while(clock.ElapsedMilliseconds<1000);
        throw new InvalidOperationException("Native sizing did not reach active="+active+
            "; flags="+info.Flags+", moveSize="+info.MoveSize+", capture="+info.Capture);
    }
    static int FinishDrag(IntPtr h,int processId,Rect initial,int x) {
        // Native sizing preserves the border hit offset. Observe the actual
        // edge and correct that offset using mouse movement while still held;
        // do not restore the size through a separate window-size API.
        WaitForSizingState(h,processId,true);
        int y=initial.Top+1, corrections=0;
        Rect bounds=initial;
        for(int attempt=0;attempt<4;attempt++) {
            CheckOwnedForeground(h,processId);
            if(!SetCursorPos(x,y)) throw new InvalidOperationException("Final drag cursor update failed.");
            var settle=Stopwatch.StartNew();
            do {
                Thread.Sleep(5);
                if(!GetWindowRect(h,out bounds)) throw new InvalidOperationException("Final drag rectangle unavailable.");
                if(bounds.Top==initial.Top) break;
            } while(settle.ElapsedMilliseconds<300);
            if(bounds.Top==initial.Top) break;
            y-=bounds.Top-initial.Top; corrections++;
        }
        Button(false);
        WaitForSizingState(h,processId,false);
        if(!GetWindowRect(h,out bounds) || bounds.Left!=initial.Left || bounds.Top!=initial.Top ||
            bounds.Right!=initial.Right || bounds.Bottom!=initial.Bottom)
            throw new InvalidOperationException("The completed native drag did not restore its final boundary: "+
                bounds.Left+","+bounds.Top+","+bounds.Right+","+bounds.Bottom+"; requested top="+initial.Top);
        return corrections;
    }
    static void WaitInputTimer(IntPtr timer,double milliseconds) {
        long due=-(long)(Math.Max(0.1,milliseconds)*10000);
        if(!SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false) || WaitForSingleObject(timer,1000)!=0)
            throw new InvalidOperationException("External high-resolution input timer failed.");
    }
    public static Result Run(int processId,IntPtr h,int phaseSeconds) {
        var result=new Result(); Point oldCursor; GetCursorPos(out oldCursor);
        bool held=false; IntPtr timer=IntPtr.Zero;
        using(var process=Process.GetProcessById(processId)) {
            try {
                timer=CreateWaitableTimerExW(IntPtr.Zero,null,2,0x1F0003);
                if(timer==IntPtr.Zero) throw new InvalidOperationException("External high-resolution input timer unavailable.");
                Rect initial; if(!GetWindowRect(h,out initial)) throw new InvalidOperationException("Window rectangle unavailable.");
                var monitor=new MonitorInfo {Size=Marshal.SizeOf(typeof(MonitorInfo))};
                if(!GetMonitorInfoW(MonitorFromWindow(h,2),ref monitor)) throw new InvalidOperationException("Monitor unavailable.");
                int width=initial.Right-initial.Left, height=initial.Bottom-initial.Top;
                int margin=Math.Min(100,Math.Max(8,(monitor.Work.Bottom-monitor.Work.Top-height)/2));
                if(!SetWindowPos(h,IntPtr.Zero,monitor.Work.Left+24,monitor.Work.Top+margin,0,0,0x15))
                    throw new InvalidOperationException("Could not position the owned window.");
                uint ignored; uint foreign=GetWindowThreadProcessId(GetForegroundWindow(),out ignored), own=GetCurrentThreadId();
                bool attached=foreign!=0 && foreign!=own && AttachThreadInput(own,foreign,true);
                try {SetForegroundWindow(h);} finally {if(attached) AttachThreadInput(own,foreign,false);}
                Thread.Sleep(100);
                CheckOwnedForeground(h,processId);
                GetWindowRect(h,out initial); result.InitialRect=initial;
                double amplitude=Math.Max(4,Math.Min(70,initial.Top-monitor.Work.Top-2));
                int anchorX=initial.Left+width/2, anchorY=initial.Top+1;
                foreach(var phase in new[]{"idle-before","mouse-sweep","mouse-fast-reverse","edge-drag","alternating","idle-after"}) {
                    bool idle=phase.StartsWith("idle");
                    int duration=idle?10000:phaseSeconds*1000;
                    int rate=phase=="mouse-sweep"?60:phase=="mouse-fast-reverse"?240:125;
                    var clock=Stopwatch.StartNew(); double nextInput=0,nextSample=0; int sent=0,corrections=0,starts=0,ends=0;
                    process.Refresh(); double cpuBefore=process.TotalProcessorTime.TotalMilliseconds;
                    while(clock.Elapsed.TotalMilliseconds<duration) {
                        double elapsed=clock.Elapsed.TotalMilliseconds;
                        bool drag=phase=="edge-drag" || (phase=="alternating" && (int)(elapsed/3000)%2==1);
                        if(!idle && elapsed>=nextInput) {
                            CheckOwnedForeground(h,processId);
                            if(drag && !held) {
                                SetCursorPos(anchorX,anchorY); Thread.Sleep(20);
                                IntPtr hit;
                                long point=((long)(anchorY&65535)<<16)|(uint)(anchorX&65535);
                                if(SendMessageTimeoutW(h,0x84,IntPtr.Zero,new IntPtr(point),3,1000,out hit)==IntPtr.Zero || hit.ToInt64()!=12)
                                    throw new InvalidOperationException("The observed top border did not return HTTOP; drag was not started.");
                                Button(true); held=true;
                                WaitForSizingState(h,processId,true); starts++;
                            } else if(!drag && held) {
                                corrections+=FinishDrag(h,processId,initial,anchorX); held=false; ends++;
                            }
                            if(drag) {
                                int y=anchorY+(int)Math.Round(amplitude*Math.Sin(elapsed/280.0));
                                if(!SetCursorPos(anchorX,y)) throw new InvalidOperationException("Cursor update failed.");
                            } else {
                                Rect client; GetClientRect(h,out client);
                                double wave=phase=="mouse-fast-reverse"?Math.Sin(elapsed/35.0):Math.Sin(elapsed/400.0);
                                var point=new Point {X=40+(int)((client.Right-80)*(wave+1)/2),Y=140+(int)(70*Math.Sin(elapsed/200.0))};
                                ClientToScreen(h,ref point); SetCursorPos(point.X,point.Y);
                            }
                            sent++;
                            nextInput=Math.Max(nextInput+1000.0/rate,clock.Elapsed.TotalMilliseconds);
                        }
                        if(elapsed>=nextSample) {
                            process.Refresh(); Rect bounds; GetWindowRect(h,out bounds);
                            IntPtr ignoredResult;
                            bool responsive=SendMessageTimeoutW(h,0,IntPtr.Zero,IntPtr.Zero,3,1000,out ignoredResult)!=IntPtr.Zero;
                            result.Samples.Add(new Sample {Phase=phase,PhaseMs=elapsed,CpuMs=process.TotalProcessorTime.TotalMilliseconds,
                                WorkingSetBytes=process.WorkingSet64,PrivateBytes=process.PrivateMemorySize64,Sent=sent,
                                Threads=process.Threads.Count,Handles=process.HandleCount,Responding=responsive,
                                Left=bounds.Left,Top=bounds.Top,Width=bounds.Right-bounds.Left,Height=bounds.Bottom-bounds.Top});
                            nextSample=clock.Elapsed.TotalMilliseconds+250;
                        }
                        double untilNext=idle?nextSample-clock.Elapsed.TotalMilliseconds:nextInput-clock.Elapsed.TotalMilliseconds;
                        WaitInputTimer(timer,Math.Min(10,Math.Max(0.1,untilNext)));
                    }
                    if(held){corrections+=FinishDrag(h,processId,initial,anchorX);held=false;ends++;}
                    process.Refresh(); Rect final; GetWindowRect(h,out final);
                    bool dimensions=final.Left==initial.Left && final.Top==initial.Top && final.Right==initial.Right && final.Bottom==initial.Bottom;
                    result.Phases.Add(new PhaseResult {Phase=phase,DurationMs=clock.Elapsed.TotalMilliseconds,CpuMs=process.TotalProcessorTime.TotalMilliseconds-cpuBefore,
                        Sent=sent,RequestedRate=idle?0:rate,ActualMovesPerSecond=sent*1000.0/clock.Elapsed.TotalMilliseconds,FinalDimensionsPassed=dimensions,FinalPositionCorrections=corrections,ConfirmedDragStarts=starts,ConfirmedDragEnds=ends,FinalRect=final});
                    if(!dimensions) throw new InvalidOperationException("The drag did not preserve its final requested rectangle.");
                }
                result.Passed=result.Samples.TrueForAll(s=>s.Responding);
                if(!result.Passed) result.Failure="Unresponsive process samples were observed.";
            } catch(Exception e) {result.Failure=e.ToString();}
            finally {
                try {if(held) Button(false);}
                finally {SetCursorPos(oldCursor.X,oldCursor.Y);if(timer!=IntPtr.Zero)CloseHandle(timer);}
            }
        }
        return result;
    }
}
'@
}
$owned=$null
$result=$null
$info=[Diagnostics.ProcessStartInfo]::new()
$info.FileName=$ProbePath
$info.WorkingDirectory=Split-Path -Parent $ProbePath
$info.UseShellExecute=$false
$info.RedirectStandardOutput=$true
$info.RedirectStandardError=$true
$info.Arguments='--exit-after-ms '+[string](($PhaseSeconds*4+90)*1000)
$info.EnvironmentVariables['JALIUM_WORKING_SET_TRIM']='off'
$info.EnvironmentVariables['JALIUM_HOVER_TRACE']=if($Trace){'1'}else{'0'}
$info.EnvironmentVariables['JALIUM_HOVER_TRACE_FILE']=Join-Path $OutputDirectory 'hover-trace.log'
[pscustomobject]@{ProbePath=$ProbePath;ProbeSha256=(Get-FileHash -LiteralPath $ProbePath).Hash;ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;PhaseSeconds=$PhaseSeconds;Trace=[bool]$Trace;InputKind='OS SetCursorPos and SendInput; real HTTOP modal border drag, no synthetic resize messages or SetWindowPos size changes during phases';InputTimer='One owned high-resolution waitable timer in the external driver only';ModalHandoff='GetGUIThreadInfo on the owned GUI thread confirms GUI_INMOVESIZE and hwndMoveSize after press, and loop exit after release';FinalEdgePolicy='Observe top edge and correct the hit offset with at most four cursor moves while still held; require the final rectangle after modal-loop exit';StartedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'environment.json')
try {
    $owned=[Diagnostics.Process]::Start($info)
    $stdout=$owned.StandardOutput.ReadToEndAsync();$stderr=$owned.StandardError.ReadToEndAsync()
    $ready=[Diagnostics.Stopwatch]::StartNew()
    do{$owned.Refresh();if($owned.HasExited){throw 'Probe exited during startup.'};if($ready.Elapsed.TotalSeconds -gt 30){throw 'Window startup timed out.'};Start-Sleep -Milliseconds 50}while($owned.MainWindowHandle -eq [IntPtr]::Zero)
    $result=[OwnedWindowDragStress]::Run($owned.Id,$owned.MainWindowHandle,$PhaseSeconds)
    $result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'raw.json')
    $result.Samples | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'samples.csv')
    $result.Phases | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutputDirectory 'phases.csv')
    $result.Phases | Format-Table Phase,Sent,ActualMovesPerSecond,CpuMs,FinalDimensionsPassed -AutoSize
    if(-not $result.Passed){throw $result.Failure}
} finally {
    if($null -ne $owned){
        try {
            if(-not $owned.HasExited){[void]$owned.CloseMainWindow();if(-not $owned.WaitForExit(5000)){$owned.Kill();[void]$owned.WaitForExit(5000)}}
            [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.log'),$stdout.GetAwaiter().GetResult())
            [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.log'),$stderr.GetAwaiter().GetResult())
            [pscustomobject]@{Passed=$null -ne $result -and $result.Passed -and $owned.ExitCode -eq 0;ExitCode=$owned.ExitCode;Failure=if($null -ne $result){$result.Failure}else{'Driver did not start'};DiagnosticOnly=$true;BudgetEvidence=$false} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'result.json')
        } finally {$owned.Dispose()}
    }
}
