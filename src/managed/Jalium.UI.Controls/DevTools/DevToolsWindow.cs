using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Animation;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ShapePath = Jalium.UI.Controls.Shapes.Path;

namespace Jalium.UI.Controls.DevTools;

/// <summary>
/// Developer tools window for inspecting the visual tree and element properties.
/// Features: syntax-highlighted values, color swatches, inline editing, font preview,
/// toolbar, element picker, search, breadcrumb, box model, grid/style/resource/binding inspectors.
/// Top-level layout is a TabControl that pins every DevTools surface (Inspector, Logical, Layout,
/// Events, Bindings, Resources, Perf, UIA, Tools, REPL).
/// </summary>
public partial class DevToolsWindow : Window
{
    private const int SearchRefreshDelayMilliseconds = 150;

    private readonly Window _targetWindow;
    private readonly Grid _mainGrid;
    private readonly InspectorTreeList _visualTreeView;
    private readonly StackPanel _propertiesPanel;
    private readonly UIElement _propertiesScrollViewer;
    private readonly TextBox _searchTextBox;
    private readonly DispatcherTimer _searchRefreshTimer;
    private Visual? _selectedVisual;
    private DevToolsOverlay? _overlay;
    private int _rowIndex;

    // Inspector tree view mode (visual vs logical vs flat). The segmented toggle
    // in the inspector toolbar flips this; RefreshVisualTree rebuilds accordingly.
    internal enum InspectorViewMode
    {
        Visual = 0,
        Logical = 1,
        Flat = 2,
    }
    private InspectorViewMode _inspectorViewMode = InspectorViewMode.Visual;
    private DevToolsUi.SegmentedToggle? _inspectorViewToggle;

    private void SetInspectorViewMode(InspectorViewMode mode)
    {
        if (_inspectorViewMode == mode) return;
        _inspectorViewMode = mode;
        RefreshVisualTree();
    }

    /// <summary>
    /// Returns the children that should appear under <paramref name="visual"/> for
    /// the currently-selected inspector view mode. Visual = all visual children;
    /// Logical = filter template decoration (content/items presenters); Flat will
    /// be handled separately by <see cref="RefreshVisualTree"/>.
    /// </summary>
    internal IEnumerable<Visual> EnumerateChildrenForCurrentMode(Visual visual)
    {
        int count = visual.VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child == null) continue;

