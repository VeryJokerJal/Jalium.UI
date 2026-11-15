using System.Text;
using Jalium.UI.Controls.TerminalEmulator;

namespace Jalium.UI.Tests;

public sealed class MacOSPtyProcessTests
{
    [Theory]
    [InlineData(80, 24)]
    [InlineData(117, 43)]
    public async Task TheFirstChildCommandSeesItsInitialDimensions(int columns, int rows)
    {
        using var session = Create("-c \"stty size\"", columns: columns, rows: rows);
        var output = await ReadToEnd(session).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal($"{rows} {columns}", output.Trim());
        await WaitForExit(session);
        Assert.Equal(0, session.GetExitCode());
    }

    [Fact]
    public async Task ShellExecutesWithoutAResumeSignalAndPreservesItsExitCode()
    {
        using var session = Create("-c \"printf __one_pty_started__; exit 7\"");
        var output = await ReadToEnd(session).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Contains("__one_pty_started__", output);
        await WaitForExit(session);
        Assert.Equal(7, session.GetExitCode());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InteractiveShellUsesItsPrivateDirectoryAndTerminalSize(bool resize)
    {
        var root = Path.Combine(PhysicalTempDirectory(), "one-pty-中文🙂-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var session = Create(workingDirectory: root);
            var reading = ReadToEnd(session);
            if (resize) session.Resize(103, 37);
            session.Write(Encoding.UTF8.GetBytes("stty size; tty; pwd; printf '__one_pty_done__\\n'; exit\n"));
            var output = await reading.WaitAsync(TimeSpan.FromSeconds(4));
            var lines = output.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).ToArray();
            Assert.Contains(resize ? "37 103" : "24 80", lines);
            Assert.Contains(Path.GetFullPath(root), lines);
            Assert.Contains(lines, line => line.StartsWith(OperatingSystem.IsMacOS() ? "/dev/tty" : "/dev/pts/", StringComparison.Ordinal));
            Assert.Contains("__one_pty_done__", lines);
            await WaitForExit(session);
            Assert.Equal(0, session.GetExitCode());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DisposingALiveShellReapsItAndIsIdempotent()
    {
        using var session = Create();
        Assert.False(session.HasExited);
        session.Dispose();
        Assert.True(session.HasExited);
        Assert.InRange(session.GetExitCode(), 128, 255);
        session.Dispose();
        Assert.True(session.HasExited);
    }

    private static string PhysicalTempDirectory()
    {
        var temp = Path.GetTempPath();
        // Shell pwd and NuGet directory walking return physical macOS paths.
        // Keep fixture identities physical without weakening their assertions.
        if (OperatingSystem.IsMacOS())
        {
            foreach (var alias in new[] { "/var", "/tmp" })
                if (temp.StartsWith(alias + "/", StringComparison.Ordinal)
                    && new DirectoryInfo(alias).ResolveLinkTarget(true)?.FullName == "/private" + alias)
                    return "/private" + temp;
        }
        return temp;
    }

    private static UnixPtySession Create(string? arguments = null, string? workingDirectory = null, int columns = 80, int rows = 24) =>
        Assert.IsType<UnixPtySession>(UnixPty.Create("/bin/sh", arguments, workingDirectory ?? Path.GetTempPath(),
            new Dictionary<string, string> { ["LANG"] = "en_US.UTF-8", ["LC_ALL"] = "en_US.UTF-8" }, columns, rows));

    private static Task<string> ReadToEnd(UnixPtySession session) => Task.Factory.StartNew(() =>
    {
        using var bytes = new MemoryStream();
        while (session.Read() is { } chunk) bytes.Write(chunk);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static async Task WaitForExit(UnixPtySession session)
    {
        var deadline = Environment.TickCount64 + 4000;
        while (!session.HasExited)
        {
            if (Environment.TickCount64 >= deadline) throw new TimeoutException("The owned shell did not exit within four seconds.");
            await Task.Delay(10);
        }
    }
}
