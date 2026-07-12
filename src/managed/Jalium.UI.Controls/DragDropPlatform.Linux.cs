using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;

namespace Jalium.UI;

/// <summary>
/// Linux drag source and drop-target bridge. The X11 XDND and Wayland
/// data-device implementations both produce the same native event stream, so
/// hit testing and routed-event semantics stay identical across compositors.
/// </summary>
internal static class LinuxDropTarget
{
    private sealed class DropState
    {
        public ulong SessionId;
        public UIElement? CurrentTarget;
        public DataObject CurrentData = new();
        public DragDropEffects AllowedEffects;
        public Point Position;
    }

    private static readonly Dictionary<Window, DropState> s_states = [];

    internal static void RevokeWindow(Window window)
    {
        if (s_states.Remove(window, out DropState? state))
            RaiseLeave(state);
    }

    internal static void ProcessEvent(Window window, PlatformEvent evt)
    {
        if (!OperatingSystem.IsLinux())
            return;

        switch (evt.Type)
        {
            case PlatformEventType.DragEnter:
                BeginOrReplace(window, evt);
                break;

            case PlatformEventType.DragOver:
                ProcessOver(window, evt);
                break;

            case PlatformEventType.Drop:
                ProcessDrop(window, evt);
                break;

            case PlatformEventType.DragLeave:
                End(window, evt, notifyNative: true);
                break;

            case PlatformEventType.DragFinished:
                // Source completion is reported to the synchronous
                // jalium_drag_begin call. It is intentionally not routed as a
                // target event, but it must tear down a possible self-drag.
                End(window, evt, notifyNative: false);
                break;
        }
    }

    private static void BeginOrReplace(Window window, PlatformEvent evt)
    {
        if (s_states.Remove(window, out DropState? previous))
            RaiseLeave(previous);

        var state = new DropState
        {
            SessionId = evt.DragSessionId,
            CurrentData = CreateDataObject(evt.DragMimeTypes, evt.DragDataMimeType, evt.DragData),
            AllowedEffects = MaskEffects(evt.DragAllowedEffects),
            Position = ToDip(window, evt),
        };
        s_states[window] = state;

        state.CurrentTarget = HitDropTarget(window, state.Position);
        DragDropEffects selected = DragDropEffects.None;
        if (state.CurrentTarget != null)
        {
            selected = RaiseDragEvent(
                state.CurrentTarget,
                DragDrop.PreviewDragEnterEvent,
                DragDrop.DragEnterEvent,
                state.CurrentData,
                MapKeyStates(evt.DragKeyStates),
                state.AllowedEffects,
                state.Position);
        }

        window.SetPlatformDragEffect(state.SessionId,
            (uint)SelectSingleEffect(selected, state.AllowedEffects, evt.DragKeyStates));
    }

    private static void ProcessOver(Window window, PlatformEvent evt)
    {
        if (!s_states.TryGetValue(window, out DropState? state) || state.SessionId != evt.DragSessionId)
        {
            BeginOrReplace(window, evt);
            if (!s_states.TryGetValue(window, out state))
                return;
        }

        state.Position = ToDip(window, evt);
        state.AllowedEffects = MaskEffects(evt.DragAllowedEffects);
        UIElement? target = HitDropTarget(window, state.Position);

        if (target != state.CurrentTarget)
        {
            if (state.CurrentTarget != null)
            {
                _ = RaiseDragEvent(
                    state.CurrentTarget,
                    DragDrop.PreviewDragLeaveEvent,
                    DragDrop.DragLeaveEvent,
                    state.CurrentData,
                    MapKeyStates(evt.DragKeyStates),
                    state.AllowedEffects,
                    state.Position);
            }

            state.CurrentTarget = target;
            if (target != null)
            {
                _ = RaiseDragEvent(
                    target,
                    DragDrop.PreviewDragEnterEvent,
                    DragDrop.DragEnterEvent,
                    state.CurrentData,
                    MapKeyStates(evt.DragKeyStates),
                    state.AllowedEffects,
                    state.Position);
            }
        }

        DragDropEffects selected = DragDropEffects.None;
        if (state.CurrentTarget != null)
        {
            selected = RaiseDragEvent(
                state.CurrentTarget,
                DragDrop.PreviewDragOverEvent,
                DragDrop.DragOverEvent,
                state.CurrentData,
                MapKeyStates(evt.DragKeyStates),
                state.AllowedEffects,
                state.Position);
        }

        window.SetPlatformDragEffect(state.SessionId,
            (uint)SelectSingleEffect(selected, state.AllowedEffects, evt.DragKeyStates));
    }

