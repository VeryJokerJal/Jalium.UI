using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Automation;
using Jalium.UI.Threading;
using RawProvider = Jalium.UI.Automation.Provider;
using WpfClipboard = global::Jalium.UI.Clipboard;

namespace Jalium.UI.Controls.Automation.AtSpi;

/// <summary>
/// Exposes the existing <see cref="AutomationPeer"/> tree on Linux through AT-SPI2.
/// </summary>
internal static class AtSpiAccessibilityBridge
{
    private const string RootPath = "/org/a11y/atspi/accessible/root";
    private const string NullPath = "/org/a11y/atspi/null";
    private const string AccessibleInterface = "org.a11y.atspi.Accessible";
    private const string ComponentInterface = "org.a11y.atspi.Component";
    private const string ActionInterface = "org.a11y.atspi.Action";
    private const string TextInterface = "org.a11y.atspi.Text";
    private const string ValueInterface = "org.a11y.atspi.Value";
    private const string EditableTextInterface = "org.a11y.atspi.EditableText";
    private const string SelectionInterface = "org.a11y.atspi.Selection";
    private const string TableInterface = "org.a11y.atspi.Table";
    private const string ApplicationInterface = "org.a11y.atspi.Application";
    private const string ObjectEventInterface = "org.a11y.atspi.Event.Object";
    private const string WindowEventInterface = "org.a11y.atspi.Event.Window";
    private const string FocusEventInterface = "org.a11y.atspi.Event.Focus";

    private static readonly object s_gate = new();
    private static readonly ConditionalWeakTable<AutomationPeer, AtSpiNode> s_peerNodes = new();
    private static readonly Dictionary<string, AtSpiNode> s_pathNodes = new(StringComparer.Ordinal);
    private static readonly Dictionary<Window, AtSpiNode> s_windowNodes = new();
    private static readonly AtSpiAutomationEventSink s_eventSink = new();
    private static nint s_connection;
    private static nint s_nodeInfo;
    private static string s_uniqueName = string.Empty;
    private static AtSpiNode? s_root;
    private static Thread? s_mainContextThread;
    private static AtSpiNode? s_focusedNode;
    private static long s_nextObjectId;
    private static int s_applicationId;
    private static volatile bool s_startAttempted;
    private static volatile bool s_active;
    private static string s_status = OperatingSystem.IsLinux() ? "not-started" : "not-linux";
    private static string? s_lastError;

    internal static bool IsActive => s_active;
    internal static string Status => Volatile.Read(ref s_status);
    internal static string? LastError => Volatile.Read(ref s_lastError);
    internal static string IntrospectionDocument => IntrospectionXml;

    internal static void NotifyWindowCreated(Window window)
    {
        if (!OperatingSystem.IsLinux() || IsDisabled())
            return;

        ArgumentNullException.ThrowIfNull(window);
        if (!EnsureStarted())
            return;

        var peer = window.GetAutomationPeer();
        if (peer == null)
            return;

        var descriptor = DescribePeer(peer);
        AtSpiNode node;
        int index;
        lock (s_gate)
        {
            if (s_windowNodes.TryGetValue(window, out _))
                return;

            node = EnsurePeerNodeLocked(peer, window, window.Dispatcher, descriptor);
            s_windowNodes.Add(window, node);
            index = s_windowNodes.Count - 1;
            s_root!.CachedChildPaths = s_windowNodes.Values.Select(static value => value.Path).ToArray();
        }

        AutomationPeer.EventSink = s_eventSink;
        EmitChildrenChanged(s_root!, "add", index, node);
        EmitWindowSignal(node, "Create");
        AtSpiTrace.Log($"window registered path={node.Path} name={Safe(() => peer.GetName(), string.Empty)}");
    }

    internal static void NotifyWindowDestroyed(Window window)
    {
        if (!OperatingSystem.IsLinux())
            return;

        AtSpiNode? windowNode;
        List<AtSpiNode> nodes;
        int oldIndex;
        lock (s_gate)
        {
            if (!s_windowNodes.Remove(window, out windowNode) || windowNode == null)
                return;

            oldIndex = Array.IndexOf(s_root?.CachedChildPaths ?? [], windowNode.Path);
            nodes = s_pathNodes.Values
                .Where(value => ReferenceEquals(value.Window, window))
                .OrderByDescending(static value => value.Path.Length)
                .ToList();
            foreach (var node in nodes)
            {
                node.Defunct = true;
                _ = s_pathNodes.Remove(node.Path);
                if (node.Peer != null)
                    _ = s_peerNodes.Remove(node.Peer);
            }

            if (s_root != null)
                s_root.CachedChildPaths = s_windowNodes.Values.Select(static value => value.Path).ToArray();
        }

        EmitWindowSignal(windowNode, "Destroy");
        EmitChildrenChanged(s_root!, "remove", oldIndex, windowNode);
        foreach (var node in nodes)
            node.Unregister(s_connection);
        AtSpiNative.Flush(s_connection);

        if (ReferenceEquals(s_focusedNode, windowNode) ||
            (s_focusedNode != null && ReferenceEquals(s_focusedNode.Window, window)))
        {
            s_focusedNode = null;
        }

        AtSpiTrace.Log($"window unregistered path={windowNode.Path}");
    }

    internal static void NotifyWindowActivated(Window window, bool active)
    {
        if (!s_active)
            return;

        AtSpiNode? node;
        lock (s_gate)
            _ = s_windowNodes.TryGetValue(window, out node);
        if (node == null)
            return;

        EmitWindowSignal(node, active ? "Activate" : "Deactivate");
        EmitStateChanged(node, "active", active);
        if (active && node.Peer != null)
            RaiseFocusChanged(node.Peer);
    }

    internal static void HandleMethodCall(
        nint userData,
        string interfaceName,
        string methodName,
        nint parameters,
        nint invocation)
    {
        var node = NodeFromUserData(userData);
        if (node.Defunct)
        {
            AtSpiNative.ReturnError(invocation, "The accessible object is defunct.");
            return;
        }

        string response = interfaceName switch
        {
            AccessibleInterface => HandleAccessibleMethod(node, methodName, parameters),
            ComponentInterface => HandleComponentMethod(node, methodName, parameters),
            ActionInterface => HandleActionMethod(node, methodName, parameters),
            TextInterface => HandleTextMethod(node, methodName, parameters),
            EditableTextInterface => HandleEditableTextMethod(node, methodName, parameters),
            SelectionInterface => HandleSelectionMethod(node, methodName, parameters),
            TableInterface => HandleTableMethod(node, methodName, parameters),
            ApplicationInterface => HandleApplicationMethod(methodName),
            _ => throw new NotSupportedException($"Unsupported AT-SPI interface '{interfaceName}'."),
        };
        AtSpiNative.ReturnValue(invocation, response);
    }

    internal static nint HandleGetProperty(nint userData, string interfaceName, string propertyName)
    {
        var node = NodeFromUserData(userData);
        string variant = interfaceName switch
        {
            AccessibleInterface => GetAccessibleProperty(node, propertyName),
            ActionInterface => GetActionProperty(node, propertyName),
            TextInterface => GetTextProperty(node, propertyName),
            ValueInterface => GetValueProperty(node, propertyName),
            EditableTextInterface when propertyName == "version" => "uint32 1",
            SelectionInterface => GetSelectionProperty(node, propertyName),
            TableInterface => GetTableProperty(node, propertyName),
            ApplicationInterface => GetApplicationProperty(propertyName),
            ComponentInterface when propertyName == "version" => "uint32 1",
            _ => throw new NotSupportedException($"Unsupported AT-SPI property '{interfaceName}.{propertyName}'."),
        };
        return AtSpiNative.CreatePropertyVariant(variant);
    }

    internal static bool HandleSetProperty(
        nint userData,
        string interfaceName,
        string propertyName,
        nint value)
    {
        if (interfaceName == ApplicationInterface && propertyName == "Id")
        {
            s_applicationId = AtSpiNative.GetDirectInt32(value);
            return true;
        }

        if (interfaceName == ValueInterface && propertyName == "CurrentValue")
        {
            var node = NodeFromUserData(userData);
            var newValue = AtSpiNative.GetDirectDouble(value);
            return node.TryInvoke(peer =>
            {
                if (peer.GetPattern(PatternInterface.RangeValue) is
                        RawProvider.IRangeValueProvider { IsReadOnly: false } provider)
                {
                    provider.SetValue(newValue);
                }
            });
        }

        return false;
    }

    private static string GetValueProperty(AtSpiNode node, string property)
    {
        if (property == "version")
            return "uint32 1";

        var (minimum, maximum, current, increment) = node.Invoke(() =>
            node.Peer!.GetPattern(PatternInterface.RangeValue) is RawProvider.IRangeValueProvider provider
                ? (provider.Minimum, provider.Maximum, provider.Value, provider.SmallChange)
                : (0.0, 0.0, 0.0, 0.0));

        static string Format(double value) =>
            double.IsFinite(value)
                ? value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : "0";

        return property switch
        {
            "MinimumValue" => $"double {Format(minimum)}",
            "MaximumValue" => $"double {Format(maximum)}",
            "CurrentValue" => $"double {Format(current)}",
            "MinimumIncrement" => $"double {Format(increment)}",
            "Text" => AtSpiModel.Quote(Format(current)),
            _ => throw new NotSupportedException($"Unsupported Value property '{property}'."),
        };
    }

