using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Printing;
using Jalium.UI.Notifications;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This smoke test must run on Linux.");
    return 2;
}

if (!FileDialog.IsPortalAvailable)
{
    Console.Error.WriteLine("org.freedesktop.portal.FileChooser is unavailable.");
    return 3;
}

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
var dialog = new OpenFileDialog
{
    Title = "Jalium.UI portal smoke (automatically cancelled)",
    Filter = "Text files|*.txt;*.md|All files|*",
    InitialDirectory = "/tmp",
    CheckFileExists = false,
    PortalTimeout = TimeSpan.FromSeconds(8),
    CancellationToken = cancellation.Token
};

var stopwatch = Stopwatch.StartNew();
var result = dialog.ShowDialog(IntPtr.Zero);
stopwatch.Stop();

if (result != false || stopwatch.Elapsed > TimeSpan.FromSeconds(6))
{
    Console.Error.WriteLine(
        $"Unexpected portal cancellation result={result}, elapsed={stopwatch.Elapsed}.");
    return 4;
}

var notificationManager = SystemNotificationManager.Current;
var notificationsSupported = notificationManager.IsSupported;
if (Environment.GetEnvironmentVariable("JALIUM_EXPECT_NO_NOTIFICATION_DAEMON") == "1")
{
    if (notificationsSupported)
    {
        Console.Error.WriteLine("libnotify incorrectly reported support without a notification daemon.");
        return 5;
    }

    try
    {
        notificationManager.Initialize("org.jalium.portal-smoke", "Jalium portal smoke");
        Console.Error.WriteLine("Notification initialization unexpectedly succeeded without a daemon.");
        return 6;
    }
    catch (InvalidOperationException ex) when (
        ex.Message.Contains("daemon", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("D-Bus", StringComparison.OrdinalIgnoreCase))
    {
        // Expected, and importantly the failure explains the missing service.
    }
}

Console.WriteLine(
    $"FileChooser request/cancel passed in {stopwatch.Elapsed.TotalMilliseconds:F0} ms; " +
    $"Print portal available={PrintDialog.IsPortalPrintAvailable}; " +
    $"notifications supported={notificationsSupported}.");
return 0;