    private static void ProcessDrop(Window window, PlatformEvent evt)
    {
        if (!s_states.TryGetValue(window, out DropState? state) || state.SessionId != evt.DragSessionId)
        {
            BeginOrReplace(window, evt);
            if (!s_states.TryGetValue(window, out state))
                return;
        }

        state.Position = ToDip(window, evt);
        state.AllowedEffects = MaskEffects(evt.DragAllowedEffects);
        state.CurrentData = CreateDataObject(evt.DragMimeTypes, evt.DragDataMimeType, evt.DragData);
        UIElement? target = HitDropTarget(window, state.Position) ?? state.CurrentTarget;
        DragDropEffects selected = DragDropEffects.None;
        if (target != null)
        {
            selected = RaiseDragEvent(
                target,
                DragDrop.PreviewDropEvent,
                DragDrop.DropEvent,
                state.CurrentData,
                MapKeyStates(evt.DragKeyStates),
                state.AllowedEffects,
                state.Position);
        }

        window.SetPlatformDragEffect(state.SessionId,
            (uint)SelectSingleEffect(selected, state.AllowedEffects, evt.DragKeyStates));
        state.CurrentTarget = null;
        s_states.Remove(window);
    }

    private static void End(Window window, PlatformEvent evt, bool notifyNative)
    {
        if (s_states.Remove(window, out DropState? state))
            RaiseLeave(state);
        if (notifyNative)
            window.SetPlatformDragEffect(evt.DragSessionId, 0);
    }

    private static void RaiseLeave(DropState state)
    {
        if (state.CurrentTarget == null)
            return;
        _ = RaiseDragEvent(
            state.CurrentTarget,
            DragDrop.PreviewDragLeaveEvent,
            DragDrop.DragLeaveEvent,
            state.CurrentData,
            DragDropKeyStates.None,
            state.AllowedEffects,
            state.Position);
        state.CurrentTarget = null;
    }

    private static Point ToDip(Window window, PlatformEvent evt)
    {
        double scale = window.DpiScale > 0 ? window.DpiScale : 1;
        return new Point(evt.MouseX / scale, evt.MouseY / scale);
    }

    private static UIElement? HitDropTarget(Window window, Point position)
    {
        UIElement? hit = window.HitTest(position)?.VisualHit as UIElement;
        return DragDropPlatform.FindDropTargetElement(hit);
    }

    private static DragDropEffects RaiseDragEvent(
        UIElement target,
        RoutedEvent previewEvent,
        RoutedEvent bubbleEvent,
        DataObject data,
        DragDropKeyStates keys,
        DragDropEffects allowed,
        Point position)
    {
        var preview = new DragEventArgs(previewEvent, data, keys, allowed, position);
        target.RaiseEvent(preview);
        if (preview.Handled)
            return preview.Effects;

        var bubble = new DragEventArgs(bubbleEvent, data, keys, allowed, position);
        target.RaiseEvent(bubble);
        return bubble.Effects;
    }

    internal static DataObject CreateDataObject(
        string[]? mimeTypes,
        string? dataMimeType,
        byte[]? bytes)
    {
        var result = new DataObject();
        foreach (string mime in mimeTypes ?? [])
        {
            if (IsUriList(mime))
                result.SetData(DataFormats.FileDrop, Array.Empty<string>());
            else if (IsText(mime))
            {
                result.SetData(DataFormats.Text, string.Empty);
                result.SetData(DataFormats.UnicodeText, string.Empty);
            }
            result.SetData(mime, Array.Empty<byte>());
        }

        if (bytes == null || string.IsNullOrEmpty(dataMimeType))
            return result;

        result.SetData(dataMimeType, bytes);
        string text = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        if (IsUriList(dataMimeType))
        {
            string[] files = ParseUriList(text);
            result.SetData(DataFormats.FileDrop, files);
        }
        else if (IsText(dataMimeType))
        {
            result.SetData(DataFormats.Text, text);
            result.SetData(DataFormats.UnicodeText, text);
            result.SetData(DataFormats.StringFormat, text);
        }
        return result;
    }

    internal static string[] ParseUriList(string uriList)
    {
        var paths = new List<string>();
        foreach (string rawLine in uriList.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                paths.Add(uri.LocalPath);
        }
        return [.. paths];
    }

    private static bool IsUriList(string mime) =>
        mime.Equals("text/uri-list", StringComparison.OrdinalIgnoreCase);

