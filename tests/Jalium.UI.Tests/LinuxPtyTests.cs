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
    public async Task UnixPty_RunsShellCommand_CapturesOutputAndExitCode()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var process = new TerminalProcess();
        var output = new StringBuilder();
        var outputSignal = NewSignal();
        var exitSignal = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputReceived += chunk =>
        {
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains("hello-from-pty", StringComparison.Ordinal))
                    outputSignal.TrySetResult(true);
            }
        };
        process.ProcessExited += code =>
        {
            exitSignal.TrySetResult(code);
        };

        process.Start("/bin/sh", "-c \"echo hello-from-pty\"");

        Assert.True(await WaitForSignalAsync(outputSignal.Task),
            $"pty output did not arrive; got: {Snapshot(output)}");
        Assert.True(await WaitForSignalAsync(exitSignal.Task),
            "shell did not exit after completing its command");
        Assert.Equal(0, await exitSignal.Task);
    }

    [Fact]
    public async Task UnixPty_InteractiveShell_EchoesInputAndExits()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var process = new TerminalProcess();
        var output = new StringBuilder();
        var markerSignal = NewSignal();
        var exitSignal = NewSignal();

        process.OutputReceived += chunk =>
        {
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains("interactive-42", StringComparison.Ordinal))
                    markerSignal.TrySetResult(true);
            }
        };
        process.ProcessExited += _ => exitSignal.TrySetResult(true);

        process.Start("/bin/sh", workingDirectory: "/tmp");
        Assert.True(process.IsRunning);

        // Resize must not throw while the shell is live (TIOCSWINSZ path).
        process.NotifyResize(100, 30);

        process.WriteInput("echo interactive-$((6*7))\n");
        Assert.True(await WaitForSignalAsync(markerSignal.Task),
            $"interactive marker missing; got: {Snapshot(output)}");

        process.WriteInput("exit\n");
        Assert.True(await WaitForSignalAsync(exitSignal.Task),
            "shell did not exit after 'exit'");
    }

    [Fact]
    public async Task UnixPty_SpawnsWithControllingTerminal()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var process = new TerminalProcess();
        var output = new StringBuilder();
        var signal = NewSignal();

        process.OutputReceived += chunk =>
        {
            lock (output)
            {
                output.Append(chunk);
                // `tty` prints the pty slave path only when stdin is a real
                // terminal; "not a tty" would mean the controlling-terminal
                // wiring (setsid + first-open) regressed.
                if (output.ToString().Contains("/dev/pts/", StringComparison.Ordinal))
                    signal.TrySetResult(true);
            }
        };

        process.Start("/bin/sh", "-c tty");

        Assert.True(await WaitForSignalAsync(signal.Task),
            $"expected a /dev/pts/N path from `tty`; got: {Snapshot(output)}");
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<bool> WaitForSignalAsync(Task signal)
    {
        var completed = await Task.WhenAny(signal, Task.Delay(TimeSpan.FromSeconds(10)));
        if (!ReferenceEquals(completed, signal))
            return false;

        await signal;
        return true;
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output)
        {
            return output.ToString();
        }
    }
}
