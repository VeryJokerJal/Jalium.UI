using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Jalium.UI.Controls.Platform;

internal enum LinuxPortalResponseStatus
{
    Success,
    Cancelled,
    Failed,
    TimedOut,
    Unavailable
}

internal sealed record LinuxPortalResponse(
    LinuxPortalResponseStatus Status,
    IReadOnlyList<string> Values,
    string? Error = null)
{
    internal static LinuxPortalResponse Unavailable(string error) =>
        new(LinuxPortalResponseStatus.Unavailable, Array.Empty<string>(), error);

    internal static LinuxPortalResponse Failed(string error) =>
        new(LinuxPortalResponseStatus.Failed, Array.Empty<string>(), error);
}

internal sealed record LinuxPortalFileChooserOptions(
    string Title,
    bool Save,
    bool Multiple,
    bool Directory,
    string? CurrentFolder,
    string? CurrentName,
    IReadOnlyList<(string Name, string Pattern)> Filters,
    int FilterIndex = 1,
    TimeSpan? Timeout = null);

/// <summary>
/// Desktop-neutral Linux integration through xdg-desktop-portal.
/// </summary>
/// <remarks>
/// Portal requests are tied to the caller's D-Bus connection. Consequently a
/// one-shot <c>gdbus call</c> process cannot correctly wait for the asynchronous
/// Request.Response signal: its connection disappears as soon as the method
/// returns. This client uses the same libgio library that powers gdbus and keeps
/// one connection alive for the call, signal subscription, cancellation, and
/// response. The signal is subscribed before the call using the predictable
/// request path derived from the unique bus name and handle token, avoiding the
/// fast-response race described by the portal API.
/// </remarks>
internal static partial class LinuxDesktopPortal
{
    private const string PortalDestination = "org.freedesktop.portal.Desktop";
    private const string PortalObjectPath = "/org/freedesktop/portal/desktop";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(2);

    internal static bool IsInterfaceAvailable(string interfaceName)
    {
        if (!OperatingSystem.IsLinux() ||
            string.IsNullOrWhiteSpace(interfaceName) ||
            !GioNative.IsAvailable)
        {
            return false;
        }

        nint connection = 0;
        nint parameters = 0;
        nint reply = 0;
        nint error = 0;
        try
        {
            connection = GioNative.OpenSessionBus(out error);
            if (connection == 0)
                return false;

            parameters = GioNative.ParseVariant(
                $"('{EscapeGVariantString(interfaceName)}', 'version')", out error);
            if (parameters == 0)
                return false;

            reply = GioNative.CallSync(
                connection,
                PortalDestination,
                PortalObjectPath,
                "org.freedesktop.DBus.Properties",
                "Get",
                parameters,
                timeoutMilliseconds: 3000,
                out error);
            return reply != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            GioNative.FreeError(ref error);
            GioNative.UnrefVariant(ref reply);
            GioNative.UnrefVariant(ref parameters);
            GioNative.UnrefObject(ref connection);
        }
    }

    internal static LinuxPortalResponse ShowFileChooser(
        nint owner,
        LinuxPortalFileChooserOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!OperatingSystem.IsLinux())
            return LinuxPortalResponse.Unavailable("xdg-desktop-portal is only available on Linux.");

        if (!IsInterfaceAvailable("org.freedesktop.portal.FileChooser"))
        {
            return LinuxPortalResponse.Unavailable(
                "The org.freedesktop.portal.FileChooser portal or libgio is unavailable.");
        }

        var token = CreateHandleToken();
        var method = options.Save ? "SaveFile" : "OpenFile";
        var title = string.IsNullOrWhiteSpace(options.Title)
            ? options.Save ? "Save File" : options.Directory ? "Select Folder" : "Open File"
            : options.Title;
        var parameters =
            $"('{EscapeGVariantString(BuildParentWindow(owner))}', " +
            $"'{EscapeGVariantString(title)}', " +
            $"{BuildFileChooserOptions(options, token)})";

        var response = RunRequest(
            "org.freedesktop.portal.FileChooser",
            method,
            parameters,
            token,
            options.Timeout ?? DefaultRequestTimeout,
            cancellationToken);

        if (response.Status != LinuxPortalResponseStatus.Success)
            return response;

