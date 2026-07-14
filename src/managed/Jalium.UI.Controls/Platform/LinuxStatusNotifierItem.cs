using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Jalium.UI.Media;
using Jalium.UI.Notifications;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Exports a <see cref="NotifyIcon"/> over the freedesktop/KDE
/// StatusNotifierItem protocol.  Both interface names are exposed because the
/// published freedesktop specification uses <c>org.freedesktop</c>, while the
/// deployed KDE/AppIndicator ecosystem still commonly requests <c>org.kde</c>.
/// </summary>
internal sealed class LinuxStatusNotifierItem : IDisposable
{
    internal const string ObjectPath = "/StatusNotifierItem";
    internal const string MenuObjectPath = "/NO_DBUSMENU";
    internal const string FreedesktopInterface = "org.freedesktop.StatusNotifierItem";
    internal const string KdeInterface = "org.kde.StatusNotifierItem";
    internal static bool ItemIsMenu => false;

    internal const string IntrospectionXml = """
        <node>
          <interface name="org.freedesktop.StatusNotifierItem">
            <property name="Category" type="s" access="read"/>
            <property name="Id" type="s" access="read"/>
            <property name="Title" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="WindowId" type="u" access="read"/>
            <property name="IconName" type="s" access="read"/>
            <property name="IconPixmap" type="a(iiay)" access="read"/>
            <property name="OverlayIconName" type="s" access="read"/>
            <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionIconName" type="s" access="read"/>
            <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionMovieName" type="s" access="read"/>
            <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
            <property name="ItemIsMenu" type="b" access="read"/>
            <property name="Menu" type="o" access="read"/>
            <property name="IconThemePath" type="s" access="read"/>
            <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
            <signal name="NewTitle"/><signal name="NewIcon"/><signal name="NewAttentionIcon"/>
            <signal name="NewOverlayIcon"/><signal name="NewToolTip"/>
            <signal name="NewStatus"><arg name="status" type="s"/></signal>
          </interface>
          <interface name="org.kde.StatusNotifierItem">
            <property name="Category" type="s" access="read"/>
            <property name="Id" type="s" access="read"/>
            <property name="Title" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="WindowId" type="u" access="read"/>
            <property name="IconName" type="s" access="read"/>
            <property name="IconPixmap" type="a(iiay)" access="read"/>
            <property name="OverlayIconName" type="s" access="read"/>
            <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionIconName" type="s" access="read"/>
            <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionMovieName" type="s" access="read"/>
            <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
            <property name="ItemIsMenu" type="b" access="read"/>
            <property name="Menu" type="o" access="read"/>
            <property name="IconThemePath" type="s" access="read"/>
            <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
            <signal name="NewTitle"/><signal name="NewIcon"/><signal name="NewAttentionIcon"/>
            <signal name="NewOverlayIcon"/><signal name="NewToolTip"/>
            <signal name="NewStatus"><arg name="status" type="s"/></signal>
          </interface>
        </node>
        """;

    private static int s_nextId;
    private static readonly ConcurrentDictionary<nint, LinuxStatusNotifierItem> s_balloons = new();
    private static readonly LinuxStatusNotifierNative.NotifyActionCallback s_balloonAction = OnBalloonAction;
    private static readonly LinuxStatusNotifierNative.NotifyClosedCallback s_balloonClosed = OnBalloonClosed;

    private readonly NotifyIcon _owner;
    private readonly object _gate = new();
    private readonly string _id;
    private readonly string _freedesktopBusName;
    private readonly string _kdeBusName;
    private nint _connection;
    private nint _nodeInfo;
    private uint _freedesktopRegistration;
    private uint _kdeRegistration;
    private GCHandle _selfHandle;
    private Thread? _pump;
    private volatile bool _disposed;
    private string _title;
    private string _toolTip;
    private string _status = "Active";
    private Pixmap? _icon;
    private nint _balloon;

