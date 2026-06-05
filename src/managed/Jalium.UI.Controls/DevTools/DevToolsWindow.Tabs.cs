using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private TabControl? _rootTabs;
    private TabItem? _inspectorTab;
    private TabItem? _layoutTab;
    private TabItem? _eventsTab;
    private TabItem? _bindingsTab;
    private TabItem? _resourcesTab;
    private TabItem? _perfTab;
    private TabItem? _uiaTab;
    private TabItem? _toolsTab;
    private TabItem? _replTab;

    // Legacy brush aliases (read from the central theme so existing partial
    // files continue to compile).
    private static readonly SolidColorBrush BrushTabHeader      = DevToolsTheme.TextPrimary;
    private static readonly SolidColorBrush BrushTabHeaderMuted = DevToolsTheme.TextSecondary;
    private static readonly SolidColorBrush BrushSurfaceDark    = DevToolsTheme.Surface;

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools tab layout includes the REPL which evaluates user input via reflection.")]
    private UIElement BuildTabLayout()
    {
        // Logical tree view is folded into the Inspector tab via the segmented
        // view-mode switcher — no dedicated "Logical" tab anymore.
        _inspectorTab = MakeTab("Inspector", _mainGrid);
        _layoutTab    = MakeTab("Layout",    BuildLayoutTab());
        _eventsTab    = MakeTab("Events",    BuildEventsTab());
        _bindingsTab  = MakeTab("Bindings",  BuildBindingsTab());
        _resourcesTab = MakeTab("Resources", BuildResourcesTab());
        _perfTab      = MakeTab("Perf",      BuildPerfTab());
        _uiaTab       = MakeTab("UIA",       BuildUiaTab());
        _toolsTab     = MakeTab("Tools",     BuildToolsTab());
        _replTab      = MakeTab("REPL",      BuildReplTab());

        _rootTabs = new TabControl
        {
            Background = DevToolsTheme.Surface,
            TabStripBackground = DevToolsTheme.Chrome,
            TabStripBorderBrush = DevToolsTheme.BorderSubtle,
            TabStripHeight = 38,
        };

        _rootTabs.Items.Add(_inspectorTab);
        _rootTabs.Items.Add(_layoutTab);
        _rootTabs.Items.Add(_eventsTab);
        _rootTabs.Items.Add(_bindingsTab);
        _rootTabs.Items.Add(_resourcesTab);
        _rootTabs.Items.Add(_perfTab);
        _rootTabs.Items.Add(_uiaTab);
        _rootTabs.Items.Add(_toolsTab);
        _rootTabs.Items.Add(_replTab);

        _rootTabs.SelectionChanged += OnRootTabSelectionChanged;

        return _rootTabs;
    }

    private static TabItem MakeTab(string header, UIElement content)
    {
        // Instrument front-panel "channel select": uppercase Bahnschrift labels with
        // a thin signal-amber indicator under the active channel.
        return new TabItem
        {
            Header = DevToolsUi.Tracked(header.ToUpperInvariant()),
            Content = content,
            IndicatorBrush = DevToolsTheme.Accent,
            IndicatorHeight = 2,
            SelectedBackground = DevToolsTheme.Surface,
            HoverBackground = DevToolsTheme.ControlHover,
            Foreground = DevToolsTheme.TextPrimary,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
        };
    }

    private void OnRootTabSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (_rootTabs == null) return;
        var selected = _rootTabs.SelectedItem as TabItem;
        if (selected == null) return;

        // Every tab activation recreates its UI (stats rows, graph nodes,
        // property cards). Wrap in the ignored-creation scope so those new
        // UIElements are flagged from their field initializer — no
        // constructor-time InvalidateMeasure leaks into Layout stats.
        using var __scope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();

        if (selected == _layoutTab) OnLayoutTabActivated();
        else if (selected == _eventsTab) OnEventsTabActivated();
        else if (selected == _bindingsTab) OnBindingsTabActivated();
        else if (selected == _perfTab) OnPerfTabActivated();
        else if (selected == _uiaTab) OnUiaTabActivated();
        else if (selected == _resourcesTab) OnResourcesTabActivated();
        else if (selected == _toolsTab) OnToolsTabActivated();
        else if (selected == _replTab) OnReplTabActivated();
    }

    private static Border MakeTabShell(UIElement content)
    {
        return new Border
        {
            Background = DevToolsTheme.Surface,
            Padding = new Thickness(DevToolsTheme.GutterBase),
            Child = content,
            ClipToBounds = true,
        };
    }

    // ── Empty placeholders for partial-class implementations ────────────
    // Each builder below lives in its own file. The default implementations
    // return a simple placeholder so the project keeps compiling while any
    // individual Tab file is temporarily missing. The real partial methods
    // have higher priority at link time.

    partial void OnLayoutTabActivated();
    partial void OnEventsTabActivated();
    partial void OnBindingsTabActivated();
    partial void OnPerfTabActivated();
    partial void OnUiaTabActivated();
    partial void OnResourcesTabActivated();
    partial void OnToolsTabActivated();
    partial void OnReplTabActivated();
}