            if (_inspectorViewMode == InspectorViewMode.Logical)
            {
                // Hide template plumbing in Logical mode so the user sees
                // meaningful user-level structure, not the template visual tree.
                if (IsTemplateDecoration(child)) continue;
            }
            yield return child;
        }
    }

    private static bool IsTemplateDecoration(Visual visual)
    {
        // Keep named FrameworkElements (user markup) visible; filter out typical
        // template scaffolding (ItemsPresenter, ContentPresenter, panels with no
        // Name that exist purely to host a template).
        if (visual is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
            return false;
        var name = visual.GetType().Name;
        return name.EndsWith("Presenter", StringComparison.Ordinal)
            || name.EndsWith("Host", StringComparison.Ordinal);
    }

    // Toolbar state
    private bool _isPickerActive;
    private DevToolsUi.DevToolsButton? _pickerButton;

    // ── Inspector flat virtualized list state ──
    private readonly BulkObservableCollection<InspectorRow> _rows = new();
    private readonly Dictionary<Visual, InspectorRow> _rowByVisual = new();
    private readonly HashSet<Visual> _expandedVisuals = new();
    private string? _activeFilter;

    // ── Palette (re-exported from DevToolsTheme for legacy call sites in this file) ──
    private static readonly SolidColorBrush BrushString       = DevToolsTheme.TokenString;
    private static readonly SolidColorBrush BrushNumber       = DevToolsTheme.TokenNumber;
    private static readonly SolidColorBrush BrushBool         = DevToolsTheme.TokenBool;
    private static readonly SolidColorBrush BrushEnum         = DevToolsTheme.TokenEnum;
    private static readonly SolidColorBrush BrushNull         = DevToolsTheme.TextMuted;
    private static readonly SolidColorBrush BrushThickness    = DevToolsTheme.TokenType;
    private static readonly SolidColorBrush BrushPropName     = DevToolsTheme.TokenProperty;
    private static readonly SolidColorBrush BrushSection      = DevToolsTheme.Accent;
    private static readonly SolidColorBrush BrushType         = DevToolsTheme.TokenBool;
    private static readonly SolidColorBrush BrushKeyword      = DevToolsTheme.TokenKeyword;
    private static readonly SolidColorBrush BrushEditBg       = DevToolsTheme.Control;
    private static readonly SolidColorBrush BrushEditBorder   = DevToolsTheme.Border;
    private static readonly SolidColorBrush BrushSwatchBorder = DevToolsTheme.BorderStrong;
    private static readonly SolidColorBrush BrushRowAlt       = DevToolsTheme.RowAlt;
    private static readonly SolidColorBrush BrushToolbarBg    = DevToolsTheme.Chrome;
    private static readonly SolidColorBrush BrushToolbarBorder = DevToolsTheme.BorderSubtle;
    private static readonly SolidColorBrush BrushAccent       = DevToolsTheme.Accent;
    private static readonly SolidColorBrush BrushBreadcrumbSep = DevToolsTheme.TextMuted;
    private static readonly SolidColorBrush BrushBoxMargin = new(Color.FromArgb(0xB4, DevToolsTheme.WarningColor.R, DevToolsTheme.WarningColor.G, DevToolsTheme.WarningColor.B));
    private static readonly SolidColorBrush BrushBoxBorder = new(Color.FromArgb(0xB4, DevToolsTheme.AccentColor.R, DevToolsTheme.AccentColor.G, DevToolsTheme.AccentColor.B));
    private static readonly SolidColorBrush BrushBoxPadding = new(Color.FromArgb(0xB4, DevToolsTheme.SuccessColor.R, DevToolsTheme.SuccessColor.G, DevToolsTheme.SuccessColor.B));
    private static readonly SolidColorBrush BrushBoxContent = new(Color.FromArgb(0xB4, DevToolsTheme.InfoColor.R, DevToolsTheme.InfoColor.G, DevToolsTheme.InfoColor.B));
    private static readonly SolidColorBrush BrushBoxLabel = new(DevToolsTheme.TextSecondaryColor);
    // Property-category legend — a muted, graphite-friendly categorical palette
    // (mapped onto the instrument tokens where one fits) so category bullets stay
    // distinguishable without reintroducing neon devtools-blue.
    private static readonly SolidColorBrush BrushCategoryFramework = DevToolsTheme.Info;
    private static readonly SolidColorBrush BrushCategoryLayout = DevToolsTheme.Success;
    private static readonly SolidColorBrush BrushCategoryAppearance = DevToolsTheme.Warning;
    private static readonly SolidColorBrush BrushCategoryTypography = DevToolsTheme.TokenKeyword;
    private static readonly SolidColorBrush BrushCategoryContent = new(Color.FromRgb(0x6F, 0xA8, 0xC7));
    private static readonly SolidColorBrush BrushCategoryItems = DevToolsTheme.TokenType;
    private static readonly SolidColorBrush BrushCategoryData = DevToolsTheme.TokenEnum;
    private static readonly SolidColorBrush BrushCategoryInput = new(Color.FromRgb(0xE0, 0x81, 0x7C));
    private static readonly SolidColorBrush BrushCategoryBehavior = new(Color.FromRgb(0x9D, 0x8B, 0xD6));
    private static readonly SolidColorBrush BrushCategoryState = new(Color.FromRgb(0x9E, 0xC0, 0x7A));
    private static readonly SolidColorBrush BrushCategoryOther = DevToolsTheme.TextSecondary;
    private const double NameWidth = 145;
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<DependencyPropertyInspectorEntry>> s_dependencyPropertyCache = new();

    private sealed record DependencyPropertyInspectorEntry(DependencyProperty Property, DevToolsPropertyCategory Category);

    protected override bool CanOpenDevTools => false;

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevToolsWindow includes a REPL and inspector that reflect on user types.")]
    public DevToolsWindow(Window targetWindow)
    {
        _targetWindow = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));

        // Tell the diagnostics layer not to log anything produced by DevTools itself —
        // otherwise the Events/Layout/Bindings tabs are flooded with hover, click,
        // scroll, text-input events generated by the tool's own UI.
        Jalium.UI.Diagnostics.DiagnosticsScope.ExcludeRoot(this);

        // Anything constructed inside this scope has IsDiagnosticsIgnored set
        // via the Visual field-initializer — closes the window where a new
        // UIElement fires InvalidateMeasure from its constructor (Header /
        // Foreground / DP defaults) before AddVisualChild can inherit the flag.
        using var __devToolsCreationScope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();

        Title = $"DevTools · {targetWindow.Title}";
        Width = 1040;
        Height = 860;
        SystemBackdrop = WindowBackdropType.Mica;
        Background = DevToolsTheme.Chrome;

        // Layout: rootGrid has 2 rows (toolbar, content).
        // contentGrid has 3 columns (left=search+tree, middle=splitter, right=properties).
        _mainGrid = new Grid();
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // row 0: toolbar
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // row 1: content

        // 鈹€鈹€ Toolbar 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        var toolbar = CreateToolbar();
        Grid.SetRow(toolbar, 0);
        _mainGrid.Children.Add(toolbar);

        // 鈹€鈹€ Content grid (3 columns) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        Grid.SetRow(contentGrid, 1);
        _mainGrid.Children.Add(contentGrid);

        // 鈹€鈹€ Left column: search + tree (stacked vertically) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // search
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // tree
        Grid.SetColumn(leftGrid, 0);
        contentGrid.Children.Add(leftGrid);

        _searchTextBox = new TextBox
        {
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextPrimary,
            Background = DevToolsTheme.Control,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterSm, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            PlaceholderText = "Filter tree…",
        };
        _searchTextBox.TextChanged += OnSearchTextChanged;

        // View-mode switcher — compact segmented control with three icon buttons.
        // Glyphs: ≡ (flat list) · ▦ (grid = visual) · ⧉ (layered = logical).
        _inspectorViewToggle = new DevToolsUi.SegmentedToggle();
        _inspectorViewToggle.AddSegment("≡", "Flat list", () => SetInspectorViewMode(InspectorViewMode.Flat));
        _inspectorViewToggle.AddSegment("▦", "Visual tree", () => SetInspectorViewMode(InspectorViewMode.Visual));
        _inspectorViewToggle.AddSegment("⧉", "Logical tree", () => SetInspectorViewMode(InspectorViewMode.Logical));
        _inspectorViewToggle.SetSelectedSilent((int)_inspectorViewMode);

        var searchRow = new Grid();
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_searchTextBox, 0);
        Grid.SetColumn(_inspectorViewToggle, 1);
        searchRow.Children.Add(_searchTextBox);
        searchRow.Children.Add(_inspectorViewToggle);

        var searchRowHost = new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessBottom,
            Padding = new Thickness(DevToolsTheme.GutterSm, DevToolsTheme.GutterSm, DevToolsTheme.GutterSm, DevToolsTheme.GutterSm),
            Child = searchRow,
        };
        Grid.SetRow(searchRowHost, 0);
        leftGrid.Children.Add(searchRowHost);

        var treeView = new InspectorTreeList
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
        };
        treeView.ToggleExpand = ToggleExpand;
        treeView.ItemsSource = _rows;
        treeView.SelectionChanged += OnInspectorSelectionChanged;
        // PreviewMouseRightButtonUp is declared in UIElement but never actually
        // raised by the framework. Subscribe to the generic PreviewMouseUp and
        // filter on ChangedButton==Right instead.
        treeView.AddHandler(UIElement.PreviewMouseUpEvent, new Input.MouseButtonEventHandler(OnVisualTreeRightClick));
        Grid.SetRow(treeView, 1);
        leftGrid.Children.Add(treeView);

        var splitter = new GridSplitter
        {
            Width = 6,
            Background = DevToolsTheme.BorderSubtle,
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(splitter, 1);
        contentGrid.Children.Add(splitter);
        // ── Right column: properties panel ──
        var propertiesPanel = new StackPanel
        {
            Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterSm, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm)
        };
        var scrollViewer = new ScrollViewer
        {
            Content = propertiesPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var rightBorder = new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            Child = scrollViewer,
            ClipToBounds = true
        };
        Grid.SetColumn(rightBorder, 2);
        contentGrid.Children.Add(rightBorder);

        _visualTreeView = treeView;
        _propertiesScrollViewer = scrollViewer;
        _propertiesPanel = propertiesPanel;

        // Root content is a TabControl; the _mainGrid (Inspector tab content) is wrapped by BuildTabLayout().
        Content = BuildTabLayout();

        // Add placeholder text so the right pane has initial content
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = "Select an element to inspect",
            Foreground = DevToolsTheme.TextMuted,
            FontFamily = DevToolsTheme.UiFont,
            FontSize = DevToolsTheme.FontBase,
            Margin = new Thickness(8, 16, 8, 8)
        });

        _searchRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SearchRefreshDelayMilliseconds)
        };
        _searchRefreshTimer.Tick += OnSearchRefreshTimerTick;

        RefreshVisualTree();

        _overlay = new DevToolsOverlay(_targetWindow);
        _targetWindow.DevToolsOverlay = _overlay;

        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        Closing += OnDevToolsClosing;
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Toolbar
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private Border CreateToolbar()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };

        toolbar.Children.Add(DevToolsUi.Button("Refresh",  () => RefreshVisualTree(), DevToolsUi.ButtonStyle.Default, icon: "↻"));
        _pickerButton = DevToolsUi.Toggle("Pick",       () => { if (_isPickerActive) DeactivatePicker(); else ActivatePicker(); }, _isPickerActive, icon: "◎");
        toolbar.Children.Add(_pickerButton);
        toolbar.Children.Add(DevToolsUi.VerticalDivider());
        toolbar.Children.Add(DevToolsUi.Button("Expand",   () => ExpandAll(),   icon: "⊕"));
        toolbar.Children.Add(DevToolsUi.Button("Collapse", () => CollapseAll(), icon: "⊖"));
        toolbar.Children.Add(DevToolsUi.VerticalDivider());
        toolbar.Children.Add(DevToolsUi.Button("Copy",     () => CopyElementInfo(), icon: "⧉"));

        return DevToolsUi.Toolbar(toolbar);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshVisualTree();

    private void OnPickerClick(object sender, RoutedEventArgs e)
    {
        if (_isPickerActive)
            DeactivatePicker();
        else
            ActivatePicker();
    }

    private void OnExpandAllClick(object sender, RoutedEventArgs e) => ExpandAll();
    private void OnCollapseAllClick(object sender, RoutedEventArgs e) => CollapseAll();

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyElementInfo();

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Element Picker Mode
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    internal void ActivatePicker()
    {
        _isPickerActive = true;
        if (_pickerButton != null)
            _pickerButton.IsActive = true;

        _targetWindow.PreviewMouseMove += OnTargetPreviewMouseMove;
        _targetWindow.PreviewMouseDown += OnTargetPreviewMouseDown;
    }

    private void DeactivatePicker()
    {
        _isPickerActive = false;
        if (_pickerButton != null)
            _pickerButton.IsActive = false;

        _targetWindow.PreviewMouseMove -= OnTargetPreviewMouseMove;
        _targetWindow.PreviewMouseDown -= OnTargetPreviewMouseDown;
    }

    private void OnTargetPreviewMouseMove(object sender, RoutedEventArgs e)
    {
        if (!_isPickerActive || e is not MouseEventArgs me) return;

        var hit = HitTestVisualTree(_targetWindow, me.Position);
        if (hit != null)
            _overlay?.HighlightElement(hit as UIElement);
    }

    private void OnTargetPreviewMouseDown(object sender, RoutedEventArgs e)
    {
        if (!_isPickerActive || e is not MouseButtonEventArgs me) return;

        // Only respond to left mouse button click
        if (me.ChangedButton != MouseButton.Left) return;

        var hit = HitTestVisualTree(_targetWindow, me.Position);
        if (hit != null)
            RevealInInspector(hit);

        DeactivatePicker();
        me.Handled = true;
    }

    /// <summary>
    /// Walks the visual tree to find the deepest element at the given window-relative point.
    /// </summary>
    private static Visual? HitTestVisualTree(Visual root, Point windowPoint)
    {
        return HitTestRecursive(root, windowPoint, 0, 0);
    }

    private static Visual? HitTestRecursive(Visual current, Point windowPoint, double offsetX, double offsetY)
    {
        Visual? deepestHit = null;

        int count = current.VisualChildrenCount;
        // Walk children in reverse order (topmost first)
        for (int i = count - 1; i >= 0; i--)
        {
            var child = current.GetVisualChild(i);
            if (child is not UIElement uiChild) continue;

            if (uiChild.Visibility != Visibility.Visible) continue;

            // Honor the hit-test contract — elements with IsHitTestVisible=false (and
            // their entire subtree) must be invisible to picking. This is what makes
            // AdornerLayer, FocusVisualAdorner and similar overlays opt out of picker
            // hits even though they cover the full window region.
            if (!uiChild.IsHitTestVisible) continue;

            // OverlayLayer is hit-test-visible (it dispatches input to popups), but the
            // picker should still skip past it so the user lands on the underlying app
            // content rather than the overlay host itself.
            if (uiChild is OverlayLayer) continue;

            var bounds = uiChild.VisualBounds;
            double childX = offsetX + bounds.X;
            double childY = offsetY + bounds.Y;

            // Check if point falls within this child's bounds
            if (windowPoint.X >= childX && windowPoint.X <= childX + bounds.Width &&
                windowPoint.Y >= childY && windowPoint.Y <= childY + bounds.Height)
            {
                // Recurse deeper
                var deeper = HitTestRecursive(child, windowPoint, childX, childY);
                return deeper ?? child;
            }
        }

        return deepestHit;
    }

    /// <summary>
    /// Switches to the Inspector tab, normalizes view state (Visual mode + cleared
    /// search filter) and locates <paramref name="target"/> in the tree. Callers from
    /// Layout / Events / ContextMenu / breadcrumb / picker all go through this so the
    /// reveal flow is uniform and resilient.
    /// </summary>
    internal void RevealInInspector(Visual? target)
    {
        if (target == null) return;

        // Switch to the Inspector tab.
        if (_rootTabs != null && _inspectorTab != null && _rootTabs.SelectedItem != _inspectorTab)
            _rootTabs.SelectedItem = _inspectorTab;

        // Normalize: clear any active filter + force Visual mode so every visual is
        // locatable, then rebuild the flat row list before locating the target.
        _searchTextBox.Text = "";
        _inspectorViewMode = InspectorViewMode.Visual;
        _inspectorViewToggle?.SetSelectedSilent((int)InspectorViewMode.Visual);
        RefreshVisualTree();

        // SelectVisualInTree expands the ancestor chain, rebuilds rows, selects the
        // target row (driving the properties panel + overlay via SelectionChanged) and
        // scrolls it into view. Selection is only claimed if the target resolves.
        SelectVisualInTree(target);
    }

    /// <summary>
    /// Locates the row for <paramref name="visual"/>, expanding its ancestor chain so the
    /// row becomes visible, selects it (which drives the properties panel + overlay via
    /// <see cref="OnInspectorSelectionChanged"/>) and scrolls it into view. Returns true when
    /// the visual resolves to a row under the target window. Assumes the caller has normalized
    /// state (Visual mode, no filter) — use <see cref="RevealInInspector"/> otherwise.
    /// </summary>
    private bool SelectVisualInTree(Visual visual)
    {
        if (visual == null) return false;

        var chain = BuildAncestorChain(visual);
        if (chain == null) return false;

        // Expand every ancestor (not the target itself) so the target's row is emitted.
        bool changed = false;
        for (int i = 0; i < chain.Count - 1; i++)
            changed |= _expandedVisuals.Add(chain[i]);

        if (changed || !_rowByVisual.ContainsKey(visual))
            RebuildVisibleRows();

        if (!_rowByVisual.TryGetValue(visual, out var row))
            return false;

        _visualTreeView.SelectedItem = row; // → OnInspectorSelectionChanged updates panel + overlay
        ScrollRowIntoView(row);
        return true;
    }

    /// <summary>
    /// Builds the chain [_targetWindow, …, visual] via VisualParent, falling back to
    /// TemplatedParent when the visual parent is gone (popups / recycled template parts).
    /// Returns null when <paramref name="visual"/> is not under the target window.
    /// </summary>
    private List<Visual>? BuildAncestorChain(Visual visual)
    {
        var chain = new List<Visual>();
        var visited = new HashSet<Visual>();
        Visual? v = visual;
        while (v != null && visited.Add(v))
        {
            chain.Add(v);
            if (v == _targetWindow) break;
            Visual? parent = v.VisualParent;
            if (parent == null && v is FrameworkElement fe)
                parent = fe.TemplatedParent as Visual;
            v = parent;
        }

        if (chain.Count == 0 || chain[^1] != _targetWindow) return null;
        chain.Reverse(); // [_targetWindow, …, visual]
        return chain;
    }

    /// <summary>
    /// Scrolls the row at the given index into view. Virtualization-aware (the target row's
    /// container may not be realized yet), so it routes through the panel's index-based bring-
    /// into-view rather than waiting for the container to materialize. Deferred one dispatcher
    /// turn so the row-list reset + measure settle first.
    /// </summary>
    private void ScrollRowIntoView(InspectorRow row)
    {
        int index = _rows.IndexOf(row);
        if (index < 0) return;
        Dispatcher.BeginInvoke(() => _visualTreeView.BringRowIntoView(index));
    }

    //鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Search / Filter
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        var filter = _searchTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(filter))
            RefreshVisualTree();         // clear filter + rebuild immediately
        else
            RestartSearchRefreshTimer(); // debounced → OnSearchRefreshTimerTick → RefreshVisualTree
    }

    private static bool MatchesSearch(Visual visual, string filter)
    {
        var typeName = visual.GetType().Name;
        if (typeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (visual is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name) &&
            fe.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        if (visual is TextBlock tb && !string.IsNullOrEmpty(tb.Text) &&
            tb.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Builds the inspector row label for a visual: "TypeName [#Name | "text" | "title"] [WxH] [(childCount)]".
    /// </summary>
    internal static string GetVisualDisplayName(Visual visual)
    {
        var typeName = visual.GetType().Name;
        var suffix = "";

        // Append dimensions for FrameworkElements
        if (visual is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
        {
            suffix = $" {fe.ActualWidth:F0}×{fe.ActualHeight:F0}";
        }

        // Append child count for containers
        int childCount = visual.VisualChildrenCount;
        if (childCount > 0)
        {
            suffix += $" ({childCount})";
        }

        if (visual is Window window)
        {
            return $"{typeName} \"{window.Title}\"{suffix}";
        }
        if (visual is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
        {
            var text = textBlock.Text.Length > 20 ? textBlock.Text[..20] + "..." : textBlock.Text;
            return $"{typeName} \"{text}\"{suffix}";
        }
        if (visual is ContentControl { Content: string contentString })
        {
            var text = contentString.Length > 20 ? contentString[..20] + "..." : contentString;
            return $"{typeName} \"{text}\"{suffix}";
        }
        if (visual is FrameworkElement namedFe && !string.IsNullOrEmpty(namedFe.Name))
        {
            return $"{typeName} #{namedFe.Name}{suffix}";
        }

        return $"{typeName}{suffix}";
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Expand / Collapse All
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void ExpandAll()
    {
        if (_activeFilter != null) return; // filtered view is already fully expanded
        // A stale in-flight reveal range would wrongly hide rows of the rebuilt list (its
        // [start,end) indices are meaningless after the rebuild) — cancel it first.
        CancelExpandAnimation();
        _expandedVisuals.Clear();
        AddExpandableRecursive(_targetWindow);
        RebuildVisibleRows();
    }

    private void AddExpandableRecursive(Visual visual)
    {
        var children = EnumerateChildrenForCurrentMode(visual).ToList();
        if (children.Count == 0) return;
        _expandedVisuals.Add(visual);
        foreach (var child in children)
            AddExpandableRecursive(child);
    }

    private void CollapseAll()
    {
        CancelExpandAnimation(); // see ExpandAll: stale reveal ranges must not outlive the rebuild
        _expandedVisuals.Clear();
        _expandedVisuals.Add(_targetWindow);
        RebuildVisibleRows();
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Copy Element Info
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void CopyElementInfo()
    {
        if (_selectedVisual == null) return;

        var type = _selectedVisual.GetType();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Type: {type.Name}");

        if (_selectedVisual is FrameworkElement fe)
        {
            if (!string.IsNullOrEmpty(fe.Name))
                sb.AppendLine($"Name: {fe.Name}");
            sb.AppendLine($"Size: {fe.ActualWidth:F1} x {fe.ActualHeight:F1}");
            sb.AppendLine($"Margin: {fe.Margin}");

            if (fe is Control ctrl)
            {
                sb.AppendLine($"FontSize: {ctrl.FontSize}");
                if (ctrl.Background is SolidColorBrush bg)
                    sb.AppendLine($"Background: #{bg.Color.R:X2}{bg.Color.G:X2}{bg.Color.B:X2}");
                if (ctrl.Foreground is SolidColorBrush fg)
                    sb.AppendLine($"Foreground: #{fg.Color.R:X2}{fg.Color.G:X2}{fg.Color.B:X2}");
            }

            if (fe is TextBlock tb)
                sb.AppendLine($"Text: {tb.Text}");
        }

        Clipboard.SetText(sb.ToString());
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Keyboard Shortcuts
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private bool _isClosing;

    private void OnDevToolsClosing(object? sender, EventArgs e)
    {
        _searchRefreshTimer.Stop();
        CancelExpandAnimation();

        if (_isPickerActive)
            DeactivatePicker();

        // Stop the overlay's per-frame highlight animation BEFORE dropping the
        // references. That animation runs on a DispatcherTimer whose 1ms interval
        // makes it piggyback on the static CompositionTarget.Rendering event.
        // Without RemoveOverlay() (-> StopAnimation -> DispatcherTimer.Stop) the
        // orphaned overlay stays rooted by that static event and keeps forcing a
        // full-window repaint of the target window every frame after DevTools is
        // closed (drops to ~11 FPS on iGPU, and leaks the window + element subtree).
        _overlay?.RemoveOverlay();
        _targetWindow.DevToolsOverlay = null;
        _overlay = null;

        Jalium.UI.Diagnostics.DiagnosticsScope.IncludeRoot(this);
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            RefreshVisualTree();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_isPickerActive)
            {
                DeactivatePicker();
                e.Handled = true;
            }
            else
            {
                CloseDevTools();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F12)
        {
            CloseDevTools();
            e.Handled = true;
        }
        else if (e.IsControlDown)
        {
            switch (e.Key)
            {
                case Key.F:
                    _searchTextBox.Focus();
                    e.Handled = true;
                    break;
                case Key.C:
                    if (e.IsShiftDown)
                    {
                        // Ctrl+Shift+C: Toggle element picker
                        if (_isPickerActive) DeactivatePicker(); else ActivatePicker();
                    }
                    else
                    {
                        // Ctrl+C: Copy element info
                        CopyElementInfo();
                    }
                    e.Handled = true;
                    break;
                case Key.E:
                    if (e.IsShiftDown) CollapseAll(); else ExpandAll();
                    e.Handled = true;
                    break;
            }
        }
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Visual Tree
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void RefreshVisualTree()
    {
        _searchRefreshTimer.Stop();
        CancelExpandAnimation();

        var filter = _searchTextBox.Text?.Trim() ?? "";
        _activeFilter = string.IsNullOrEmpty(filter) ? null : filter;

        // Reset expansion to "root only" — a refresh collapses everything back to the root,
        // matching the previous rebuild semantics.
        _expandedVisuals.Clear();
        _expandedVisuals.Add(_targetWindow);

        RebuildVisibleRows();
    }

    /// <summary>
    /// Recomputes the flat list of currently-visible rows and pushes it to the ListBox in a
    /// single Reset. Only row DATA is built here (cheap, O(visible)); the ListBox's
    /// VirtualizingStackPanel realizes containers for the viewport only — so neither the build
    /// nor a later per-row state change touches the whole tree.
    /// </summary>
    private void RebuildVisibleRows()
    {
        var rows = new List<InspectorRow>();
        if (_activeFilter is { } filter)
            AppendFilteredRows(_targetWindow, 0, filter, rows);
        else if (_inspectorViewMode == InspectorViewMode.Flat)
            AppendFlatRows(_targetWindow, rows);
        else
            AppendHierarchicalRows(_targetWindow, 0, rows);

        _rowByVisual.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            r.Index = i;                 // used by the reveal animation's stagger
            _rowByVisual[r.Visual] = r;
        }

        _rows.Reset(rows);

        // Row objects are all new — restore the selection highlight for the inspected visual.
        // OnInspectorSelectionChanged short-circuits on the same Visual, so this won't churn
        // the properties panel.
        if (_selectedVisual != null && _rowByVisual.TryGetValue(_selectedVisual, out var sel))
            _visualTreeView.SelectedItem = sel;
        else
            _visualTreeView.SelectedItem = null;

        // Replacing the collection only invalidates the panel; the inner ScrollViewer keeps its
        // previous extent/offset until it re-measures. Force that so a shrunk list (e.g. Collapse
        // All) refreshes immediately instead of leaving a stale offset only a resize would clear.
        _visualTreeView.SyncScrollAfterReset();
    }

    private void AppendHierarchicalRows(Visual visual, int depth, List<InspectorRow> rows)
    {
        var children = EnumerateChildrenForCurrentMode(visual).ToList();
        bool expanded = _expandedVisuals.Contains(visual);
        rows.Add(new InspectorRow(visual, depth) { HasChildren = children.Count > 0, IsExpanded = expanded, ChevronAngle = expanded ? 0 : -90 });
        if (expanded)
            foreach (var child in children)
                AppendHierarchicalRows(child, depth + 1, rows);
    }

    // Flat view: root at depth 0, every descendant flattened to depth 1, no expanders
    // (mirrors the previous BuildFlatBatch; walk depth capped to guard pathological trees).
    private void AppendFlatRows(Visual root, List<InspectorRow> rows)
    {
        rows.Add(new InspectorRow(root, 0));
        AppendFlatDescendants(root, 0, rows);
    }

    private void AppendFlatDescendants(Visual visual, int walkDepth, List<InspectorRow> rows)
    {
        if (walkDepth >= 24) return;
        int count = visual.VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child == null) continue;
            rows.Add(new InspectorRow(child, 1));
            AppendFlatDescendants(child, walkDepth + 1, rows);
        }
    }

    // Filtered view: keep a node when it (or any descendant) matches; matching paths are
    // auto-expanded. Mirrors the previous CreateFilteredTreeViewItem (raw visual children).
    private bool AppendFilteredRows(Visual visual, int depth, string filter, List<InspectorRow> rows)
    {
        int start = rows.Count;
        var row = new InspectorRow(visual, depth) { IsExpanded = true, ChevronAngle = 0 };
        rows.Add(row);

        bool anyChildKept = false;
        int count = visual.VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child == null) continue;
            anyChildKept |= AppendFilteredRows(child, depth + 1, filter, rows);
        }

        if (MatchesSearch(visual, filter) || anyChildKept)
        {
            row.HasChildren = anyChildKept;
            return true;
        }

        rows.RemoveRange(start, rows.Count - start); // drop self + tentatively-added descendants
        return false;
    }

    private void ToggleExpand(InspectorRow row)
    {
        if (_activeFilter != null) return;  // filtered view stays fully expanded
        if (!row.HasChildren) return;

        bool willExpand = !_expandedVisuals.Contains(row.Visual);

        // No realized rows (not shown yet / headless) → just toggle + rebuild instantly.
        if (_visualTreeView.GetRealizedContainers().Count == 0)
        {
            if (willExpand) _expandedVisuals.Add(row.Visual);
            else _expandedVisuals.Remove(row.Visual);
            RebuildVisibleRows();
            return;
        }

        CompleteExpandAnimation(); // finish any in-flight animation first

        if (willExpand)
        {
            _expandedVisuals.Add(row.Visual);
            RebuildVisibleRows();                              // descendants now present, animate them in
            BeginExpandCollapseAnimation(row.Visual, expanding: true);
        }
        else
        {
            // Collapse: animate the still-present descendants out, then remove them on completion.
            BeginExpandCollapseAnimation(row.Visual, expanding: false);
        }
    }

    // ── Expand/collapse reveal animation (render-only clipped slide + chevron rotation) ────
    // TRUE virtualization: rows always keep their full layout height, so the VSP only ever realizes
    // the viewport. The reveal is applied PER-FRAME to the realized containers only (O(viewport)),
    // via a clipped content slide (ClipToBounds + translate) — never via layout/height and never via
    // opacity (per-row alpha layers black out / thrash the render target on a large expand). So it
    // never stacks rows at one offset, never realizes the whole subtree, and needs no cap.
    private const double ExpandAnimMs = 260;
    private const double CollapseAnimMs = 180;
    private const double AnimStaggerStep = 0.06;
    private const double AnimStaggerMax = 0.5;

    private AnimationTickSubscription? _revealSubscription;   // constructed once, reused across reveals
    private bool _revealActive;
    private int _animStart = -1;   // first revealed/removed row index (inclusive)
    private int _animEnd = -1;     // exclusive
    private InspectorRow? _animChevronRow;
    private double _animChevronFrom;
    private double _animChevronTo;
    private bool _animExpanding;
    private long _animStartTimestamp;   // Stopwatch ticks, taken from the unified frame timebase
    private double _animDurationMs;
    private double _animProgress;       // latest global progress, read by the Bind-side reveal provider
    private Func<int, double>? _revealProgressForIndex;   // cached provider delegate
    private Visual? _pendingCollapseVisual;

    /// <summary>
    /// Frame driver for the reveal animation. A separate object rather than the window
    /// implementing <see cref="IFrameAnimatable"/> itself: <see cref="UIElement"/> already
    /// implements that interface for element animations, and re-implementing it on this
    /// subclass would shadow the base dispatch for the window's own animation subscription.
    /// </summary>
    private sealed class RevealDriver : IFrameAnimatable
    {
        private readonly DevToolsWindow _owner;

        public RevealDriver(DevToolsWindow owner) => _owner = owner;

        public bool OnAnimationFrame(long frameTimestamp) => _owner.OnRevealFrame(frameTimestamp);
    }

    private void BeginExpandCollapseAnimation(Visual toggledVisual, bool expanding)
    {
        if (!_rowByVisual.TryGetValue(toggledVisual, out var parentRow))
            return;

        int parentIndex = parentRow.Index;
        if (parentIndex < 0 || parentIndex >= _rows.Count || !ReferenceEquals(_rows[parentIndex], parentRow))
            parentIndex = _rows.IndexOf(parentRow);
        if (parentIndex < 0)
            return;

        // Reveal range = the contiguous descendant block (Depth > parent's Depth). One-time index
        // scan only (no realization). The per-frame loop below touches realized containers whose
        // Index falls in this range, so it stays O(viewport) regardless of subtree size.
        int end = parentIndex + 1;
        while (end < _rows.Count && _rows[end].Depth > parentRow.Depth)
            end++;

        _animStart = parentIndex + 1;
        _animEnd = end;

        _animChevronRow = parentRow;
        // Fixed endpoints by direction. On expand the rebuilt row is already IsExpanded, so its
        // ChevronAngle is the final 0 — capturing it as "from" would animate 0→0 (no rotation).
        _animChevronFrom = expanding ? -90 : 0;
        _animChevronTo = expanding ? 0 : -90;
        // Write the from-angle into the model now: the toggled row's container binds before the
        // first engine tick and must show the rotation start, not the rebuilt row's final angle.
        parentRow.ChevronAngle = _animChevronFrom;

        _animExpanding = expanding;
        _animDurationMs = expanding ? ExpandAnimMs : CollapseAnimMs;
        // t0 is pinned by the FIRST engine tick (sentinel 0), not here: this runs in a
        // mouse-event handler, and the rebuild + Reset + first-frame realization that
        // follow can take tens of ms — charging them to the animation clock makes the
        // first visible frame start mid-curve (rows pop in at different progress).
        _animStartTimestamp = 0;
        _animProgress = 0;
        _pendingCollapseVisual = expanding ? null : toggledVisual;

        // Publish the reveal range + progress function BEFORE the async layout realizes
        // containers: rows bound during the animation (frame 0 after the Reset, or scrolled into
        // view mid-reveal) take their current staggered progress at Bind time instead of flashing
        // fully revealed for one frame.
        _revealProgressForIndex ??= index => RowRevealAt(index, _animProgress, _animExpanding);
        _visualTreeView.SetActiveReveal(_animStart, _animEnd, _revealProgressForIndex);

        _revealActive = true;
        _revealSubscription ??= new AnimationTickSubscription(new RevealDriver(this), weak: false);
        AnimationManager.Register(_revealSubscription);
    }

    /// <summary>Staggered eased reveal progress for one row. Shared by the per-frame loop and the
    /// Bind-side provider so a row realized mid-animation lands exactly on the driven curve.</summary>
    private double RowRevealAt(int index, double progress, bool expanding)
    {
        double stagger = Math.Min(AnimStaggerMax, (index - _animStart) * AnimStaggerStep);
        double t = Math.Clamp((progress - stagger) / Math.Max(0.0001, 1.0 - stagger), 0.0, 1.0);
        double eased = EaseOutCubic(t);
        return expanding ? eased : 1.0 - eased;
    }

    private bool OnRevealFrame(long frameTimestamp)
    {
        if (!_revealActive)
            return false;

        // First engine tick pins t0 (rows bound before this read _animProgress=0 via the
        // provider, seamlessly matching the progress=0 this frame computes).
        if (_animStartTimestamp == 0)
            _animStartTimestamp = frameTimestamp;

        double progress = _animDurationMs <= 0
            ? 1.0
            : Math.Clamp(
                Stopwatch.GetElapsedTime(_animStartTimestamp, frameTimestamp).TotalMilliseconds / _animDurationMs,
                0.0, 1.0);
        _animProgress = progress;

        if (_animChevronRow != null)
        {
            double chevronEased = EaseOutCubic(progress);
            _animChevronRow.ChevronAngle = _animChevronFrom + (_animChevronTo - _animChevronFrom) * chevronEased;
        }

        var containers = _visualTreeView.GetRealizedContainers();
        for (int i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            var r = container.Row;
            if (r == null) continue;

            if (r.Index >= _animStart && r.Index < _animEnd)
                container.SetRevealProgress(RowRevealAt(r.Index, progress, _animExpanding));

            if (ReferenceEquals(r, _animChevronRow))
                container.ApplyChevron();
        }

        // No per-frame ItemsHost re-measure bump here: SetRevealProgress's composition-only
        // invalidation reliably schedules a present now that the dirty-region contract fixes
        // (promote-on-raw-area + no swallowed empty-bounds frames) are in place.

        if (progress >= 1.0)
        {
            CompleteExpandAnimation();
            return false;
        }

        return true;
    }

    private void CompleteExpandAnimation()
    {
        if (!_revealActive)
            return;

        _revealActive = false;
        AnimationManager.Unregister(_revealSubscription!);
        _visualTreeView.ClearActiveReveal();

        var collapseVisual = _pendingCollapseVisual;
        _pendingCollapseVisual = null;

        if (_animChevronRow != null)
        {
            _animChevronRow.ChevronAngle = _animChevronTo;
            _animChevronRow = null;
        }

        _animStart = _animEnd = -1;

        // Normalize realized rows to their resting state (fully revealed, final chevron).
        var containers = _visualTreeView.GetRealizedContainers();
        for (int i = 0; i < containers.Count; i++)
        {
            containers[i].SetRevealProgress(1.0);
            containers[i].ApplyChevron();
        }

        if (collapseVisual != null)
        {
            _expandedVisuals.Remove(collapseVisual); // now actually drop the collapsed descendants
            RebuildVisibleRows();
        }
    }

    private void CancelExpandAnimation()
    {
        if (_revealActive)
        {
            _revealActive = false;
            AnimationManager.Unregister(_revealSubscription!);
        }

        _visualTreeView.ClearActiveReveal();
        _pendingCollapseVisual = null;
        _animChevronRow = null;
        _animStart = _animEnd = -1;
        var containers = _visualTreeView.GetRealizedContainers();
        for (int i = 0; i < containers.Count; i++)
            containers[i].SetRevealProgress(1.0);
    }

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);

    private void OnInspectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_visualTreeView.SelectedItem is InspectorRow row)
        {
            // Selection restored after a row-list rebuild points at the same Visual — skip the
            // expensive properties-panel rebuild + overlay update in that case.
            if (ReferenceEquals(row.Visual, _selectedVisual)) return;
            _selectedVisual = row.Visual;
            UpdatePropertiesPanel(row.Visual);
            _overlay?.HighlightElement(row.Visual as UIElement);
        }
    }

    private void OnVisualTreeRightClick(object sender, Input.MouseButtonEventArgs me)
    {
        if (me.ChangedButton != Input.MouseButton.Right) return;
        if (me.OriginalSource is not Visual hit) return;

        // Walk up from the clicked visual to find the hosting row container.
        InspectorRow? row = null;
        for (var cur = hit; cur != null; cur = cur.VisualParent)
        {
            if (cur is InspectorRowContainer container && container.Row != null) { row = container.Row; break; }
        }
        if (row == null) return;

        // Select the row so the rest of the UI (overlay, properties panel) agrees with the
        // menu target (drives OnInspectorSelectionChanged).
        _visualTreeView.SelectedItem = row;

        OpenElementContextMenu(row.Visual, _visualTreeView);
        me.Handled = true;
    }

    private void RestartSearchRefreshTimer()
    {
        _searchRefreshTimer.Stop();
        _searchRefreshTimer.Start();
    }

    private void OnSearchRefreshTimerTick(object? sender, EventArgs e)
    {
        _searchRefreshTimer.Stop();
        RefreshVisualTree();
    }


    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Properties Panel 鈥?syntax-highlighted, editable, color swatches
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void UpdatePropertiesPanel(Visual? visual)
    {
        _propertiesPanel.Children.Clear();
        _rowIndex = 0;

        if (visual == null)
        {
            _propertiesScrollViewer.InvalidateMeasure();
            return;
        }

        // Debug: add a visible marker at the top to confirm the panel updates
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = $"[{visual.GetType().Name}]",
            Foreground = DevToolsTheme.Accent,
            FontFamily = DevToolsTheme.DisplayFont,
            FontSize = DevToolsTheme.FontLg,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 4, 4, 4)
        });

        try
        {
            UpdatePropertiesPanelCore(visual);
        }
        catch (Exception ex)
        {
            _propertiesPanel.Children.Add(new TextBlock
            {
                Text = $"Error: {ex.GetType().Name}: {ex.Message}",
                Foreground = DevToolsTheme.Error,
                FontSize = 11,
                Margin = new Thickness(8, 4, 4, 4)
            });
        }

        // Force the ScrollViewer to re-measure after content changed
        _propertiesScrollViewer.InvalidateMeasure();
        InvalidateWindow();
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "AddBindingInspector 'enumerates static DependencyProperty fields on FrameworkElement subtypes via reflection.' DevTools is an opt-in developer inspector control (UseDevTools()); consumers that inspect their own FrameworkElement subtypes under trimming/AOT must keep those types' static DependencyProperty fields preserved. That preservation is the documented consumer responsibility, not a defect of this site.")]
    private void UpdatePropertiesPanelCore(Visual visual)
    {
        var type = visual.GetType();
        AddTypeHeader(type);

        // 鈹€鈹€ Element Statistics 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        if (visual is UIElement)
        {
            AddElementStats(visual);
        }

        // 鈹€鈹€ Breadcrumb path 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        AddBreadcrumb(visual);

        // 鈹€鈹€ Box model diagram 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        if (visual is FrameworkElement boxFe)
        {
            AddBoxModel(boxFe);
        }

        if (visual is DependencyObject dependencyObject)
        {
            AddCategorizedDependencyPropertyInspector(dependencyObject);
        }

        // 鈹€鈹€ UIElement 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        if (visual is UIElement uiElement)
        {
            AddSection("UIElement");
            AddSize("DesiredSize", uiElement.DesiredSize);
            AddRect("VisualBounds", uiElement.VisualBounds);
            AddEnum("Visibility", uiElement.Visibility, v => ForceSetValue(uiElement, UIElement.VisibilityProperty, v));
            AddBool("IsEnabled", uiElement.IsEnabled, v => ForceSetValue(uiElement, UIElement.IsEnabledProperty, v));
            AddNum("Opacity", uiElement.Opacity, "F2", v => ForceSetValue(uiElement, UIElement.OpacityProperty, (double)v));
            AddBool("ClipToBounds", uiElement.ClipToBounds, v => uiElement.ClipToBounds = v);
            AddBool("Focusable", uiElement.Focusable, v => uiElement.Focusable = v);
            AddBool("IsMouseOver", uiElement.IsMouseOver);
            AddBool("IsKeyboardFocused", uiElement.IsKeyboardFocused);

            // 鈹€鈹€ FrameworkElement 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
            if (uiElement is FrameworkElement fe)
            {
                AddSection("Layout");
                AddNum("ActualWidth", fe.ActualWidth, "F1");
                AddNum("ActualHeight", fe.ActualHeight, "F1");
                AddNum("Width", fe.Width, "F1", v => fe.Width = v);
                AddNum("Height", fe.Height, "F1", v => fe.Height = v);
                AddNum("MinWidth", fe.MinWidth, "F1", v => fe.MinWidth = v);
                AddNum("MinHeight", fe.MinHeight, "F1", v => fe.MinHeight = v);
                AddNum("MaxWidth", fe.MaxWidth, "F1", v => fe.MaxWidth = v);
                AddNum("MaxHeight", fe.MaxHeight, "F1", v => fe.MaxHeight = v);
                AddThickness("Margin", fe.Margin, v => fe.Margin = v);
                AddEnum("HorizontalAlignment", fe.HorizontalAlignment, v => fe.HorizontalAlignment = (HorizontalAlignment)v);
                AddEnum("VerticalAlignment", fe.VerticalAlignment, v => fe.VerticalAlignment = (VerticalAlignment)v);
                if (!string.IsNullOrEmpty(fe.Name))
                    AddStr("Name", fe.Name);
                if (fe.DataContext != null)
                    AddStr("DataContext", fe.DataContext.GetType().Name);

                // 鈹€鈹€ Grid attached 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe.VisualParent is Grid)
                {
                    AddSection("Grid Attached");
                    AddNum("Grid.Row", Grid.GetRow(fe), "F0", v => Grid.SetRow(fe, (int)v));
                    AddNum("Grid.Column", Grid.GetColumn(fe), "F0", v => Grid.SetColumn(fe, (int)v));
                    if (Grid.GetRowSpan(fe) > 1)
                        AddNum("Grid.RowSpan", Grid.GetRowSpan(fe), "F0", v => Grid.SetRowSpan(fe, (int)v));
                    if (Grid.GetColumnSpan(fe) > 1)
                        AddNum("Grid.ColumnSpan", Grid.GetColumnSpan(fe), "F0", v => Grid.SetColumnSpan(fe, (int)v));
                }

                // 鈹€鈹€ Canvas attached 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe.VisualParent is Canvas)
                {
                    AddSection("Canvas Position");
                    AddNum("Canvas.Left", Canvas.GetLeft(fe), "F1", v => Canvas.SetLeft(fe, v));
                    AddNum("Canvas.Top", Canvas.GetTop(fe), "F1", v => Canvas.SetTop(fe, v));
                }

                // 鈹€鈹€ Control 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is Control control)
                {
                    AddSection("Appearance");
                    AddBrush("Background", control.Background, v => ForceSetValue(control, Control.BackgroundProperty, v));
                    AddBrush("Foreground", control.Foreground, v => ForceSetValue(control, Control.ForegroundProperty, v));
                    AddBrush("BorderBrush", control.BorderBrush, v => ForceSetValue(control, Control.BorderBrushProperty, v));
                    AddThickness("BorderThickness", control.BorderThickness, v => ForceSetValue(control, Control.BorderThicknessProperty, v));
                    AddThickness("Padding", control.Padding, v => ForceSetValue(control, Control.PaddingProperty, v));
                    AddStr("CornerRadius", control.CornerRadius.ToString());

                    AddSection("Typography");
                    AddNum("FontSize", control.FontSize, "F1", v => control.FontSize = v);
                    AddFontFamily("FontFamily", control.FontFamily.Source, control);
                    AddFontWeight("FontWeight", control.FontWeight);
                    AddEnum("HorizContentAlign", control.HorizontalContentAlignment);
                    AddEnum("VertContentAlign", control.VerticalContentAlignment);
                }

                // 鈹€鈹€ TextBlock 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is TextBlock tb)
                {
                    AddSection("TextBlock");
                    AddEditable("Text", tb.Text ?? "", v => tb.Text = v);
                    AddEnum("TextWrapping", tb.TextWrapping);
                    AddEnum("TextAlignment", tb.TextAlignment);
                    AddEnum("TextTrimming", tb.TextTrimming);

                    AddSection("Typography");
                    AddBrush("Foreground", tb.Foreground, v => ForceSetValue(tb, TextBlock.ForegroundProperty, v));
                    AddNum("FontSize", tb.FontSize, "F1", v => ForceSetValue(tb, TextBlock.FontSizeProperty, (double)v));
                    AddFontFamily("FontFamily", tb.FontFamily.Source, tb);
                    AddFontWeight("FontWeight", tb.FontWeight);
                }

                // 鈹€鈹€ Border 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is Border border)
                {
                    AddSection("Border");
                    AddBrush("Background", border.Background, v => ForceSetValue(border, Border.BackgroundProperty, v));
                    AddBrush("BorderBrush", border.BorderBrush, v => ForceSetValue(border, Border.BorderBrushProperty, v));
                    AddThickness("BorderThickness", border.BorderThickness, v => ForceSetValue(border, Border.BorderThicknessProperty, v));
                    AddThickness("Padding", border.Padding, v => ForceSetValue(border, Border.PaddingProperty, v));
                    AddStr("CornerRadius", border.CornerRadius.ToString());
                    if (border.Child != null)
                        AddStr("Child", border.Child.GetType().Name);
                    else
                        AddNull("Child");
                }

                // 鈹€鈹€ ContentControl 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is ContentControl cc && fe is not NavigationView)
                {
                    AddSection("Content");
                    if (cc.Content != null)
                    {
                        AddStr("Content", cc.Content.ToString() ?? "");
                        AddStr("Content Type", cc.Content.GetType().Name);
                    }
                    else
                        AddNull("Content");
                }

                // 鈹€鈹€ StackPanel 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is StackPanel sp)
                {
                    AddSection("StackPanel");
                    AddEnum("Orientation", sp.Orientation);
                    AddNum("Children", sp.Children.Count, "F0");
                }

                // 鈹€鈹€ Grid (with definitions inspector) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is Grid grid)
                {
                    AddSection("Grid");
                    AddNum("Rows", grid.RowDefinitions.Count, "F0");
                    AddNum("Columns", grid.ColumnDefinitions.Count, "F0");
                    AddNum("Children", grid.Children.Count, "F0");
                    AddGridDefinitions(grid);
                }

                // 鈹€鈹€ Image 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is Image img)
                {
                    AddSection("Image");
                    if (img.Source != null)
                        AddStr("Source", img.Source.ToString() ?? "(unknown)");
                    else
                        AddNull("Source");
                    AddEnum("Stretch", img.Stretch);
                }

                // 鈹€鈹€ ToggleSwitch 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is ToggleSwitch ts)
                {
                    AddSection("ToggleSwitch");
                    AddBool("IsOn", ts.IsOn, v => ts.IsOn = v);
                    AddStr("Header", ts.Header?.ToString() ?? "");
                    AddStr("OnContent", ts.OnContent?.ToString() ?? "On");
                    AddStr("OffContent", ts.OffContent?.ToString() ?? "Off");
                    AddBrush("OnBackground", ts.OnBackground);
                    AddBrush("OffBackground", ts.OffBackground);
                }

                // 鈹€鈹€ NavigationView 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is NavigationView nv)
                {
                    AddSection("NavigationView");
                    AddBool("IsPaneOpen", nv.IsPaneOpen, v => nv.IsPaneOpen = v);
                    AddEnum("PaneDisplayMode", nv.PaneDisplayMode);
                    AddEditable("PaneTitle", nv.PaneTitle ?? "", v => nv.PaneTitle = v);
                    AddNum("OpenPaneLength", nv.OpenPaneLength, "F0", v => nv.OpenPaneLength = v);
                    AddNum("CompactPaneLength", nv.CompactPaneLength, "F0", v => nv.CompactPaneLength = v);
                    AddBool("IsSettingsVisible", nv.IsSettingsVisible, v => nv.IsSettingsVisible = v);
                    AddBool("IsBackEnabled", nv.IsBackEnabled, v => nv.IsBackEnabled = v);
                    if (nv.Header != null)
                        AddStr("Header", nv.Header.ToString() ?? "");
                }

                // 鈹€鈹€ Popup 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is Popup popup)
                {
                    AddSection("Popup");
                    AddBool("IsOpen", popup.IsOpen, v => popup.IsOpen = v);
                    AddEnum("Placement", popup.Placement);
                    AddNum("HorizontalOffset", popup.HorizontalOffset, "F1", v => popup.HorizontalOffset = v);
                    AddNum("VerticalOffset", popup.VerticalOffset, "F1", v => popup.VerticalOffset = v);
                    AddBool("StaysOpen", popup.StaysOpen, v => popup.StaysOpen = v);
                }

                // 鈹€鈹€ ScrollViewer 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is ScrollViewer sv)
                {
                    AddSection("ScrollViewer");
                    AddNum("HorizontalOffset", sv.HorizontalOffset, "F1");
                    AddNum("VerticalOffset", sv.VerticalOffset, "F1");
                    AddNum("ExtentWidth", sv.ExtentWidth, "F1");
                    AddNum("ExtentHeight", sv.ExtentHeight, "F1");
                    AddNum("ViewportWidth", sv.ViewportWidth, "F1");
                    AddNum("ViewportHeight", sv.ViewportHeight, "F1");
                }

                // 鈹€鈹€ ItemsControl 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is ItemsControl ic && fe is not ComboBox && fe is not ListBox)
                {
                    AddSection("ItemsControl");
                    AddNum("Items.Count", ic.Items.Count, "F0");
                }

                // 鈹€鈹€ ComboBox 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is ComboBox cb)
                {
                    AddSection("ComboBox");
                    AddBool("IsDropDownOpen", cb.IsDropDownOpen, v => cb.IsDropDownOpen = v);
                    if (cb.SelectedItem != null)
                        AddStr("SelectedItem", cb.SelectedItem.ToString() ?? "");
                    else
                        AddNull("SelectedItem");
                    AddNum("SelectedIndex", cb.SelectedIndex, "F0");
                    AddNum("Items.Count", cb.Items.Count, "F0");
                }

                // 鈹€鈹€ ListBox 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is ListBox lb)
                {
                    AddSection("ListBox");
                    if (lb.SelectedItem != null)
                        AddStr("SelectedItem", lb.SelectedItem.ToString() ?? "");
                    else
                        AddNull("SelectedItem");
                    AddNum("SelectedIndex", lb.SelectedIndex, "F0");
                    AddNum("Items.Count", lb.Items.Count, "F0");
                }

                // 鈹€鈹€ Style Inspector (safe) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                try
                {
                    if (fe.Style != null)
                        AddStyleInspector(fe.Style);
                }
                catch { }

                // 鈹€鈹€ Resource Inspector (safe) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                try
                {
                    if (fe.Resources is { Count: > 0 } res)
                        AddResourceInspector(res);
                }
                catch { }

                // 鈹€鈹€ Binding Inspector (safe) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                try
                {
                    AddBindingInspector(fe);
                }
                catch { }

                // Template XAML reveal button
                try
                {
                    AppendTemplateXamlViewer(fe);
                }
                catch { }
            }
        }
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Element Statistics
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "GetCategorizedDependencyProperties 'enumerates static DependencyProperty fields on the target runtime type via reflection.' DevTools is an opt-in developer inspector control (UseDevTools()); consumers that inspect their own runtime types under trimming/AOT must keep those types' static DependencyProperty fields preserved. That preservation is the documented consumer responsibility, not a defect of this site.")]
    private void AddCategorizedDependencyPropertyInspector(DependencyObject dependencyObject)
    {
        var entries = GetCategorizedDependencyProperties(dependencyObject.GetType());
        if (entries.Count == 0)
        {
            return;
        }

        AddSection($"Properties by Category ({entries.Count})");

        foreach (var group in entries
                     .GroupBy(static entry => entry.Category)
                     .OrderBy(static group => GetCategorySortOrder(group.Key)))
        {
            var categoryEntries = group.ToList();
            AddCategoryHeader(group.Key, categoryEntries.Count);

            foreach (var entry in categoryEntries.OrderBy(static entry => entry.Property.Name, StringComparer.Ordinal))
            {
                AddCategorizedDependencyProperty(dependencyObject, entry);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Enumerates static DependencyProperty fields on the target runtime type via reflection.")]
    private static IReadOnlyList<DependencyPropertyInspectorEntry> GetCategorizedDependencyProperties(Type targetType)
    {
        return s_dependencyPropertyCache.GetOrAdd(targetType, static type =>
        {
            var entries = new Dictionary<int, DependencyPropertyInspectorEntry>();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            foreach (var field in fields)
            {
                if (field.FieldType != typeof(DependencyProperty))
                {
                    continue;
                }

                if (field.GetValue(null) is not DependencyProperty dependencyProperty)
                {
                    continue;
                }

                entries.TryAdd(
                    dependencyProperty.GlobalIndex,
                    new DependencyPropertyInspectorEntry(
                        dependencyProperty,
                        ResolveDependencyPropertyCategory(dependencyProperty, type)));
            }

            return entries.Values
                .OrderBy(static entry => GetCategorySortOrder(entry.Category))
                .ThenBy(static entry => entry.Property.Name, StringComparer.Ordinal)
                .ToArray();
        });
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools diagnostic that reflects on DependencyProperty owner type fields/methods to discover [DevToolsPropertyCategory] attributes.")]
    private static DevToolsPropertyCategory ResolveDependencyPropertyCategory(DependencyProperty dependencyProperty, Type targetType)
    {
        if (TryGetPropertyCategory(targetType, dependencyProperty.Name, out var category))
        {
            return category;
        }

        if (TryGetPropertyCategory(dependencyProperty.OwnerType, dependencyProperty.Name, out category))
        {
            return category;
        }

        var field = dependencyProperty.OwnerType.GetField(
            dependencyProperty.Name + "Property",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (TryGetDeclaredCategory(field, out category))
        {
            return category;
        }

        var getter = dependencyProperty.OwnerType.GetMethod(
            "Get" + dependencyProperty.Name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (TryGetDeclaredCategory(getter, out category))
        {
            return category;
        }

        var setter = dependencyProperty.OwnerType.GetMethod(
            "Set" + dependencyProperty.Name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (TryGetDeclaredCategory(setter, out category))
        {
            return category;
        }

        return DevToolsPropertyCategory.Other;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflectively reads a property on the runtime type to discover its DevToolsPropertyCategory attribute.")]
    private static bool TryGetPropertyCategory(Type type, string propertyName, out DevToolsPropertyCategory category)
    {
        var property = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        return TryGetDeclaredCategory(property, out category);
    }

    private static bool TryGetDeclaredCategory(MemberInfo? member, out DevToolsPropertyCategory category)
    {
        if (member == null)
        {
            category = DevToolsPropertyCategory.Other;
            return false;
        }

        if (member.GetCustomAttribute<DevToolsPropertyCategoryAttribute>(inherit: true) is { } attribute)
        {
            category = attribute.Category;
            return true;
        }

        if (member.GetCustomAttribute<CategoryAttribute>(inherit: true) is { } categoryAttribute &&
            TryParseCategory(categoryAttribute.Category, out category))
        {
            return true;
        }

        category = DevToolsPropertyCategory.Other;
        return false;
    }

    private static bool TryParseCategory(string categoryName, out DevToolsPropertyCategory category)
    {
        return Enum.TryParse(categoryName, ignoreCase: true, out category);
    }

    private static int GetCategorySortOrder(DevToolsPropertyCategory category)
    {
        return category switch
        {
            DevToolsPropertyCategory.Framework => 0,
            DevToolsPropertyCategory.Layout => 1,
            DevToolsPropertyCategory.Appearance => 2,
            DevToolsPropertyCategory.Typography => 3,
            DevToolsPropertyCategory.Content => 4,
            DevToolsPropertyCategory.Items => 5,
            DevToolsPropertyCategory.Data => 6,
            DevToolsPropertyCategory.Input => 7,
            DevToolsPropertyCategory.Behavior => 8,
            DevToolsPropertyCategory.State => 9,
            _ => 10
        };
    }

    private static SolidColorBrush GetCategoryBrush(DevToolsPropertyCategory category)
    {
        return category switch
        {
            DevToolsPropertyCategory.Framework => BrushCategoryFramework,
            DevToolsPropertyCategory.Layout => BrushCategoryLayout,
            DevToolsPropertyCategory.Appearance => BrushCategoryAppearance,
            DevToolsPropertyCategory.Typography => BrushCategoryTypography,
            DevToolsPropertyCategory.Content => BrushCategoryContent,
            DevToolsPropertyCategory.Items => BrushCategoryItems,
            DevToolsPropertyCategory.Data => BrushCategoryData,
            DevToolsPropertyCategory.Input => BrushCategoryInput,
            DevToolsPropertyCategory.Behavior => BrushCategoryBehavior,
            DevToolsPropertyCategory.State => BrushCategoryState,
            _ => BrushCategoryOther
        };
    }

    private void AddCategoryHeader(DevToolsPropertyCategory category, int count)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 8, 4, 1)
        };

        header.Children.Add(new TextBlock
        {
            Text = "\u25cf",
            Foreground = GetCategoryBrush(category),
            FontSize = DevToolsTheme.FontXS,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        header.Children.Add(new TextBlock
        {
            Text = DevToolsUi.Tracked($"{category} ({count})".ToUpperInvariant()),
            Foreground = DevToolsTheme.TextSecondary,
            FontFamily = DevToolsTheme.DisplayFont,
            FontSize = DevToolsTheme.FontXS,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        _propertiesPanel.Children.Add(header);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "GetInspectablePropertyValue is a 'DevTools diagnostic that reflects on the runtime DependencyObject type to read CLR property values.' DevTools is an opt-in developer inspector control (UseDevTools()); consumers that inspect their own runtime types under trimming/AOT must keep those types' property accessors preserved. That preservation is the documented consumer responsibility, not a defect of this site.")]
    private void AddCategorizedDependencyProperty(DependencyObject target, DependencyPropertyInspectorEntry entry)
    {
        var property = entry.Property;
        var value = GetInspectablePropertyValue(target, property);
        var nameBrush = GetCategoryBrush(entry.Category);

        switch (value)
        {
            case null:
                AddNull(property.Name, nameBrush);
                break;

            case string text when property.PropertyType == typeof(string):
                if (property.ReadOnly)
                {
                    AddStr(property.Name, text, nameBrush);
                }
                else
                {
                    AddEditable(property.Name, text, v => TrySetDependencyPropertyValue(target, property, v), nameBrush);
                }
                break;

            case bool boolValue when property.PropertyType == typeof(bool):
                AddBool(
                    property.Name,
                    boolValue,
                    property.ReadOnly ? null : v => TrySetDependencyPropertyValue(target, property, v),
                    nameBrush);
                break;

            case double numberValue when property.PropertyType == typeof(double):
                AddNum(
                    property.Name,
                    numberValue,
                    "F1",
                    property.ReadOnly ? null : v => TrySetDependencyPropertyValue(target, property, v),
                    nameBrush);
                break;

            case float floatValue when property.PropertyType == typeof(float):
                AddNum(
                    property.Name,
                    floatValue,
                    "F1",
                    property.ReadOnly ? null : v => TrySetDependencyPropertyValue(target, property, (float)v),
                    nameBrush);
                break;

            case Enum enumValue:
                AddEnum(
                    property.Name,
                    enumValue,
                    property.ReadOnly ? null : v => TrySetDependencyPropertyValue(target, property, v),
                    nameBrush);
                break;

            case Brush brushValue:
                AddBrush(
                    property.Name,
                    brushValue,
                    property.ReadOnly ? null : v => TrySetDependencyPropertyValue(target, property, v),
                    nameBrush);
                break;

            case Thickness thicknessValue:
                AddThickness(
                    property.Name,
                    thicknessValue,
                    property.ReadOnly ? null : v => TrySetDependencyPropertyValue(target, property, v),
                    nameBrush);
                break;

            case Size sizeValue:
                AddSize(property.Name, sizeValue, nameBrush);
                break;

            case Rect rectValue:
                AddRect(property.Name, rectValue, nameBrush);
                break;

            default:
                if (!TryAddSerializableEditor(target, property, value, nameBrush))
                {
                    AddFormattedDependencyPropertyValue(property.Name, value, nameBrush);
                }
                break;
        }

        AppendValueSourceBadge(target, property);
    }

    /// <summary>
    /// For property types that can round-trip through a string (an <see cref="ValueSerializer"/>
    /// such as ImageSource via its Uri, Geometry via path markup, etc.), renders an editable text
    /// box instead of a read-only type string. Returns false when the value cannot be serialized.
    /// </summary>
    private bool TryAddSerializableEditor(DependencyObject target, DependencyProperty property, object value, Brush? nameBrush)
    {
        if (property.ReadOnly)
        {
            return false;
        }

        var serializer = ValueSerializer.GetSerializerFor(property.PropertyType);
        if (serializer == null ||
            !serializer.CanConvertToString(value, null) ||
            !serializer.CanConvertFromString(string.Empty, null))
        {
            return false;
        }

        string text;
        try
        {
            text = serializer.ConvertToString(value, null);
        }
        catch
        {
            return false;
        }

        AddEditable(property.Name, text, v => SetFromValueSerializer(target, property, serializer, v), nameBrush);
        return true;
    }

    private static void SetFromValueSerializer(DependencyObject target, DependencyProperty property, ValueSerializer serializer, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var converted = serializer.ConvertFromString(text, null);
            TrySetDependencyPropertyValue(target, property, converted);
        }
        catch
        {
            // Ignore conversion failures while the user is still typing a valid value.
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools diagnostic that reflects on the runtime DependencyObject type to read CLR property values.")]
    private static object? GetInspectablePropertyValue(DependencyObject target, DependencyProperty property)
    {
        var clrProperty = target.GetType().GetProperty(
            property.Name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

        if (clrProperty != null && clrProperty.GetIndexParameters().Length == 0)
        {
            try
            {
                return clrProperty.GetValue(target);
            }
            catch
            {
                // Fall back to the raw dependency property value when a CLR wrapper throws.
            }
        }

        return target.GetValue(property);
    }

    private static void TrySetDependencyPropertyValue(DependencyObject target, DependencyProperty property, object? value)
    {
        try
        {
            if (value == null || property.PropertyType.IsInstanceOfType(value))
            {
                target.SetValue(property, value);
                return;
            }

            if (TryChangeType(value, property.PropertyType, out var converted))
            {
                target.SetValue(property, converted);
            }
        }
        catch
        {
            // DevTools editors should stay resilient when a conversion fails.
        }
    }

    private static bool TryChangeType(object value, Type targetType, out object? convertedValue)
    {
        try
        {
            if (targetType.IsEnum)
            {
                convertedValue = value is string text
                    ? Enum.Parse(targetType, text, ignoreCase: true)
                    : Enum.ToObject(targetType, value);
                return true;
            }

            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
            {
                convertedValue = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch
        {
            // Ignored on purpose; the caller treats failed conversion as a no-op.
        }

        convertedValue = null;
        return false;
    }

    private void AddFormattedDependencyPropertyValue(string name, object? value, Brush? nameBrush = null)
    {
        if (value == null)
        {
            AddNull(name, nameBrush);
            return;
        }

        var row = Row(name, nameBrush);
        row.Children.Add(new TextBlock
        {
            Text = FormatDependencyPropertyValue(value),
            Foreground = GetDependencyPropertyValueBrush(value),
            FontSize = 11
        });
    }

    private static string FormatDependencyPropertyValue(object value)
    {
        return value switch
        {
            DependencyObject dependencyObject => dependencyObject.GetType().Name,
            Type type => type.Name,
            System.Collections.IEnumerable enumerable when value is not string =>
                enumerable is System.Collections.ICollection collection
                    ? $"{value.GetType().Name} ({collection.Count})"
                    : value.GetType().Name,
            _ => value.ToString() ?? value.GetType().Name
        };
    }

    private static SolidColorBrush GetDependencyPropertyValueBrush(object value)
    {
        return value switch
        {
            bool => BrushBool,
            Enum => BrushEnum,
            string => BrushString,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => BrushNumber,
            Thickness => BrushThickness,
            _ => BrushType
        };
    }

    private void AddElementStats(Visual visual)
    {
        int depth = 0;
        Visual? cur = visual;
        while (cur?.VisualParent != null) { depth++; cur = cur.VisualParent; }

        int descendants = CountDescendants(visual);
        int directChildren = visual.VisualChildrenCount;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 4, 2)
        };

        row.Children.Add(MakeStatLabel($"Depth: {depth}"));
        row.Children.Add(MakeStatLabel($"Children: {directChildren}"));
        row.Children.Add(MakeStatLabel($"Descendants: {descendants}"));

        _propertiesPanel.Children.Add(row);
    }

    private static TextBlock MakeStatLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, 12, 0)
        };
    }

    private static int CountDescendants(Visual visual)
    {
        int count = 0;
        int childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                count++;
                count += CountDescendants(child);
            }
        }
        return count;
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Breadcrumb Path
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void AddBreadcrumb(Visual visual)
    {
        var ancestors = new List<Visual>();
        Visual? cur = visual;
        while (cur != null)
        {
            ancestors.Insert(0, cur);
            cur = cur.VisualParent;
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 4, 6)
        };

        for (int i = 0; i < ancestors.Count; i++)
        {
            if (i > 0)
            {
                row.Children.Add(new TextBlock
                {
                    Text = " / ",
                    Foreground = BrushBreadcrumbSep,
                    FontFamily = DevToolsTheme.MonoFont,
                    FontSize = DevToolsTheme.FontXS
                });
            }

            var ancestor = ancestors[i];
            var isLast = (i == ancestors.Count - 1);
            var crumb = new TextBlock
            {
                Text = ancestor.GetType().Name,
                FontFamily = DevToolsTheme.MonoFont,
                FontSize = DevToolsTheme.FontXS,
                Foreground = isLast
                    ? BrushAccent
                    : new SolidColorBrush(DevToolsTheme.TextSecondaryColor)
            };

            if (!isLast)
            {
                // Make clickable
                var clickable = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                    Child = crumb,
                    Padding = new Thickness(1, 0, 1, 0)
                };
                var target = ancestor;
                clickable.MouseDown += (_, _) =>
                {
                    RevealInInspector(target);
                };
                row.Children.Add(clickable);
            }
            else
            {
                row.Children.Add(crumb);
            }
        }

        _propertiesPanel.Children.Add(row);
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Box Model Diagram (Margin > Border > Padding > Content)
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void AddBoxModel(FrameworkElement fe)
    {
        var margin = fe.Margin;
        Thickness borderT = default;
        Thickness padding = default;

        if (fe is Control ctrl)
        {
            borderT = ctrl.BorderThickness;
            padding = ctrl.Padding;
        }
        else if (fe is Border border)
        {
            borderT = border.BorderThickness;
            padding = border.Padding;
        }

        // Only show if there's something interesting
        bool hasMargin = margin.Left != 0 || margin.Top != 0 || margin.Right != 0 || margin.Bottom != 0;
        bool hasBorder = borderT.Left != 0 || borderT.Top != 0 || borderT.Right != 0 || borderT.Bottom != 0;
        bool hasPadding = padding.Left != 0 || padding.Top != 0 || padding.Right != 0 || padding.Bottom != 0;

        if (!hasMargin && !hasBorder && !hasPadding) return;

        AddSection("Box Model");

        // Build nested boxes using borders
        // Outermost = margin, inner = border, inner = padding, innermost = content
        double totalW = 240;
        double totalH = 100;

        var marginBox = new Border
        {
            Background = BrushBoxMargin,
            Width = totalW,
            Height = totalH,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 2, 4, 4)
        };

        var marginLabel = new TextBlock
        {
            Text = "margin",
            FontSize = 8,
            Foreground = BrushBoxLabel,
            Margin = new Thickness(2, 1, 0, 0)
        };

        var borderBox = new Border
        {
            Background = BrushBoxBorder,
            Margin = new Thickness(
                Math.Max(margin.Left > 0 ? 14 : 4, 4),
                Math.Max(margin.Top > 0 ? 14 : 4, 4),
                Math.Max(margin.Right > 0 ? 14 : 4, 4),
                Math.Max(margin.Bottom > 0 ? 14 : 4, 4))
        };

        var paddingBox = new Border
        {
            Background = BrushBoxPadding,
            Margin = new Thickness(
                Math.Max(borderT.Left > 0 ? 12 : 3, 3),
                Math.Max(borderT.Top > 0 ? 12 : 3, 3),
                Math.Max(borderT.Right > 0 ? 12 : 3, 3),
                Math.Max(borderT.Bottom > 0 ? 12 : 3, 3))
        };

        var contentBox = new Border
        {
            Background = BrushBoxContent,
            Margin = new Thickness(
                Math.Max(padding.Left > 0 ? 10 : 2, 2),
                Math.Max(padding.Top > 0 ? 10 : 2, 2),
                Math.Max(padding.Right > 0 ? 10 : 2, 2),
                Math.Max(padding.Bottom > 0 ? 10 : 2, 2))
        };

        var contentLabel = new TextBlock
        {
            Text = $"{fe.ActualWidth:F0} x {fe.ActualHeight:F0}",
            FontSize = 9,
            Foreground = BrushBoxLabel,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        contentBox.Child = contentLabel;
        paddingBox.Child = contentBox;
        borderBox.Child = paddingBox;

        var marginGrid = new Grid();
        marginGrid.Children.Add(borderBox);
        marginGrid.Children.Add(marginLabel);
        marginBox.Child = marginGrid;

        // Margin values overlay
        AddBoxValueLabels(marginGrid, margin, "m");

        _propertiesPanel.Children.Add(marginBox);

        // Legend
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 4, 4)
        };

        if (hasMargin)
            legend.Children.Add(MakeBoxLegend(BrushBoxMargin, $"Margin: {FormatThickness(margin)}"));
        if (hasBorder)
            legend.Children.Add(MakeBoxLegend(BrushBoxBorder, $"Border: {FormatThickness(borderT)}"));
        if (hasPadding)
            legend.Children.Add(MakeBoxLegend(BrushBoxPadding, $"Padding: {FormatThickness(padding)}"));

        _propertiesPanel.Children.Add(legend);
    }

    private static void AddBoxValueLabels(Grid container, Thickness values, string prefix)
    {
        if (values.Top != 0)
        {
            var top = new TextBlock
            {
                Text = $"{values.Top:F0}",
                FontSize = 8,
                Foreground = BrushBoxLabel,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };
            container.Children.Add(top);
        }

        if (values.Bottom != 0)
        {
            var bottom = new TextBlock
            {
                Text = $"{values.Bottom:F0}",
                FontSize = 8,
                Foreground = BrushBoxLabel,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2)
            };
            container.Children.Add(bottom);
        }

        if (values.Left != 0)
        {
            var left = new TextBlock
            {
                Text = $"{values.Left:F0}",
                FontSize = 8,
                Foreground = BrushBoxLabel,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };
            container.Children.Add(left);
        }

        if (values.Right != 0)
        {
            var right = new TextBlock
            {
                Text = $"{values.Right:F0}",
                FontSize = 8,
                Foreground = BrushBoxLabel,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0)
            };
            container.Children.Add(right);
        }
    }

    private static Border MakeBoxLegend(SolidColorBrush color, string text)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        inner.Children.Add(new Border
        {
            Width = 9,
            Height = 9,
            Background = color,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        inner.Children.Add(new TextBlock
        {
            Text = DevToolsUi.Tracked(text.ToUpperInvariant()),
            FontFamily = DevToolsTheme.DisplayFont,
            FontSize = DevToolsTheme.FontXS,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = DevToolsTheme.TextSecondary
        });

        return new Border
        {
            Child = inner,
            Margin = new Thickness(0, 0, 10, 0)
        };
    }

    private static string FormatThickness(Thickness t)
    {
        if (t.Left == t.Right && t.Top == t.Bottom && t.Left == t.Top)
            return $"{t.Left:F0}";
        if (t.Left == t.Right && t.Top == t.Bottom)
            return $"{t.Left:F0},{t.Top:F0}";
        return $"{t.Left:F0},{t.Top:F0},{t.Right:F0},{t.Bottom:F0}";
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Grid Definition Inspector
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void AddGridDefinitions(Grid grid)
    {
        if (grid.RowDefinitions.Count > 0)
        {
            AddSection("Row Definitions");
            for (int i = 0; i < grid.RowDefinitions.Count; i++)
            {
                var rd = grid.RowDefinitions[i];
                var row = Row($"Row[{i}]");
                row.Children.Add(new TextBlock
                {
                    Text = rd.Height.ToString(),
                    Foreground = BrushEnum,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"Actual: {rd.ActualHeight:F1}",
                    Foreground = BrushNumber,
                    FontSize = 10
                });
            }
        }

        if (grid.ColumnDefinitions.Count > 0)
        {
            AddSection("Column Definitions");
            for (int i = 0; i < grid.ColumnDefinitions.Count; i++)
            {
                var cd = grid.ColumnDefinitions[i];
                var row = Row($"Col[{i}]");
                row.Children.Add(new TextBlock
                {
                    Text = cd.Width.ToString(),
                    Foreground = BrushEnum,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"Actual: {cd.ActualWidth:F1}",
                    Foreground = BrushNumber,
                    FontSize = 10
                });
            }
        }
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Style Inspector
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void AddStyleInspector(Style style)
    {
        AddSection("Style");

        if (style.TargetType != null)
        {
            var row = Row("TargetType");
            row.Children.Add(new TextBlock
            {
                Text = style.TargetType.Name,
                Foreground = BrushType,
                FontSize = 11
            });
        }

        if (style.BasedOn != null)
        {
            var row = Row("BasedOn");
            row.Children.Add(new TextBlock
            {
                Text = style.BasedOn.TargetType?.Name ?? "(unknown)",
                Foreground = BrushType,
                FontSize = 11
            });
        }

        if (style.Setters.Count > 0)
        {
            for (int i = 0; i < style.Setters.Count; i++)
            {
                if (style.Setters[i] is not Setter setter)
                    continue;
                var propName = setter.Property?.Name ?? "?";
                var row = Row($"  {propName}");

                if (setter.Value is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    var swatch = new Border
                    {
                        Width = 10, Height = 10,
                        Background = scb,
                        BorderBrush = BrushSwatchBorder,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(0, 1, 4, 1)
                    };
                    row.Children.Add(swatch);
                    row.Children.Add(new TextBlock
                    {
                        Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
                        Foreground = BrushString,
                        FontSize = 11
                    });
                }
                else if (setter.Value is double d)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = d.ToString("F1"),
                        Foreground = BrushNumber,
                        FontSize = 11
                    });
                }
                else if (setter.Value is Enum enumVal)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = enumVal.ToString(),
                        Foreground = BrushEnum,
                        FontSize = 11
                    });
                }
                else if (setter.Value is bool b)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = b ? "true" : "false",
                        Foreground = BrushBool,
                        FontSize = 11
                    });
                }
                else if (setter.Value == null)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = "null",
                        Foreground = BrushNull,
                        FontSize = 11
                    });
                }
                else
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = setter.Value.ToString() ?? "",
                        Foreground = BrushString,
                        FontSize = 11
                    });
                }
            }
        }

        AppendStyleXamlViewer(style);
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Resource Inspector
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void AddResourceInspector(IDictionary<object, object?> resources)
    {
        AddSection($"Resources ({resources.Count})");

        foreach (var key in resources.Keys)
        {
            var val = resources[key];
            var row = Row(key.ToString() ?? "?");

            if (val is SolidColorBrush scb)
            {
                var c = scb.Color;
                var swatch = new Border
                {
                    Width = 10, Height = 10,
                    Background = scb,
                    BorderBrush = BrushSwatchBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 1, 4, 1)
                };
                row.Children.Add(swatch);
                row.Children.Add(new TextBlock
                {
                    Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
                    Foreground = BrushString,
                    FontSize = 11
                });
            }
            else if (val is Style style)
            {
                row.Children.Add(new TextBlock
                {
                    Text = $"Style ({style.TargetType?.Name ?? "?"})",
                    Foreground = BrushType,
                    FontSize = 11
                });
            }
            else if (val == null)
            {
                row.Children.Add(new TextBlock
                {
                    Text = "null",
                    Foreground = BrushNull,
                    FontSize = 11
                });
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = $"{val.GetType().Name}: {val}",
                    Foreground = BrushEnum,
                    FontSize = 11
                });
            }
        }
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Binding Inspector
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools binding inspector enumerates static DependencyProperty fields on FrameworkElement subtypes via reflection.")]
    private void AddBindingInspector(FrameworkElement fe)
    {
        // Check common DependencyProperties for bindings via reflection
        var type = fe.GetType();
        var bindingEntries = new List<(string propName, string path, string mode)>();

        // Get all static DependencyProperty fields
        var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
        foreach (var field in fields)
        {
            if (field.FieldType != typeof(DependencyProperty)) continue;

            if (field.GetValue(null) is not DependencyProperty dp)
                continue;

            try
            {
                var exprBase = fe.GetBindingExpression(dp);
                if (exprBase is BindingExpression expr)
                {
                    var binding = expr.ParentBinding;
                    var pathStr = binding.Path?.Path ?? "(no path)";
                    var modeStr = binding.Mode.ToString();
                    bindingEntries.Add((dp.Name, pathStr, modeStr));
                }
            }
            catch { }
        }

        if (bindingEntries.Count == 0) return;

        AddSection($"Bindings ({bindingEntries.Count})");

        foreach (var (propName, path, mode) in bindingEntries)
        {
            var row = Row(propName);

            row.Children.Add(new TextBlock
            {
                Text = path,
                Foreground = BrushString,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0)
            });
            row.Children.Add(new TextBlock
            {
                Text = $"({mode})",
                Foreground = BrushEnum,
                FontSize = 10
            });
        }
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Row helpers 鈥?each creates one property row with correct styling
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void AddTypeHeader(Type type)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 8, 4, 2)
        };

        row.Children.Add(new TextBlock
        {
            Text = "class ",
            Foreground = BrushKeyword,
            FontSize = 13
        });
        row.Children.Add(new TextBlock
        {
            Text = type.Name,
            Foreground = BrushType,
            FontSize = 13,
            FontWeight = FontWeights.Bold
        });

        if (type.BaseType != null && type.BaseType != typeof(object))
        {
            row.Children.Add(new TextBlock
            {
                Text = $" : {type.BaseType.Name}",
                Foreground = BrushSection,
                FontSize = 12
            });
        }

        _propertiesPanel.Children.Add(row);
    }

    private void AddSection(string name)
    {
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = "\u25b8 " + DevToolsUi.Tracked(name.ToUpperInvariant()),
            Foreground = BrushSection,
            FontFamily = DevToolsTheme.DisplayFont,
            FontSize = DevToolsTheme.FontSm,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 10, 4, 2)
        });
    }

    /// <summary>Creates a horizontal row with the property name already added.</summary>
    private StackPanel Row(string name, Brush? nameBrush = null)
    {
        var isAlt = _rowIndex % 2 == 1;
        _rowIndex++;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 1, 4, 1)
        };

        row.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = nameBrush ?? BrushPropName,
            FontSize = 11,
            Width = NameWidth
        });

        if (isAlt)
        {
            var wrapper = new Border
            {
                Background = BrushRowAlt,
                Padding = new Thickness(0, 1, 0, 1),
                Child = row
            };
            _propertiesPanel.Children.Add(wrapper);
        }
        else
        {
            _propertiesPanel.Children.Add(row);
        }

        return row;
    }

    // 鈹€鈹€ 1. String value (orange) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddStr(string name, string value, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);
        row.Children.Add(new TextBlock
        {
            Text = $"\"{value}\"",
            Foreground = BrushString,
            FontSize = 11
        });
    }

    // 鈹€鈹€ 2. Editable string (orange + TextBox) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddEditable(string name, string value, Action<string> setter, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);
        var tb = new TextBox
        {
            Text = value,
            FontSize = 11,
            Foreground = BrushString,
            Background = BrushEditBg,
            BorderBrush = BrushEditBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 1, 3, 1),
            MinWidth = 150
        };
        tb.TextChanged += (_, _) =>
        {
            try { setter(tb.Text); } catch { }
        };
        row.Children.Add(tb);
    }

    // 鈹€鈹€ 3. Number value (green), optionally editable 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddNum(string name, double value, string fmt = "F1", Action<double>? setter = null, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);
        var text = double.IsNaN(value) ? "NaN"
                 : double.IsInfinity(value) ? "\u221e"
                 : value.ToString(fmt);

        if (setter != null)
        {
            var tb = new TextBox
            {
                Text = text,
                FontSize = 11,
                Foreground = BrushNumber,
                Background = BrushEditBg,
                BorderBrush = BrushEditBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3, 1, 3, 1),
                Width = 80
            };
            tb.TextChanged += (_, _) =>
            {
                if (double.TryParse(tb.Text, out double v))
                {
                    try { setter(v); } catch { }
                }
            };
            row.Children.Add(tb);
        }
        else
        {
            row.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = BrushNumber,
                FontSize = 11
            });
        }
    }

    // 鈹€鈹€ 4. Boolean value (blue, click-to-toggle) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddBool(string name, bool value, Action<bool>? setter = null, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);

        var indicator = new TextBlock
        {
            Text = value ? "\u25cf " : "\u25cb ",
            Foreground = value ? BrushBool : BrushNull,
            FontSize = 11
        };
        var valText = new TextBlock
        {
            Text = value ? "true" : "false",
            Foreground = BrushBool,
            FontSize = 11,
            FontWeight = value ? FontWeights.SemiBold : FontWeights.Normal
        };

        if (setter != null)
        {
            var click = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Padding = new Thickness(2, 0, 8, 0),
                CornerRadius = new CornerRadius(3)
            };
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(indicator);
            inner.Children.Add(valText);
            click.Child = inner;

            click.MouseDown += (_, _) =>
            {
                try
                {
                    setter(!value);
                    if (_selectedVisual != null) UpdatePropertiesPanel(_selectedVisual);
                }
                catch { }
            };
            row.Children.Add(click);
        }
        else
        {
            row.Children.Add(indicator);
            row.Children.Add(valText);
        }
    }

    // 鈹€鈹€ 5. Enum value (gold, click-to-cycle) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddEnum<T>(string name, T value, Action<object>? setter = null) where T : struct, Enum
    {
        AddEnum(name, (Enum)(object)value, setter);
    }

    private void AddEnum(string name, Enum value, Action<object>? setter = null, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);

        if (setter != null)
        {
            var click = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Padding = new Thickness(2, 0, 8, 0),
                CornerRadius = new CornerRadius(3)
            };
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(new TextBlock
            {
                Text = value.ToString(),
                Foreground = BrushEnum,
                FontSize = 11
            });
            inner.Children.Add(new TextBlock
            {
                Text = " \u25b8",
                Foreground = BrushNull,
                FontSize = 9
            });
            click.Child = inner;

            click.MouseDown += (_, _) =>
            {
                try
                {
                    // Enumerate the underlying values (AOT-safe; no array of the enum type is
                    // constructed at runtime) then box each raw value back to the enum type via
                    // Enum.ToObject, which has no AOT cost on an already-loaded enum Type. The
                    // resulting boxed values compare/cycle identically to Enum.GetValues output.
                    var type = value.GetType();
                    var rawValues = Enum.GetValuesAsUnderlyingType(type);
                    var values = new object[rawValues.Length];
                    for (int i = 0; i < rawValues.Length; i++)
                    {
                        values[i] = Enum.ToObject(type, rawValues.GetValue(i)!);
                    }

                    int index = Array.IndexOf(values, value);
                    setter(values[(index + 1) % values.Length]);
                    if (_selectedVisual != null) UpdatePropertiesPanel(_selectedVisual);
                }
                catch { }
            };
            row.Children.Add(click);
        }
        else
        {
            row.Children.Add(new TextBlock
            {
                Text = value.ToString(),
                Foreground = BrushEnum,
                FontSize = 11
            });
        }
    }

    // 鈹€鈹€ 6. Null value (gray) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddNull(string name, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);
        row.Children.Add(new TextBlock
        {
            Text = "null",
            Foreground = BrushNull,
            FontSize = 11
        });
    }

    // 鈹€鈹€ 7. Brush/Color with swatch rectangle 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddBrush(string name, Brush? brush, Action<Brush>? setter = null, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);

        if (brush is SolidColorBrush scb)
        {
            var c = scb.Color;

            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                Background = new SolidColorBrush(c),
                BorderBrush = BrushSwatchBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = (Cursor)Cursors.Hand,
                Margin = new Thickness(0, 1, 6, 1)
            };
            row.Children.Add(swatch);

            string hex = c.A < 255
                ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            TextBox? tb = null;
            if (setter != null)
            {
                tb = new TextBox
                {
                    Text = hex,
                    FontSize = 11,
                    Foreground = BrushString,
                    Background = BrushEditBg,
                    BorderBrush = BrushEditBorder,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(3, 1, 3, 1),
                    Width = 90
                };
                tb.TextChanged += (_, _) =>
                {
                    if (TryParseHexColor(tb.Text, out var nc))
                    {
                        try
                        {
                            var nb = new SolidColorBrush(nc);
                            setter(nb);
                            swatch.Background = nb;
                        }
                        catch { }
                    }
                };
                row.Children.Add(tb);
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = hex,
                    Foreground = BrushString,
                    FontSize = 11
                });
            }

            // Click swatch to open color picker popup
            swatch.MouseDown += (_, _) =>
            {
                var picker = new ColorPicker
                {
                    Color = ((SolidColorBrush)swatch.Background!).Color,
                    Width = 260,
                    IsAlphaEnabled = true
                };
                var popupBorder = new Border
                {
                    Background = new SolidColorBrush(DevToolsTheme.SurfaceRaisedColor),
                    BorderBrush = BrushAccent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Child = picker
                };
                var popup = new Popup
                {
                    Child = popupBorder,
                    PlacementTarget = swatch,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false,
                    IsOpen = true
                };
                picker.ColorChanged += (_, args) =>
                {
                    swatch.Background = new SolidColorBrush(args.NewColor);
                    if (setter != null)
                    {
                        var nb = new SolidColorBrush(args.NewColor);
                        setter(nb);
                    }
                    if (tb != null)
                    {
                        var nc = args.NewColor;
                        tb.Text = nc.A < 255
                            ? $"#{nc.A:X2}{nc.R:X2}{nc.G:X2}{nc.B:X2}"
                            : $"#{nc.R:X2}{nc.G:X2}{nc.B:X2}";
                    }
                };
            };
        }
        else if (brush == null)
        {
            if (setter != null)
            {
                // Clickable empty swatch to create a new color via picker
                var nullSwatch = new Border
                {
                    Width = 14,
                    Height = 14,
                    Background = null,
                    BorderBrush = BrushSwatchBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 1, 6, 1)
                };
                var nullText = new TextBlock
                {
                    Text = "null",
                    Foreground = BrushNull,
                    FontSize = 11
                };
                row.Children.Add(nullSwatch);
                row.Children.Add(nullText);

                nullSwatch.MouseDown += (_, _) =>
                {
                    var picker = new ColorPicker
                    {
                        Color = Color.White,
                        Width = 260,
                        IsAlphaEnabled = true
                    };
                    var popupBorder = new Border
                    {
                        Background = new SolidColorBrush(DevToolsTheme.SurfaceRaisedColor),
                        BorderBrush = BrushAccent,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8),
                        Child = picker
                    };
                    var popup = new Popup
                    {
                        Child = popupBorder,
                        PlacementTarget = nullSwatch,
                        Placement = PlacementMode.Bottom,
                        StaysOpen = false,
                        IsOpen = true
                    };
                    picker.ColorChanged += (_, args) =>
                    {
                        var nb = new SolidColorBrush(args.NewColor);
                        nullSwatch.Background = nb;
                        setter(nb);
                        var nc = args.NewColor;
                        nullText.Text = nc.A < 255
                            ? $"#{nc.A:X2}{nc.R:X2}{nc.G:X2}{nc.B:X2}"
                            : $"#{nc.R:X2}{nc.G:X2}{nc.B:X2}";
                        nullText.Foreground = BrushString;
                    };
                };
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = "null",
                    Foreground = BrushNull,
                    FontSize = 11
                });
            }
        }
        else
        {
            row.Children.Add(new TextBlock
            {
                Text = brush.GetType().Name,
                Foreground = BrushEnum,
                FontSize = 11
            });
        }
    }

    // 鈹€鈹€ 8. Thickness value (teal, editable) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddThickness(string name, Thickness t, Action<Thickness>? setter = null, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);

        string text = t.Left == t.Right && t.Top == t.Bottom && t.Left == t.Top
            ? $"{t.Left:F0}"
            : t.Left == t.Right && t.Top == t.Bottom
                ? $"{t.Left:F0},{t.Top:F0}"
                : $"{t.Left:F0},{t.Top:F0},{t.Right:F0},{t.Bottom:F0}";

        if (setter != null)
        {
            var tb = new TextBox
            {
                Text = text,
                FontSize = 11,
                Foreground = BrushThickness,
                Background = BrushEditBg,
                BorderBrush = BrushEditBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3, 1, 3, 1),
                Width = 120
            };
            tb.TextChanged += (_, _) =>
            {
                if (TryParseThickness(tb.Text, out var nt))
                {
                    try { setter(nt); } catch { }
                }
            };
            row.Children.Add(tb);
        }
        else
        {
            row.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = BrushThickness,
                FontSize = 11
            });
        }
    }

    // 鈹€鈹€ 9. Size value (green) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddSize(string name, Size size, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);
        row.Children.Add(new TextBlock
        {
            Text = $"{size.Width:F1} \u00d7 {size.Height:F1}",
            Foreground = BrushNumber,
            FontSize = 11
        });
    }

    // 鈹€鈹€ 10. Rect value (green) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddRect(string name, Rect rect, Brush? nameBrush = null)
    {
        var row = Row(name, nameBrush);
        row.Children.Add(new TextBlock
        {
            Text = $"{rect.X:F1}, {rect.Y:F1}  {rect.Width:F1} \u00d7 {rect.Height:F1}",
            Foreground = BrushNumber,
            FontSize = 11
        });
    }

    // 鈹€鈹€ 11. Font family (rendered in that font) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddFontFamily(string name, string? fontFamily, DependencyObject? target = null)
    {
        var row = Row(name);

        if (target == null)
        {
            var display = fontFamily ?? "(default)";
            row.Children.Add(new TextBlock
            {
                Text = $"\"{display}\"",
                Foreground = BrushString,
                FontSize = 11,
                FontFamily = new FontFamily(fontFamily ?? FrameworkElement.DefaultFontFamilyName)
            });
            return;
        }

        // The family is a plain string, so offer direct text editing on every platform.
        var editor = new TextBox
        {
            Text = fontFamily ?? string.Empty,
            FontSize = 11,
            Foreground = BrushString,
            Background = BrushEditBg,
            BorderBrush = BrushEditBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 1, 3, 1),
            MinWidth = 140,
            FontFamily = new FontFamily(fontFamily ?? FrameworkElement.DefaultFontFamilyName)
        };
        editor.TextChanged += (_, _) => SetTargetFontFamily(target, editor.Text);
        row.Children.Add(editor);

        // Windows additionally hosts the native common font dialog (family + size + weight + style + color).
        if (Jalium.UI.Controls.Platform.PlatformFactory.IsWindows)
        {
            var button = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(8, 0, 0, 0),
                CornerRadius = new CornerRadius(3)
            };
            button.Child = new TextBlock
            {
                Text = "Aa…",
                Foreground = BrushType,
                FontSize = 11
            };
            button.MouseDown += (_, _) => ShowFontDialogFor(target, fontFamily);
            row.Children.Add(button);
        }
    }

    private static void SetTargetFontFamily(DependencyObject target, string value)
    {
        try
        {
            switch (target)
            {
                case TextBlock tb:
                    ForceSetValue(tb, TextBlock.FontFamilyProperty, new FontFamily(value));
                    break;
                case Control c:
                    ForceSetValue(c, Control.FontFamilyProperty, new FontFamily(value));
                    break;
            }
        }
        catch
        {
            // DevTools should never crash the host because a font edit failed.
        }
    }

    private void ShowFontDialogFor(DependencyObject target, string? currentFamily)
    {
        try
        {
            var dialog = new FontDialog();
            if (!string.IsNullOrWhiteSpace(currentFamily))
            {
                dialog.FontFamily = new FontFamily(currentFamily);
            }

            switch (target)
            {
                case TextBlock tb:
                    dialog.FontSize = tb.FontSize;
                    dialog.FontWeight = tb.FontWeight;
                    dialog.FontStyle = tb.FontStyle;
                    break;
                case Control c:
                    dialog.FontSize = c.FontSize;
                    dialog.FontWeight = c.FontWeight;
                    dialog.FontStyle = c.FontStyle;
                    break;
            }

            if (!dialog.ShowDialog())
            {
                return;
            }

            var family = dialog.FontFamily?.Source;
            switch (target)
            {
                case TextBlock tb:
                    if (!string.IsNullOrEmpty(family)) ForceSetValue(tb, TextBlock.FontFamilyProperty, new FontFamily(family));
                    ForceSetValue(tb, TextBlock.FontSizeProperty, dialog.FontSize);
                    ForceSetValue(tb, TextBlock.FontWeightProperty, dialog.FontWeight);
                    ForceSetValue(tb, TextBlock.FontStyleProperty, dialog.FontStyle);
                    break;
                case Control c:
                    if (!string.IsNullOrEmpty(family)) ForceSetValue(c, Control.FontFamilyProperty, new FontFamily(family));
                    ForceSetValue(c, Control.FontSizeProperty, dialog.FontSize);
                    ForceSetValue(c, Control.FontWeightProperty, dialog.FontWeight);
                    ForceSetValue(c, Control.FontStyleProperty, dialog.FontStyle);
                    break;
            }

            if (_selectedVisual != null)
            {
                UpdatePropertiesPanel(_selectedVisual);
            }
        }
        catch
        {
            // DevTools should never crash the host because a font edit failed.
        }
    }

    // 鈹€鈹€ 12. Font weight (rendered with that weight) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private void AddFontWeight(string name, FontWeight weight)
    {
        var row = Row(name);
        row.Children.Add(new TextBlock
        {
            Text = $"{weight} ({weight.ToOpenTypeWeight()})",
            Foreground = BrushNumber,
            FontSize = 11,
            FontWeight = weight
        });
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Parsing utilities
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    /// <summary>
    /// Forces a dependency property value from DevTools by clearing animated and trigger layer values
    /// that would otherwise override the local value on the next frame.
    /// </summary>
    private static void ForceSetValue(DependencyObject obj, DependencyProperty dp, object? value)
    {
        // Clear animated value (highest priority — overrides local)
        obj.ClearAnimatedValue(dp);

        // Clear trigger layer values that could re-apply
        obj.ClearLayerValue(dp, DependencyObject.LayerValueSource.TemplateTrigger);
        obj.ClearLayerValue(dp, DependencyObject.LayerValueSource.StyleTrigger);

        // Set as local value (highest non-animated priority)
        obj.SetValue(dp, value);
    }

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        hex = hex.TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex[0..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                color = Color.FromRgb(r, g, b);
                return true;
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex[0..2], 16);
                byte r = Convert.ToByte(hex[2..4], 16);
                byte g = Convert.ToByte(hex[4..6], 16);
                byte b = Convert.ToByte(hex[6..8], 16);
                color = Color.FromArgb(a, r, g, b);
                return true;
            }
        }
        catch { }

        return false;
    }

    private static bool TryParseThickness(string text, out Thickness result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        try
        {
            if (parts.Length == 1 && double.TryParse(parts[0], out double uniform))
            {
                result = new Thickness(uniform);
                return true;
            }
            if (parts.Length == 2 && double.TryParse(parts[0], out double h) && double.TryParse(parts[1], out double v))
            {
                result = new Thickness(h, v, h, v);
                return true;
            }
            if (parts.Length == 4 &&
                double.TryParse(parts[0], out double l) && double.TryParse(parts[1], out double t) &&
                double.TryParse(parts[2], out double r) && double.TryParse(parts[3], out double b))
            {
                result = new Thickness(l, t, r, b);
                return true;
            }
        }
        catch { }

        return false;
    }

    public new void CloseDevTools()
    {
        if (_isClosing) return;
        _isClosing = true;

        if (_isPickerActive)
            DeactivatePicker();

        // Tear down the highlight overlay's frame animation before nulling the
        // references. RemoveOverlay() -> StopAnimation() -> DispatcherTimer.Stop()
        // unsubscribes from CompositionTarget.Rendering, releasing the GC root and
        // stopping the perpetual full-window repaint. Idempotent: Close() below
        // fires OnDevToolsClosing where _overlay is already null (the ?. is a no-op).
        _overlay?.RemoveOverlay();
        _targetWindow.DevToolsOverlay = null;
        _overlay = null;

        Close();
    }
}