    private static string HandleEditableTextMethod(AtSpiNode node, string method, nint parameters)
    {
        bool Apply(Func<string, string?> transform)
        {
            bool applied = false;
            bool invoked = node.TryInvoke(peer =>
            {
                if (peer.GetPattern(PatternInterface.Value) is not
                        RawProvider.IValueProvider { IsReadOnly: false } provider)
                {
                    return;
                }

                if (transform(provider.Value ?? string.Empty) is { } updated)
                {
                    provider.SetValue(updated);
                    applied = true;
                }
            });
            if (invoked && applied)
            {
                string currentText = node.Invoke(() => AtSpiModel.GetText(node.Peer!));
                EmitTextChanges(node, currentText);
            }
            return invoked && applied;
        }

        switch (method)
        {
            case "SetTextContents":
            {
                string text = AtSpiNative.GetString(parameters, 0);
                return $"({Bool(Apply(_ => text))},)";
            }

            case "InsertText":
            {
                int position = AtSpiNative.GetInt32(parameters, 0);
                string text = AtSpiNative.GetString(parameters, 1);
                int length = AtSpiNative.GetInt32(parameters, 2);
                if (length >= 0)
                    text = AtSpiModel.SliceText(text, 0, length);
                bool ok = Apply(current =>
                {
                    var map = new AtSpiTextMap(current);
                    return current.Insert(map.ToUtf16Offset(position), text);
                });
                return $"({Bool(ok)},)";
            }

            case "DeleteText":
            {
                int start = AtSpiNative.GetInt32(parameters, 0);
                int end = AtSpiNative.GetInt32(parameters, 1);
                bool ok = Apply(current =>
                {
                    var map = new AtSpiTextMap(current);
                    int from = map.ToUtf16Offset(Math.Min(start, end));
                    int to = map.ToUtf16Offset(Math.Max(start, end));
                    return current.Remove(from, to - from);
                });
                return $"({Bool(ok)},)";
            }

            case "CopyText":
            {
                int start = AtSpiNative.GetInt32(parameters, 0);
                int end = AtSpiNative.GetInt32(parameters, 1);
                string current = node.Invoke(() =>
                    node.Peer!.GetPattern(PatternInterface.Value) is RawProvider.IValueProvider provider
                        ? provider.Value ?? string.Empty
                        : string.Empty);
                var map = new AtSpiTextMap(current);
                int from = Math.Clamp(Math.Min(start, end), 0, map.CharacterCount);
                int to = Math.Clamp(Math.Max(start, end), from, map.CharacterCount);
                WpfClipboard.SetText(map.Slice(from, to));
                // CopyText intentionally has no D-Bus return value in AT-SPI2.
                return "()";
            }

            case "CutText":
            {
                int start = AtSpiNative.GetInt32(parameters, 0);
                int end = AtSpiNative.GetInt32(parameters, 1);
                bool ok = false;
                _ = node.TryInvoke(peer =>
                {
                    if (peer.GetPattern(PatternInterface.Value) is not
                            RawProvider.IValueProvider { IsReadOnly: false } provider)
                    {
                        return;
                    }

                    string current = provider.Value ?? string.Empty;
                    var map = new AtSpiTextMap(current);
                    int fromScalar = Math.Clamp(Math.Min(start, end), 0, map.CharacterCount);
                    int toScalar = Math.Clamp(Math.Max(start, end), fromScalar, map.CharacterCount);
                    int from = map.ToUtf16Offset(fromScalar);
                    int to = map.ToUtf16Offset(toScalar);
                    try
                    {
                        WpfClipboard.SetText(map.Slice(fromScalar, toScalar));
                    }
                    catch (ExternalException)
                    {
                        return;
                    }
                    provider.SetValue(current.Remove(from, to - from));
                    ok = true;
                });
                if (ok)
                {
                    string currentText = node.Invoke(() => AtSpiModel.GetText(node.Peer!));
                    EmitTextChanges(node, currentText);
                }
                return $"({Bool(ok)},)";
            }

            case "PasteText":
            {
                int position = AtSpiNative.GetInt32(parameters, 0);
                if (!WpfClipboard.ContainsText())
                    return "(false,)";
                string clipboardText = WpfClipboard.GetText();
                bool ok = Apply(current =>
                {
                    var map = new AtSpiTextMap(current);
                    return current.Insert(map.ToUtf16Offset(position), clipboardText);
                });
                return $"({Bool(ok)},)";
            }

            default:
                throw new NotSupportedException($"Unsupported EditableText method '{method}'.");
        }
    }