    private LinuxStatusNotifierItem(NotifyIcon owner)
    {
        _owner = owner;
        int instance = Interlocked.Increment(ref s_nextId);
        _id = $"jalium-ui-{Environment.ProcessId}-{instance}";
        _freedesktopBusName = $"org.freedesktop.StatusNotifierItem-{Environment.ProcessId}-{instance}";
        _kdeBusName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-{instance}";
        _title = GetTitle(owner.Text);
        _toolTip = owner.Text ?? string.Empty;
        _icon = CapturePixmap(owner.Icon);
    }

    internal static bool TryCreate(NotifyIcon owner, out LinuxStatusNotifierItem? item)
    {
        item = null;
        if (!OperatingSystem.IsLinux() || !LinuxStatusNotifierNative.IsAvailable)
            return false;

        var candidate = new LinuxStatusNotifierItem(owner);
        if (!candidate.TryInitialize())
        {
            candidate.Dispose();
            return false;
        }

        item = candidate;
        return true;
    }

    internal void UpdateTitle(string? text)
    {
        lock (_gate)
        {
            _title = GetTitle(text);
            _toolTip = text ?? string.Empty;
        }

        Emit("NewTitle");
        Emit("NewToolTip");
    }

    internal void UpdateIcon(ImageSource? source)
    {
        lock (_gate)
            _icon = CapturePixmap(source);
        Emit("NewIcon");
        Emit("NewToolTip");
    }

    internal void ShowBalloon(int timeout, string title, string text, BalloonTipIcon icon)
    {
        if (_disposed || !LinuxStatusNotifierNative.TryInitializeNotifications(GetTitle(title)))
            return;

        CloseBalloon(raiseClosed: false);
        nint notification = LinuxStatusNotifierNative.CreateNotification(
            string.IsNullOrWhiteSpace(title) ? _title : title,
            text ?? string.Empty,
            icon switch
            {
                BalloonTipIcon.Error => "dialog-error",
                BalloonTipIcon.Warning => "dialog-warning",
                BalloonTipIcon.Info => "dialog-information",
                _ => string.Empty,
            });
        if (notification == 0)
            return;

        LinuxStatusNotifierNative.SetNotificationTimeout(notification, Math.Max(0, timeout));
        LinuxStatusNotifierNative.SetNotificationUrgency(notification, icon == BalloonTipIcon.Error ? 2 : 1);
        LinuxStatusNotifierNative.AddNotificationAction(notification, s_balloonAction);
        LinuxStatusNotifierNative.ConnectNotificationClosed(notification, s_balloonClosed);
        if (!LinuxStatusNotifierNative.ShowNotification(notification, out _))
        {
            LinuxStatusNotifierNative.UnrefObject(notification);
            return;
        }

        lock (_gate)
            _balloon = notification;
        s_balloons[notification] = this;
    }

    internal string GetPropertyVariantKindForTesting(string propertyName) => propertyName switch
    {
        "WindowId" => "u",
        "IconPixmap" or "OverlayIconPixmap" or "AttentionIconPixmap" => "a(iiay)",
        "ToolTip" => "(sa(iiay)ss)",
        "ItemIsMenu" => "b",
        "Menu" => "o",
        _ => "s",
    };

    internal static byte[] ConvertBgraToNetworkArgb(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length % 4 != 0)
            throw new ArgumentException("BGRA data must contain complete 32-bit pixels.", nameof(bgra));

        var result = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            result[i] = bgra[i + 3];
            result[i + 1] = bgra[i + 2];
            result[i + 2] = bgra[i + 1];
            result[i + 3] = bgra[i];
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        CloseBalloon(raiseClosed: false);
        _pump?.Join(TimeSpan.FromMilliseconds(250));
        _pump = null;