        var paths = ConvertFileUrisToPaths(response.Values);
        return paths.Count == 0
            ? LinuxPortalResponse.Failed("The file chooser returned no usable local file URI.")
            : response with { Values = paths };
    }

    internal static bool OpenUri(
        nint owner,
        string uri,
        bool ask,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(uri) ||
            !IsInterfaceAvailable("org.freedesktop.portal.OpenURI"))
        {
            return false;
        }

        var token = CreateHandleToken();
        var options = "{" +
                      $"'handle_token': <'{EscapeGVariantString(token)}'>, " +
                      $"'ask': <{ask.ToString().ToLowerInvariant()}>" +
                      "}";
        var parameters =
            $"('{EscapeGVariantString(BuildParentWindow(owner))}', " +
            $"'{EscapeGVariantString(uri)}', {options})";

        var response = RunRequest(
            "org.freedesktop.portal.OpenURI",
            "OpenURI",
            parameters,
            token,
            timeout ?? DefaultRequestTimeout,
            cancellationToken);
        return response.Status == LinuxPortalResponseStatus.Success;
    }

    internal static bool TryNormalizeOpenUriTarget(string? target, out string uri)
    {
        uri = string.Empty;
        if (string.IsNullOrWhiteSpace(target) || target.Contains('\0'))
            return false;

        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute) &&
            !string.IsNullOrWhiteSpace(absolute.Scheme))
        {
            uri = absolute.AbsoluteUri;
            return true;
        }

        if (target[0] == '/')
        {
            uri = new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = target }.Uri.AbsoluteUri;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a portal parent handle. Raw Wayland surface pointers are not
    /// exported Wayland handles, so Wayland and missing owners use the valid
    /// empty-parent form. X11 window IDs can be passed directly.
    /// </summary>
    internal static string BuildParentWindow(nint owner)
    {
        if (owner == 0)
            return string.Empty;

        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(sessionType, "x11", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) &&
             !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))))
        {
            return $"x11:{owner.ToInt64():x}";
        }

        return string.Empty;
    }

    internal static string BuildFileChooserOptions(
        LinuxPortalFileChooserOptions options,
        string handleToken)
    {
        var entries = new List<string>
        {
            $"'handle_token': <'{EscapeGVariantString(handleToken)}'>",
            $"'multiple': <{options.Multiple.ToString().ToLowerInvariant()}>"
        };

        if (!options.Save)
            entries.Add($"'directory': <{options.Directory.ToString().ToLowerInvariant()}>");

        if (!string.IsNullOrWhiteSpace(options.CurrentFolder))
        {
            // Preserve an already-absolute Linux path even when the pure
            // command-builder tests execute on Windows.
            var folder = options.CurrentFolder[0] == '/'
                ? options.CurrentFolder
                : Path.GetFullPath(options.CurrentFolder);
            entries.Add($"'current_folder': <{BuildByteArray(folder)}>");
        }

        if (options.Save && !string.IsNullOrWhiteSpace(options.CurrentName))
            entries.Add($"'current_name': <'{EscapeGVariantString(options.CurrentName)}'>");

        var filters = BuildFilters(options.Filters);
        if (filters.Count > 0)
        {
            entries.Add($"'filters': <[{string.Join(", ", filters)}]>");
            var selectedIndex = Math.Clamp(options.FilterIndex - 1, 0, filters.Count - 1);
            entries.Add($"'current_filter': <{filters[selectedIndex]}>");
        }

        return "{" + string.Join(", ", entries) + "}";
    }

    private static List<string> BuildFilters(IReadOnlyList<(string Name, string Pattern)> filters)
    {
        var result = new List<string>(filters.Count);
        foreach (var (name, combinedPattern) in filters)
        {
            if (string.IsNullOrWhiteSpace(combinedPattern))
                continue;

            var patterns = combinedPattern
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static pattern => pattern.Length > 0)
                .Select(pattern => $"(uint32 0, '{EscapeGVariantString(pattern)}')")
                .ToArray();
            if (patterns.Length == 0)
                continue;

            var displayName = string.IsNullOrWhiteSpace(name) ? combinedPattern : name;
            result.Add($"('{EscapeGVariantString(displayName)}', [{string.Join(", ", patterns)}])");
        }

        return result;
    }

    private static string BuildByteArray(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var values = new string[bytes.Length + 1];
        for (var i = 0; i < bytes.Length; i++)
            values[i] = bytes[i].ToString(CultureInfo.InvariantCulture);
        values[^1] = "0";
        return $"[byte {string.Join(", ", values)}]";
    }

    internal static string EscapeGVariantString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    internal static bool TryParseRequestPath(string output, out string requestPath)
    {
        var match = RequestPathRegex().Match(output ?? string.Empty);
        requestPath = match.Success ? match.Value : string.Empty;
        return match.Success;
    }

    internal static bool TryParseResponse(
        string message,
        string expectedRequestPath,
        out LinuxPortalResponse response)
    {
        response = LinuxPortalResponse.Failed("The portal response could not be parsed.");
        if (string.IsNullOrWhiteSpace(message) ||
            !message.Contains(expectedRequestPath, StringComparison.Ordinal) ||
            !message.Contains("org.freedesktop.portal.Request.Response", StringComparison.Ordinal))
        {
            return false;
        }

        var codeMatch = ResponseCodeRegex().Match(message);
        if (!codeMatch.Success ||
            !uint.TryParse(codeMatch.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        if (code == 1)
        {
            response = new LinuxPortalResponse(
                LinuxPortalResponseStatus.Cancelled, Array.Empty<string>());
            return true;
        }

        if (code != 0)
        {
            response = LinuxPortalResponse.Failed($"The portal rejected the request (response {code}).");
            return true;
        }

        response = new LinuxPortalResponse(
            LinuxPortalResponseStatus.Success,
            ExtractUriValues(message));
        return true;
    }

    internal static IReadOnlyList<string> ConvertFileUrisToPaths(IEnumerable<string> uris)
    {
        var paths = new List<string>();
        foreach (var value in uris)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !uri.IsFile ||
                (!string.IsNullOrEmpty(uri.Host) &&
                 !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            if (path.Length > 0 && !path.Contains('\0'))
                paths.Add(path);
        }

        return paths;
    }

    private static IReadOnlyList<string> ExtractUriValues(string message)
    {
        var keyIndex = message.IndexOf("'uris'", StringComparison.Ordinal);
        if (keyIndex < 0)
            return Array.Empty<string>();

        var arrayStart = message.IndexOf('[', keyIndex);
        if (arrayStart < 0)
            return Array.Empty<string>();

        var result = new List<string>();
        for (var i = arrayStart + 1; i < message.Length; i++)
        {
            if (message[i] == ']')
                break;
            if (message[i] != '\'' && message[i] != '"')
                continue;

            var quote = message[i++];
            var builder = new StringBuilder();
            var escaped = false;
            for (; i < message.Length; i++)
            {
                var character = message[i];
                if (escaped)
                {
                    builder.Append(character switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => character
                    });
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    break;
                }
                else
                {
                    builder.Append(character);
                }
            }

            result.Add(builder.ToString());
        }

        return result;
    }

    private static LinuxPortalResponse RunRequest(
        string interfaceName,
        string methodName,
        string parameterText,
        string handleToken,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        nint connection = 0;
        nint parameters = 0;
        nint reply = 0;
        nint error = 0;
        uint subscription = 0;
        GCHandle responseHandle = default;
        string? requestPath = null;

        try
        {
            connection = GioNative.OpenSessionBus(out error);
            if (connection == 0)
            {
                return LinuxPortalResponse.Unavailable(
                    $"Unable to connect to the D-Bus user session: {GioNative.ReadError(error)}");
            }

            var uniqueName = GioNative.GetUniqueName(connection);
            if (string.IsNullOrWhiteSpace(uniqueName))
                return LinuxPortalResponse.Unavailable("The D-Bus connection has no unique name.");

            requestPath = BuildRequestPath(uniqueName, handleToken);
            var state = new PortalSignalState(requestPath);
            responseHandle = GCHandle.Alloc(state);
            subscription = GioNative.SubscribeResponse(
                connection,
                requestPath,
                GCHandle.ToIntPtr(responseHandle));
            if (subscription == 0)
                return LinuxPortalResponse.Failed("Unable to subscribe to the portal response signal.");

            parameters = GioNative.ParseVariant(parameterText, out error);
            if (parameters == 0)
            {
                return LinuxPortalResponse.Failed(
                    $"Unable to encode the portal request: {GioNative.ReadError(error)}");
            }

            reply = GioNative.CallSync(
                connection,
                PortalDestination,
                PortalObjectPath,
                interfaceName,
                methodName,
                parameters,
                timeoutMilliseconds: (int)Math.Clamp(timeout.TotalMilliseconds, 1, 15000),
                out error);
            if (reply == 0)
            {
                return LinuxPortalResponse.Failed(
                    $"The desktop portal call failed: {GioNative.ReadError(error)}");
            }

            var replyText = GioNative.PrintVariant(reply);
            if (!TryParseRequestPath(replyText, out var returnedPath))
                return LinuxPortalResponse.Failed("The portal returned an invalid request object path.");

            if (!string.Equals(returnedPath, requestPath, StringComparison.Ordinal))
            {
                return LinuxPortalResponse.Failed(
                    "The portal did not honor the request handle token.");
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
            {
                while (GioNative.IterateMainContext())
                {
                    if (Volatile.Read(ref state.Response) is { } completed)
                        return completed;
                }

                if (Volatile.Read(ref state.Response) is { } response)
                    return response;

                Thread.Sleep(10);
            }

            TryCloseRequest(connection, requestPath);
            return new LinuxPortalResponse(
                cancellationToken.IsCancellationRequested
                    ? LinuxPortalResponseStatus.Cancelled
                    : LinuxPortalResponseStatus.TimedOut,
                Array.Empty<string>(),
                cancellationToken.IsCancellationRequested
                    ? "The desktop portal request was cancelled."
                    : "The desktop portal request timed out.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return LinuxPortalResponse.Unavailable($"libgio portal transport is unavailable: {ex.Message}");
        }
        finally
        {
            if (connection != 0 && subscription != 0)
            {
                GioNative.Unsubscribe(connection, subscription);
                subscription = 0;
            }
            else if (responseHandle.IsAllocated)
            {
                responseHandle.Free();
            }
            GioNative.FreeError(ref error);
            GioNative.UnrefVariant(ref reply);
            GioNative.UnrefVariant(ref parameters);
            GioNative.UnrefObject(ref connection);
        }
    }

    private static string BuildRequestPath(string uniqueName, string handleToken)
    {
        var sender = uniqueName.TrimStart(':').Replace('.', '_');
        return $"/org/freedesktop/portal/desktop/request/{sender}/{handleToken}";
    }

    private static void TryCloseRequest(nint connection, string requestPath)
    {
        nint parameters = 0;
        nint reply = 0;
        nint error = 0;
        try
        {
            parameters = GioNative.ParseVariant("()", out error);
            if (parameters == 0)
                return;
            reply = GioNative.CallSync(
                connection,
                PortalDestination,
                requestPath,
                RequestInterface,
                "Close",
                parameters,
                timeoutMilliseconds: 2000,
                out error);
        }
        catch
        {
            // The request may already have completed and removed its object.
        }
        finally
        {
            GioNative.FreeError(ref error);
            GioNative.UnrefVariant(ref reply);
            GioNative.UnrefVariant(ref parameters);
        }
    }

    private sealed class PortalSignalState(string requestPath)
    {
        internal string RequestPath { get; } = requestPath;
        internal LinuxPortalResponse? Response;
    }

    private static readonly GioNative.SignalCallback s_responseCallback = OnPortalResponse;
    private static readonly GioNative.DestroyNotify s_destroySignalState = DestroyPortalSignalState;

    private static void DestroyPortalSignalState(nint userData)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.IsAllocated)
                handle.Free();
        }
        catch
        {
            // Never allow an exception to cross the unmanaged callback boundary.
        }
    }

    private static void OnPortalResponse(
        nint connection,
        nint senderName,
        nint objectPath,
        nint interfaceName,
        nint signalName,
        nint parameters,
        nint userData)
    {
        try
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is not PortalSignalState state)
                return;

            var actualPath = Marshal.PtrToStringUTF8(objectPath) ?? state.RequestPath;
            var message = $"{actualPath}: {RequestInterface}.Response " +
                          GioNative.PrintVariant(parameters);
            if (TryParseResponse(message, state.RequestPath, out var response))
                Volatile.Write(ref state.Response, response);
        }
        catch
        {
            // Never allow an exception to cross the unmanaged callback boundary.
        }
    }

    private static string CreateHandleToken() =>
        $"jalium_{Environment.ProcessId}_{Guid.NewGuid():N}";

    [GeneratedRegex(@"/org/freedesktop/portal/desktop/request/[A-Za-z0-9_/-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex RequestPathRegex();

    [GeneratedRegex(@"Request\.Response\s*\(uint32\s+(\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResponseCodeRegex();

    private static class GioNative
    {
        private const string Gio = "libgio-2.0.so.0";
        private const string Glib = "libglib-2.0.so.0";
        private const string GObject = "libgobject-2.0.so.0";
        private static readonly Lazy<bool> s_available = new(() =>
        {
            try
            {
                if (!NativeLibrary.TryLoad(Gio, out var handle))
                    return false;
                NativeLibrary.Free(handle);
                return true;
            }
            catch
            {
                return false;
            }
        });

        internal static bool IsAvailable => s_available.Value;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void SignalCallback(
            nint connection,
            nint senderName,
            nint objectPath,
            nint interfaceName,
            nint signalName,
            nint parameters,
            nint userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DestroyNotify(nint userData);

        internal static nint OpenSessionBus(out nint error) =>
            g_bus_get_sync(2, 0, out error); // G_BUS_TYPE_SESSION

        internal static string GetUniqueName(nint connection)
        {
            var value = g_dbus_connection_get_unique_name(connection);
            return value == 0 ? string.Empty : Marshal.PtrToStringUTF8(value) ?? string.Empty;
        }

        internal static nint ParseVariant(string text, out nint error) =>
            g_variant_parse(0, text, 0, 0, out error);

        internal static nint CallSync(
            nint connection,
            string busName,
            string objectPath,
            string interfaceName,
            string methodName,
            nint parameters,
            int timeoutMilliseconds,
            out nint error) =>
            g_dbus_connection_call_sync(
                connection, busName, objectPath, interfaceName, methodName,
                parameters, 0, 0, timeoutMilliseconds, 0, out error);

        internal static uint SubscribeResponse(nint connection, string requestPath, nint userData) =>
            g_dbus_connection_signal_subscribe(
                connection,
                PortalDestination,
                RequestInterface,
                "Response",
                requestPath,
                null,
                0,
                s_responseCallback,
                userData,
                s_destroySignalState);

        internal static void Unsubscribe(nint connection, uint subscription) =>
            g_dbus_connection_signal_unsubscribe(connection, subscription);

        internal static bool IterateMainContext() => g_main_context_iteration(0, false);

        internal static string PrintVariant(nint variant)
        {
            if (variant == 0)
                return string.Empty;
            var printed = g_variant_print(variant, true);
            if (printed == 0)
                return string.Empty;
            try
            {
                return Marshal.PtrToStringUTF8(printed) ?? string.Empty;
            }
            finally
            {
                g_free(printed);
            }
        }

        internal static string ReadError(nint error)
        {
            if (error == 0)
                return "unknown error";
            var message = Marshal.ReadIntPtr(error, 8);
            return message == 0 ? "unknown error" :
                Marshal.PtrToStringUTF8(message) ?? "unknown error";
        }

        internal static void FreeError(ref nint error)
        {
            if (error == 0)
                return;
            g_error_free(error);
            error = 0;
        }

        internal static void UnrefVariant(ref nint variant)
        {
            if (variant == 0)
                return;
            g_variant_unref(variant);
            variant = 0;
        }

        internal static void UnrefObject(ref nint value)
        {
            if (value == 0)
                return;
            g_object_unref(value);
            value = 0;
        }

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_bus_get_sync(int busType, nint cancellable, out nint error);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_dbus_connection_get_unique_name(nint connection);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_dbus_connection_call_sync(
            nint connection,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string busName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string methodName,
            nint parameters,
            nint replyType,
            int flags,
            int timeoutMilliseconds,
            nint cancellable,
            out nint error);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint g_dbus_connection_signal_subscribe(
            nint connection,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sender,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string interfaceName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string member,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string objectPath,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? arg0,
            int flags,
            SignalCallback callback,
            nint userData,
            DestroyNotify userDataFreeFunction);

        [DllImport(Gio, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_dbus_connection_signal_unsubscribe(
            nint connection,
            uint subscriptionId);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_variant_parse(
            nint type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            nint limit,
            nint endPointer,
            out nint error);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern nint g_variant_print(nint variant, [MarshalAs(UnmanagedType.Bool)] bool typeAnnotate);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_variant_unref(nint variant);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool g_main_context_iteration(
            nint context,
            [MarshalAs(UnmanagedType.Bool)] bool mayBlock);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_error_free(nint error);

        [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_free(nint value);

        [DllImport(GObject, CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_object_unref(nint value);
    }
}
