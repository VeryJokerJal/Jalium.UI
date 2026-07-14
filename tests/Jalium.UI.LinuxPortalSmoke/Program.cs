using System.Diagnostics;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Notifications;
using Jalium.UI.Threading;
using Microsoft.Win32;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using FileDialog = Microsoft.Win32.FileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

if (args is ["--print-fd-smoke"])
    return await RunPrintFileDescriptorSmoke();
if (args is ["--logind-smoke"])
    return RunLogindSessionEndingSmoke();
if (args is ["--xsmp-smoke"])
    return RunXsmpSessionEndingSmoke();
if (args is ["--status-notifier-smoke"])
    return RunStatusNotifierSmoke();

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

static async Task<int> RunPrintFileDescriptorSmoke()
{
    if (!OperatingSystem.IsLinux())
    {
        Console.Error.WriteLine("The print portal FD smoke must run on Linux.");
        return 20;
    }

    Assembly controls = typeof(PrintDialog).Assembly;
    Type portal = controls.GetType(
        "Jalium.UI.Controls.Platform.LinuxDesktopPortal", throwOnError: true)!;
    MethodInfo prepare = portal.GetMethod(
        "PreparePrint", BindingFlags.Static | BindingFlags.NonPublic)!;
    MethodInfo submit = portal.GetMethod(
        "SubmitPrint", BindingFlags.Static | BindingFlags.NonPublic)!;

    object preparation = prepare.Invoke(
        null,
        [nint.Zero, "Jalium fake portal print", TimeSpan.FromSeconds(5), CancellationToken.None])!;
    bool success = (bool)preparation.GetType().GetProperty(
        "IsSuccess", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preparation)!;
    uint token = (uint)preparation.GetType().GetProperty("Token")!.GetValue(preparation)!;
    if (!success || token != 777)
    {
        Console.Error.WriteLine($"PreparePrint failed or returned the wrong token: {preparation}");
        return 21;
    }

    string pdfPath = Path.Combine(
        Path.GetTempPath(), $"jalium-portal-fd-{Environment.ProcessId}.pdf");
    try
    {
        await File.WriteAllBytesAsync(pdfPath, "%PDF-1.4\n%%EOF\n"u8.ToArray());
        using var pdf = new FileStream(
            pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        object response = submit.Invoke(
            null,
            [nint.Zero, "Jalium fake portal print",
             pdf.SafeFileHandle.DangerousGetHandle().ToInt32(), token,
             TimeSpan.FromSeconds(5), CancellationToken.None])!;
        string status = response.GetType().GetProperty("Status")!
            .GetValue(response)!.ToString()!;
        if (!string.Equals(status, "Success", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Print submission failed: " + response);
            return 22;
        }
    }
    finally
    {
        try { File.Delete(pdfPath); }
        catch { }
    }

    Console.WriteLine("PRINT_PORTAL_CLIENT_OK");
    return 0;
}

static int RunLogindSessionEndingSmoke()
{
    if (!OperatingSystem.IsLinux())
    {
        Console.Error.WriteLine("The logind smoke must run on Linux.");
        return 30;
    }

    Type monitorType = typeof(Application).Assembly.GetType(
        "Jalium.UI.Controls.Platform.LinuxSessionMonitor", throwOnError: true)!;
    MethodInfo factory = monitorType.GetMethod(
        "TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
    using var callbackReached = new ManualResetEventSlim();
    ReasonSessionEnding? receivedReason = null;
    var callback = new Func<ReasonSessionEnding, bool>(reason =>
    {
        receivedReason = reason;
        callbackReached.Set();
        return true;
    });

    // Optional parameters still occupy a slot when invoking through reflection.
    using var monitor = (IDisposable?)factory.Invoke(null, [callback, null]);
    if (monitor == null)
    {
        Console.Error.WriteLine("LinuxSessionMonitor could not connect to fake logind.");
        return 31;
    }
    if (!callbackReached.Wait(TimeSpan.FromSeconds(5)) ||
        receivedReason != ReasonSessionEnding.Shutdown)
    {
        Console.Error.WriteLine("PrepareForShutdown did not reach SessionEnding.");
        return 32;
    }

    Console.WriteLine("LOGIND_SESSION_ENDING_CLIENT_OK");
    return 0;
}

static int RunXsmpSessionEndingSmoke()
{
    if (!OperatingSystem.IsLinux())
    {
        Console.Error.WriteLine("The XSMP smoke must run on Linux.");
        return 40;
    }

    Type monitorType = typeof(Application).Assembly.GetType(
        "Jalium.UI.Controls.Platform.LinuxXsmpSessionMonitor", throwOnError: true)!;
    MethodInfo factory = monitorType.GetMethod(
        "TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
    using var endingReached = new ManualResetEventSlim();
    using var cancellationReached = new ManualResetEventSlim();
    ReasonSessionEnding? receivedReason = null;
    var callback = new Func<ReasonSessionEnding, bool>(reason =>
    {
        receivedReason = reason;
        endingReached.Set();
        return false;
    });
    var cancelled = new Action(cancellationReached.Set);
    var died = new Action(() => { });

    using var monitor = (IDisposable?)factory.Invoke(
        null, [callback, cancelled, died]);
    if (monitor == null)
    {
        Console.Error.WriteLine(
            "LinuxXsmpSessionMonitor could not connect to the fake session manager.");
        return 41;
    }
    if (!endingReached.Wait(TimeSpan.FromSeconds(5)) ||
        receivedReason != ReasonSessionEnding.Logoff)
    {
        Console.Error.WriteLine("XSMP SaveYourself did not raise Logoff SessionEnding.");
        return 42;
    }
    if (!cancellationReached.Wait(TimeSpan.FromSeconds(5)))
    {
        Console.Error.WriteLine("XSMP cancellation interaction was not acknowledged.");
        return 43;
    }

    Console.WriteLine("XSMP_SESSION_ENDING_CLIENT_OK");
    return 0;
}

static int RunStatusNotifierSmoke()
{
    if (!OperatingSystem.IsLinux())
    {
        Console.Error.WriteLine("The StatusNotifierItem smoke must run on Linux.");
        return 50;
    }

    int clicks = 0;
    int doubleClicks = 0;
    int contextMenus = 0;
    int wheelEvents = 0;
    int wheelDelta = 0;
    int balloonClicks = 0;
    int balloonCloses = 0;

    var contextMenu = new ContextMenu { StaysOpen = true };
    contextMenu.Items.Add(new MenuItem { Header = "Jalium smoke item" });
    contextMenu.Opened += (_, _) => contextMenus++;

    BitmapSource icon = BitmapSource.Create(
        2, 1, 96, 96, PixelFormats.Bgra32, null,
        new byte[]
        {
            0x10, 0x20, 0x30, 0xFF,
            0x40, 0x50, 0x60, 0x80,
        },
        8);

    using var notifyIcon = new NotifyIcon
    {
        Text = "Jalium SNI smoke",
        Icon = icon,
        ContextMenu = contextMenu,
    };
    notifyIcon.Click += (_, _) => clicks++;
    notifyIcon.DoubleClick += (_, _) => doubleClicks++;
    notifyIcon.MouseWheel += (_, e) =>
    {
        wheelEvents++;
        wheelDelta = e.Delta;
    };
    notifyIcon.BalloonTipClicked += (_, _) => balloonClicks++;
    notifyIcon.BalloonTipClosed += (_, _) => balloonCloses++;

    notifyIcon.Visible = true;
    notifyIcon.ShowBalloonTip(
        5000, "Jalium notification smoke", "fake notification action", BalloonTipIcon.Info);

    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    var deadline = Stopwatch.StartNew();
    while (deadline.Elapsed < TimeSpan.FromSeconds(12))
    {
        dispatcher.ProcessQueue();
        if (clicks >= 2 && doubleClicks >= 1 && contextMenus >= 1 &&
            wheelEvents >= 1 && wheelDelta == 120 &&
            balloonClicks >= 1 && balloonCloses >= 1)
        {
            contextMenu.Close();
            Console.WriteLine(
                $"STATUS_NOTIFIER_CLIENT_OK clicks={clicks} doubleClicks={doubleClicks} " +
                $"contextMenus={contextMenus} wheelDelta={wheelDelta} " +
                $"balloonClicks={balloonClicks} balloonCloses={balloonCloses}");
            return 0;
        }
        Thread.Sleep(10);
    }

    contextMenu.Close();
    Console.Error.WriteLine(
        $"StatusNotifier callbacks incomplete: clicks={clicks}, doubleClicks={doubleClicks}, " +
        $"contextMenus={contextMenus}, wheelEvents={wheelEvents}, wheelDelta={wheelDelta}, " +
        $"balloonClicks={balloonClicks}, balloonCloses={balloonCloses}.");
    return 51;
}