    private static bool IsText(string mime) =>
        mime.Equals("UTF8_STRING", StringComparison.OrdinalIgnoreCase) ||
        mime.Equals("STRING", StringComparison.OrdinalIgnoreCase) ||
        mime.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase);

    private static DragDropKeyStates MapKeyStates(uint states) =>
        (DragDropKeyStates)(states & 0x3f);

    private static DragDropEffects MaskEffects(uint effects) =>
        (DragDropEffects)(effects & 0x07);

    internal static DragDropEffects SelectSingleEffect(
        DragDropEffects requested,
        DragDropEffects allowed,
        uint keyStates)
    {
        DragDropEffects available = requested & allowed &
            (DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        if ((keyStates & (uint)DragDropKeyStates.ControlKey) != 0 && available.HasFlag(DragDropEffects.Copy))
            return DragDropEffects.Copy;
        if ((keyStates & (uint)DragDropKeyStates.ShiftKey) != 0 && available.HasFlag(DragDropEffects.Move))
            return DragDropEffects.Move;
        if ((keyStates & (uint)DragDropKeyStates.AltKey) != 0 && available.HasFlag(DragDropEffects.Link))
            return DragDropEffects.Link;
        if (available.HasFlag(DragDropEffects.Copy)) return DragDropEffects.Copy;
        if (available.HasFlag(DragDropEffects.Move)) return DragDropEffects.Move;
        if (available.HasFlag(DragDropEffects.Link)) return DragDropEffects.Link;
        return DragDropEffects.None;
    }
}

internal static partial class DragDropPlatform
{
    private static DragDropEffects DoLinuxDragDrop(
        DependencyObject dragSource,
        IDataObject data,
        DragDropEffects allowedEffects)
    {
        Window? window = FindOwningWindow(dragSource);
        if (window == null)
            return DragDropEffects.None;

        using var payload = LinuxDragPayload.Create(data);
        if (payload.Items.Length == 0)
            return DragDropEffects.None;

        uint effect = window.BeginPlatformDrag(payload.Items, (uint)allowedEffects & 0x07);
        return (DragDropEffects)(effect & 0x07);
    }

    private static Window? FindOwningWindow(DependencyObject source)
    {
        if (source is Window sourceWindow)
            return sourceWindow;
        if (source is not Visual visual)
            return null;

        Visual? current = visual;
        while (current != null)
        {
            if (current is Window window)
                return window;
            current = current.VisualParent;
        }
        return null;
    }

    private sealed class LinuxDragPayload : IDisposable
    {
        private readonly List<nint> _allocations = [];
        public NativeDragDataItem[] Items { get; private set; } = [];

        public static LinuxDragPayload Create(IDataObject data)
        {
            var payload = new LinuxDragPayload();
            var representations = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] files && files.Length != 0)
            {
                representations["text/uri-list"] = Encoding.UTF8.GetBytes(BuildUriList(files));
            }

            string? text = data.GetData(DataFormats.UnicodeText) as string ??
                           data.GetData(DataFormats.Text) as string ??
                           data.GetData(DataFormats.StringFormat) as string;
            if (text != null)
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(text);
                representations["text/plain;charset=utf-8"] = utf8;
                representations["text/plain"] = utf8;
                representations["UTF8_STRING"] = utf8;
            }

            foreach (string format in data.GetFormats(false))
            {
                if (!format.Contains('/', StringComparison.Ordinal) || representations.ContainsKey(format))
                    continue;
                object? value = data.GetData(format, false);
                if (value is byte[] raw)
                    representations[format] = raw;
                else if (value is string stringValue)
                    representations[format] = Encoding.UTF8.GetBytes(stringValue);
            }

            var items = new List<NativeDragDataItem>(representations.Count);
            foreach ((string mime, byte[] representation) in representations)
            {
                // MIME names are ASCII by specification; HGlobal keeps the
                // allocator paired with the shared disposal path below.
                nint mimePointer = Marshal.StringToHGlobalAnsi(mime);
                payload._allocations.Add(mimePointer);

                int allocationSize = Math.Max(1, representation.Length);
                nint dataPointer = Marshal.AllocHGlobal(allocationSize);
                payload._allocations.Add(dataPointer);
                if (representation.Length != 0)
                    Marshal.Copy(representation, 0, dataPointer, representation.Length);

                items.Add(new NativeDragDataItem
                {
                    MimeType = mimePointer,
                    Data = dataPointer,
                    DataSize = (uint)representation.Length,
                });
            }
            payload.Items = [.. items];
            return payload;
        }

        private static string BuildUriList(IEnumerable<string> files)
        {
            var builder = new StringBuilder();
            foreach (string file in files)
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;
                if (Uri.TryCreate(file, UriKind.Absolute, out Uri? existing) && existing.IsFile)
                    builder.Append(existing.AbsoluteUri);
                else
                    builder.Append(new Uri(Path.GetFullPath(file)).AbsoluteUri);
                builder.Append("\r\n");
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            foreach (nint allocation in _allocations)
                Marshal.FreeHGlobal(allocation);
            _allocations.Clear();
            Items = [];
        }
    }
}