        LinuxStatusNotifierNative.UnregisterObject(_connection, _freedesktopRegistration);
        LinuxStatusNotifierNative.UnregisterObject(_connection, _kdeRegistration);
        _freedesktopRegistration = 0;
        _kdeRegistration = 0;
        LinuxStatusNotifierNative.ReleaseName(_connection, _freedesktopBusName);
        LinuxStatusNotifierNative.ReleaseName(_connection, _kdeBusName);
        LinuxStatusNotifierNative.Flush(_connection);

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        LinuxStatusNotifierNative.UnrefNodeInfo(ref _nodeInfo);
        LinuxStatusNotifierNative.UnrefObject(ref _connection);
    }

    private bool TryInitialize()
    {
        _connection = LinuxStatusNotifierNative.OpenSessionBus(out _);
        if (_connection == 0)
            return false;

        bool freedesktopName = LinuxStatusNotifierNative.RequestName(_connection, _freedesktopBusName);
        bool kdeName = LinuxStatusNotifierNative.RequestName(_connection, _kdeBusName);
        if (!freedesktopName && !kdeName)
            return false;

        _nodeInfo = LinuxStatusNotifierNative.ParseNodeInfo(IntrospectionXml);
        if (_nodeInfo == 0)
            return false;

        _selfHandle = GCHandle.Alloc(this);
        nint userData = GCHandle.ToIntPtr(_selfHandle);
        nint freedesktop = LinuxStatusNotifierNative.LookupInterface(_nodeInfo, FreedesktopInterface);
        nint kde = LinuxStatusNotifierNative.LookupInterface(_nodeInfo, KdeInterface);
        if (freedesktopName && freedesktop != 0)
        {
            _freedesktopRegistration = LinuxStatusNotifierNative.RegisterObject(
                _connection, ObjectPath, freedesktop, userData);
        }
        if (kdeName && kde != 0)
        {
            _kdeRegistration = LinuxStatusNotifierNative.RegisterObject(
                _connection, ObjectPath, kde, userData);
        }
        if (_freedesktopRegistration == 0 && _kdeRegistration == 0)
            return false;

        if (_kdeRegistration != 0)
        {
            LinuxStatusNotifierNative.RegisterWithWatcher(
                _connection, "org.kde.StatusNotifierWatcher", "org.kde.StatusNotifierWatcher", _kdeBusName);
        }
        if (_freedesktopRegistration != 0)
        {
            LinuxStatusNotifierNative.RegisterWithWatcher(
                _connection, "org.freedesktop.StatusNotifierWatcher", "org.freedesktop.StatusNotifierWatcher", _freedesktopBusName);
        }

        _pump = new Thread(PumpMainContext)
        {
            IsBackground = true,
            Name = "Jalium.StatusNotifierItem",
        };
        _pump.Start();
        LinuxStatusNotifierNative.Flush(_connection);
        return true;
    }

    private void PumpMainContext()
    {
        while (!_disposed)
        {
            try
            {
                while (LinuxStatusNotifierNative.IterateMainContext())
                {
                }
            }
            catch (DllNotFoundException)
            {
                return;
            }
            Thread.Sleep(20);
        }
    }

    private void Emit(string signalName, string? status = null)
    {
        if (_disposed || _connection == 0)
            return;
        if (_freedesktopRegistration != 0)
            LinuxStatusNotifierNative.EmitSignal(_connection, FreedesktopInterface, signalName, status);
        if (_kdeRegistration != 0)
            LinuxStatusNotifierNative.EmitSignal(_connection, KdeInterface, signalName, status);
    }

    private nint GetProperty(string propertyName)
    {
        lock (_gate)
        {
            return propertyName switch
            {
                "Category" => LinuxStatusNotifierNative.NewString("ApplicationStatus"),
                "Id" => LinuxStatusNotifierNative.NewString(_id),
                "Title" => LinuxStatusNotifierNative.NewString(_title),
                "Status" => LinuxStatusNotifierNative.NewString(_status),
                "WindowId" => LinuxStatusNotifierNative.NewUInt32(0),
                "IconName" => LinuxStatusNotifierNative.NewString(_icon == null ? "application-x-executable" : string.Empty),
                "IconPixmap" => LinuxStatusNotifierNative.NewPixmaps(_icon),
                "OverlayIconName" or "AttentionIconName" or "AttentionMovieName" or "IconThemePath" =>
                    LinuxStatusNotifierNative.NewString(string.Empty),
                "OverlayIconPixmap" or "AttentionIconPixmap" => LinuxStatusNotifierNative.NewPixmaps(null),
                "ToolTip" => LinuxStatusNotifierNative.NewToolTip(_icon, _title, _toolTip),
                // ItemIsMenu does not mean "this item has a context menu". It
                // means the item is menu-only and hosts should prefer
                // ContextMenu over Activate even for the primary action. A
                // NotifyIcon still exposes its activation events, so reporting
                // true here makes left-click activation disappear in several
                // SNI hosts. /NO_DBUSMENU is the deployed Qt/KDE sentinel for
                // an application-rendered ContextMenu method.
                "ItemIsMenu" => LinuxStatusNotifierNative.NewBoolean(ItemIsMenu),
                "Menu" => LinuxStatusNotifierNative.NewObjectPath(MenuObjectPath),
                _ => 0,
            };
        }
    }

    private void HandleMethod(string methodName, nint parameters, nint invocation)
    {
        int x = LinuxStatusNotifierNative.GetInt32(parameters, 0);
        // Scroll's second argument is an orientation string; the activation and
        // context-menu methods use a second int32 coordinate.
        int y = methodName == "Scroll" ? 0 : LinuxStatusNotifierNative.GetInt32(parameters, 1);
        switch (methodName)
        {
            case "Activate":
            case "SecondaryActivate":
                _owner.RaiseActivationFromPlatform();
                break;
            case "ContextMenu":
                _owner.OpenContextMenuFromPlatform(x, y);
                break;
            case "Scroll":
                // NotifyIcon inherits UIElement.MouseWheel.  Preserve the SNI
                // delta so applications can respond to tray-host scrolling even
                // though the protocol's horizontal/vertical orientation has no
                // separate WPF-compatible event surface.
                _owner.RaiseScrollFromPlatform(x);
                break;
        }
        LinuxStatusNotifierNative.ReturnEmpty(invocation);
    }

    private void CloseBalloon(bool raiseClosed)
    {
        nint notification;
        lock (_gate)
        {
            notification = _balloon;
            _balloon = 0;
        }
        if (notification == 0)
            return;

        s_balloons.TryRemove(notification, out _);
        LinuxStatusNotifierNative.CloseNotification(notification);
        LinuxStatusNotifierNative.UnrefObject(notification);
        if (raiseClosed)
            _owner.RaiseBalloonClosedFromPlatform();
    }

    private static Pixmap? CapturePixmap(ImageSource? source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            return null;
        if (bitmap.PixelWidth > 512 || bitmap.PixelHeight > 512 || bitmap.Format.BitsPerPixel != 32)
            return null;

        try
        {
            int stride = checked(bitmap.PixelWidth * 4);
            var pixels = new byte[checked(stride * bitmap.PixelHeight)];
            bitmap.CopyPixels(pixels, stride, 0);
            string format = bitmap.Format.ToString();
            byte[] argb;
            if (format is "Bgra32" or "Pbgra32" or "Bgr32")
            {
                if (format == "Bgr32")
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                        pixels[i] = 255;
                }
                argb = ConvertBgraToNetworkArgb(pixels);
            }
            else if (format is "Rgba32" or "Rgb32")
            {
                argb = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    argb[i] = format == "Rgb32" ? (byte)255 : pixels[i + 3];
                    argb[i + 1] = pixels[i];
                    argb[i + 2] = pixels[i + 1];
                    argb[i + 3] = pixels[i + 2];
                }
            }
            else
            {
                return null;
            }
            return new Pixmap(bitmap.PixelWidth, bitmap.PixelHeight, argb);
        }
        catch
        {
            return null;
        }
    }

    private static string GetTitle(string? preferred) =>
        string.IsNullOrWhiteSpace(preferred) ? AppDomain.CurrentDomain.FriendlyName : preferred.Trim();

    private static LinuxStatusNotifierItem? FromUserData(nint userData)
    {
        if (userData == 0)
            return null;
        try { return GCHandle.FromIntPtr(userData).Target as LinuxStatusNotifierItem; }
        catch { return null; }
    }

    internal static void HandleNativeMethod(nint userData, string methodName, nint parameters, nint invocation)
    {
        var item = FromUserData(userData);
        if (item == null || item._disposed)
        {
            LinuxStatusNotifierNative.ReturnError(invocation, "The status notifier item is no longer available.");
            return;
        }
        item.HandleMethod(methodName, parameters, invocation);
    }

    internal static nint HandleNativeGetProperty(nint userData, string propertyName) =>
        FromUserData(userData)?.GetProperty(propertyName) ?? 0;

    private static void OnBalloonAction(nint notification, nint action, nint userData)
    {
        if (s_balloons.TryGetValue(notification, out var item))
            item._owner.RaiseBalloonClickedFromPlatform();
    }

    private static void OnBalloonClosed(nint notification, nint userData)
    {
        if (!s_balloons.TryRemove(notification, out var item))
            return;
        lock (item._gate)
        {
            if (item._balloon == notification)
                item._balloon = 0;
        }
        LinuxStatusNotifierNative.UnrefObject(notification);
        item._owner.RaiseBalloonClosedFromPlatform();
    }

    internal sealed record Pixmap(int Width, int Height, byte[] NetworkArgb);
}

