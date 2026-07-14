using System.Text;
using Jalium.UI.Controls.TerminalEmulator;

namespace Jalium.UI.Tests;

/// <summary>
/// End-to-end tests for the POSIX pty backend behind the Terminal control.
/// These run on the Linux CI slice; on other platforms they no-op (the same
/// early-return pattern the other Linux tests use until a runtime-skip
/// mechanism is adopted).
/// </summary>
public sealed class LinuxPtyTests
{
    [Fact]
    public void UnixPty_RunsShellCommand_CapturesOutputAndExitCode()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var process = new TerminalProcess();
        var output = new StringBuilder();
        var outputSignal = new ManualResetEventSlim(false);
        var exitSignal = new ManualResetEventSlim(false);
        var exitCode = int.MinValue;

        process.OutputReceived += chunk =>
        {
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains("hello-from-pty", StringComparison.Ordinal))
                    outputSignal.Set();
            }
        };
        process.ProcessExited += code =>
        {
            exitCode = code;
            exitSignal.Set();
        };

        process.Start("/bin/sh", "-c \"echo hello-from-pty\"");

        Assert.True(outputSignal.Wait(TimeSpan.FromSeconds(10)),
            $"pty output did not arrive; got: {Snapshot(output)}");
        Assert.True(exitSignal.Wait(TimeSpan.FromSeconds(10)),
            "shell did not exit after completing its command");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void UnixPty_InteractiveShell_EchoesInputAndExits()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var process = new TerminalProcess();
        var output = new StringBuilder();
        var markerSignal = new ManualResetEventSlim(false);
        var exitSignal = new ManualResetEventSlim(false);

        process.OutputReceived += chunk =>
        {
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains("interactive-42", StringComparison.Ordinal))
                    markerSignal.Set();
            }
        };
        process.ProcessExited += _ => exitSignal.Set();

        process.Start("/bin/sh", workingDirectory: "/tmp");
        Assert.True(process.IsRunning);

        // Resize must not throw while the shell is live (TIOCSWINSZ path).
        process.NotifyResize(100, 30);

        process.WriteInput("echo interactive-$((6*7))\n");
        Assert.True(markerSignal.Wait(TimeSpan.FromSeconds(10)),
            $"interactive marker missing; got: {Snapshot(output)}");

        process.WriteInput("exit\n");
        Assert.True(exitSignal.Wait(TimeSpan.FromSeconds(10)),
            "shell did not exit after 'exit'");
    }

    [Fact]
    public void UnixPty_SpawnsWithControllingTerminal()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var process = new TerminalProcess();
        var output = new StringBuilder();
        var signal = new ManualResetEventSlim(false);

        process.OutputReceived += chunk =>
        {
            lock (output)
            {
                output.Append(chunk);
                // `tty` prints the pty slave path only when stdin is a real
                // terminal; "not a tty" would mean the controlling-terminal
                // wiring (setsid + first-open) regressed.
                if (output.ToString().Contains("/dev/pts/", StringComparison.Ordinal))
                    signal.Set();
            }
        };

        process.Start("/bin/sh", "-c tty");

        Assert.True(signal.Wait(TimeSpan.FromSeconds(10)),
            $"expected a /dev/pts/N path from `tty`; got: {Snapshot(output)}");
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output)
        {
            return output.ToString();
        }
    }
}
