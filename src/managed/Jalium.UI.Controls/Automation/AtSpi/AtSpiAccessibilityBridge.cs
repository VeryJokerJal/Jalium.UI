using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Jalium.UI.Automation;
using Jalium.UI.Threading;

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

        return false;
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
            descriptor.Role, descriptor.HasAction, descriptor.HasText);
        var interfaces = new List<string> { AccessibleInterface, ComponentInterface };
        if (descriptor.HasAction) interfaces.Add(ActionInterface);
        if (descriptor.HasText) interfaces.Add(TextInterface);

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
        bool hasText = peer.GetPattern(PatternInterface.Text) is ITextProvider ||
                       peer.GetPattern(PatternInterface.Value) is IValueProvider ||
                       role is AtSpiRole.Label or AtSpiRole.Text or AtSpiRole.Entry or AtSpiRole.PasswordText;
        return new AtSpiNodeDescriptor(role, hasAction, hasText);
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
                var children = GetChildren(node, updateCache: true);
                var child = index >= 0 && index < children.Count ? children[index] : null;
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
                    if (peer.GetPattern(PatternInterface.ScrollItem) is IScrollItemProvider scroll)
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
        switch (method)
        {
            case "GetStringAtOffset":
            case "GetTextAtOffset":
            {
                var result = AtSpiModel.GetStringAtOffset(
                    text, AtSpiNative.GetInt32(parameters, 0), AtSpiNative.GetUInt32(parameters, 1));
                return $"({AtSpiModel.Quote(result.Text)}, {result.Start}, {result.End})";
            }
            case "GetText":
                return $"({AtSpiModel.Quote(AtSpiModel.SliceText(text, AtSpiNative.GetInt32(parameters, 0), AtSpiNative.GetInt32(parameters, 1)))},)";
            case "SetCaretOffset":
            {
                int offset = Math.Clamp(AtSpiNative.GetInt32(parameters, 0), 0, text.Length);
                return $"({Bool(SelectText(node, offset, 0))},)";
            }
            case "GetTextBeforeOffset":
            {
                int offset = Math.Clamp(AtSpiNative.GetInt32(parameters, 0), 0, text.Length);
                string before = text[..offset];
                var result = before.Length == 0
                    ? (string.Empty, 0, 0)
                    : AtSpiModel.GetStringAtOffset(before, before.Length - 1, AtSpiNative.GetUInt32(parameters, 1));
                return $"({AtSpiModel.Quote(result.Item1)}, {result.Item2}, {result.Item3})";
            }
            case "GetTextAfterOffset":
            {
                int offset = Math.Clamp(AtSpiNative.GetInt32(parameters, 0) + 1, 0, text.Length);
                if (offset >= text.Length) return "('', 0, 0)";
                var result = AtSpiModel.GetStringAtOffset(text, offset, AtSpiNative.GetUInt32(parameters, 1));
                return $"({AtSpiModel.Quote(result.Text)}, {result.Start}, {result.End})";
            }
            case "GetCharacterAtOffset":
            {
                int offset = AtSpiNative.GetInt32(parameters, 0);
                int codePoint = -1;
                if (offset >= 0 && offset < text.Length)
                {
                    char current = text[offset];
                    if (char.IsHighSurrogate(current) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]))
                        codePoint = char.ConvertToUtf32(current, text[offset + 1]);
                    else if (char.IsLowSurrogate(current) && offset > 0 && char.IsHighSurrogate(text[offset - 1]))
                        codePoint = char.ConvertToUtf32(text[offset - 1], current);
                    else
                        codePoint = current;
                }
                return $"({codePoint},)";
            }
            case "GetAttributeValue":
                return "('',)";
            case "GetAttributes":
            case "GetAttributeRun":
                return $"(@a{{ss}} {{}}, 0, {text.Length})";
            case "GetDefaultAttributes":
            case "GetDefaultAttributeSet":
                return "(@a{ss} {},)";
            case "GetCharacterExtents":
            case "GetRangeExtents":
            {
                uint coord = AtSpiNative.GetUInt32(parameters, method == "GetCharacterExtents" ? 1 : 2);
                var rect = GetBounds(node, coord);
                return $"({rect.X}, {rect.Y}, {rect.Width}, {rect.Height})";
            }
            case "GetOffsetAtPoint":
            {
                var rect = GetBounds(node, AtSpiNative.GetUInt32(parameters, 2));
                int x = AtSpiNative.GetInt32(parameters, 0);
                int offset = rect.Width <= 0 || text.Length == 0
                    ? 0
                    : Math.Clamp((int)((x - rect.X) / (double)rect.Width * text.Length), 0, text.Length);
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
                return $"({Bool(SelectText(node, start, Math.Max(0, end - start)))},)";
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
                    if (peer.GetPattern(PatternInterface.Text) is ITextProvider provider)
                        provider.ScrollIntoView(start, Math.Max(0, end - start));
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
            "ChildCount" => $"int32 {GetChildren(node, updateCache: true).Count}",
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
            "CharacterCount" => $"int32 {node.Invoke(() => AtSpiModel.GetText(node.Peer!).Length)}",
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

    private static IReadOnlyList<AtSpiNode> GetChildren(AtSpiNode node, bool updateCache)
    {
        if (node.IsApplicationRoot)
        {
            lock (s_gate)
                return s_windowNodes.Values.ToArray();
        }

        var peers = node.Invoke(() => node.Peer!.GetChildren());
        var children = new List<AtSpiNode>(peers.Count);
        foreach (var peer in peers)
            children.Add(EnsurePeerNode(peer, node.Window!, node.Dispatcher!));
        if (updateCache)
            node.CachedChildPaths = children.Select(static child => child.Path).ToArray();
        return children;
    }

    private static AtSpiNode? GetParent(AtSpiNode node)
    {
        if (node.IsApplicationRoot)
            return null;
        var parent = node.Invoke(() => node.Peer!.GetParent());
        return parent == null ? s_root : EnsurePeerNode(parent, node.Window!, node.Dispatcher!);
    }

    private static int GetIndexInParent(AtSpiNode node)
    {
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

            if (coordinateType == 0 && node.Window != null)
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

    private static (int Start, int Length) GetTextSelection(AtSpiNode node) =>
        node.Invoke(() => node.Peer!.GetPattern(PatternInterface.Text) is ITextProvider provider
            ? (provider.SelectionStart, provider.SelectionLength)
            : (0, 0));

    private static bool SelectText(AtSpiNode node, int start, int length)
    {
        return node.TryInvoke(peer =>
        {
            if (peer.GetPattern(PatternInterface.Text) is not ITextProvider provider)
                throw new NotSupportedException("This object does not provide selectable text.");
            start = Math.Clamp(start, 0, provider.Text.Length);
            length = Math.Clamp(length, 0, provider.Text.Length - start);
            provider.Select(start, length);
        });
    }

    private static AtSpiAction GetAction(AutomationPeer peer)
    {
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider)
            return new(AtSpiActionKind.Invoke, "click", "Activate the control");
        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider)
            return new(AtSpiActionKind.Toggle, "toggle", "Toggle the control state");
        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider)
            return new(AtSpiActionKind.Select, "select", "Select this item");
        if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider)
            return new(AtSpiActionKind.ExpandOrCollapse, "toggle", "Expand or collapse the control");
        if (peer.GetPattern(PatternInterface.ScrollItem) is IScrollItemProvider)
            return new(AtSpiActionKind.ScrollIntoView, "show", "Scroll this item into view");
        return default;
    }

    private static void ExecuteAction(AutomationPeer peer, AtSpiActionKind kind)
    {
        switch (kind)
        {
            case AtSpiActionKind.Invoke:
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                break;
            case AtSpiActionKind.Toggle:
                ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
                break;
            case AtSpiActionKind.Select:
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!).Select();
                break;
            case AtSpiActionKind.ExpandOrCollapse:
            {
                var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
                if (expand.ExpandCollapseState == ExpandCollapseState.Collapsed) expand.Expand();
                else expand.Collapse();
                break;
            }
            case AtSpiActionKind.ScrollIntoView:
                ((IScrollItemProvider)peer.GetPattern(PatternInterface.ScrollItem)!).ScrollIntoView();
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
                EmitObjectSignal(node, "TextChanged", "insert", 0, 0, $"<{AtSpiModel.Quote(AtSpiModel.GetText(peer))}>");
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
            bool hasText)
        {
            Path = path;
            Peer = peer;
            Window = window;
            Dispatcher = dispatcher;
            Role = role;
            HasAction = hasAction;
            HasText = hasText;
        }

        internal string Path { get; }
        internal AutomationPeer? Peer { get; }
        internal Window? Window { get; }
        internal Dispatcher? Dispatcher { get; }
        internal AtSpiRole Role { get; }
        internal bool HasAction { get; }
        internal bool HasText { get; }
        internal bool IsApplicationRoot => Peer == null;
        internal bool Defunct { get; set; }
        internal string[] CachedChildPaths { get; set; } = [];
        internal List<string> Interfaces { get; } = [];
        internal List<uint> RegistrationIds { get; } = [];
        internal nint UserData => _handle.IsAllocated ? GCHandle.ToIntPtr(_handle) : 0;

        internal static AtSpiNode CreateApplicationRoot() =>
            new(RootPath, null, null, null, AtSpiRole.Application, false, false);

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
        bool HasText);

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