internal static class LinuxStatusNotifierNative
{
    private const string Gio = "libgio-2.0.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Notify = "libnotify.so.4";
    private const int SessionBus = 2;

    private static readonly MethodCallCallback s_methodCall = OnMethodCall;
    private static readonly GetPropertyCallback s_getProperty = OnGetProperty;
    private static readonly SetPropertyCallback s_setProperty = OnSetProperty;
    private static readonly nint s_vtable = CreateVTable();
    private static readonly Lazy<bool> s_available = new(ProbeAvailability);

    internal static bool IsAvailable => s_available.Value;

    internal static nint OpenSessionBus(out string? errorMessage)
    {
        errorMessage = null;
        nint error = 0;
        try
        {
            nint connection = g_bus_get_sync(SessionBus, 0, out error);
            if (connection == 0)
                errorMessage = ReadError(error);
            else
                g_dbus_connection_set_exit_on_close(connection, false);
            return connection;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return 0;
        }
        finally
        {
            FreeError(ref error);
        }
    }

    internal static nint ParseNodeInfo(string xml)
    {
        nint error = 0;
        try { return g_dbus_node_info_new_for_xml(xml, out error); }
        finally { FreeError(ref error); }
    }

    internal static nint LookupInterface(nint nodeInfo, string interfaceName) =>
        g_dbus_node_info_lookup_interface(nodeInfo, interfaceName);

