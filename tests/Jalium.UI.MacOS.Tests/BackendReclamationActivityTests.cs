using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class BackendReclamationActivityTests
{
    private static void Stamp(Window window, string field, long value) =>
        typeof(Window).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    [Theory]
    [InlineData(1, 100, -1, false)]
    [InlineData(1, 249, -1, false)]
    [InlineData(1, 250, -1, true)]
    [InlineData(1, 5000, 1, false)]
    [InlineData(2, 5000, 1, true)]
    [InlineData(0, 5000, -1, false)]
    public void OnlyANewIdlePresentationEpochAllowsBackendCleanup(long frames, long age, long reclaimed, bool expected)
    {
        var window = new Window();
        Stamp(window, "_successfulPresentCount", frames);
        Stamp(window, "_lastSuccessfulPresentTickMs", 10000 - age);
        Stamp(window, "_lastBackendReclaimedPresentCount", reclaimed);
        Assert.Equal(expected, window.BackendResourcesMayBeReclaimed(10000, 250));
        window.Close();
    }

    [Fact]
    public void NewPresentationsProtectBothFullAndPartialFramesIndependentlyPerWindow()
    {
        var active = new Window(); var idle = new Window();
        foreach (var window in new[] { active, idle })
        {
            Stamp(window, "_successfulPresentCount", 1);
            Stamp(window, "_lastSuccessfulPresentTickMs", 1000);
        }
        typeof(Window).GetMethod("RecordSuccessfulPresent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(active, null);
        long now = Environment.TickCount64;
        Assert.False(active.BackendResourcesMayBeReclaimed(now, 250));
        Assert.True(idle.BackendResourcesMayBeReclaimed(now, 250));
        active.Close(); idle.Close();
    }
}