    private static string HandleSelectionMethod(AtSpiNode node, string method, nint parameters)
    {
        switch (method)
        {
            case "GetSelectedChild":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                IReadOnlyList<AtSpiNode> selected = GetSelectedNodes(node);
                AtSpiNode? child = index >= 0 && index < selected.Count ? selected[index] : null;
                return $"({Reference(child)},)";
            }
            case "SelectChild":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                bool selected = TrySelectChild(node, index, add: true);
                return $"({Bool(selected)},)";
            }
            case "DeselectSelectedChild":
            {
                int selectedIndex = AtSpiNative.GetInt32(parameters, 0);
                IReadOnlyList<AtSpiNode> selected = GetSelectedNodes(node);
                bool deselected = selectedIndex >= 0 && selectedIndex < selected.Count &&
                    TryChangeSelectionItem(node, selected[selectedIndex].Peer, SelectionChange.Remove);
                return $"({Bool(deselected)},)";
            }
            case "IsChildSelected":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                bool isSelected = IsChildSelected(node, index);
                return $"({Bool(isSelected)},)";
            }
            case "SelectAll":
                return $"({Bool(TrySelectAll(node))},)";
            case "ClearSelection":
                return $"({Bool(TryClearSelection(node))},)";
            case "DeselectChild":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                bool deselected = TrySelectChild(node, index, add: false);
                return $"({Bool(deselected)},)";
            }
            default:
                throw new NotSupportedException($"Unsupported Selection method '{method}'.");
        }
    }

    private static string HandleTableMethod(AtSpiNode node, string method, nint parameters)
    {
        var (rows, columns) = GetTableDimensions(node);
        switch (method)
        {
            case "GetAccessibleAt":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                int column = AtSpiNative.GetInt32(parameters, 1);
                return $"({Reference(GetTableCellNode(node, row, column))},)";
            }
            case "GetIndexAt":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                int column = AtSpiNative.GetInt32(parameters, 1);
                long wideIndex = IsValidTableCell(row, column, rows, columns)
                    ? (long)row * columns + column
                    : -1;
                int index = wideIndex is >= 0 and <= int.MaxValue ? (int)wideIndex : -1;
                return $"({index},)";
            }
            case "GetRowAtIndex":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                int row = index >= 0 && columns > 0 && index < (long)rows * columns
                    ? index / columns
                    : -1;
                return $"({row},)";
            }
            case "GetColumnAtIndex":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                int column = index >= 0 && columns > 0 && index < (long)rows * columns
                    ? index % columns
                    : -1;
                return $"({column},)";
            }
            case "GetRowDescription":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                return $"({AtSpiModel.Quote(GetTableHeaderName(node, row, rowHeader: true))},)";
            }
            case "GetColumnDescription":
            {
                int column = AtSpiNative.GetInt32(parameters, 0);
                return $"({AtSpiModel.Quote(GetTableHeaderName(node, column, rowHeader: false))},)";
            }
            case "GetRowExtentAt":
            case "GetColumnExtentAt":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                int column = AtSpiNative.GetInt32(parameters, 1);
                int extent = IsValidTableCell(row, column, rows, columns) ? 1 : -1;
                return $"({extent},)";
            }
            case "GetRowHeader":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                return $"({Reference(GetTableHeaderNode(node, row, rowHeader: true))},)";
            }
            case "GetColumnHeader":
            {
                int column = AtSpiNative.GetInt32(parameters, 0);
                return $"({Reference(GetTableHeaderNode(node, column, rowHeader: false))},)";
            }
            case "GetSelectedRows":
                return $"({IntArray(GetSelectedTableRows(node))},)";
            case "GetSelectedColumns":
                // Jalium's grid providers expose row/item selection. They do not
                // currently advertise whole-column selection.
                return "(@ai [],)";
            case "IsRowSelected":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                return $"({Bool(GetSelectedTableRows(node).Contains(row))},)";
            }
            case "IsColumnSelected":
                return "(false,)";
            case "IsSelected":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                int column = AtSpiNative.GetInt32(parameters, 1);
                return $"({Bool(IsTableCellSelected(node, row, column))},)";
            }
            case "AddRowSelection":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                return $"({Bool(TryChangeTableRowSelection(node, row, SelectionChange.Add))},)";
            }
            case "RemoveRowSelection":
            {
                int row = AtSpiNative.GetInt32(parameters, 0);
                return $"({Bool(TryChangeTableRowSelection(node, row, SelectionChange.Remove))},)";
            }
            case "AddColumnSelection":
            case "RemoveColumnSelection":
                return "(false,)";
            case "GetRowColumnExtentsAtIndex":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                bool valid = index >= 0 && columns > 0 && index < (long)rows * columns;
                int row = valid ? index / columns : -1;
                int column = valid ? index % columns : -1;
                bool selected = valid && IsTableCellSelected(node, row, column);
                return $"({Bool(valid)}, {row}, {column}, {(valid ? 1 : 0)}, {(valid ? 1 : 0)}, {Bool(selected)})";
            }
            default:
                throw new NotSupportedException($"Unsupported Table method '{method}'.");
        }
    }

    private static string GetSelectionProperty(AtSpiNode node, string property) => property switch
    {
        "version" => "uint32 1",
        "NSelectedChildren" => $"int32 {GetSelectedNodes(node).Count}",
        _ => throw new NotSupportedException($"Unsupported Selection property '{property}'."),
    };

    private static string GetTableProperty(AtSpiNode node, string property)
    {
        var (rows, columns) = GetTableDimensions(node);
        return property switch
        {
            "version" => "uint32 1",
            "NRows" => $"int32 {rows}",
            "NColumns" => $"int32 {columns}",
            "Caption" or "Summary" => Reference(null),
            "NSelectedRows" => $"int32 {GetSelectedTableRows(node).Length}",
            "NSelectedColumns" => "int32 0",
            _ => throw new NotSupportedException($"Unsupported Table property '{property}'."),
        };
    }

    private static bool EnsureStarted()
    {
        if (s_active)
            return true;
        if (s_startAttempted)
            return false;

        lock (s_gate)
        {
            if (s_active)
                return true;
            if (s_startAttempted)
                return false;

            s_startAttempted = true;
            if (!AtSpiNative.IsAvailable)
                return FailStartup("libgio-2.0 is unavailable.");

            s_connection = AtSpiNative.OpenAccessibilityBus(out s_uniqueName, out var error);
            if (s_connection == 0)
                return FailStartup(error ?? "Unable to connect to org.a11y.Bus.");

            s_nodeInfo = AtSpiNative.ParseNodeInfo(IntrospectionXml, out error);
            if (s_nodeInfo == 0)
                return FailStartup(error ?? "Unable to parse the AT-SPI introspection document.");

            s_root = AtSpiNode.CreateApplicationRoot();
            if (!RegisterNodeLocked(s_root, [AccessibleInterface, ApplicationInterface], out error))
                return FailStartup(error ?? "Unable to register the AT-SPI application root.");
            s_pathNodes.Add(s_root.Path, s_root);

            StartMainContextPump();
            if (!AtSpiNative.EmbedApplication(s_connection, s_uniqueName, RootPath, out error))
                return FailStartup(error ?? "The AT-SPI registry rejected the application root.");

            s_active = true;
            s_status = "active";
            s_lastError = null;
            AutomationPeer.EventSink = s_eventSink;
            AtSpiTrace.Log($"registered application bus={s_uniqueName} root={RootPath}");
            return true;
        }
    }

    private static bool FailStartup(string message)
    {
        s_active = false;
        s_status = "unavailable";
        s_lastError = message;
        AtSpiTrace.Log($"bridge unavailable: {message}");
        return false;
    }

    private static void StartMainContextPump()
    {
        if (s_mainContextThread != null)
            return;

        s_mainContextThread = new Thread(static () =>
        {
            while (true)
            {
                try { AtSpiNative.IterateMainContext(); }
                catch (Exception ex)
                {
                    AtSpiTrace.Log($"GMainContext iteration failed: {ex.Message}");
                    Thread.Sleep(50);
                }
            }
        })
        {
            IsBackground = true,
            Name = "Jalium AT-SPI2",
        };
        s_mainContextThread.Start();
    }

    private static AtSpiNode EnsurePeerNode(AutomationPeer peer, Window window, Dispatcher dispatcher)
    {
        lock (s_gate)
        {
            if (s_peerNodes.TryGetValue(peer, out var existing))
                return existing;
        }

        AtSpiNodeDescriptor descriptor = dispatcher.CheckAccess()
            ? DescribePeer(peer)
            : dispatcher.Invoke(() => DescribePeer(peer));
        lock (s_gate)
            return EnsurePeerNodeLocked(peer, window, dispatcher, descriptor);
    }

    private static AtSpiNode EnsurePeerNodeLocked(
        AutomationPeer peer,
        Window window,
        Dispatcher dispatcher,
        AtSpiNodeDescriptor descriptor)
    {
        if (s_peerNodes.TryGetValue(peer, out var existing))
            return existing;

        string path = $"/org/a11y/atspi/accessible/{Interlocked.Increment(ref s_nextObjectId)}";
        var node = new AtSpiNode(
            path, peer, window, dispatcher,
            descriptor.Role, descriptor.HasAction, descriptor.HasText, descriptor.InitialText);
        var interfaces = new List<string> { AccessibleInterface, ComponentInterface };
        if (descriptor.HasAction) interfaces.Add(ActionInterface);
        if (descriptor.HasText) interfaces.Add(TextInterface);
        if (descriptor.HasValue) interfaces.Add(ValueInterface);
        if (descriptor.HasEditableText) interfaces.Add(EditableTextInterface);
        if (descriptor.HasSelection) interfaces.Add(SelectionInterface);
        if (descriptor.HasTable) interfaces.Add(TableInterface);

        if (!RegisterNodeLocked(node, interfaces, out var error))
            throw new InvalidOperationException(error ?? $"Unable to register AT-SPI object {path}.");

        s_peerNodes.Add(peer, node);
        s_pathNodes.Add(path, node);
        return node;
    }

    private static AtSpiNodeDescriptor DescribePeer(AutomationPeer peer)
    {
        AtSpiRole role = AtSpiModel.MapRole(peer.GetAutomationControlType(), peer.GetClassName());
        bool hasAction = GetAction(peer).Kind != AtSpiActionKind.None;
        bool hasText = peer.GetPattern(PatternInterface.Text) is RawProvider.ITextProvider ||
                       peer.GetPattern(PatternInterface.Value) is RawProvider.IValueProvider ||
                       role is AtSpiRole.Label or AtSpiRole.Text or AtSpiRole.Entry or AtSpiRole.PasswordText;
        // org.a11y.atspi.Value: sliders, progress bars, scroll bars, spinners.
        bool hasValue = peer.GetPattern(PatternInterface.RangeValue) is RawProvider.IRangeValueProvider;
        // org.a11y.atspi.EditableText: writable Value pattern on a text role
        // lets assistive tech type into TextBox/RichTextBox.
        bool hasEditableText = hasText &&
            peer.GetPattern(PatternInterface.Value) is RawProvider.IValueProvider { IsReadOnly: false };
        bool hasSelection = peer.GetPattern(PatternInterface.Selection) is RawProvider.ISelectionProvider;
        bool hasTable = peer.GetPattern(PatternInterface.Table) is RawProvider.ITableProvider;
        string initialText = hasText ? AtSpiModel.GetText(peer) : string.Empty;
        return new AtSpiNodeDescriptor(
            role, hasAction, hasText, hasValue, hasEditableText, hasSelection, hasTable, initialText);
    }

    private static bool RegisterNodeLocked(
        AtSpiNode node,
        IReadOnlyList<string> interfaces,
        out string? error)
    {
        error = null;
        node.AllocateHandle();
        foreach (string interfaceName in interfaces)
        {
            nint interfaceInfo = AtSpiNative.LookupInterface(s_nodeInfo, interfaceName);
            if (interfaceInfo == 0)
            {
                error = $"Missing introspection for {interfaceName}.";
                node.Unregister(s_connection);
                return false;
            }

            uint registration = AtSpiNative.RegisterObject(
                s_connection, node.Path, interfaceInfo, node.UserData, out error);
            if (registration == 0)
            {
                node.Unregister(s_connection);
                return false;
            }

            node.RegistrationIds.Add(registration);
            node.Interfaces.Add(interfaceName);
        }

        return true;
    }

    private static string HandleAccessibleMethod(AtSpiNode node, string method, nint parameters)
    {
        switch (method)
        {
            case "GetChildAtIndex":
            {
                int index = AtSpiNative.GetInt32(parameters, 0);
                AtSpiNode? child = GetChildAtIndex(node, index);
                return $"({Reference(child)},)";
            }
            case "GetChildren":
            {
                var children = GetChildren(node, updateCache: true);
                string values = string.Join(", ", children.Select(Reference));
                return $"(@a(so) [{values}],)";
            }
            case "GetIndexInParent":
                return $"(int32 {GetIndexInParent(node)},)";
            case "GetRelationSet":
                return "(@a(ua(so)) [],)";
            case "GetRole":
                return $"(uint32 {(uint)node.Role},)";
            case "GetRoleName":
            case "GetLocalizedRoleName":
                return $"({AtSpiModel.Quote(AtSpiModel.GetRoleName(node.Role))},)";
            case "GetState":
            {
                ulong states = node.IsApplicationRoot
                    ? AtSpiModel.State(AtSpiState.Enabled) |
                      AtSpiModel.State(AtSpiState.Sensitive) |
                      AtSpiModel.State(AtSpiState.Visible) |
                      AtSpiModel.State(AtSpiState.Showing)
                    : node.Invoke(() => AtSpiModel.GetStates(node.Peer!, node.Defunct));
                uint[] words = AtSpiModel.PackStates(states);
                return $"(@au [uint32 {words[0]}, uint32 {words[1]}],)";
            }
            case "GetAttributes":
                return "(@a{ss} {'toolkit': 'Jalium.UI'},)";
            case "GetApplication":
                return $"({Reference(s_root)},)";
            case "GetInterfaces":
            {
                string values = string.Join(", ", node.Interfaces.Select(AtSpiModel.Quote));
                return $"(@as [{values}],)";
            }
            default:
                throw new NotSupportedException($"Unsupported Accessible method '{method}'.");
        }
    }

    private static string HandleComponentMethod(AtSpiNode node, string method, nint parameters)
    {
        uint coordinateType;
        AtSpiRect bounds;
        switch (method)
        {
            case "Contains":
                coordinateType = AtSpiNative.GetUInt32(parameters, 2);
                bounds = GetBounds(node, coordinateType);
                return $"({Bool(bounds.Contains(AtSpiNative.GetInt32(parameters, 0), AtSpiNative.GetInt32(parameters, 1)))},)";
            case "GetAccessibleAtPoint":
            {
                int x = AtSpiNative.GetInt32(parameters, 0);
                int y = AtSpiNative.GetInt32(parameters, 1);
                coordinateType = AtSpiNative.GetUInt32(parameters, 2);
                return $"({Reference(FindAccessibleAtPoint(node, x, y, coordinateType))},)";
            }
            case "GetExtents":
                bounds = GetBounds(node, AtSpiNative.GetUInt32(parameters, 0));
                return $"(({bounds.X}, {bounds.Y}, {bounds.Width}, {bounds.Height}),)";
            case "GetPosition":
                bounds = GetBounds(node, AtSpiNative.GetUInt32(parameters, 0));
                return $"({bounds.X}, {bounds.Y})";
            case "GetSize":
                bounds = GetBounds(node, 1);
                return $"({bounds.Width}, {bounds.Height})";
            case "GetLayer":
                return "(uint32 2,)"; // ATSPI_COMPONENT_LAYER_WIDGET
            case "GetMDIZOrder":
                return "(int16 0,)";
            case "GrabFocus":
                return $"({Bool(node.TryInvoke(static peer => peer.SetFocus()))},)";
            case "GetAlpha":
                return "(double 1.0,)";
            case "SetExtents":
            case "SetPosition":
            case "SetSize":
                return "(false,)";
            case "ScrollTo":
            case "ScrollToPoint":
                return $"({Bool(node.TryInvoke(static peer =>
                {
                    if (peer.GetPattern(PatternInterface.ScrollItem) is RawProvider.IScrollItemProvider scroll)
                        scroll.ScrollIntoView();
                }))},)";
            default:
                throw new NotSupportedException($"Unsupported Component method '{method}'.");
        }
    }

    private static string HandleActionMethod(AtSpiNode node, string method, nint parameters)
    {
        var action = node.Invoke(() => GetAction(node.Peer!));
        int index = method == "GetActions" ? 0 : AtSpiNative.GetInt32(parameters, 0);
        bool valid = index == 0 && action.Kind != AtSpiActionKind.None;
        return method switch
        {
            "GetDescription" => $"({AtSpiModel.Quote(valid ? action.Description : string.Empty)},)",
            "GetName" or "GetLocalizedName" => $"({AtSpiModel.Quote(valid ? action.Name : string.Empty)},)",
            "GetKeyBinding" => "('',)",
            "GetActions" when valid =>
                $"(@a(sss) [({AtSpiModel.Quote(action.Name)}, {AtSpiModel.Quote(action.Description)}, '')],)",
            "GetActions" => "(@a(sss) [],)",
            "DoAction" => $"({Bool(valid && node.TryInvoke(peer => ExecuteAction(peer, action.Kind)))},)",
            _ => throw new NotSupportedException($"Unsupported Action method '{method}'."),
        };
    }

    private static string HandleTextMethod(AtSpiNode node, string method, nint parameters)
    {
        string text = node.Invoke(() => AtSpiModel.GetText(node.Peer!));
        var map = new AtSpiTextMap(text);
        switch (method)
        {
            case "GetStringAtOffset":
            {
                var result = AtSpiModel.GetStringAtOffset(
                    text, AtSpiNative.GetInt32(parameters, 0), AtSpiNative.GetUInt32(parameters, 1));
                return $"({AtSpiModel.Quote(result.Text)}, {result.Start}, {result.End})";
            }
            case "GetTextAtOffset":
            {
                var result = AtSpiModel.GetLegacyTextAtOffset(
                    text,
                    AtSpiNative.GetInt32(parameters, 0),
                    AtSpiNative.GetUInt32(parameters, 1),
                    AtSpiLegacyTextPosition.At);
                return $"({AtSpiModel.Quote(result.Text)}, {result.Start}, {result.End})";
            }
            case "GetText":
                return $"({AtSpiModel.Quote(AtSpiModel.SliceText(text, AtSpiNative.GetInt32(parameters, 0), AtSpiNative.GetInt32(parameters, 1)))},)";
            case "SetCaretOffset":
            {
                int offset = Math.Clamp(AtSpiNative.GetInt32(parameters, 0), 0, map.CharacterCount);
                return $"({Bool(SelectText(node, offset, 0))},)";
            }
            case "GetTextBeforeOffset":
            {
                var result = AtSpiModel.GetLegacyTextAtOffset(
                    text,
                    AtSpiNative.GetInt32(parameters, 0),
                    AtSpiNative.GetUInt32(parameters, 1),
                    AtSpiLegacyTextPosition.Before);
                return $"({AtSpiModel.Quote(result.Text)}, {result.Start}, {result.End})";
            }
            case "GetTextAfterOffset":
            {
                var result = AtSpiModel.GetLegacyTextAtOffset(
                    text,
                    AtSpiNative.GetInt32(parameters, 0),
                    AtSpiNative.GetUInt32(parameters, 1),
                    AtSpiLegacyTextPosition.After);
                return $"({AtSpiModel.Quote(result.Text)}, {result.Start}, {result.End})";
            }
            case "GetCharacterAtOffset":
                return $"({map.GetCodePoint(AtSpiNative.GetInt32(parameters, 0))},)";
            case "GetAttributeValue":
                return "('',)";
            case "GetAttributes":
            case "GetAttributeRun":
                return $"(@a{{ss}} {{}}, 0, {map.CharacterCount})";
            case "GetDefaultAttributes":
            case "GetDefaultAttributeSet":
                return "(@a{ss} {},)";
            case "GetCharacterExtents":
            case "GetRangeExtents":
            {
                bool character = method == "GetCharacterExtents";
                int start = AtSpiNative.GetInt32(parameters, 0);
                int end = character ? start + 1 : AtSpiNative.GetInt32(parameters, 1);
                uint coord = AtSpiNative.GetUInt32(parameters, character ? 1 : 2);
                var rect = GetTextRangeBounds(node, start, Math.Max(0, end - start), coord);
                return $"({rect.X}, {rect.Y}, {rect.Width}, {rect.Height})";
            }
            case "GetOffsetAtPoint":
            {
                int x = AtSpiNative.GetInt32(parameters, 0);
                int y = AtSpiNative.GetInt32(parameters, 1);
                uint coord = AtSpiNative.GetUInt32(parameters, 2);
                int offset = GetTextOffsetAtPoint(node, map.CharacterCount, x, y, coord);
                return $"({offset},)";
            }
            case "GetNSelections":
            {
                var selection = GetTextSelection(node);
                return $"({(selection.Length > 0 ? 1 : 0)},)";
            }
            case "GetSelection":
            {
                var selection = GetTextSelection(node);
                return $"({selection.Start}, {selection.Start + selection.Length})";
            }
            case "AddSelection":
            case "SetSelection":
            {
                int offset = method == "SetSelection" ? 1 : 0;
                int start = AtSpiNative.GetInt32(parameters, offset);
                int end = AtSpiNative.GetInt32(parameters, offset + 1);
                int from = Math.Min(start, end);
                int to = Math.Max(start, end);
                return $"({Bool(SelectText(node, from, to - from))},)";
            }
            case "RemoveSelection":
            {
                var selection = GetTextSelection(node);
                return $"({Bool(SelectText(node, selection.Start, 0))},)";
            }
            case "GetBoundedRanges":
                return "(@a(iisv) [],)";
            case "ScrollSubstringTo":
            case "ScrollSubstringToPoint":
            {
                int start = AtSpiNative.GetInt32(parameters, 0);
                int end = AtSpiNative.GetInt32(parameters, 1);
                bool scrolled = node.TryInvoke(peer =>
                {
                    if (peer.GetPattern(PatternInterface.Text) is RawProvider.ITextProvider provider)
                    {
                        IAutomationTextProviderSource? source = GetTextSource(provider);
                        if (source is null)
                            return;

                        var providerMap = new AtSpiTextMap(source.Text);
                        int fromScalar = Math.Clamp(Math.Min(start, end), 0, providerMap.CharacterCount);
                        int toScalar = Math.Clamp(Math.Max(start, end), fromScalar, providerMap.CharacterCount);
                        int fromUtf16 = providerMap.ToUtf16Offset(fromScalar);
                        int toUtf16 = providerMap.ToUtf16Offset(toScalar);
                        source.ScrollIntoView(fromUtf16, toUtf16 - fromUtf16);
                    }
                });
                return $"({Bool(scrolled)},)";
            }
            default:
                throw new NotSupportedException($"Unsupported Text method '{method}'.");
        }
    }

    private static string HandleApplicationMethod(string method) => method switch
    {
        "GetLocale" => $"({AtSpiModel.Quote(CurrentLocale())},)",
        "GetApplicationBusAddress" => "('',)",
        _ => throw new NotSupportedException($"Unsupported Application method '{method}'."),
    };

    private static string GetAccessibleProperty(AtSpiNode node, string property)
    {
        return property switch
        {
            "version" => "uint32 1",
            "Name" => AtSpiModel.Quote(node.IsApplicationRoot ? ApplicationName() : node.Invoke(() => node.Peer!.GetName())),
            "Description" => AtSpiModel.Quote(node.IsApplicationRoot ? "Jalium.UI application" : node.Invoke(() => node.Peer!.GetHelpText())),
            "Parent" => Reference(GetParent(node)),
            "ChildCount" => $"int32 {GetAccessibleChildCount(node)}",
            "Locale" => AtSpiModel.Quote(CurrentLocale()),
            "AccessibleId" => AtSpiModel.Quote(node.IsApplicationRoot ? "application" : node.Invoke(() => node.Peer!.GetAutomationId())),
            "HelpText" => AtSpiModel.Quote(node.IsApplicationRoot ? string.Empty : node.Invoke(() => node.Peer!.GetHelpText())),
            _ => throw new NotSupportedException($"Unsupported Accessible property '{property}'."),
        };
    }

    private static string GetActionProperty(AtSpiNode node, string property) => property switch
    {
        "version" => "uint32 1",
        "NActions" => $"int32 {(node.Invoke(() => GetAction(node.Peer!).Kind) == AtSpiActionKind.None ? 0 : 1)}",
        _ => throw new NotSupportedException($"Unsupported Action property '{property}'."),
    };

    private static string GetTextProperty(AtSpiNode node, string property)
    {
        return property switch
        {
            "version" => "uint32 1",
            "CharacterCount" => $"int32 {node.Invoke(() => AtSpiModel.GetCharacterCount(AtSpiModel.GetText(node.Peer!)))}",
            "CaretOffset" => $"int32 {GetTextSelection(node).Start}",
            _ => throw new NotSupportedException($"Unsupported Text property '{property}'."),
        };
    }

    private static string GetApplicationProperty(string property)
    {
        string version = typeof(Window).Assembly.GetName().Version?.ToString() ?? "1.0";
        return property switch
        {
            "ToolkitName" => "'Jalium.UI'",
            "Version" or "ToolkitVersion" => AtSpiModel.Quote(version),
            "AtspiVersion" => "'2.1'",
            "InterfaceVersion" => "uint32 1",
            "Id" => $"int32 {s_applicationId}",
            _ => throw new NotSupportedException($"Unsupported Application property '{property}'."),
        };
    }

    private static (int Rows, int Columns) GetTableDimensions(AtSpiNode node) =>
        node.Invoke(() => GetGridProvider(node.Peer!) is { } grid
            ? (Math.Max(0, grid.RowCount), Math.Max(0, grid.ColumnCount))
            : (0, 0));

    private static RawProvider.IGridProvider? GetGridProvider(AutomationPeer peer) =>
        peer.GetPattern(PatternInterface.Table) as RawProvider.IGridProvider ??
        peer.GetPattern(PatternInterface.Grid) as RawProvider.IGridProvider;

    private static RawProvider.ITableProvider? GetTableProvider(AutomationPeer peer) =>
        peer.GetPattern(PatternInterface.Table) as RawProvider.ITableProvider;

    private static AutomationPeer? GetPeer(RawProvider.IRawElementProviderSimple? provider) =>
        provider is RawProvider.IAutomationPeerRawProvider raw ? raw.Peer : null;

    private static AutomationPeer[] GetSelectionPeers(AutomationPeer peer)
    {
        object? pattern = peer.GetPattern(PatternInterface.Selection);
        if (pattern is RawProvider.ISelectionProvider provider)
        {
            return provider.GetSelection()
                .Select(GetPeer)
                .Where(static value => value is not null)
                .Cast<AutomationPeer>()
                .ToArray();
        }
        return [];
    }

    private static bool CanSelectMultiple(AutomationPeer peer)
    {
        object? pattern = peer.GetPattern(PatternInterface.Selection);
        return pattern is RawProvider.ISelectionProvider provider && provider.CanSelectMultiple;
    }

    private static bool IsSelectionRequired(AutomationPeer peer)
    {
        object? pattern = peer.GetPattern(PatternInterface.Selection);
        return pattern is RawProvider.ISelectionProvider provider && provider.IsSelectionRequired;
    }

    private static IReadOnlyList<AtSpiNode> GetSelectedNodes(AtSpiNode node)
    {
        AutomationPeer[] selected = node.Invoke(() => GetSelectionPeers(node.Peer!));
        if (selected.Length == 0)
            return [];

        if (node.Interfaces.Contains(TableInterface, StringComparer.Ordinal))
        {
            var (rows, columns) = GetTableDimensions(node);
            if (rows == 0 || columns == 0)
                return [];

            AutomationPeer[] ordinaryChildren = node.Invoke(() => node.Peer!.GetChildren().ToArray());
            var result = new List<AtSpiNode>(selected.Length);
            var addedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (AutomationPeer selectedPeer in selected)
            {
                AtSpiNode? selectedNode = null;
                var coordinate = FindTableCoordinate(node, selectedPeer, rows, columns);
                if (coordinate is { } cell)
                {
                    selectedNode = GetTableCellNode(node, cell.Row, cell.Column);
                }
                else
                {
                    int row = Array.FindIndex(ordinaryChildren, child => ReferenceEquals(child, selectedPeer));
                    if (row >= 0 && row < rows)
                        selectedNode = GetTableCellNode(node, row, 0);
                }

                if (selectedNode != null && addedPaths.Add(selectedNode.Path))
                    result.Add(selectedNode);
            }
            return result;
        }

        return selected
            .Select(peer => EnsurePeerNode(peer, node.Window!, node.Dispatcher!))
            .ToArray();
    }

    private static (int Row, int Column)? FindTableCoordinate(
        AtSpiNode node,
        AutomationPeer target,
        int rows,
        int columns)
    {
        return node.Invoke<(int Row, int Column)?>(() =>
        {
            RawProvider.IGridProvider? grid = GetGridProvider(node.Peer!);
            if (grid == null)
                return null;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (ReferenceEquals(GetPeer(grid.GetItem(row, column)), target))
                        return (row, column);
                }
            }
            return null;
        });
    }

    private static AtSpiNode? GetTableCellNode(AtSpiNode table, int row, int column)
    {
        var (rows, columns) = GetTableDimensions(table);
        if (!IsValidTableCell(row, column, rows, columns))
            return null;

        AutomationPeer? peer = table.Invoke(() =>
        {
            RawProvider.IGridProvider? grid = GetGridProvider(table.Peer!);
            return grid == null ? null : GetPeer(grid.GetItem(row, column));
        });
        if (peer == null)
            return null;

        AtSpiNode cell = EnsurePeerNode(peer, table.Window!, table.Dispatcher!);
        lock (s_gate)
        {
            cell.LogicalParent = table;
            long logicalIndex = (long)row * columns + column;
            cell.LogicalIndex = logicalIndex <= int.MaxValue ? (int)logicalIndex : -1;
        }
        return cell;
    }

    private static AtSpiNode? GetTableHeaderNode(AtSpiNode table, int index, bool rowHeader)
    {
        AutomationPeer? peer = table.Invoke(() =>
        {
            RawProvider.ITableProvider? provider = GetTableProvider(table.Peer!);
            RawProvider.IRawElementProviderSimple[] headers = provider == null
                ? []
                : rowHeader ? provider.GetRowHeaders() : provider.GetColumnHeaders();
            return index >= 0 && index < headers.Length ? GetPeer(headers[index]) : null;
        });
        return peer == null ? null : EnsurePeerNode(peer, table.Window!, table.Dispatcher!);
    }

    private static string GetTableHeaderName(AtSpiNode table, int index, bool rowHeader)
    {
        AtSpiNode? header = GetTableHeaderNode(table, index, rowHeader);
        return header?.Invoke(() => header.Peer!.GetName()) ?? string.Empty;
    }

    private static int[] GetSelectedTableRows(AtSpiNode table)
    {
        var (rows, columns) = GetTableDimensions(table);
        if (rows == 0 || columns == 0)
            return [];

        return table.Invoke(() =>
        {
            AutomationPeer[] selected = GetSelectionPeers(table.Peer!);
            if (selected.Length == 0)
                return [];

            var selectedSet = new HashSet<AutomationPeer>(selected);
            var result = new SortedSet<int>();
            AutomationPeer[] ordinaryChildren = table.Peer!.GetChildren().ToArray();
            for (int row = 0; row < Math.Min(rows, ordinaryChildren.Length); row++)
            {
                if (selectedSet.Contains(ordinaryChildren[row]))
                    _ = result.Add(row);
            }

            RawProvider.IGridProvider? grid = GetGridProvider(table.Peer!);
            if (grid != null)
            {
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        AutomationPeer? cell = GetPeer(grid.GetItem(row, column));
                        if (cell != null && selectedSet.Contains(cell))
                        {
                            _ = result.Add(row);
                            break;
                        }
                    }
                }
            }

            return result.ToArray();
        });
    }

    private static bool IsTableCellSelected(AtSpiNode table, int row, int column)
    {
        var (rows, columns) = GetTableDimensions(table);
        if (!IsValidTableCell(row, column, rows, columns))
            return false;
        if (GetSelectedTableRows(table).Contains(row))
            return true;

        return table.Invoke(() =>
        {
            RawProvider.IGridProvider? grid = GetGridProvider(table.Peer!);
            AutomationPeer? cell = grid == null ? null : GetPeer(grid.GetItem(row, column));
            return cell != null && GetSelectionPeers(table.Peer!).Contains(cell);
        });
    }

    private static bool TrySelectChild(AtSpiNode node, int index, bool add)
    {
        if (index < 0)
            return false;

        if (node.Interfaces.Contains(TableInterface, StringComparer.Ordinal))
        {
            var (rows, columns) = GetTableDimensions(node);
            if (columns == 0 || index >= (long)rows * columns)
                return false;
            return TryChangeTableRowSelection(
                node,
                index / columns,
                add ? SelectionChange.Add : SelectionChange.Remove);
        }

        IReadOnlyList<AtSpiNode> children = GetChildren(node, updateCache: true);
        if (index >= children.Count)
            return false;
        return TryChangeSelectionItem(
            node,
            children[index].Peer,
            add ? SelectionChange.Add : SelectionChange.Remove);
    }

    private static bool IsChildSelected(AtSpiNode node, int index)
    {
        if (index < 0)
            return false;
        if (node.Interfaces.Contains(TableInterface, StringComparer.Ordinal))
        {
            var (rows, columns) = GetTableDimensions(node);
            return columns > 0 && index < (long)rows * columns &&
                IsTableCellSelected(node, index / columns, index % columns);
        }

        IReadOnlyList<AtSpiNode> children = GetChildren(node, updateCache: true);
        if (index >= children.Count || children[index].Peer == null)
            return false;
        return node.Invoke(() => GetSelectionPeers(node.Peer!).Contains(children[index].Peer));
    }

    private static bool TrySelectAll(AtSpiNode node)
    {
        if (!node.Invoke(() => CanSelectMultiple(node.Peer!)))
            return false;

        if (node.Interfaces.Contains(TableInterface, StringComparer.Ordinal))
        {
            int rows = GetTableDimensions(node).Rows;
            bool result = true;
            for (int row = 0; row < rows; row++)
                result &= TryChangeTableRowSelection(node, row, SelectionChange.Add);
            return result;
        }

        IReadOnlyList<AtSpiNode> children = GetChildren(node, updateCache: true);
        bool allSelected = true;
        foreach (AtSpiNode child in children)
            allSelected &= TryChangeSelectionItem(node, child.Peer, SelectionChange.Add);
        return allSelected;
    }

    private static bool TryClearSelection(AtSpiNode node)
    {
        if (node.Invoke(() => IsSelectionRequired(node.Peer!)))
            return false;

        AutomationPeer[] selected = node.Invoke(() => GetSelectionPeers(node.Peer!));
        bool cleared = true;
        foreach (AutomationPeer peer in selected)
            cleared &= TryChangeSelectionItem(node, peer, SelectionChange.Remove);
        return cleared;
    }

    private static bool TryChangeTableRowSelection(
        AtSpiNode table,
        int row,
        SelectionChange change)
    {
        var (rows, columns) = GetTableDimensions(table);
        if (row < 0 || row >= rows)
            return false;

        AutomationPeer? itemPeer = table.Invoke(() =>
        {
            RawProvider.IGridProvider? grid = GetGridProvider(table.Peer!);
            AutomationPeer? firstCell = columns > 0 && grid != null
                ? GetPeer(grid.GetItem(row, 0))
                : null;
            if (HasSelectionItemPattern(firstCell))
                return firstCell;

            IReadOnlyList<AutomationPeer> children = table.Peer!.GetChildren();
            return row < children.Count ? children[row] : null;
        });
        return TryChangeSelectionItem(table, itemPeer, change);
    }

    private static bool HasSelectionItemPattern(AutomationPeer? peer) =>
        peer?.GetPattern(PatternInterface.SelectionItem) is RawProvider.ISelectionItemProvider;

    private static bool TryChangeSelectionItem(
        AtSpiNode container,
        AutomationPeer? itemPeer,
        SelectionChange change)
    {
        if (itemPeer == null)
            return false;

        bool changed = false;
        bool invoked = container.TryInvoke(peer =>
        {
            object? pattern = itemPeer.GetPattern(PatternInterface.SelectionItem);
            bool useAdd = change == SelectionChange.Add && CanSelectMultiple(peer);
            if (pattern is RawProvider.ISelectionItemProvider provider)
            {
                if (change == SelectionChange.Remove) provider.RemoveFromSelection();
                else if (useAdd) provider.AddToSelection();
                else provider.Select();
                changed = true;
            }
        });
        return invoked && changed;
    }

    private static bool IsValidTableCell(int row, int column, int rows, int columns) =>
        row >= 0 && row < rows && column >= 0 && column < columns;

    private static string IntArray(IEnumerable<int> values)
    {
        string content = string.Join(", ", values.Select(static value => $"int32 {value}"));
        return $"@ai [{content}]";
    }

    private static int GetAccessibleChildCount(AtSpiNode node)
    {
        if (node.Interfaces.Contains(TableInterface, StringComparer.Ordinal))
        {
            var (rows, columns) = GetTableDimensions(node);
            return (int)Math.Min(int.MaxValue, (long)rows * columns);
        }
        return GetChildren(node, updateCache: true).Count;
    }

    private static AtSpiNode? GetChildAtIndex(AtSpiNode node, int index)
    {
        if (index < 0)
            return null;
        if (node.Interfaces.Contains(TableInterface, StringComparer.Ordinal))
        {
            var (rows, columns) = GetTableDimensions(node);
            if (columns == 0 || index >= (long)rows * columns)
                return null;
            return GetTableCellNode(node, index / columns, index % columns);
        }

        IReadOnlyList<AtSpiNode> children = GetChildren(node, updateCache: true);
        return index < children.Count ? children[index] : null;
    }

    private static IReadOnlyList<AtSpiNode> GetChildren(AtSpiNode node, bool updateCache)
    {
        if (node.IsApplicationRoot)
        {
            lock (s_gate)
                return s_windowNodes.Values.ToArray();
        }

        List<AtSpiNode> children;
        if (node.Interfaces.Contains(TableInterface, StringComparer.Ordinal))
        {
            var (rows, columns) = GetTableDimensions(node);
            long count = (long)rows * columns;
            if (count > int.MaxValue)
                throw new InvalidOperationException("The AT-SPI table is too large to materialize its child array.");
            children = new List<AtSpiNode>((int)count);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (GetTableCellNode(node, row, column) is { } cell)
                        children.Add(cell);
                }
            }
        }
        else
        {
            var peers = node.Invoke(() => node.Peer!.GetChildren());
            children = new List<AtSpiNode>(peers.Count);
            foreach (var peer in peers)
                children.Add(EnsurePeerNode(peer, node.Window!, node.Dispatcher!));
        }
        if (updateCache)
            node.CachedChildPaths = children.Select(static child => child.Path).ToArray();
        return children;
    }

    private static AtSpiNode? GetParent(AtSpiNode node)
    {
        if (node.IsApplicationRoot)
            return null;
        if (node.LogicalParent is { } logicalParent)
            return logicalParent;
        var parent = node.Invoke(() => node.Peer!.GetParent());
        return parent == null ? s_root : EnsurePeerNode(parent, node.Window!, node.Dispatcher!);
    }

    private static int GetIndexInParent(AtSpiNode node)
    {
        if (node.LogicalIndex >= 0)
            return node.LogicalIndex;
        var parent = GetParent(node);
        if (parent == null)
            return -1;
        var children = GetChildren(parent, updateCache: true);
        for (int index = 0; index < children.Count; index++)
        {
            if (ReferenceEquals(children[index], node))
                return index;
        }
        return -1;
    }

    private static AtSpiNode FindAccessibleAtPoint(AtSpiNode node, int x, int y, uint coordinateType)
    {
        var children = GetChildren(node, updateCache: true);
        for (int index = children.Count - 1; index >= 0; index--)
        {
            var child = children[index];
            if (GetBounds(child, coordinateType).Contains(x, y))
                return FindAccessibleAtPoint(child, x, y, coordinateType);
        }
        return node;
    }

    private static AtSpiRect GetBounds(AtSpiNode node, uint coordinateType)
    {
        if (node.IsApplicationRoot || node.Peer == null)
            return default;

        return node.Invoke(() =>
        {
            Rect mapped = node.Peer.GetBoundingRectangle();
            double scale = node.Window?.DpiScale ?? 1.0;
            if (!double.IsFinite(scale) || scale <= 0) scale = 1.0;
            double x = mapped.X * scale;
            double y = mapped.Y * scale;

            if (node.Window != null &&
                ShouldApplyScreenWindowOffset(coordinateType, HasGlobalScreenCoordinates()))
            {
                if (double.IsFinite(node.Window.Left)) x += node.Window.Left * scale;
                if (double.IsFinite(node.Window.Top)) y += node.Window.Top * scale;
            }
            else if (coordinateType == 2)
            {
                var parent = node.Peer.GetParent();
                if (parent is not null)
                {
                    Rect parentMapped = parent.GetBoundingRectangle();
                    x -= parentMapped.X * scale;
                    y -= parentMapped.Y * scale;
                }
            }

            return new AtSpiRect(
                ToInt(x), ToInt(y),
                Math.Max(0, ToInt(mapped.Width * scale)),
                Math.Max(0, ToInt(mapped.Height * scale)));
        });
    }

    internal static bool ShouldApplyScreenWindowOffset(
        uint coordinateType,
        bool hasGlobalScreenCoordinates) =>
        coordinateType == 0 && hasGlobalScreenCoordinates;

    private static bool HasGlobalScreenCoordinates()
    {
        if (!OperatingSystem.IsLinux())
            return true;

        try
        {
            // X11 supplies a compositor-independent global root coordinate.
            // Core Wayland intentionally does not. Unknown Linux backends use
            // the honest WINDOW-coordinate fallback rather than fabricating a
            // screen origin from Window.Left/Top.
            return Jalium.UI.Interop.NativeMethods.PlatformGetCurrent() ==
                (int)Jalium.UI.Interop.NativePlatform.LinuxX11;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static AtSpiRect GetTextRangeBounds(
        AtSpiNode node,
        int start,
        int length,
        uint coordinateType)
    {
        IReadOnlyList<Rect> localRectangles = node.Invoke(() =>
        {
            if (node.Peer!.GetPattern(PatternInterface.Text) is not RawProvider.ITextProvider provider)
                return Array.Empty<Rect>();
            IAutomationTextProviderSource? source = GetTextSource(provider);
            if (source is null)
                return Array.Empty<Rect>();

            var map = new AtSpiTextMap(source.Text);
            int clampedStart = Math.Clamp(start, 0, map.CharacterCount);
            int clampedLength = Math.Clamp(length, 0, map.CharacterCount - clampedStart);
            int utf16Start = map.ToUtf16Offset(clampedStart);
            int utf16End = map.ToUtf16Offset(clampedStart + clampedLength);
            return source.GetBoundingRectangles(utf16Start, utf16End - utf16Start);
        });
        if (localRectangles.Count == 0)
            return default;

        AtSpiRect ownerBounds = GetBounds(node, coordinateType);
        double scale = node.Window?.DpiScale ?? 1.0;
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1.0;

        Rect union = Rect.Empty;
        foreach (Rect local in localRectangles)
        {
            if (local.IsEmpty || !double.IsFinite(local.X) || !double.IsFinite(local.Y) ||
                !double.IsFinite(local.Width) || !double.IsFinite(local.Height))
            {
                continue;
            }
            var mapped = new Rect(
                ownerBounds.X + local.X * scale,
                ownerBounds.Y + local.Y * scale,
                Math.Max(0, local.Width * scale),
                Math.Max(0, local.Height * scale));
            union = union.IsEmpty ? mapped : Rect.Union(union, mapped);
        }

        return union.IsEmpty
            ? default
            : new AtSpiRect(
                ToInt(union.X),
                ToInt(union.Y),
                Math.Max(0, ToInt(union.Width)),
                Math.Max(0, ToInt(union.Height)));
    }

    private static int GetTextOffsetAtPoint(
        AtSpiNode node,
        int characterCount,
        int x,
        int y,
        uint coordinateType)
    {
        if (characterCount <= 0)
            return 0;

        int nearest = 0;
        long nearestDistance = long.MaxValue;
        for (int offset = 0; offset < characterCount; offset++)
        {
            AtSpiRect bounds = GetTextRangeBounds(node, offset, 1, coordinateType);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;
            if (bounds.Contains(x, y))
                return offset;

            int nearestX = Math.Clamp(x, bounds.X, bounds.X + bounds.Width);
            int nearestY = Math.Clamp(y, bounds.Y, bounds.Y + bounds.Height);
            long dx = (long)x - nearestX;
            long dy = (long)y - nearestY;
            long distance = dx * dx + dy * dy;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = offset;
            }
        }
        return nearest;
    }

    private static (int Start, int Length) GetTextSelection(AtSpiNode node) =>
        node.Invoke(() =>
        {
            if (node.Peer!.GetPattern(PatternInterface.Text) is not RawProvider.ITextProvider provider)
                return (0, 0);
            IAutomationTextProviderSource? source = GetTextSource(provider);
            if (source is null)
                return (0, 0);

            var map = new AtSpiTextMap(source.Text);
            int start = map.ToScalarOffset(source.SelectionStart);
            int end = source.SelectionLength == 0
                ? start
                : map.ToScalarOffset(
                    source.SelectionStart + source.SelectionLength,
                    roundUp: true);
            return (start, Math.Max(0, end - start));
        });

    private static bool SelectText(AtSpiNode node, int start, int length)
    {
        return node.TryInvoke(peer =>
        {
            if (peer.GetPattern(PatternInterface.Text) is not RawProvider.ITextProvider provider)
                throw new NotSupportedException("This object does not provide selectable text.");
            IAutomationTextProviderSource? source = GetTextSource(provider);
            if (source is null)
                throw new NotSupportedException("This text provider does not expose an editable offset source.");

            var map = new AtSpiTextMap(source.Text);
            int scalarStart = Math.Clamp(start, 0, map.CharacterCount);
            int scalarLength = Math.Clamp(length, 0, map.CharacterCount - scalarStart);
            int utf16Start = map.ToUtf16Offset(scalarStart);
            int utf16End = map.ToUtf16Offset(scalarStart + scalarLength);
            source.Select(utf16Start, utf16End - utf16Start);
        });
    }

    private static IAutomationTextProviderSource? GetTextSource(RawProvider.ITextProvider provider) =>
        provider is RawProvider.AutomationTextProvider adapter ? adapter.Source : null;

    private static AtSpiAction GetAction(AutomationPeer peer)
    {
        if (peer.GetPattern(PatternInterface.Invoke) is RawProvider.IInvokeProvider)
            return new(AtSpiActionKind.Invoke, "click", "Activate the control");
        if (peer.GetPattern(PatternInterface.Toggle) is RawProvider.IToggleProvider)
            return new(AtSpiActionKind.Toggle, "toggle", "Toggle the control state");
        if (peer.GetPattern(PatternInterface.SelectionItem) is RawProvider.ISelectionItemProvider)
            return new(AtSpiActionKind.Select, "select", "Select this item");
        if (peer.GetPattern(PatternInterface.ExpandCollapse) is RawProvider.IExpandCollapseProvider)
            return new(AtSpiActionKind.ExpandOrCollapse, "toggle", "Expand or collapse the control");
        if (peer.GetPattern(PatternInterface.ScrollItem) is RawProvider.IScrollItemProvider)
            return new(AtSpiActionKind.ScrollIntoView, "show", "Scroll this item into view");
        return default;
    }

    private static void ExecuteAction(AutomationPeer peer, AtSpiActionKind kind)
    {
        switch (kind)
        {
            case AtSpiActionKind.Invoke:
                ((RawProvider.IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                break;
            case AtSpiActionKind.Toggle:
                ((RawProvider.IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
                break;
            case AtSpiActionKind.Select:
                ((RawProvider.ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!).Select();
                break;
            case AtSpiActionKind.ExpandOrCollapse:
            {
                var expand = (RawProvider.IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
                if (expand.ExpandCollapseState == ExpandCollapseState.Collapsed) expand.Expand();
                else expand.Collapse();
                break;
            }
            case AtSpiActionKind.ScrollIntoView:
                ((RawProvider.IScrollItemProvider)peer.GetPattern(PatternInterface.ScrollItem)!).ScrollIntoView();
                break;
        }
    }

    private static void RaiseAutomationEvent(AutomationPeer peer, AutomationEvents eventId)
    {
        if (!TryGetNode(peer, out var node))
            return;

        switch (eventId)
        {
            case AutomationEvents.AutomationFocusChanged:
                RaiseFocusChanged(peer);
                break;
            case AutomationEvents.StructureChanged:
                RefreshChildrenAndEmitDiff(node);
                break;
            case AutomationEvents.TextPatternOnTextChanged:
                EmitTextChanges(node, AtSpiModel.GetText(peer));
                break;
            case AutomationEvents.TextPatternOnTextSelectionChanged:
                EmitObjectSignal(node, "TextSelectionChanged", string.Empty, 0, 0, "<0>");
                break;
            case AutomationEvents.SelectionItemPatternOnElementAddedToSelection:
            case AutomationEvents.SelectionItemPatternOnElementRemovedFromSelection:
            case AutomationEvents.SelectionItemPatternOnElementSelected:
            case AutomationEvents.SelectionPatternOnInvalidated:
                EmitObjectSignal(node, "SelectionChanged", string.Empty, 0, 0, "<0>");
                break;
        }
    }

    private static void RaisePropertyChanged(
        AutomationPeer peer,
        AutomationProperty property,
        object? oldValue,
        object? newValue)
    {
        if (!TryGetNode(peer, out var node))
            return;

        if (ReferenceEquals(property, AutomationProperty.BoundingRectangleProperty))
        {
            var bounds = GetBounds(node, 0);
            EmitObjectSignal(node, "BoundsChanged", string.Empty, 0, 0,
                $"<({bounds.X}, {bounds.Y}, {bounds.Width}, {bounds.Height})>");
            return;
        }

        string atSpiName = property.Name switch
        {
            "Name" => "accessible-name",
            "AutomationId" => "accessible-id",
            "Value" or "RangeValue" => "accessible-value",
            _ => property.Name.ToLowerInvariant(),
        };
        EmitObjectSignal(node, "PropertyChange", atSpiName, 0, 0, Variant(newValue));

        // Writable text providers commonly surface value changes through the
        // Value property rather than raising a second TextPattern event. Keep
        // the AT-SPI-only text cache in sync here; a later TextPattern event is
        // harmless because the cached value already matches.
        if (node.HasText && ReferenceEquals(property, AutomationProperty.ValueProperty))
            EmitTextChanges(node, AtSpiModel.GetText(peer));

        if (ReferenceEquals(property, AutomationProperty.IsEnabledProperty))
            EmitStateChanged(node, "enabled", newValue is true);
        else if (ReferenceEquals(property, AutomationProperty.HasKeyboardFocusProperty))
            EmitStateChanged(node, "focused", newValue is true);
        else if (ReferenceEquals(property, AutomationProperty.IsOffscreenProperty))
        {
            bool visible = newValue is not true;
            EmitStateChanged(node, "visible", visible);
            EmitStateChanged(node, "showing", visible);
        }
        else if (ReferenceEquals(property, AutomationProperty.ToggleStateProperty))
            EmitStateChanged(node, "checked", Equals(newValue, ToggleState.On));
    }

    private static void EmitTextChanges(AtSpiNode node, string currentText)
    {
        AtSpiTextChange change;
        lock (node)
        {
            change = AtSpiModel.GetTextChange(node.CachedText, currentText);
            node.CachedText = currentText;
        }

        // AT-SPI replacements are represented as a delete followed by an
        // insert at the same Unicode-scalar offset.
        if (change.DeletedCount > 0)
        {
            EmitObjectSignal(
                node,
                "TextChanged",
                "delete",
                change.Offset,
                change.DeletedCount,
                $"<{AtSpiModel.Quote(change.DeletedText)}>");
        }
        if (change.InsertedCount > 0)
        {
            EmitObjectSignal(
                node,
                "TextChanged",
                "insert",
                change.Offset,
                change.InsertedCount,
                $"<{AtSpiModel.Quote(change.InsertedText)}>");
        }
    }

    private static void RaiseFocusChanged(AutomationPeer peer)
    {
        if (!TryGetNode(peer, out var node))
            return;

        var old = Interlocked.Exchange(ref s_focusedNode, node);
        if (old != null && !ReferenceEquals(old, node))
            EmitStateChanged(old, "focused", false);
        EmitStateChanged(node, "focused", true);
        EmitObjectSignal(node, "Focus", string.Empty, 1, 0, "<0>", FocusEventInterface);
    }

    private static void RefreshChildrenAndEmitDiff(AtSpiNode parent)
    {
        string[] previous = parent.CachedChildPaths;
        var current = GetChildren(parent, updateCache: true);
        string[] currentPaths = current.Select(static child => child.Path).ToArray();

        for (int index = 0; index < previous.Length; index++)
        {
            if (!currentPaths.Contains(previous[index], StringComparer.Ordinal))
            {
                AtSpiNode? removed;
                lock (s_gate) _ = s_pathNodes.TryGetValue(previous[index], out removed);
                EmitChildrenChanged(parent, "remove", index, removed);
            }
        }

        for (int index = 0; index < current.Count; index++)
        {
            if (!previous.Contains(current[index].Path, StringComparer.Ordinal))
                EmitChildrenChanged(parent, "add", index, current[index]);
        }
    }

    private static void EmitChildrenChanged(AtSpiNode parent, string operation, int index, AtSpiNode? child) =>
        EmitObjectSignal(parent, "ChildrenChanged", operation, index, 0,
            child == null ? $"<{Reference(null)}>" : $"<{Reference(child)}>");

    private static void EmitStateChanged(AtSpiNode node, string state, bool enabled) =>
        EmitObjectSignal(node, "StateChanged", state, enabled ? 1 : 0, 0, "<0>");

    private static void EmitWindowSignal(AtSpiNode node, string signal) =>
        EmitObjectSignal(node, signal, string.Empty, 0, 0, "<0>", WindowEventInterface);

    private static void EmitObjectSignal(
        AtSpiNode node,
        string signal,
        string detail,
        int detail1,
        int detail2,
        string variant,
        string interfaceName = ObjectEventInterface)
    {
        if (!s_active || node.Defunct && signal != "Destroy")
            return;
        _ = AtSpiNative.EmitSignal(
            s_connection,
            node.Path,
            interfaceName,
            signal,
            $"({AtSpiModel.Quote(detail)}, {detail1}, {detail2}, {variant}, @a{{sv}} {{}})");
    }

    private static bool TryGetNode(AutomationPeer peer, out AtSpiNode node)
    {
        lock (s_gate)
        {
            if (s_peerNodes.TryGetValue(peer, out node!))
                return true;
        }

        DependencyObject? owner = peer.Owner;
        if (owner is null)
        {
            node = null!;
            return false;
        }

        var window = Window.GetWindow(owner);
        if (window == null || !s_active)
        {
            node = null!;
            return false;
        }

        node = EnsurePeerNode(peer, window, window.Dispatcher);
        return true;
    }

    private static AtSpiNode NodeFromUserData(nint userData)
    {
        if (userData == 0)
            throw new InvalidOperationException("AT-SPI object registration has no user data.");
        return (AtSpiNode)(GCHandle.FromIntPtr(userData).Target ??
            throw new InvalidOperationException("AT-SPI object registration has expired."));
    }

    private static string Reference(AtSpiNode? node)
    {
        if (node == null)
            return AtSpiModel.ObjectReference(string.Empty, NullPath);
        return AtSpiModel.ObjectReference(s_uniqueName, node.Path);
    }

    private static string Variant(object? value) => value switch
    {
        null => "<''>",
        string text => $"<{AtSpiModel.Quote(text)}>",
        bool flag => $"<{Bool(flag)}>",
        int number => $"<int32 {number}>",
        uint number => $"<uint32 {number}>",
        double number => $"<double {number.ToString("R", CultureInfo.InvariantCulture)}>",
        _ => $"<{AtSpiModel.Quote(value.ToString())}>",
    };

    private static string Bool(bool value) => value ? "true" : "false";

    private static int ToInt(double value)
    {
        if (!double.IsFinite(value)) return 0;
        return (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);
    }

    private static string CurrentLocale()
    {
        string locale = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrWhiteSpace(locale) ? "C" : locale.Replace('-', '_');
    }

    private static string ApplicationName()
    {
        string value = AppDomain.CurrentDomain.FriendlyName;
        return string.IsNullOrWhiteSpace(value)
            ? System.Diagnostics.Process.GetCurrentProcess().ProcessName
            : value;
    }

    private static bool IsDisabled()
    {
        string? value = Environment.GetEnvironmentVariable("JALIUM_ATSPI_DISABLE");
        if (value is not ("1" or "true" or "TRUE" or "yes" or "YES"))
            return false;
        s_status = "disabled";
        return true;
    }

    private static T Safe<T>(Func<T> action, T fallback)
    {
        try { return action(); }
        catch { return fallback; }
    }

    private sealed class AtSpiAutomationEventSink : IAutomationEventSink
    {
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId) =>
            RaiseAutomationEvent(peer, eventId);

        public void OnPropertyChangedRaised(
            AutomationPeer peer,
            AutomationProperty property,
            object? oldValue,
            object? newValue) =>
            RaisePropertyChanged(peer, property, oldValue, newValue);

        public void OnFocusChanged(AutomationPeer peer) => RaiseFocusChanged(peer);
    }

    private sealed class AtSpiNode
    {
        private GCHandle _handle;

        internal AtSpiNode(
            string path,
            AutomationPeer? peer,
            Window? window,
            Dispatcher? dispatcher,
            AtSpiRole role,
            bool hasAction,
            bool hasText,
            string initialText)
        {
            Path = path;
            Peer = peer;
            Window = window;
            Dispatcher = dispatcher;
            Role = role;
            HasAction = hasAction;
            HasText = hasText;
            CachedText = initialText;
        }

        internal string Path { get; }
        internal AutomationPeer? Peer { get; }
        internal Window? Window { get; }
        internal Dispatcher? Dispatcher { get; }
        internal AtSpiRole Role { get; }
        internal bool HasAction { get; }
        internal bool HasText { get; }
        internal string CachedText { get; set; }
        internal bool IsApplicationRoot => Peer == null;
        internal bool Defunct { get; set; }
        internal AtSpiNode? LogicalParent { get; set; }
        internal int LogicalIndex { get; set; } = -1;
        internal string[] CachedChildPaths { get; set; } = [];
        internal List<string> Interfaces { get; } = [];
        internal List<uint> RegistrationIds { get; } = [];
        internal nint UserData => _handle.IsAllocated ? GCHandle.ToIntPtr(_handle) : 0;

        internal static AtSpiNode CreateApplicationRoot() =>
            new(RootPath, null, null, null, AtSpiRole.Application, false, false, string.Empty);

        internal void AllocateHandle()
        {
            if (!_handle.IsAllocated)
                _handle = GCHandle.Alloc(this, GCHandleType.Normal);
        }

        internal T Invoke<T>(Func<T> callback)
        {
            if (Defunct)
                throw new InvalidOperationException("The accessible object is defunct.");
            if (Dispatcher == null || Dispatcher.CheckAccess())
                return callback();
            if (Dispatcher.HasShutdownStarted)
                throw new InvalidOperationException("The UI dispatcher is shutting down.");
            return Dispatcher.Invoke(callback);
        }

        internal bool TryInvoke(Action<AutomationPeer> callback)
        {
            if (Peer == null || Defunct)
                return false;
            try
            {
                _ = Invoke(() =>
                {
                    callback(Peer);
                    return true;
                });
                return true;
            }
            catch (Exception ex)
            {
                AtSpiTrace.Log($"UI action failed path={Path}: {ex}");
                return false;
            }
        }

        internal void Unregister(nint connection)
        {
            foreach (uint id in RegistrationIds)
                AtSpiNative.UnregisterObject(connection, id);
            RegistrationIds.Clear();
            Interfaces.Clear();
            if (_handle.IsAllocated)
                _handle.Free();
        }
    }

    private readonly record struct AtSpiAction(
        AtSpiActionKind Kind,
        string Name,
        string Description);

    private readonly record struct AtSpiNodeDescriptor(
        AtSpiRole Role,
        bool HasAction,
        bool HasText,
        bool HasValue,
        bool HasEditableText,
        bool HasSelection,
        bool HasTable,
        string InitialText);

    private enum SelectionChange
    {
        Add,
        Remove,
    }

    private enum AtSpiActionKind
    {
        None,
        Invoke,
        Toggle,
        Select,
        ExpandOrCollapse,
        ScrollIntoView,
    }

    private readonly record struct AtSpiRect(int X, int Y, int Width, int Height)
    {
        internal bool Contains(int x, int y) =>
            Width > 0 && Height > 0 && x >= X && y >= Y && x < X + Width && y < Y + Height;
    }

    private const string IntrospectionXml = """
        <node>
          <interface name="org.a11y.atspi.Accessible">
            <property name="version" type="u" access="read"/>
            <property name="Name" type="s" access="read"/>
            <property name="Description" type="s" access="read"/>
            <property name="Parent" type="(so)" access="read"/>
            <property name="ChildCount" type="i" access="read"/>
            <property name="Locale" type="s" access="read"/>
            <property name="AccessibleId" type="s" access="read"/>
            <property name="HelpText" type="s" access="read"/>
            <method name="GetChildAtIndex"><arg direction="in" type="i"/><arg direction="out" type="(so)"/></method>
            <method name="GetChildren"><arg direction="out" type="a(so)"/></method>
            <method name="GetIndexInParent"><arg direction="out" type="i"/></method>
            <method name="GetRelationSet"><arg direction="out" type="a(ua(so))"/></method>
            <method name="GetRole"><arg direction="out" type="u"/></method>
            <method name="GetRoleName"><arg direction="out" type="s"/></method>
            <method name="GetLocalizedRoleName"><arg direction="out" type="s"/></method>
            <method name="GetState"><arg direction="out" type="au"/></method>
            <method name="GetAttributes"><arg direction="out" type="a{ss}"/></method>
            <method name="GetApplication"><arg direction="out" type="(so)"/></method>
            <method name="GetInterfaces"><arg direction="out" type="as"/></method>
          </interface>
          <interface name="org.a11y.atspi.Component">
            <property name="version" type="u" access="read"/>
            <method name="Contains"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="b"/></method>
            <method name="GetAccessibleAtPoint"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="(so)"/></method>
            <method name="GetExtents"><arg direction="in" type="u"/><arg direction="out" type="(iiii)"/></method>
            <method name="GetPosition"><arg direction="in" type="u"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetSize"><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetLayer"><arg direction="out" type="u"/></method>
            <method name="GetMDIZOrder"><arg direction="out" type="n"/></method>
            <method name="GrabFocus"><arg direction="out" type="b"/></method>
            <method name="GetAlpha"><arg direction="out" type="d"/></method>
            <method name="SetExtents"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="b"/></method>
            <method name="SetPosition"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="b"/></method>
            <method name="SetSize"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="ScrollTo"><arg direction="in" type="u"/><arg direction="out" type="b"/></method>
            <method name="ScrollToPoint"><arg direction="in" type="u"/><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
          </interface>
          <interface name="org.a11y.atspi.Action">
            <property name="version" type="u" access="read"/>
            <property name="NActions" type="i" access="read"/>
            <method name="GetDescription"><arg direction="in" type="i"/><arg direction="out" type="s"/></method>
            <method name="GetName"><arg direction="in" type="i"/><arg direction="out" type="s"/></method>
            <method name="GetLocalizedName"><arg direction="in" type="i"/><arg direction="out" type="s"/></method>
            <method name="GetKeyBinding"><arg direction="in" type="i"/><arg direction="out" type="s"/></method>
            <method name="GetActions"><arg direction="out" type="a(sss)"/></method>
            <method name="DoAction"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
          </interface>
          <interface name="org.a11y.atspi.Text">
            <property name="version" type="u" access="read"/>
            <property name="CharacterCount" type="i" access="read"/>
            <property name="CaretOffset" type="i" access="read"/>
            <method name="GetStringAtOffset"><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="s"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetText"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="s"/></method>
            <method name="SetCaretOffset"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="GetTextBeforeOffset"><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="s"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetTextAtOffset"><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="s"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetTextAfterOffset"><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="s"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetCharacterAtOffset"><arg direction="in" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetAttributeValue"><arg direction="in" type="i"/><arg direction="in" type="s"/><arg direction="out" type="s"/></method>
            <method name="GetAttributes"><arg direction="in" type="i"/><arg direction="out" type="a{ss}"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetDefaultAttributes"><arg direction="out" type="a{ss}"/></method>
            <method name="GetCharacterExtents"><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="i"/><arg direction="out" type="i"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetOffsetAtPoint"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="i"/></method>
            <method name="GetNSelections"><arg direction="out" type="i"/></method>
            <method name="GetSelection"><arg direction="in" type="i"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="AddSelection"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="RemoveSelection"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="SetSelection"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="GetRangeExtents"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="i"/><arg direction="out" type="i"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetBoundedRanges"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="in" type="u"/><arg direction="in" type="u"/><arg direction="out" type="a(iisv)"/></method>
            <method name="GetAttributeRun"><arg direction="in" type="i"/><arg direction="in" type="b"/><arg direction="out" type="a{ss}"/><arg direction="out" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetDefaultAttributeSet"><arg direction="out" type="a{ss}"/></method>
            <method name="ScrollSubstringTo"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="out" type="b"/></method>
            <method name="ScrollSubstringToPoint"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="in" type="u"/><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
          </interface>
          <interface name="org.a11y.atspi.Value">
            <property name="version" type="u" access="read"/>
            <property name="MinimumValue" type="d" access="read"/>
            <property name="MaximumValue" type="d" access="read"/>
            <property name="MinimumIncrement" type="d" access="read"/>
            <property name="CurrentValue" type="d" access="readwrite"/>
            <property name="Text" type="s" access="read"/>
          </interface>
          <interface name="org.a11y.atspi.EditableText">
            <property name="version" type="u" access="read"/>
            <method name="SetTextContents"><arg direction="in" type="s"/><arg direction="out" type="b"/></method>
            <method name="InsertText"><arg direction="in" type="i"/><arg direction="in" type="s"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="CopyText"><arg direction="in" type="i"/><arg direction="in" type="i"/></method>
            <method name="CutText"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="DeleteText"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="PasteText"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
          </interface>
          <interface name="org.a11y.atspi.Selection">
            <property name="version" type="u" access="read"/>
            <property name="NSelectedChildren" type="i" access="read"/>
            <method name="GetSelectedChild"><arg direction="in" type="i"/><arg direction="out" type="(so)"/></method>
            <method name="SelectChild"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="DeselectSelectedChild"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="IsChildSelected"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="SelectAll"><arg direction="out" type="b"/></method>
            <method name="ClearSelection"><arg direction="out" type="b"/></method>
            <method name="DeselectChild"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
          </interface>
          <interface name="org.a11y.atspi.Table">
            <property name="version" type="u" access="read"/>
            <property name="NRows" type="i" access="read"/>
            <property name="NColumns" type="i" access="read"/>
            <property name="Caption" type="(so)" access="read"/>
            <property name="Summary" type="(so)" access="read"/>
            <property name="NSelectedRows" type="i" access="read"/>
            <property name="NSelectedColumns" type="i" access="read"/>
            <method name="GetAccessibleAt"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="(so)"/></method>
            <method name="GetIndexAt"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetRowAtIndex"><arg direction="in" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetColumnAtIndex"><arg direction="in" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetRowDescription"><arg direction="in" type="i"/><arg direction="out" type="s"/></method>
            <method name="GetColumnDescription"><arg direction="in" type="i"/><arg direction="out" type="s"/></method>
            <method name="GetRowExtentAt"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetColumnExtentAt"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="i"/></method>
            <method name="GetRowHeader"><arg direction="in" type="i"/><arg direction="out" type="(so)"/></method>
            <method name="GetColumnHeader"><arg direction="in" type="i"/><arg direction="out" type="(so)"/></method>
            <method name="GetSelectedRows"><arg direction="out" type="ai"/></method>
            <method name="GetSelectedColumns"><arg direction="out" type="ai"/></method>
            <method name="IsRowSelected"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="IsColumnSelected"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="IsSelected"><arg direction="in" type="i"/><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="AddRowSelection"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="AddColumnSelection"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="RemoveRowSelection"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="RemoveColumnSelection"><arg direction="in" type="i"/><arg direction="out" type="b"/></method>
            <method name="GetRowColumnExtentsAtIndex">
              <arg direction="in" type="i"/><arg direction="out" type="b"/>
              <arg direction="out" type="i"/><arg direction="out" type="i"/>
              <arg direction="out" type="i"/><arg direction="out" type="i"/>
              <arg direction="out" type="b"/>
            </method>
          </interface>
          <interface name="org.a11y.atspi.Application">
            <property name="ToolkitName" type="s" access="read"/>
            <property name="Version" type="s" access="read"/>
            <property name="ToolkitVersion" type="s" access="read"/>
            <property name="AtspiVersion" type="s" access="read"/>
            <property name="InterfaceVersion" type="u" access="read"/>
            <property name="Id" type="i" access="readwrite"/>
            <method name="GetLocale"><arg direction="in" type="u"/><arg direction="out" type="s"/></method>
            <method name="GetApplicationBusAddress"><arg direction="out" type="s"/></method>
          </interface>
        </node>
        """;
}