    internal static uint RegisterObject(nint connection, string path, nint interfaceInfo, nint userData)
    {
        nint error = 0;
        try
        {
            return g_dbus_connection_register_object(
                connection, path, interfaceInfo, s_vtable, userData, 0, out error);
        }
        finally { FreeError(ref error); }
    }

    internal static void UnregisterObject(nint connection, uint registration)
    {
        if (connection != 0 && registration != 0)
            _ = g_dbus_connection_unregister_object(connection, registration);
    }

    internal static bool RequestName(nint connection, string name)
    {
        nint parameters = NewTuple(NewString(name), NewUInt32(0));
        nint reply = Call(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus", "RequestName", parameters, 1500);
        UnrefVariant(ref parameters);
        if (reply == 0)
            return false;
        nint child = g_variant_get_child_value(reply, 0);
        uint result = child == 0 ? 0 : g_variant_get_uint32(child);
        UnrefVariant(ref child);
        UnrefVariant(ref reply);
        return result is 1 or 4;
    }

    internal static void ReleaseName(nint connection, string name)
    {
        if (connection == 0)
            return;
        nint parameters = NewTuple(NewString(name));
        nint reply = Call(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus", "ReleaseName", parameters, 500);
        UnrefVariant(ref parameters);
        UnrefVariant(ref reply);
    }

    internal static void RegisterWithWatcher(
        nint connection, string watcherName, string watcherInterface, string serviceName)
    {
        nint parameters = NewTuple(NewString(serviceName));
        nint reply = Call(connection, watcherName, "/StatusNotifierWatcher", watcherInterface,
            "RegisterStatusNotifierItem", parameters, 750);
        UnrefVariant(ref parameters);
        UnrefVariant(ref reply);
    }

    internal static void Flush(nint connection)
    {
        if (connection == 0)
            return;
        nint error = 0;
        try { _ = g_dbus_connection_flush_sync(connection, 0, out error); }
        finally { FreeError(ref error); }
    }

    internal static bool EmitSignal(nint connection, string interfaceName, string signal, string? status)
    {
        nint parameters = status == null ? NewTuple() : NewTuple(NewString(status));
        nint error = 0;
        try
        {
            return g_dbus_connection_emit_signal(connection, null, LinuxStatusNotifierItem.ObjectPath,
                interfaceName, signal, parameters, out error);
        }
        finally
        {
            FreeError(ref error);
            UnrefVariant(ref parameters);
        }
    }

    internal static void ReturnEmpty(nint invocation)
    {
        nint value = NewTuple();
        g_dbus_method_invocation_return_value(invocation, value);
        UnrefVariant(ref value);
    }

    internal static void ReturnError(nint invocation, string message) =>
        g_dbus_method_invocation_return_dbus_error(
            invocation, "org.freedesktop.DBus.Error.Failed", message);

    internal static nint NewString(string value) => Sink(g_variant_new_string(value ?? string.Empty));
    internal static nint NewUInt32(uint value) => Sink(g_variant_new_uint32(value));
    internal static nint NewBoolean(bool value) => Sink(g_variant_new_boolean(value));
    internal static nint NewObjectPath(string value) => Sink(g_variant_new_object_path(value));

    internal static nint NewPixmaps(LinuxStatusNotifierItem.Pixmap? pixmap)
    {
        nint type = g_variant_type_new("a(iiay)");
        nint builder = g_variant_builder_new(type);
        g_variant_type_free(type);
        if (pixmap != null)
        {
            nint bytes = NewByteArray(pixmap.NetworkArgb);
            nint tuple = NewTuple(Sink(g_variant_new_int32(pixmap.Width)),
                Sink(g_variant_new_int32(pixmap.Height)), bytes);
            g_variant_builder_add_value(builder, tuple);
            UnrefVariant(ref tuple);
        }
        nint result = Sink(g_variant_builder_end(builder));
        g_variant_builder_unref(builder);
        return result;
    }

    internal static nint NewToolTip(
        LinuxStatusNotifierItem.Pixmap? pixmap, string title, string description) =>
        NewTuple(NewString(string.Empty), NewPixmaps(pixmap), NewString(title), NewString(description));

    internal static int GetInt32(nint tuple, int index)
    {
        if (tuple == 0 || index < 0 || (nuint)index >= g_variant_n_children(tuple))
            return 0;
        nint child = g_variant_get_child_value(tuple, (nuint)index);
        try { return child == 0 ? 0 : g_variant_get_int32(child); }
        finally { UnrefVariant(ref child); }
    }

    internal static bool IterateMainContext() => g_main_context_iteration(0, false);

    internal static bool TryInitializeNotifications(string appName)
    {
        try
        {
            return notify_is_initted() || notify_init(appName);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    internal static nint CreateNotification(string title, string body, string icon) =>
        notify_notification_new(title, body, icon);

    internal static void SetNotificationTimeout(nint notification, int timeout) =>
        notify_notification_set_timeout(notification, timeout);

    internal static void SetNotificationUrgency(nint notification, int urgency) =>
        notify_notification_set_urgency(notification, urgency);

    internal static void AddNotificationAction(nint notification, NotifyActionCallback callback) =>
        notify_notification_add_action(notification, "default", "Open", callback, 0, 0);

    internal static void ConnectNotificationClosed(nint notification, NotifyClosedCallback callback) =>
        _ = g_signal_connect_data(notification, "closed", Marshal.GetFunctionPointerForDelegate(callback), 0, 0, 0);

    internal static bool ShowNotification(nint notification, out string? errorMessage)
    {
        nint error = 0;
        try
        {
            bool result = notify_notification_show(notification, ref error);
            errorMessage = result ? null : ReadError(error);
            return result;
        }
        finally { FreeError(ref error); }
    }

    internal static void CloseNotification(nint notification)
    {
        if (notification == 0)
            return;
        nint error = 0;
        try { _ = notify_notification_close(notification, ref error); }
        finally { FreeError(ref error); }
    }

    internal static void UnrefNodeInfo(ref nint nodeInfo)
    {
        if (nodeInfo == 0)
            return;
        g_dbus_node_info_unref(nodeInfo);
        nodeInfo = 0;
    }

    internal static void UnrefObject(ref nint value)
    {
        if (value == 0)
            return;
        g_object_unref(value);
        value = 0;
    }

    internal static void UnrefObject(nint value)
    {
        if (value != 0)
            g_object_unref(value);
    }

    private static nint NewByteArray(byte[] bytes)
    {
        nint type = g_variant_type_new("y");
        try
        {
            unsafe
            {
                fixed (byte* pointer = bytes)
                    return Sink(g_variant_new_fixed_array(type, (nint)pointer, (nuint)bytes.Length, 1));
            }
        }
        finally { g_variant_type_free(type); }
    }

    private static nint NewTuple(params nint[] children)
    {
        nint tuple = Sink(g_variant_new_tuple(children, (nuint)children.Length));
        foreach (nint childValue in children)
        {
            nint child = childValue;
            UnrefVariant(ref child);
        }
        return tuple;
    }

    private static nint Sink(nint variant) => variant == 0 ? 0 : g_variant_ref_sink(variant);

    private static nint Call(
        nint connection, string busName, string objectPath, string interfaceName,
        string methodName, nint parameters, int timeout)
    {
        nint error = 0;
        try
        {
            return g_dbus_connection_call_sync(connection, busName, objectPath, interfaceName,
                methodName, parameters, 0, 0, timeout, 0, out error);
        }
        finally { FreeError(ref error); }
    }

    private static bool ProbeAvailability()
    {
        try
        {
            if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad(Gio, out nint handle))
                return false;
            NativeLibrary.Free(handle);
            return true;
        }
        catch { return false; }
    }

    private static nint CreateVTable()
    {
        if (!OperatingSystem.IsLinux())
            return 0;
        var table = new InterfaceVTable
        {
            MethodCall = Marshal.GetFunctionPointerForDelegate(s_methodCall),
            GetProperty = Marshal.GetFunctionPointerForDelegate(s_getProperty),
            SetProperty = Marshal.GetFunctionPointerForDelegate(s_setProperty),
        };
        nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<InterfaceVTable>());
        Marshal.StructureToPtr(table, memory, false);
        return memory;
    }

    private static void OnMethodCall(
        nint connection, nint sender, nint objectPath, nint interfaceName, nint methodName,
        nint parameters, nint invocation, nint userData)
    {
        try
        {
            LinuxStatusNotifierItem.HandleNativeMethod(userData,
                Marshal.PtrToStringUTF8(methodName) ?? string.Empty, parameters, invocation);
        }
        catch (Exception ex)
        {
            ReturnError(invocation, ex.Message);
        }
    }

    private static nint OnGetProperty(
        nint connection, nint sender, nint objectPath, nint interfaceName,
        nint propertyName, nint error, nint userData)
    {
        try
        {
            return LinuxStatusNotifierItem.HandleNativeGetProperty(
                userData, Marshal.PtrToStringUTF8(propertyName) ?? string.Empty);
        }
        catch { return 0; }
    }

    private static bool OnSetProperty(
        nint connection, nint sender, nint objectPath, nint interfaceName,
        nint propertyName, nint value, nint error, nint userData) => false;

    private static string ReadError(nint error)
    {
        if (error == 0)
            return "unknown GLib error";
        nint message = Marshal.ReadIntPtr(error, 8);
        return message == 0 ? "unknown GLib error" : Marshal.PtrToStringUTF8(message) ?? "unknown GLib error";
    }

    private static void FreeError(ref nint error)
    {
        if (error == 0)
            return;
        g_error_free(error);
        error = 0;
    }

    private static void UnrefVariant(ref nint variant)
    {
        if (variant == 0)
            return;
        g_variant_unref(variant);
        variant = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceVTable
    {
        internal nint MethodCall;
        internal nint GetProperty;
        internal nint SetProperty;
        internal nint Padding0;
        internal nint Padding1;
        internal nint Padding2;
        internal nint Padding3;
        internal nint Padding4;
        internal nint Padding5;
        internal nint Padding6;
        internal nint Padding7;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MethodCallCallback(
        nint connection, nint sender, nint objectPath, nint interfaceName, nint methodName,
        nint parameters, nint invocation, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetPropertyCallback(
        nint connection, nint sender, nint objectPath, nint interfaceName,
        nint propertyName, nint error, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool SetPropertyCallback(
        nint connection, nint sender, nint objectPath, nint interfaceName,
        nint propertyName, nint value, nint error, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void NotifyActionCallback(nint notification, nint action, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void NotifyClosedCallback(nint notification, nint userData);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_bus_get_sync(int busType, nint cancellable, out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_dbus_connection_set_exit_on_close(
        nint connection, [MarshalAs(UnmanagedType.Bool)] bool exitOnClose);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_connection_call_sync(
        nint connection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string busName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string methodName,
        nint parameters, nint replyType, int flags, int timeoutMilliseconds,
        nint cancellable, out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_node_info_new_for_xml(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string xml, out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_dbus_node_info_lookup_interface(
        nint nodeInfo, [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint g_dbus_connection_register_object(
        nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
        nint interfaceInfo, nint vtable, nint userData, nint userDataFreeFunction,
        out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_dbus_connection_unregister_object(nint connection, uint registrationId);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_dbus_connection_flush_sync(nint connection, nint cancellable, out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_dbus_method_invocation_return_value(nint invocation, nint parameters);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_dbus_method_invocation_return_dbus_error(
        nint invocation,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string errorName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string errorMessage);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_dbus_connection_emit_signal(
        nint connection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? destinationBusName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signalName,
        nint parameters, out nint error);

    [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_dbus_node_info_unref(nint nodeInfo);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_new_uint32(uint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_new_int32(int value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_new_boolean([MarshalAs(UnmanagedType.Bool)] bool value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_new_object_path([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_new_tuple([In] nint[] children, nuint length);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_new_fixed_array(
        nint elementType, nint elements, nuint elementCount, nuint elementSize);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_ref_sink(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_type_new([MarshalAs(UnmanagedType.LPUTF8Str)] string typeString);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_variant_type_free(nint type);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_builder_new(nint type);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_variant_builder_add_value(nint builder, nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_builder_end(nint builder);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_variant_builder_unref(nint builder);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint g_variant_get_child_value(nint value, nuint index);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint g_variant_n_children(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern uint g_variant_get_uint32(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int g_variant_get_int32(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_variant_unref(nint value);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool g_main_context_iteration(
        nint context, [MarshalAs(UnmanagedType.Bool)] bool mayBlock);

    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_error_free(nint error);

    [DllImport(GObject, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_object_unref(nint value);

    [DllImport(GObject, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint g_signal_connect_data(
        nint instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        nint callback, nint data, nint destroyData, int connectFlags);

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool notify_init([MarshalAs(UnmanagedType.LPUTF8Str)] string appName);

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool notify_is_initted();

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint notify_notification_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string summary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string body,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string icon);

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool notify_notification_show(nint notification, ref nint error);

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool notify_notification_close(nint notification, ref nint error);

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    private static extern void notify_notification_set_urgency(nint notification, int urgency);

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    private static extern void notify_notification_set_timeout(nint notification, int timeout);

    [DllImport(Notify, CallingConvention = CallingConvention.Cdecl)]
    private static extern void notify_notification_add_action(
        nint notification,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string action,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        NotifyActionCallback callback, nint userData, nint freeFunction);
}
