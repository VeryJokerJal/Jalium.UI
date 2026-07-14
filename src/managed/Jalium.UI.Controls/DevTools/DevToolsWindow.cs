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
using Jalium.UI.Markup;
using Jalium.UI.Threading;
using ShapePath = Jalium.UI.Shapes.Path;
using WpfClipboard = global::Jalium.UI.Clipboard;
using Jalium.UI.Diagnostics;
using System.IO;
using System.Text;
using Jalium.UI.Documents;
using System.Runtime.InteropServices;
using Jalium.UI.Interop.Win32;
using static Jalium.UI.Interop.Win32.Win32GdiMethods;
using static Jalium.UI.Interop.Win32.Win32Methods;
using Jalium.UI.Automation;
using WriteableBitmap = Jalium.UI.Media.Imaging.WriteableBitmap;

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

        WpfClipboard.SetText(sb.ToString());
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

    private void AddResourceInspector(System.Collections.IDictionary resources)
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

    #region BindingsTab

    private DevToolsUi.DevToolsButton? _bindingsRecordButton;
    private Border? _bindingsStatusPill;
    private StackPanel? _bindingsEventsPanel;
    private StackPanel? _bindingsOverviewPanel;
    private DispatcherTimer? _bindingsRefreshTimer;

    private UIElement BuildBindingsTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        _bindingsRecordButton = DevToolsUi.Button("Start recording", () =>
        {
            if (BindingDiagnostics.IsRecording)
            {
                BindingDiagnostics.StopRecording();
                StopBindingsRefreshTimer();
            }
            else
            {
                BindingDiagnostics.StartRecording();
                StartBindingsRefreshTimer();
            }
            ReflectBindingsRecordingState();
        }, DevToolsUi.ButtonStyle.Primary, icon: "●");
        toolbar.Children.Add(_bindingsRecordButton);
        toolbar.Children.Add(DevToolsUi.Button("Reset", () =>
        {
            BindingDiagnostics.Reset();
            RefreshBindingsOverview();
            RefreshBindingsEvents();
        }, icon: "↺"));

        _bindingsStatusPill = DevToolsUi.Pill("IDLE", DevToolsTheme.TextSecondary);
        toolbar.Children.Add(_bindingsStatusPill);

        var hint = DevToolsUi.Muted("Counters per binding above · live event log below.");
        hint.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0);
        toolbar.Children.Add(hint);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Overview section: framed as an instrument Panel. The same
        //    _bindingsOverviewPanel instance is re-parented into the Panel body
        //    (via its ScrollViewer); refresh logic still clears/appends to it.
        _bindingsOverviewPanel = new StackPanel();
        var overviewScroll = new ScrollViewer { Content = _bindingsOverviewPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var overviewPanel = DevToolsUi.Panel("ACTIVE BINDINGS · SELECTED ELEMENT", overviewScroll, DevToolsTheme.Accent);
        overviewPanel.Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm);
        Grid.SetRow(overviewPanel, 1);
        root.Children.Add(overviewPanel);

        // ── Event log section: framed as a Card. The count header (Eyebrow +
        //    Pill) is built inside _bindingsEventsPanel by RefreshBindingsEvents
        //    because the live count changes every refresh (Panel has no pill slot).
        _bindingsEventsPanel = new StackPanel();
        var eventsScroll = new ScrollViewer { Content = _bindingsEventsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var eventsCard = DevToolsUi.Card(eventsScroll);
        eventsCard.Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase);
        Grid.SetRow(eventsCard, 2);
        root.Children.Add(eventsCard);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    partial void OnBindingsTabActivated()
    {
        ReflectBindingsRecordingState();
        RefreshBindingsOverview();
        RefreshBindingsEvents();
        if (BindingDiagnostics.IsRecording) StartBindingsRefreshTimer();
    }

    private void StartBindingsRefreshTimer()
    {
        if (_bindingsRefreshTimer != null) return;
        _bindingsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _bindingsRefreshTimer.Tick += (_, _) =>
        {
            RefreshBindingsOverview();
            RefreshBindingsEvents();
        };
        _bindingsRefreshTimer.Start();
    }

    private void StopBindingsRefreshTimer()
    {
        _bindingsRefreshTimer?.Stop();
        _bindingsRefreshTimer = null;
    }

    private void ReflectBindingsRecordingState()
    {
        bool rec = BindingDiagnostics.IsRecording;
        if (_bindingsRecordButton != null)
        {
            _bindingsRecordButton.Label = rec ? "Stop recording" : "Start recording";
            _bindingsRecordButton.SetIcon(rec ? "■" : "●");
        }
        if (_bindingsStatusPill?.Child is TextBlock pillText)
        {
            pillText.Text = rec ? "REC" : "IDLE";
            pillText.Foreground = rec ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
            _bindingsStatusPill.Background = new SolidColorBrush(
                rec
                    ? Color.FromArgb(0x38, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B)
                    : Color.FromArgb(0x22, DevToolsTheme.TextSecondaryColor.R, DevToolsTheme.TextSecondaryColor.G, DevToolsTheme.TextSecondaryColor.B));
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "EnumerateDependencyProperties walks the inheritance chain reflecting over DependencyProperty static fields, as its own RequiresUnreferencedCode message documents. DevTools is an opt-in developer/inspector control; preserving the DependencyProperty static fields of inspected types under trimming/AOT is the documented consumer responsibility for using the DevTools Bindings tab, not a defect of this site.")]
    private void RefreshBindingsOverview()
    {
        if (_bindingsOverviewPanel == null) return;
        _bindingsOverviewPanel.Children.Clear();

        // The section title + divider are now provided by the enclosing
        // DevToolsUi.Panel("ACTIVE BINDINGS · SELECTED ELEMENT", ...), so the
        // overview content starts directly with the binding cards or an
        // empty-state caption.
        if (_selectedVisual is not FrameworkElement fe)
        {
            var empty = DevToolsUi.Muted("Select a FrameworkElement in the Inspector to see its bindings.");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
            _bindingsOverviewPanel.Children.Add(empty);
            return;
        }

        int shown = 0;
        foreach (var dp in EnumerateDependencyProperties(fe.GetType()))
        {
            var expr = fe.GetBindingExpression(dp);
            if (expr == null) continue;

            _bindingsOverviewPanel.Children.Add(BuildBindingFlowCard(fe, dp, expr));
            shown++;
        }

        if (shown == 0)
        {
            var empty = DevToolsUi.Muted("(no active bindings on selected element)");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterSm, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
            _bindingsOverviewPanel.Children.Add(empty);
        }
    }

    // ── Binding flow card ────────────────────────────────────────────────

    private Border BuildBindingFlowCard(FrameworkElement targetFe, DependencyProperty dp, BindingExpressionBase expr)
    {
        // ── Pull everything we need from the expression ───────────
        string path;
        BindingMode mode = BindingMode.Default;
        UpdateSourceTrigger trigger = UpdateSourceTrigger.Default;
        string sourceTypeName = "<unresolved>";
        string sourceDetail = "";
        object? sourceInstance = null;
        if (expr is BindingExpression be)
        {
            path = be.ParentBinding?.Path?.Path ?? "";
            mode = be.ParentBinding?.Mode ?? BindingMode.Default;
            trigger = be.ParentBinding?.UpdateSourceTrigger ?? UpdateSourceTrigger.Default;
            sourceInstance = be.ResolvedSource;
            sourceTypeName = sourceInstance?.GetType().Name ?? "<unresolved>";
            sourceDetail = be.ParentBinding?.ElementName != null
                ? $"ElementName={be.ParentBinding.ElementName}"
                : be.ParentBinding?.RelativeSource != null
                    ? $"RelativeSource={be.ParentBinding.RelativeSource.Mode}"
                    : "";
        }
        else
        {
            path = expr.GetType().Name;
        }

        var counters = BindingDiagnostics.GetCounters(targetFe, dp);
        int upT = counters?.UpdateTargetCount ?? 0;
        int upS = counters?.UpdateSourceCount ?? 0;
        int errs = counters?.ErrorCount ?? 0;

        SolidColorBrush accent = expr.Status switch
        {
            BindingStatus.Active => DevToolsTheme.Success,
            BindingStatus.Unattached => DevToolsTheme.Warning,
            BindingStatus.Inactive => DevToolsTheme.TextMuted,
            _ => DevToolsTheme.Error,
        };

        // ── Target box ──
        var targetBox = MakeEndpointBox(
            header: $"{targetFe.GetType().Name}{(string.IsNullOrEmpty(targetFe.Name) ? "" : $"  #{targetFe.Name}")}",
            primary: dp.Name,
            primaryColor: DevToolsTheme.TokenProperty,
            footer: "TARGET",
            borderAccent: accent,
            onClick: () => RevealInInspector(targetFe));

        // ── Source box ──
        var sourceFooter = trigger == UpdateSourceTrigger.Default ? "SOURCE" : $"SOURCE · {trigger}";
        var sourceBox = MakeEndpointBox(
            header: sourceTypeName,
            primary: string.IsNullOrEmpty(path) ? "(no path)" : path,
            primaryColor: DevToolsTheme.TokenString,
            footer: string.IsNullOrEmpty(sourceDetail) ? sourceFooter : $"{sourceFooter} · {sourceDetail}",
            borderAccent: accent,
            onClick: () => { if (sourceInstance is Visual sv) RevealInInspector(sv); });

        // ── Arrow column ──
        var (arrowGlyph, arrowTitle) = FormatMode(mode);
        var arrow = MakeFlowArrow(arrowGlyph, arrowTitle, accent, upT, upS, errs);

        // ── Layout the three-column flow ──
        var flow = new Grid();
        flow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        flow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        flow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(targetBox, 0);
        Grid.SetColumn(arrow, 1);
        Grid.SetColumn(sourceBox, 2);
        flow.Children.Add(targetBox);
        flow.Children.Add(arrow);
        flow.Children.Add(sourceBox);

        // ── Status strip at the bottom ──
        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0),
        };
        statusRow.Children.Add(DevToolsUi.Pill(expr.Status.ToString(), accent));
        if (counters?.LastError is { Length: > 0 } err)
        {
            statusRow.Children.Add(new TextBlock
            {
                Text = err,
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.Error,
                Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var body = new StackPanel();
        body.Children.Add(flow);
        body.Children.Add(statusRow);

        return new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x70, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase),
            Child = body,
        };
    }

    private Border MakeEndpointBox(string header, string primary, Brush primaryColor, string footer,
        SolidColorBrush borderAccent, Action onClick)
    {
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = header,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = primary,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = primaryColor,
            Margin = new Thickness(0, DevToolsTheme.GutterXS, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = footer,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, DevToolsTheme.GutterXS, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var accentBg = new SolidColorBrush(Color.FromArgb(
            0x1E, borderAccent.Color.R, borderAccent.Color.G, borderAccent.Color.B));
        var box = new Border
        {
            Background = accentBg,
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterSm, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            Child = stack,
            Cursor = Cursors.Hand,
        };
        box.MouseDown += (_, _) => onClick();
        return box;
    }

    private UIElement MakeFlowArrow(string glyph, string title, SolidColorBrush accent, int upT, int upS, int errs)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, 0),
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = DevToolsTheme.Font2XL,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, DevToolsTheme.GutterXS, 0, DevToolsTheme.GutterXS),
        });

        var countsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        countsRow.Children.Add(MakeCounter("T", upT, DevToolsTheme.TokenNumber));
        countsRow.Children.Add(MakeCounter("S", upS, DevToolsTheme.Info));
        countsRow.Children.Add(MakeCounter("E", errs, errs > 0 ? DevToolsTheme.Error : DevToolsTheme.TextMuted));
        panel.Children.Add(countsRow);

        return panel;
    }

    private static TextBlock MakeCounter(string prefix, int value, SolidColorBrush color)
    {
        return new TextBlock
        {
            Text = $"{prefix} {value}",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = color,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
        };
    }

    private static (string glyph, string title) FormatMode(BindingMode mode) => mode switch
    {
        BindingMode.OneWay => ("→", "OneWay"),
        BindingMode.TwoWay => ("⇄", "TwoWay"),
        BindingMode.OneTime => ("▸|", "OneTime"),
        BindingMode.OneWayToSource => ("←", "OneWayToSource"),
        _ => ("⇢", "Default"),
    };

    // Shared with ElementContextMenu.cs; the definition lives there so that
    // partial files do not redeclare the method.

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools enumerates DependencyProperty static fields by walking the inheritance chain via reflection.")]
    private static IEnumerable<DependencyProperty> EnumerateDependencyProperties(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                      | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
            foreach (var f in fields)
            {
                if (typeof(DependencyProperty).IsAssignableFrom(f.FieldType))
                {
                    if (f.GetValue(null) is DependencyProperty dp)
                        yield return dp;
                }
            }
        }
    }

    private void RefreshBindingsEvents()
    {
        if (_bindingsEventsPanel == null) return;
        _bindingsEventsPanel.Children.Clear();
        var entries = BindingDiagnostics.Snapshot();

        // Count header: "EVENT LOG" eyebrow + live-count pill (Panel has no pill
        // slot, so the header is built here and the count stays visible).
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
        };
        header.Children.Add(DevToolsUi.Eyebrow("EVENT LOG"));
        header.Children.Add(DevToolsUi.Pill(
            entries.Count.ToString(),
            entries.Count > 0 ? DevToolsTheme.Accent : DevToolsTheme.TextSecondary));
        _bindingsEventsPanel.Children.Add(header);

        if (entries.Count == 0)
        {
            var empty = DevToolsUi.Muted("No binding events yet. Start recording and interact with bound controls.");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
            _bindingsEventsPanel.Children.Add(empty);
            return;
        }

        // Column-caption header row mirroring BuildBindingEventRow's 5-column grid
        // (TIME / KIND / TARGET / arrow / SOURCE), with a hairline beneath it.
        _bindingsEventsPanel.Children.Add(BuildBindingEventColumnHeader());
        _bindingsEventsPanel.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
        });

        int show = Math.Min(entries.Count, 120);
        for (int i = entries.Count - 1; i >= entries.Count - show; i--)
        {
            _bindingsEventsPanel.Children.Add(BuildBindingEventRow(entries[i]));
        }
    }

    // Column captions for the event log, mirroring the 5-column layout of
    // BuildBindingEventRow so the TIME / KIND / TARGET / SOURCE headers sit
    // directly above their data. The horizontal inset matches a data row's
    // outer margin + accent border + inner padding so columns line up.
    private static UIElement BuildBindingEventColumnHeader()
    {
        var grid = new Grid
        {
            Margin = new Thickness(
                DevToolsTheme.GutterBase + 2 + DevToolsTheme.GutterLg,
                DevToolsTheme.GutterXS,
                DevToolsTheme.GutterBase + DevToolsTheme.GutterLg,
                DevToolsTheme.GutterXS),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });       // timestamp
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });      // kind pill
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // target
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // source

        var time = DevToolsUi.Eyebrow("TIME");
        var kind = DevToolsUi.Eyebrow("KIND");
        var target = DevToolsUi.Eyebrow("TARGET");
        var source = DevToolsUi.Eyebrow("SOURCE");
        Grid.SetColumn(time, 0);
        Grid.SetColumn(kind, 1);
        Grid.SetColumn(target, 2);
        Grid.SetColumn(source, 4);
        grid.Children.Add(time);
        grid.Children.Add(kind);
        grid.Children.Add(target);
        grid.Children.Add(source);
        return grid;
    }

    private static UIElement BuildBindingEventRow(BindingDiagnostics.BindingEventEntry entry)
    {
        // Pick a color + directional glyph that matches the event kind.
        var (glyph, accent) = entry.Kind switch
        {
            BindingDiagnostics.BindingEventKind.UpdateTarget   => ("←", DevToolsTheme.Info),
            BindingDiagnostics.BindingEventKind.UpdateSource   => ("→", DevToolsTheme.Warning),
            BindingDiagnostics.BindingEventKind.Activated      => ("●", DevToolsTheme.Success),
            BindingDiagnostics.BindingEventKind.StatusChanged  => ("◐", DevToolsTheme.TextMuted),
            BindingDiagnostics.BindingEventKind.Error          => ("✕", DevToolsTheme.Error),
            _ => ("·", DevToolsTheme.TextMuted),
        };

        var targetBlock = new TextBlock
        {
            Text = $"{entry.TargetTypeName}.{entry.TargetPropertyName}",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenProperty,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var arrow = new TextBlock
        {
            Text = glyph,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0),
            Width = 16,
            TextAlignment = TextAlignment.Center,
        };
        var sourceBlock = new TextBlock
        {
            Text = entry.SourceDescription,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenString,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });       // timestamp
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });      // kind pill
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // target
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // source

        var ts = new TextBlock
        {
            Text = entry.Timestamp.ToString("HH:mm:ss.fff"),
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var kindPill = DevToolsUi.Pill(entry.Kind.ToString(), accent);
        kindPill.Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0);

        Grid.SetColumn(ts, 0);
        Grid.SetColumn(kindPill, 1);
        Grid.SetColumn(targetBlock, 2);
        Grid.SetColumn(arrow, 3);
        Grid.SetColumn(sourceBlock, 4);
        grid.Children.Add(ts);
        grid.Children.Add(kindPill);
        grid.Children.Add(targetBlock);
        grid.Children.Add(arrow);
        grid.Children.Add(sourceBlock);

        return new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x60, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterXS, DevToolsTheme.GutterLg, DevToolsTheme.GutterXS),
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, 2),
            Child = grid,
        };
    }

    #endregion

    #region ElementContextMenu

    // Reused between Inspector and Logical tabs — a single ContextMenu instance
    // cannot be hosted by two different PlacementTargets simultaneously, so we
    // build a fresh one per open.
    private void OpenElementContextMenu(Visual target, UIElement placementTarget)
    {
        if (target == null) return;

        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = Jalium.UI.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false,
        };

        // Everything is dispatched on a subsequent dispatcher turn so the popup
        // has fully closed (and its HWND released) before we do anything that
        // interacts with other top-level windows — SaveFileDialog modal pumps,
        // Clipboard APIs, and screenshot PrintWindow calls have all been
        // observed to no-op when fired during the Click bubble that is also
        // tearing down the ContextMenu popup.
        menu.Items.Add(MakeMenuItem("Reveal in Inspector", "→",
            () => RevealInInspector(target)));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Export element as XAML…", "⇣",
            () => Defer(() => ExportVisualAsXaml(target, recurse: false))));
        menu.Items.Add(MakeMenuItem("Export subtree as XAML…", "⇓",
            () => Defer(() => ExportVisualAsXaml(target, recurse: true))));
        menu.Items.Add(MakeMenuItem("Copy XAML to clipboard", "⧉",
            () => Defer(() => CopyVisualXamlToClipboard(target))));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Save element screenshot…", "▢",
            () => Defer(() => SaveElementScreenshot(target))));
        menu.Items.Add(MakeMenuItem("Save whole window screenshot…", "◱",
            () => Defer(() => SaveWholeWindowScreenshot())));

        menu.Items.Add(new Separator());

        // Delete removes the element from the live tree; it is offered always but
        // disabled (with an explanatory tooltip) when the parent container has no
        // safe removal API. Undo only appears when there is something to restore.
        bool canDelete = CanDeleteElement(target, out var deleteReason);
        var deleteItem = MakeMenuItem("Delete element", "✕",
            () => Defer(() => DeleteElement(target)));
        if (!canDelete)
        {
            deleteItem.IsEnabled = false;
            deleteItem.ToolTip = deleteReason;
        }
        menu.Items.Add(deleteItem);

        if (_deleteRecord != null)
        {
            menu.Items.Add(MakeMenuItem($"Undo delete ({_deleteRecord.Label})", "↩",
                () => Defer(() => UndoDelete())));
        }

        // MousePoint placement pops the menu up at the current cursor location, which
        // is exactly where the right-click happened.
        menu.IsOpen = true;
    }

    /// <summary>
    /// Schedules <paramref name="action"/> for the next dispatcher turn. Used by
    /// context-menu actions that spawn a modal file dialog or touch the clipboard —
    /// running synchronously inside the MenuItem.Click bubble would race the popup
    /// teardown and make the action appear to do nothing.
    /// </summary>
    private void Defer(Action action)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try { action(); }
            catch (Exception ex)
            {
                // Surface the failure in the Tools tab status line so the user has
                // some feedback even when an export silently fails.
                SetToolStatus(_exportStatusText, $"Action failed: {ex.Message}", isError: true);
                SetToolStatus(_screenshotStatusText, $"Action failed: {ex.Message}", isError: true);
            }
        });
    }

    private static MenuItem MakeMenuItem(string header, string glyph, Action onClick)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.Accent,
            Width = 16,
            TextAlignment = TextAlignment.Center,
        };
        var mi = new MenuItem
        {
            Header = header,
            Icon = icon,
        };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    // ── XAML export ──────────────────────────────────────────────────────

    private void ExportVisualAsXaml(Visual visual, bool recurse)
    {
        try
        {
            string xaml = BuildXamlFromVisual(visual, recurse, 0);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = recurse ? "Export subtree as XAML" : "Export element as XAML",
                Filter = "Jalium XAML (*.jalxaml)|*.jalxaml|XAML (*.xaml)|*.xaml|All files (*.*)|*.*",
                DefaultExt = "jalxaml",
                FileName = $"{visual.GetType().Name}-{DateTime.Now:yyyyMMdd-HHmmss}.jalxaml",
            };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName!, xaml, Encoding.UTF8);
            SetToolStatus(_exportStatusText, $"Saved XAML to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus(_exportStatusText, $"Export failed: {ex.Message}", isError: true);
        }
    }

    private void CopyVisualXamlToClipboard(Visual visual)
    {
        try
        {
            string xaml = BuildXamlFromVisual(visual, recurse: true, 0);
            WpfClipboard.SetText(xaml);
            SetToolStatus(_exportStatusText, "XAML copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetToolStatus(_exportStatusText, $"Copy failed: {ex.Message}", isError: true);
        }
    }

    // ── Screenshot ───────────────────────────────────────────────────────

    private void SaveWholeWindowScreenshot()
    {
        try
        {
            if (!TryGetTargetScreenshotSize(out int w, out int h, out string? error))
            {
                SetToolStatus(_screenshotStatusText, error ?? "Target window is not capturable.", isError: true);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save window screenshot",
                Filter = "PNG (*.png)|*.png|All files (*.*)|*.*",
                DefaultExt = "png",
                FileName = $"{_targetWindow.Title}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            };
            if (dialog.ShowDialog() != true) return;

            byte[] pixels = CaptureTargetWindowPixels(out w, out h);
            WritePngFromBgra(dialog.FileName!, pixels, w, h);
            SetToolStatus(_screenshotStatusText, $"Saved to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus(_screenshotStatusText, $"Screenshot failed: {ex.Message}", isError: true);
        }
    }

    private void SaveElementScreenshot(Visual visual)
    {
        if (visual is not UIElement ui)
        {
            SetToolStatus(_screenshotStatusText, "Selected visual is not a UIElement.", isError: true);
            return;
        }
        try
        {
            if (!TryGetTargetScreenshotSize(out int w, out int h, out string? error))
            {
                SetToolStatus(_screenshotStatusText, error ?? "Target window is not capturable.", isError: true);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save element screenshot",
                Filter = "PNG (*.png)|*.png|All files (*.*)|*.*",
                DefaultExt = "png",
                FileName = $"{visual.GetType().Name}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            };
            if (dialog.ShowDialog() != true) return;

            byte[] full = CaptureTargetWindowPixels(out w, out h);

            // Transform the element's local render box through the complete
            // visual affine chain (layout offsets, RenderOffset, scale/rotate/
            // skew), then convert the root-DIP bounds to physical back-buffer
            // pixels. The previous raw VisualBounds sum was wrong for Viewbox/
            // RenderTransform content and forgot DPI scaling on every backend.
            var localBounds = new Rect(0, 0, ui.RenderSize.Width, ui.RenderSize.Height);
            var toWindow = ui.TransformToVisual(_targetWindow);
            var bounds = toWindow?.TransformBounds(localBounds) ?? ui.VisualBounds;
            double scale = _targetWindow.DpiScale > 0 ? _targetWindow.DpiScale : 1.0;
            if (!TryCalculateScreenshotCrop(bounds, scale, w, h, out Int32Rect crop))
            {
                SetToolStatus(_screenshotStatusText,
                    "Selected element is outside the target window or has zero size.", isError: true);
                return;
            }
            int x0 = crop.X;
            int y0 = crop.Y;
            int cw = crop.Width;
            int ch = crop.Height;

            byte[] cropped = new byte[cw * ch * 4];
            for (int y = 0; y < ch; y++)
            {
                int srcOffset = ((y + y0) * w + x0) * 4;
                int dstOffset = y * cw * 4;
                Buffer.BlockCopy(full, srcOffset, cropped, dstOffset, cw * 4);
            }

            WritePngFromBgra(dialog.FileName!, cropped, cw, ch);
            SetToolStatus(_screenshotStatusText, $"Saved element crop to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus(_screenshotStatusText, $"Screenshot failed: {ex.Message}", isError: true);
        }
    }

    internal static bool TryCalculateScreenshotCrop(
        Rect boundsInDips, double dpiScale, int pixelWidth, int pixelHeight,
        out Int32Rect crop)
    {
        crop = Int32Rect.Empty;
        if (boundsInDips.IsEmpty || !double.IsFinite(boundsInDips.X) ||
            !double.IsFinite(boundsInDips.Y) || !double.IsFinite(boundsInDips.Width) ||
            !double.IsFinite(boundsInDips.Height) || !double.IsFinite(dpiScale) ||
            dpiScale <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
            return false;

        double left = Math.Floor(boundsInDips.X * dpiScale);
        double top = Math.Floor(boundsInDips.Y * dpiScale);
        double right = Math.Ceiling(boundsInDips.Right * dpiScale);
        double bottom = Math.Ceiling(boundsInDips.Bottom * dpiScale);
        int x0 = (int)Math.Clamp(left, 0.0, pixelWidth);
        int y0 = (int)Math.Clamp(top, 0.0, pixelHeight);
        int x1 = (int)Math.Clamp(right, 0.0, pixelWidth);
        int y1 = (int)Math.Clamp(bottom, 0.0, pixelHeight);
        if (x1 <= x0 || y1 <= y0)
            return false;

        crop = new Int32Rect(x0, y0, x1 - x0, y1 - y0);
        return true;
    }

    private bool TryGetTargetScreenshotSize(out int width, out int height, out string? error)
    {
        width = 0;
        height = 0;
        error = null;

        if (OperatingSystem.IsWindows())
        {
            nint hwnd = _targetWindow.Handle;
            if (hwnd == nint.Zero)
            {
                error = "Target window has no HWND yet.";
                return false;
            }
            if (!GetClientRect(hwnd, out var rect))
            {
                error = "GetClientRect failed.";
                return false;
            }
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }
        else
        {
            RenderTarget? target = _targetWindow.RenderTarget;
            if (target is not { IsValid: true })
            {
                error = "Target window has no live render target yet.";
                return false;
            }
            width = target.Width;
            height = target.Height;
        }

        if (width <= 0 || height <= 0)
        {
            error = "Target window has zero size.";
            return false;
        }
        return true;
    }

    private byte[] CaptureTargetWindowPixels(out int width, out int height)
    {
        if (OperatingSystem.IsWindows())
        {
            nint hwnd = _targetWindow.Handle;
            if (hwnd == nint.Zero || !GetClientRect(hwnd, out var rect))
                throw new InvalidOperationException("Target HWND is no longer available.");
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Target window has zero size.");
            return CaptureHwndPixels(hwnd, width, height);
        }

        RenderTarget target = _targetWindow.RenderTarget
            ?? throw new InvalidOperationException("Target window has no live render target.");
        if (!target.IsValid || target.IsDrawing)
            throw new InvalidOperationException("Target render target is not ready for capture.");

        JaliumResult request = target.RequestReadback();
        if (request == JaliumResult.NotSupported)
            throw new NotSupportedException(
                $"The {target.Backend} render backend does not support frame readback.");
        if (request != JaliumResult.Ok)
            throw new InvalidOperationException($"Frame readback request failed: {request}.");

        // Linux platform windows render inline on the dispatcher thread. A full
        // frame is required even when the scene is otherwise clean because the
        // native readback request is consumed only by the next EndDraw.
        _targetWindow.ForceRenderFrame();
        if (!ReferenceEquals(target, _targetWindow.RenderTarget) || !target.IsValid)
            throw new InvalidOperationException("Render target changed while capturing the frame.");

        width = target.Width;
        height = target.Height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Captured render target has zero size.");
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        JaliumResult fetch = target.FetchReadback(pixels, (uint)stride,
            out int capturedWidth, out int capturedHeight);
        if (fetch != JaliumResult.Ok)
            throw new InvalidOperationException($"Frame readback failed: {fetch}.");
        if (capturedWidth != width || capturedHeight != height)
            throw new InvalidOperationException(
                $"Captured frame size changed from {width}x{height} to {capturedWidth}x{capturedHeight}.");
        return pixels;
    }

    private static void SetToolStatus(TextBlock? target, string message, bool isError = false)
    {
        if (target == null) return;
        target.Text = message;
        target.Foreground = isError ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
    }

    #endregion

    #region ElementDeleteUndo

    // ── Inspector element mutation: Delete / Undo ────────────────────────
    //
    // The inspector context menu can remove the selected element from the LIVE
    // visual tree and restore the most-recently-deleted one. Removal always goes
    // through the element's typed, public container API — Panel.Children,
    // Border.Child, Decorator.Child, ContentControl.Content — or, when the element
    // is a realized ItemsControl container, through the OWNING ITEMS COLLECTION
    // (Items / ItemsSource), never the items panel (which the generator would just
    // repopulate). For a templated ContentControl we route through the logical
    // ContentControl.Content rather than the template's ContentPresenter, so we do
    // not shadow a TemplateBinding with a local value. We never reach for the
    // protected Visual.AddVisualChild / RemoveVisualChild (they are inaccessible
    // from this assembly and bypass layout/z-order bookkeeping anyway).
    //
    // A single undo slot is kept — this is a diagnostics convenience, not a full
    // edit history — so deleting again replaces the slot. The deleted element is
    // intentionally kept alive by the record so it can be re-inserted.

    private ElementDeleteRecord? _deleteRecord;

    /// <summary>Carries enough state to restore one deleted element.</summary>
    private sealed class ElementDeleteRecord
    {
        public ElementDeleteRecord(string label, Func<UIElement?> restore)
        {
            Label = label;
            Restore = restore;
        }

        /// <summary>Type name of the deleted element, surfaced in the Undo label.</summary>
        public string Label { get; }

        /// <summary>
        /// Re-inserts the element through the same container API it was removed
        /// from and returns the <see cref="UIElement"/> to re-select afterwards, or
        /// <c>null</c> when a non-UIElement data item was restored (its container is
        /// regenerated by a later layout pass and is not a re-selectable element).
        /// </summary>
        public Func<UIElement?> Restore { get; }
    }

    private enum DeleteKind
    {
        None,
        PanelChild,
        BorderChild,
        DecoratorChild,
        BulletDecoratorBullet,
        ViewboxChild,
        ContentControlContent,
        ContentPresenterContent,
        ItemsControlItem,
    }

    /// <summary>
    /// True when <paramref name="target"/> can be removed from its container.
    /// <paramref name="reason"/> explains the refusal when this returns false so the
    /// menu item can show a disabled tooltip instead of failing silently on click.
    /// </summary>
    private bool CanDeleteElement(Visual target, out string reason)
        => ClassifyDeletion(target, out reason, out _) != DeleteKind.None;

    /// <summary>
    /// Classifies how (and whether) <paramref name="target"/> can be detached from
    /// its parent. Deliberately performs no mutation and captures no index — indices
    /// are read at the instant of deletion so a stale menu can never remove the
    /// wrong sibling.
    /// </summary>
    private DeleteKind ClassifyDeletion(Visual target, out string reason, out ItemsControl? itemsOwner)
    {
        reason = string.Empty;
        itemsOwner = null;

        if (target is not UIElement)
        {
            reason = "Only UI elements can be deleted.";
            return DeleteKind.None;
        }
        if (ReferenceEquals(target, _targetWindow))
        {
            reason = "Cannot delete the window root.";
            return DeleteKind.None;
        }

        // A realized ItemsControl container must be removed through its items
        // collection: pulling it out of the items panel is futile because the
        // control regenerates the container from the source on the next refresh.
        // This check runs first so an item container never falls into the Panel
        // branch (it lives directly inside the ItemsControl's items host).
        if (TryResolveItemContainer(target, out var owner, out _))
        {
            if (owner.ItemsSource == null || CanMutateItemsSource(owner.ItemsSource))
            {
                itemsOwner = owner;
                return DeleteKind.ItemsControlItem;
            }
            reason = "The bound item source is read-only or non-observable — remove the item in code.";
            return DeleteKind.None;
        }

        var parent = target.VisualParent;
        if (parent == null)
        {
            reason = "Element has no parent container.";
            return DeleteKind.None;
        }

        if (parent.VisualParent is Viewbox viewbox && ReferenceEquals(viewbox.Child, target))
        {
            return DeleteKind.ViewboxChild;
        }

        switch (parent)
        {
            case Panel:
                return DeleteKind.PanelChild;
            case Border border when ReferenceEquals(border.Child, target):
                return DeleteKind.BorderChild;
            case BulletDecorator bulletDecorator when ReferenceEquals(bulletDecorator.Bullet, target):
                return DeleteKind.BulletDecoratorBullet;
            case Decorator decorator when ReferenceEquals(decorator.Child, target):
                return DeleteKind.DecoratorChild;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, target):
                return DeleteKind.ContentControlContent;
            case ContentPresenter presenter when IsDeletableContentPresenterContent(presenter, target):
                return DeleteKind.ContentPresenterContent;
            default:
                reason = $"Parent '{parent.GetType().Name}' does not support element removal.";
                return DeleteKind.None;
        }
    }

    /// <summary>
    /// A ContentPresenter is deletable only when <paramref name="target"/> is the
    /// real content, not a wrapper the presenter generated for a string/data item
    /// (which would otherwise delete the whole underlying value). For a template
    /// part the logical owner is the templated <see cref="ContentControl"/>, so we
    /// match against that control's Content.
    /// </summary>
    private static bool IsDeletableContentPresenterContent(ContentPresenter presenter, Visual target)
    {
        // Template part of a templated ContentControl: the logical owner is that
        // control, so match (and later mutate) its Content rather than the presenter's.
        if (presenter.TemplatedParent is ContentControl owner)
            return ReferenceEquals(owner.Content, target);
        // Template part of some OTHER control: its Content is most likely a
        // TemplateBinding, and clearing it with a local value would shadow that
        // binding — refuse rather than corrupt the template.
        if (presenter.TemplatedParent != null)
            return false;
        // Standalone presenter the user authored directly: Content is a local value.
        return ReferenceEquals(presenter.Content, target);
    }

    /// <summary>
    /// True when an ItemsSource can be safely mutated AND the mutation will be
    /// reflected live: ItemsControl only re-syncs containers when the source raises
    /// collection-change notifications, so a writable-but-non-observable list (e.g.
    /// a plain <see cref="List{T}"/>) would mutate the data while the UI froze.
    /// </summary>
    private static bool CanMutateItemsSource(System.Collections.IEnumerable itemsSource)
        => itemsSource is System.Collections.IList { IsReadOnly: false, IsFixedSize: false }
           && itemsSource is System.Collections.Specialized.INotifyCollectionChanged;

    /// <summary>
    /// Resolves the owning <see cref="ItemsControl"/> and item index when
    /// <paramref name="element"/> is itself a realized item container (a DIRECT
    /// child of an ItemsControl's items host). A descendant inside an item template
    /// is deliberately NOT matched — it should remain deletable on its own rather
    /// than taking down the whole item.
    /// </summary>
    private static bool TryResolveItemContainer(Visual element, out ItemsControl owner, out int index)
    {
        owner = null!;
        index = -1;
        if (element is not UIElement container) return false;

        for (var ancestor = element.VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
        {
            if (ancestor is not ItemsControl itemsControl) continue;

            var host = itemsControl.ItemsHostInternal;
            if (host == null || !ReferenceEquals(element.VisualParent, host)) continue;

            // The generator map is authoritative whenever it knows the container:
            // the virtualizing pipeline maps every realized child before adding it
            // to the host, so a realized container always resolves through it (and
            // a panel-child index there would be the wrong, window-relative index).
            // When the generator does NOT know the container, the host is populating
            // non-virtually — a plain panel, a VirtualizingPanel with
            // IsVirtualizing=false, or the legacy pipeline — and adds exactly one
            // container per item in source order, so the child index IS the item
            // index. (That equivalence assumes the host holds only item containers in
            // order, which is true for every current host; group headers/adorners
            // added directly to a virtualizing host would break it.)
            int resolved = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
            if (resolved < 0)
                resolved = host.Children.IndexOf(container);

            if (resolved >= 0)
            {
                owner = itemsControl;
                index = resolved;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes <paramref name="target"/> from the live visual tree (per
    /// <see cref="ClassifyDeletion"/>), records an undo entry, drops every inspector
    /// reference pinned to it, and rebuilds the tree. Always invoked through
    /// <see cref="Defer"/> so the context-menu popup is fully gone first.
    /// </summary>
    private void DeleteElement(Visual target)
    {
        var kind = ClassifyDeletion(target, out _, out var itemsOwner);
        if (kind == DeleteKind.None) return;
        if (target is not UIElement element) return;

        string label = target.GetType().Name;
        Func<UIElement?>? restore = null;

        switch (kind)
        {
            case DeleteKind.PanelChild:
            {
                if (element.VisualParent is not Panel panel) return;
                int slot = panel.Children.IndexOf(element);
                if (slot < 0) return;
                panel.Children.RemoveAt(slot);
                if (element.VisualParent != null) return; // removal did not take — bail without a record
                restore = () =>
                {
                    if (element.VisualParent != null) return null;
                    panel.Children.Insert(Math.Clamp(slot, 0, panel.Children.Count), element);
                    return element;
                };
                break;
            }
            case DeleteKind.BorderChild:
            {
                if (element.VisualParent is not Border border) return;
                border.Child = null;
                if (element.VisualParent != null) return;
                restore = () =>
                {
                    if (border.Child == null && element.VisualParent == null) border.Child = element;
                    return element;
                };
                break;
            }
            case DeleteKind.DecoratorChild:
            {
                if (element.VisualParent is not Decorator decorator) return;
                decorator.Child = null;
                if (element.VisualParent != null) return;
                restore = () =>
                {
                    if (decorator.Child == null && element.VisualParent == null) decorator.Child = element;
                    return element;
                };
                break;
            }
            case DeleteKind.BulletDecoratorBullet:
            {
                if (element.VisualParent is not BulletDecorator decorator ||
                    !ReferenceEquals(decorator.Bullet, element))
                {
                    return;
                }

                decorator.Bullet = null;
                if (element.VisualParent != null) return;
                restore = () =>
                {
                    if (decorator.Bullet == null && element.VisualParent == null) decorator.Bullet = element;
                    return element;
                };
                break;
            }
            case DeleteKind.ViewboxChild:
            {
                if (element.VisualParent?.VisualParent is not Viewbox viewbox ||
                    !ReferenceEquals(viewbox.Child, element))
                {
                    return;
                }

                viewbox.Child = null;
                if (element.VisualParent != null) return;
                restore = () =>
                {
                    if (viewbox.Child == null && element.VisualParent == null) viewbox.Child = element;
                    return element;
                };
                break;
            }
            case DeleteKind.ContentControlContent:
            {
                if (element.VisualParent is not ContentControl contentControl) return;
                object? saved = contentControl.Content;
                contentControl.Content = null;
                if (element.VisualParent != null) return;
                restore = () =>
                {
                    if (contentControl.Content == null) contentControl.Content = saved;
                    return saved as UIElement;
                };
                break;
            }
            case DeleteKind.ContentPresenterContent:
            {
                if (element.VisualParent is not ContentPresenter presenter) return;

                // Prefer the logical owner of a template part so the deletion (and
                // its undo) flow through the TemplateBinding instead of pinning a
                // local value onto a template-internal presenter.
                if (presenter.TemplatedParent is ContentControl owner && ReferenceEquals(owner.Content, target))
                {
                    object? saved = owner.Content;
                    owner.Content = null;
                    if (element.VisualParent != null) return;
                    restore = () =>
                    {
                        if (owner.Content == null) owner.Content = saved;
                        return saved as UIElement;
                    };
                }
                else if (presenter.TemplatedParent == null && ReferenceEquals(presenter.Content, target))
                {
                    object? saved = presenter.Content;
                    presenter.Content = null;
                    if (element.VisualParent != null) return;
                    restore = () =>
                    {
                        if (presenter.Content == null) presenter.Content = saved;
                        return saved as UIElement;
                    };
                }
                else
                {
                    return;
                }
                break;
            }
            case DeleteKind.ItemsControlItem:
            {
                var owner = itemsOwner;
                if (owner == null) return;
                // Re-resolve the index at execution time — the menu may have been
                // open while the collection changed underneath it.
                if (!TryResolveItemContainer(target, out _, out int itemIndex)) return;

                if (owner.ItemsSource == null)
                {
                    if (itemIndex < 0 || itemIndex >= owner.Items.Count) return;
                    object data = owner.Items[itemIndex]!;
                    owner.Items.RemoveAt(itemIndex);
                    restore = () =>
                    {
                        owner.Items.Insert(Math.Clamp(itemIndex, 0, owner.Items.Count), data);
                        // Own-container items (a UIElement added straight to Items) can be
                        // re-selected; a virtualizing host re-realizes on the next layout, so
                        // the VisualParent guard in UndoDelete simply skips reveal there.
                        return data as UIElement;
                    };
                }
                else if (CanMutateItemsSource(owner.ItemsSource) && owner.ItemsSource is System.Collections.IList list)
                {
                    if (itemIndex < 0 || itemIndex >= list.Count) return;
                    object data = list[itemIndex]!;
                    list.RemoveAt(itemIndex);
                    restore = () =>
                    {
                        list.Insert(Math.Clamp(itemIndex, 0, list.Count), data);
                        return data as UIElement;
                    };
                }
                else
                {
                    return;
                }
                break;
            }
        }

        if (restore == null) return;

        _deleteRecord = new ElementDeleteRecord(label, restore);

        // The right-click handler selected `target` before opening the menu, so it
        // is the current selection. Drop every reference to the now-detached element
        // before rebuilding so no stale amber highlight or property row survives the
        // refresh.
        if (ReferenceEquals(_selectedVisual, target))
        {
            _selectedVisual = null;
            _overlay?.HighlightElement(null);
            UpdatePropertiesPanel(null);
        }

        RefreshVisualTree();
    }

    /// <summary>
    /// Restores the most-recently-deleted element and re-selects it. Invoked through
    /// <see cref="Defer"/>. The undo slot is consumed whether or not the re-insertion
    /// finds a valid slot, so a failed restore cannot loop.
    /// </summary>
    private void UndoDelete()
    {
        var record = _deleteRecord;
        _deleteRecord = null;
        if (record == null) return;

        UIElement? revealed = record.Restore();
        RefreshVisualTree();

        if (revealed != null && revealed.VisualParent != null)
            RevealInInspector(revealed);
    }

    #endregion

    #region EventsTab

    private StackPanel? _eventsGraphPanel;
    private DevToolsUi.DevToolsButton? _eventsRecordButton;
    private Border? _eventsStatusPill;
    private TextBox? _eventsFilterTextBox;
    private DispatcherTimer? _eventsRefreshTimer;
    private TextBlock? _eventsCountText;
    // Count pill in the framed card header. Mirrors the exact same entries.Count
    // value that _eventsCountText already shows — the in-panel count line remains
    // the source of truth; this is an additive header view, no new computation.
    private TextBlock? _eventsHeaderPillText;

    // Map each rendered row → the entry reference that produced it so we can
    // do incremental updates (prepend the latest samples, trim the oldest)
    // without thrashing the entire panel on every tick.
    private readonly Dictionary<RoutedEventDiagnostics.RoutedEventEntry, UIElement> _eventsRowByEntry
        = new(ReferenceEqualityComparer.Instance);

    private const int EventsMaxVisibleRows = 80;

    private UIElement BuildEventsTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ─────────────────────────────────────────────────────
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };

        _eventsRecordButton = DevToolsUi.Button("Start recording", () =>
        {
            if (RoutedEventDiagnostics.IsRecording)
            {
                RoutedEventDiagnostics.StopRecording();
                StopEventsRefreshTimer();
            }
            else
            {
                RoutedEventDiagnostics.StartRecording();
                StartEventsRefreshTimer();
            }
            ReflectEventsRecordingState();
        }, DevToolsUi.ButtonStyle.Primary, icon: "●");
        toolbar.Children.Add(_eventsRecordButton);

        toolbar.Children.Add(DevToolsUi.Button("Reset", () =>
        {
            RoutedEventDiagnostics.Reset();
            ResetEventsGraph();
        }, icon: "↺"));

        toolbar.Children.Add(DevToolsUi.VerticalDivider());

        var suppressLabel = DevToolsUi.Muted(DevToolsUi.Tracked("SUPPRESS EVENTS:"));
        suppressLabel.FontFamily = DevToolsTheme.DisplayFont;
        toolbar.Children.Add(suppressLabel);
        _eventsFilterTextBox = DevToolsUi.TextInput(240, "e.g. MouseMove, PreviewMouseMove");
        _eventsFilterTextBox.Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0);
        _eventsFilterTextBox.Text = string.Join(", ", RoutedEventDiagnostics.GetFilter());
        _eventsFilterTextBox.LostFocus += (_, _) => ApplyEventsFilter();
        toolbar.Children.Add(_eventsFilterTextBox);
        toolbar.Children.Add(DevToolsUi.Button("Apply", ApplyEventsFilter, icon: "✓"));

        toolbar.Children.Add(DevToolsUi.VerticalDivider());
        toolbar.Children.Add(MakeLegendSwatch(DevToolsTheme.InfoColor,     "Bubble"));
        toolbar.Children.Add(MakeLegendSwatch(DevToolsTheme.WarningColor,  "Tunnel"));
        toolbar.Children.Add(MakeLegendSwatch(DevToolsTheme.SuccessColor,  "Direct"));

        _eventsStatusPill = DevToolsUi.Pill("IDLE", DevToolsTheme.TextSecondary);
        toolbar.Children.Add(_eventsStatusPill);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Graph panel ──────────────────────────────────────────────────
        // The count now lives in the framed card header pill (set in refresh).
        // _eventsCountText is preserved as child[0] of the graph panel — refresh
        // and reset logic keep updating its .Text and rely on its index — but it
        // is collapsed so the count is not shown twice.
        _eventsCountText = new TextBlock
        {
            Text = "EVENTS · 0",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(DevToolsTheme.GutterSm, DevToolsTheme.GutterSm, DevToolsTheme.GutterSm, DevToolsTheme.GutterBase),
            Visibility = Visibility.Collapsed,
        };
        _eventsGraphPanel = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterBase) };
        _eventsGraphPanel.Children.Add(_eventsCountText);

        var scroll = new ScrollViewer
        {
            Content = _eventsGraphPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // Self-built card header: [ EVENTS eyebrow ........ count pill ].
        // Panel() has no pill slot, so we frame with Card() and lay out the
        // header ourselves. The pill mirrors entries.Count (set in refresh).
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerEyebrow = DevToolsUi.Eyebrow("Events", DevToolsTheme.TextSecondary);
        Grid.SetColumn(headerEyebrow, 0);
        headerRow.Children.Add(headerEyebrow);

        var headerPill = DevToolsUi.Pill("0", DevToolsTheme.Accent);
        _eventsHeaderPillText = headerPill.Child as TextBlock;
        Grid.SetColumn(headerPill, 1);
        headerRow.Children.Add(headerPill);

        var headerStack = new StackPanel { Orientation = Orientation.Vertical };
        headerStack.Children.Add(headerRow);
        headerStack.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterBase),
        });

        var cardBody = new Grid();
        cardBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(headerStack, 0);
        cardBody.Children.Add(headerStack);
        Grid.SetRow(scroll, 1);
        cardBody.Children.Add(scroll);

        var card = DevToolsUi.Card(cardBody);
        card.Margin = new Thickness(DevToolsTheme.GutterBase);
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    private static StackPanel MakeLegendSwatch(Color color, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0) };
        row.Children.Add(new Border
        {
            Width = 10, Height = 10,
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    partial void OnEventsTabActivated()
    {
        ReflectEventsRecordingState();
        RefreshEventsGraph();
        if (RoutedEventDiagnostics.IsRecording)
            StartEventsRefreshTimer();
    }

    private void StartEventsRefreshTimer()
    {
        if (_eventsRefreshTimer != null) return;
        _eventsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _eventsRefreshTimer.Tick += (_, _) => RefreshEventsGraph();
        _eventsRefreshTimer.Start();
    }

    private void StopEventsRefreshTimer()
    {
        _eventsRefreshTimer?.Stop();
        _eventsRefreshTimer = null;
    }

    private void ReflectEventsRecordingState()
    {
        bool rec = RoutedEventDiagnostics.IsRecording;
        if (_eventsRecordButton != null)
        {
            _eventsRecordButton.Label = rec ? "Stop recording" : "Start recording";
            _eventsRecordButton.SetIcon(rec ? "■" : "●");
        }
        if (_eventsStatusPill?.Child is TextBlock pillText)
        {
            pillText.Text = rec ? "REC" : "IDLE";
            pillText.Foreground = rec ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
            _eventsStatusPill.Background = new SolidColorBrush(
                rec
                    ? Color.FromArgb(0x38, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B)
                    : Color.FromArgb(0x22, DevToolsTheme.TextSecondaryColor.R, DevToolsTheme.TextSecondaryColor.G, DevToolsTheme.TextSecondaryColor.B));
        }
    }

    private void ApplyEventsFilter()
    {
        if (_eventsFilterTextBox == null) return;
        var text = _eventsFilterTextBox.Text ?? string.Empty;
        var names = text
            .Split(new[] { ',', ';', ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .Where(n => !string.IsNullOrEmpty(n));
        RoutedEventDiagnostics.SetFilter(names);
    }

    /// <summary>
    /// Drops every rendered row and the memoized reference map. Used by the
    /// "Reset" button and when the tab first activates.
    /// </summary>
    private void ResetEventsGraph()
    {
        if (_eventsGraphPanel == null) return;
        _eventsRowByEntry.Clear();
        _eventsGraphPanel.Children.Clear();
        if (_eventsCountText != null)
            _eventsGraphPanel.Children.Add(_eventsCountText);
        if (_eventsCountText != null)
            _eventsCountText.Text = "EVENTS · 0";
        if (_eventsHeaderPillText != null)
            _eventsHeaderPillText.Text = DevToolsUi.Tracked("0");
    }

    /// <summary>
    /// Incremental refresh: only the latest unseen entries are prepended to the
    /// panel, and old rows are trimmed from the bottom once the display cap is
    /// reached. This avoids the large per-tick rebuild that was causing the
    /// tab to stutter when the target window was producing many events.
    /// </summary>
    private void RefreshEventsGraph()
    {
        if (_eventsGraphPanel == null) return;
        if (_eventsCountText == null) return;

        var entries = RoutedEventDiagnostics.Snapshot();
        _eventsCountText.Text = $"EVENTS · {entries.Count}";
        if (_eventsHeaderPillText != null)
            _eventsHeaderPillText.Text = DevToolsUi.Tracked(entries.Count.ToString());

        if (entries.Count == 0)
        {
            // Panel only contains the header when empty.
            while (_eventsGraphPanel.Children.Count > 1)
                _eventsGraphPanel.Children.RemoveAt(_eventsGraphPanel.Children.Count - 1);
            _eventsRowByEntry.Clear();
            var empty = DevToolsUi.Muted("No events recorded yet. Start recording and interact with the target window.");
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, 0);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            _eventsGraphPanel.Children.Add(empty);
            return;
        }

        // Remove the "empty" hint if it's still there.
        if (_eventsGraphPanel.Children.Count == 2
            && _eventsGraphPanel.Children[1] is TextBlock tb
            && !_eventsRowByEntry.ContainsValue(tb))
        {
            _eventsGraphPanel.Children.RemoveAt(1);
        }

        // Walk the snapshot from newest → oldest; stop as soon as we hit an
        // entry we've already rendered (incremental). Record new entries in
        // order so they get prepended in the correct newest-first sequence.
        List<RoutedEventDiagnostics.RoutedEventEntry>? pending = null;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (_eventsRowByEntry.ContainsKey(entry)) break;
            (pending ??= new()).Add(entry);
            if (pending.Count >= EventsMaxVisibleRows) break;
        }

        if (pending != null)
        {
            // pending is in newest-first order, but we must insert right after
            // the header so the newest appears at the top.
            // pending[last] is the oldest of the new batch — insert it first at
            // index 1, then each newer one also at index 1, pushing older ones down.
            for (int k = pending.Count - 1; k >= 0; k--)
            {
                var row = BuildEventGraphRow(pending[k]);
                _eventsRowByEntry[pending[k]] = row;
                _eventsGraphPanel.Children.Insert(1, row);
            }
        }

        // Trim: drop oldest rows past the cap. The panel layout is:
        //   [0] countHeader, [1..] rows (newest first → oldest last).
        while (_eventsGraphPanel.Children.Count - 1 > EventsMaxVisibleRows)
        {
            int lastIdx = _eventsGraphPanel.Children.Count - 1;
            var last = _eventsGraphPanel.Children[lastIdx];
            _eventsGraphPanel.Children.RemoveAt(lastIdx);
            // Drop from the lookup as well so future ticks can rebuild if the
            // same entry ever shows up again (it won't, but keep the map tidy).
            foreach (var kvp in _eventsRowByEntry)
            {
                if (ReferenceEquals(kvp.Value, last))
                {
                    _eventsRowByEntry.Remove(kvp.Key);
                    break;
                }
            }
        }

        // Prune stale lookup keys whose entries have fallen off the diagnostics
        // ring buffer — keeps the dictionary bounded when events churn quickly.
        if (_eventsRowByEntry.Count > EventsMaxVisibleRows * 2)
        {
            var liveSet = new HashSet<RoutedEventDiagnostics.RoutedEventEntry>(entries, ReferenceEqualityComparer.Instance);
            var toRemove = _eventsRowByEntry.Keys.Where(k => !liveSet.Contains(k)).ToList();
            foreach (var k in toRemove)
                _eventsRowByEntry.Remove(k);
        }
    }

    private Border BuildEventGraphRow(RoutedEventDiagnostics.RoutedEventEntry entry)
    {
        var strategyColor = entry.Strategy switch
        {
            RoutingStrategy.Bubble => DevToolsTheme.Info,
            RoutingStrategy.Tunnel => DevToolsTheme.Warning,
            _ => DevToolsTheme.Success,
        };

        // Header row: timestamp + event name + strategy pill + handled flag
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm) };
        headerRow.Children.Add(new TextBlock
        {
            Text = entry.Timestamp.ToString("HH:mm:ss.fff"),
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = entry.EventName,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        headerRow.Children.Add(DevToolsUi.Pill(entry.Strategy.ToString().ToUpperInvariant(), strategyColor));
        if (entry.Handled)
            headerRow.Children.Add(DevToolsUi.Pill("HANDLED", DevToolsTheme.TextMuted));

        // Graph row: nodes with arrows (dispatch direction).
        // Bubble direction is source→root (list order); Tunnel flips that.
        var path = entry.Path;
        var graphRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        bool tunnel = entry.Strategy == RoutingStrategy.Tunnel;

        if (path.Count == 0)
        {
            graphRow.Children.Add(DevToolsUi.Muted("(no path captured)"));
        }
        else
        {
            int count = path.Count;
            for (int step = 0; step < count; step++)
            {
                int pathIndex = tunnel ? count - 1 - step : step;
                var node = path[pathIndex];

                bool isOriginalSource = pathIndex == 0; // always the first visual in the captured chain
                graphRow.Children.Add(BuildEventNode(node, strategyColor, isOriginalSource));

                if (step < count - 1)
                    graphRow.Children.Add(BuildArrow(strategyColor));
            }
        }

        var body = new StackPanel();
        body.Children.Add(headerRow);
        body.Children.Add(new ScrollViewer
        {
            Content = graphRow,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        return new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x80, strategyColor.Color.R, strategyColor.Color.G, strategyColor.Color.B)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(0, DevToolsTheme.RadiusBase.TopRight, DevToolsTheme.RadiusBase.BottomRight, 0),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase),
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, DevToolsTheme.GutterSm),
            Child = body,
        };
    }

    private Border BuildEventNode(RoutedEventDiagnostics.PathNode node, SolidColorBrush strategyColor, bool isOriginalSource)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        if (isOriginalSource)
        {
            label.Children.Add(new TextBlock
            {
                Text = "◉ ",
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = strategyColor,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        label.Children.Add(new TextBlock
        {
            Text = node.TypeName,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(node.ElementName))
        {
            label.Children.Add(new TextBlock
            {
                Text = $"  #{node.ElementName}",
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.Accent,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var bg = new SolidColorBrush(Color.FromArgb(
            isOriginalSource ? (byte)0x38 : (byte)0x1F,
            strategyColor.Color.R, strategyColor.Color.G, strategyColor.Color.B));

        var nodeBorder = new Border
        {
            Background = bg,
            BorderBrush = isOriginalSource ? strategyColor : DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(isOriginalSource ? 2 : 1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
            Child = label,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        // Single click-through handler. Hover highlight is intentionally skipped:
        // a node graph can have dozens of nodes per row, so per-node MouseEnter /
        // MouseLeave subscriptions turn into hundreds of live handlers in seconds.
        nodeBorder.MouseDown += (_, _) =>
        {
            if (!node.VisualRef.TryGetTarget(out var visual)) return;
            RevealInInspector(visual);
        };
        return nodeBorder;
    }

    private static UIElement BuildArrow(SolidColorBrush strategyColor)
    {
        // A slim connector line with an arrow glyph. The strategy color tints it
        // so adjacent rows stay visually bound to their event's color.
        var line = new Border
        {
            Width = 18,
            Height = 2,
            Background = new SolidColorBrush(Color.FromArgb(
                0xB0, strategyColor.Color.R, strategyColor.Color.G, strategyColor.Color.B)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var arrowText = new TextBlock
        {
            Text = "▸",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = strategyColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0),
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(DevToolsTheme.GutterXS, 0, DevToolsTheme.GutterXS, 0),
        };
        panel.Children.Add(line);
        panel.Children.Add(arrowText);
        return panel;
    }

    #endregion

    #region LayoutTab

    private DevToolsUi.DevToolsButton? _layoutRecordButton;
    private DevToolsUi.DevToolsButton? _layoutStackTraceButton;
    private DevToolsUi.DevToolsButton? _layoutSortButton;
    private TextBlock? _layoutStatusText;
    private Border? _layoutStatusPill;
    private StackPanel? _layoutStatsPanel;
    private StackPanel? _invalidationPanel;
    private DispatcherTimer? _layoutRefreshTimer;
    private int _layoutSortMode; // 0 = Measure µs, 1 = Arrange µs, 2 = Invalidations

    // ── Row-reuse pools ─────────────────────────────────────────────────
    // Rebuilding every row each tick was causing hundreds of UIElement
    // allocations per second while recording, which pinned the dispatcher
    // and produced the "界面非常卡顿" symptom. We instead build a fixed
    // pool of row skins once, then update only their text / bar column
    // widths when data changes.
    private readonly List<LayoutStatsRowSkin> _statsRowPool = new();
    private readonly List<InvalidationRowSkin> _invRowPool = new();
    private TextBlock? _statsFooterText;
    private TextBlock? _statsEmptyText;
    private Border? _invCountPill;
    private TextBlock? _invEmptyText;

    private UIElement BuildLayoutTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ──
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };

        _layoutRecordButton = DevToolsUi.Button("Start recording", ToggleLayoutRecording, DevToolsUi.ButtonStyle.Primary, icon: "●");
        toolbar.Children.Add(_layoutRecordButton);

        toolbar.Children.Add(DevToolsUi.Button("Reset", () =>
        {
            LayoutDiagnostics.Reset();
            RefreshLayoutStats();
            RefreshInvalidations();
        }, icon: "↺"));

        _layoutStackTraceButton = DevToolsUi.Toggle("Capture stacks", () =>
        {
            LayoutDiagnostics.CaptureStackTraces = !LayoutDiagnostics.CaptureStackTraces;
            if (_layoutStackTraceButton != null)
                _layoutStackTraceButton.IsActive = LayoutDiagnostics.CaptureStackTraces;
        }, LayoutDiagnostics.CaptureStackTraces, icon: "☰");
        toolbar.Children.Add(_layoutStackTraceButton);

        toolbar.Children.Add(DevToolsUi.VerticalDivider());

        _layoutSortButton = DevToolsUi.Button("Sort: Measure µs", () =>
        {
            _layoutSortMode = (_layoutSortMode + 1) % 3;
            if (_layoutSortButton != null)
                _layoutSortButton.Label = _layoutSortMode switch
                {
                    0 => "Sort: Measure µs",
                    1 => "Sort: Arrange µs",
                    _ => "Sort: Invalidations",
                };
            RefreshLayoutStats();
        }, icon: "⇅");
        toolbar.Children.Add(_layoutSortButton);

        _layoutStatusPill = DevToolsUi.Pill("IDLE", DevToolsTheme.TextSecondary);
        toolbar.Children.Add(_layoutStatusPill);

        _layoutStatusText = DevToolsUi.Muted("Recording is off — start to capture per-element measure / arrange timings.");
        _layoutStatusText.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0);
        toolbar.Children.Add(_layoutStatusText);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Stats section ──
        _layoutStatsPanel = new StackPanel();
        BuildStatsHeader(_layoutStatsPanel);

        var statsScroll = new ScrollViewer
        {
            Content = _layoutStatsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var statsCard = DevToolsUi.Panel("TOP ELEMENTS BY LAYOUT COST", statsScroll, DevToolsTheme.Info);
        statsCard.Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm);
        Grid.SetRow(statsCard, 1);
        root.Children.Add(statsCard);

        // ── Invalidation log ──
        _invalidationPanel = new StackPanel();
        BuildInvalidationHeader(_invalidationPanel);

        var invScroll = new ScrollViewer
        {
            Content = _invalidationPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // Panel has no pill slot, so the title + live count pill are built by
        // hand here and pinned above the scroll inside a Card. The pill object
        // (_invCountPill) is the same instance RefreshInvalidations updates.
        var invHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm) };
        invHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        invHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var invEyebrow = DevToolsUi.Eyebrow("INVALIDATION TIMELINE", DevToolsTheme.TextSecondary);
        Grid.SetColumn(invEyebrow, 0);
        invHeaderRow.Children.Add(invEyebrow);
        Grid.SetColumn(_invCountPill!, 1);
        invHeaderRow.Children.Add(_invCountPill!);

        var invHeader = new StackPanel { Orientation = Orientation.Vertical };
        invHeader.Children.Add(invHeaderRow);
        invHeader.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm),
        });

        // Header in an Auto row, scroll in a Star row so the timeline list is
        // height-bounded and actually scrolls (shows its scrollbar).
        var invStack = new Grid();
        invStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        invStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(invHeader, 0);
        Grid.SetRow(invScroll, 1);
        invStack.Children.Add(invHeader);
        invStack.Children.Add(invScroll);

        var invCard = DevToolsUi.Card(invStack);
        invCard.Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase);
        Grid.SetRow(invCard, 2);
        root.Children.Add(invCard);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    private void BuildStatsHeader(StackPanel panel)
    {
        // The section title now lives in the enclosing Panel header. Here we add
        // a hairline-underlined column-caption strip naming the per-row metrics,
        // then the empty / footer placeholders the refresh path toggles.
        // Inset = a stat row's outer GutterLg margin + inner GutterLg card padding
        // so the "#" caption sits over the rank chip.
        double statInset = DevToolsTheme.GutterLg + DevToolsTheme.GutterLg;
        var captions = new Grid
        {
            Margin = new Thickness(statInset, 0, statInset, DevToolsTheme.GutterXS),
        };
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rankCap = DevToolsUi.Eyebrow("#");
        rankCap.Width = 28;
        rankCap.TextAlignment = TextAlignment.Center;
        Grid.SetColumn(rankCap, 0);
        captions.Children.Add(rankCap);

        var elemCap = DevToolsUi.Eyebrow("ELEMENT · MEASURE / ARRANGE / INVALIDATIONS");
        elemCap.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0);
        Grid.SetColumn(elemCap, 1);
        captions.Children.Add(elemCap);

        panel.Children.Add(captions);
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
        });

        _statsEmptyText = DevToolsUi.Muted("No layout activity captured yet.");
        _statsEmptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _statsEmptyText.TextAlignment = TextAlignment.Center;
        _statsEmptyText.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
        panel.Children.Add(_statsEmptyText);

        _statsFooterText = DevToolsUi.Muted("", DevToolsTheme.FontXS);
        _statsFooterText.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
        _statsFooterText.Visibility = Visibility.Collapsed;
        panel.Children.Add(_statsFooterText);
    }

    private void BuildInvalidationHeader(StackPanel panel)
    {
        // The title + live count pill are now hosted by the enclosing Card header
        // (built in BuildLayoutTab). We only create the pill instance here so the
        // refresh path keeps the same object reference; it is parented there.
        _invCountPill = DevToolsUi.Pill("0", DevToolsTheme.Info);

        // Column captions aligned to the InvalidationRowSkin grid (TIME / KIND /
        // ELEMENT / SOURCE), with a hairline beneath. Left/right margin mirrors a
        // row's outer GutterLg margin + inner GutterLg padding so the captions
        // sit over their columns.
        double rowInset = DevToolsTheme.GutterLg + DevToolsTheme.GutterLg;
        var captions = new Grid
        {
            Margin = new Thickness(rowInset, 0, rowInset, DevToolsTheme.GutterXS),
        };
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var timeCap = DevToolsUi.Eyebrow("TIME");
        Grid.SetColumn(timeCap, 0);
        captions.Children.Add(timeCap);
        var kindCap = DevToolsUi.Eyebrow("KIND");
        Grid.SetColumn(kindCap, 1);
        captions.Children.Add(kindCap);
        var elemCap = DevToolsUi.Eyebrow("ELEMENT");
        Grid.SetColumn(elemCap, 2);
        captions.Children.Add(elemCap);
        var srcCap = DevToolsUi.Eyebrow("SOURCE");
        Grid.SetColumn(srcCap, 3);
        captions.Children.Add(srcCap);

        panel.Children.Add(captions);
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
        });

        _invEmptyText = DevToolsUi.Muted("No invalidations recorded.");
        _invEmptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _invEmptyText.TextAlignment = TextAlignment.Center;
        _invEmptyText.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
        panel.Children.Add(_invEmptyText);
    }

    private void ToggleLayoutRecording()
    {
        if (LayoutDiagnostics.IsRecording)
        {
            LayoutDiagnostics.StopRecording();
            StopLayoutRefreshTimer();
        }
        else
        {
            LayoutDiagnostics.StartRecording();
            StartLayoutRefreshTimer();
        }
        ReflectLayoutRecordingState();
    }

    partial void OnLayoutTabActivated()
    {
        ReflectLayoutRecordingState();
        RefreshLayoutStats();
        RefreshInvalidations();
        if (LayoutDiagnostics.IsRecording)
            StartLayoutRefreshTimer();
    }

    private void StartLayoutRefreshTimer()
    {
        if (_layoutRefreshTimer != null) return;
        // 1s cadence is enough for layout diagnostics — the old 500ms tick was
        // flooding the dispatcher with UI allocations while recording, which
        // is exactly the condition where the target window produces the most
        // Measure/Arrange callbacks.
        _layoutRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _layoutRefreshTimer.Tick += (_, _) =>
        {
            // Any new row skins added to the pool must get IsDiagnosticsIgnored
            // from their field initializer — otherwise the pool expansion
            // inside RefreshLayoutStats leaks DevTools UI into Layout stats.
            using var __scope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();
            RefreshLayoutStats();
            RefreshInvalidations();
        };
        _layoutRefreshTimer.Start();
    }

    private void StopLayoutRefreshTimer()
    {
        _layoutRefreshTimer?.Stop();
        _layoutRefreshTimer = null;
    }

    private void ReflectLayoutRecordingState()
    {
        bool rec = LayoutDiagnostics.IsRecording;
        if (_layoutRecordButton != null)
        {
            _layoutRecordButton.Label = rec ? "Stop recording" : "Start recording";
            _layoutRecordButton.SetIcon(rec ? "■" : "●");
        }
        if (_layoutStatusText != null)
            _layoutStatusText.Text = rec
                ? "Capturing per-element measure / arrange timings."
                : "Recording is off — start to capture per-element measure / arrange timings.";
        if (_layoutStatusPill?.Child is TextBlock pillText)
        {
            pillText.Text = rec ? "REC" : "IDLE";
            pillText.Foreground = rec ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
            _layoutStatusPill.Background = new SolidColorBrush(
                rec
                    ? Color.FromArgb(0x38, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B)
                    : Color.FromArgb(0x22, DevToolsTheme.TextSecondaryColor.R, DevToolsTheme.TextSecondaryColor.G, DevToolsTheme.TextSecondaryColor.B));
        }
    }

    private const int StatsTopN = 30;
    private const int InvalidationMaxRows = 120;

    private void RefreshLayoutStats()
    {
        if (_layoutStatsPanel == null) return;

        var stats = LayoutDiagnostics.SnapshotStats();

        if (stats.Count == 0)
        {
            // Hide all pooled rows + footer; show the empty placeholder.
            if (_statsEmptyText != null) _statsEmptyText.Visibility = Visibility.Visible;
            if (_statsFooterText != null) _statsFooterText.Visibility = Visibility.Collapsed;
            foreach (var row in _statsRowPool) row.Root.Visibility = Visibility.Collapsed;
            return;
        }

        if (_statsEmptyText != null) _statsEmptyText.Visibility = Visibility.Collapsed;

        Func<LayoutDiagnostics.ElementStats, double> sortKey = _layoutSortMode switch
        {
            0 => s => s.MeasureTotalMicros,
            1 => s => s.ArrangeTotalMicros,
            _ => s => s.InvalidateMeasureCount + s.InvalidateArrangeCount + s.InvalidateVisualCount,
        };

        // Pick top-N in O(N log K) instead of sorting everything; avoids
        // LINQ's sort+ToList allocations on large sets.
        var top = SelectTopN(stats, sortKey, StatsTopN);
        double maxValue = top.Count > 0 ? Math.Max(1, sortKey(top[0])) : 1;

        // Grow the pool up to what we need, reusing existing rows.
        while (_statsRowPool.Count < top.Count)
        {
            var skin = LayoutStatsRowSkin.Build(RevealStatsElement);
            // Insert the new row before the footer so footer stays last.
            int footerIndex = _statsFooterText != null
                ? _layoutStatsPanel.Children.IndexOf(_statsFooterText)
                : -1;
            if (footerIndex < 0) _layoutStatsPanel.Children.Add(skin.Root);
            else _layoutStatsPanel.Children.Insert(footerIndex, skin.Root);
            _statsRowPool.Add(skin);
        }

        for (int i = 0; i < _statsRowPool.Count; i++)
        {
            var skin = _statsRowPool[i];
            if (i < top.Count)
            {
                skin.Root.Visibility = Visibility.Visible;
                BindStatsRow(skin, top[i], i, sortKey(top[i]), maxValue);
            }
            else
            {
                skin.Root.Visibility = Visibility.Collapsed;
            }
        }

        if (_statsFooterText != null)
        {
            if (stats.Count > top.Count)
            {
                _statsFooterText.Text = $"+{stats.Count - top.Count} more elements tracked";
                _statsFooterText.Visibility = Visibility.Visible;
            }
            else
            {
                _statsFooterText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void BindStatsRow(LayoutStatsRowSkin skin, LayoutDiagnostics.ElementStats s, int rank, double primaryValue, double maxValue)
    {
        // Stash the element ref on the skin so the Reveal button (bound
        // once) can read current selection without per-refresh closures.
        skin.Current = s;

        skin.RankText.Text = (rank + 1).ToString();
        skin.NameText.Text = string.IsNullOrEmpty(s.ElementName) ? s.TypeName : $"{s.TypeName}  #{s.ElementName}";

        UpdateMetricBar(
            skin.MeasureBar,
            count: s.MeasureCount,
            totalMicros: s.MeasureTotalMicros,
            averageMicros: s.MeasureAverageMicros,
            worstMicros: s.MeasureWorstMicros,
            accent: DevToolsTheme.Info,
            proportion: _layoutSortMode == 0 ? Normalize(primaryValue, maxValue) : Normalize(s.MeasureTotalMicros, maxValue),
            highlighted: _layoutSortMode == 0);

        UpdateMetricBar(
            skin.ArrangeBar,
            count: s.ArrangeCount,
            totalMicros: s.ArrangeTotalMicros,
            averageMicros: s.ArrangeAverageMicros,
            worstMicros: s.ArrangeWorstMicros,
            accent: DevToolsTheme.Warning,
            proportion: _layoutSortMode == 1 ? Normalize(primaryValue, maxValue) : Normalize(s.ArrangeTotalMicros, maxValue),
            highlighted: _layoutSortMode == 1);

        UpdateInvalidationBar(
            skin.InvalidationBar,
            s,
            proportion: _layoutSortMode == 2 ? Normalize(primaryValue, maxValue) : 0,
            highlighted: _layoutSortMode == 2);
    }

    private void RevealStatsElement(LayoutStatsRowSkin skin)
    {
        var s = skin.Current;
        if (s == null || !s.ElementRef.TryGetTarget(out var elem)) return;
        RevealInInspector(elem);
    }

    private static List<LayoutDiagnostics.ElementStats> SelectTopN(
        IReadOnlyList<LayoutDiagnostics.ElementStats> stats,
        Func<LayoutDiagnostics.ElementStats, double> key,
        int n)
    {
        if (stats.Count <= n)
        {
            var copy = new List<LayoutDiagnostics.ElementStats>(stats);
            copy.Sort((a, b) => key(b).CompareTo(key(a)));
            return copy;
        }

        // Maintain a min-heap of size n so we only keep top-n.
        var heap = new List<(double k, LayoutDiagnostics.ElementStats v)>(n);
        foreach (var s in stats)
        {
            double k = key(s);
            if (heap.Count < n)
            {
                heap.Add((k, s));
                SiftUp(heap, heap.Count - 1);
            }
            else if (k > heap[0].k)
            {
                heap[0] = (k, s);
                SiftDown(heap, 0);
            }
        }
        heap.Sort((a, b) => b.k.CompareTo(a.k));
        var result = new List<LayoutDiagnostics.ElementStats>(heap.Count);
        foreach (var (_, v) in heap) result.Add(v);
        return result;

        static void SiftUp(List<(double k, LayoutDiagnostics.ElementStats v)> h, int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (h[p].k <= h[i].k) break;
                (h[p], h[i]) = (h[i], h[p]);
                i = p;
            }
        }
        static void SiftDown(List<(double k, LayoutDiagnostics.ElementStats v)> h, int i)
        {
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, m = i;
                if (l < h.Count && h[l].k < h[m].k) m = l;
                if (r < h.Count && h[r].k < h[m].k) m = r;
                if (m == i) break;
                (h[m], h[i]) = (h[i], h[m]);
                i = m;
            }
        }
    }

    private static double Normalize(double value, double max) => max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);

    private static void UpdateMetricBar(
        MetricBarSkin skin,
        int count, double totalMicros, double averageMicros, double worstMicros,
        SolidColorBrush accent, double proportion, bool highlighted)
    {
        skin.Label.FontWeight = highlighted ? FontWeights.SemiBold : FontWeights.Normal;
        skin.Label.Foreground = highlighted ? accent : DevToolsTheme.TextSecondary;
        skin.Fill.Background = accent;

        double pFill = Math.Max(0, Math.Min(1, proportion));
        if (pFill > 0 && pFill < 0.02) pFill = 0.02;
        double pRest = Math.Max(0, 1 - pFill);

        // Reuse the existing ColumnDefinitions instead of clearing + adding —
        // clearing triggers a re-measure of every child in the Grid.
        skin.TrackHost.ColumnDefinitions[0].Width = new GridLength(pFill > 0 ? pFill : 0.0001, GridUnitType.Star);
        skin.TrackHost.ColumnDefinitions[1].Width = new GridLength(pRest > 0 ? pRest : 0.0001, GridUnitType.Star);
        skin.Fill.Visibility = pFill > 0 ? Visibility.Visible : Visibility.Hidden;

        skin.Numbers.Text = count == 0
            ? $"{count} ×   —"
            : $"{count} ×   tot {totalMicros:F0} µs   avg {averageMicros:F1}   worst {worstMicros:F1}";
    }

    private static void UpdateInvalidationBar(
        InvalidationBarSkin skin,
        LayoutDiagnostics.ElementStats s,
        double proportion, bool highlighted)
    {
        int total = s.InvalidateMeasureCount + s.InvalidateArrangeCount + s.InvalidateVisualCount;
        skin.Label.FontWeight = highlighted ? FontWeights.SemiBold : FontWeights.Normal;
        skin.Label.Foreground = highlighted ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;

        double fill = Math.Max(0, Math.Min(1, proportion <= 0 ? 1 : proportion));
        double mShare = total > 0 ? (double)s.InvalidateMeasureCount / total * fill : 0;
        double aShare = total > 0 ? (double)s.InvalidateArrangeCount / total * fill : 0;
        double vShare = total > 0 ? (double)s.InvalidateVisualCount / total * fill : 0;
        double trail = 1.0 - mShare - aShare - vShare;
        if (trail < 0) trail = 0;

        skin.StripHost.ColumnDefinitions[0].Width = new GridLength(Math.Max(mShare, 0.0001), GridUnitType.Star);
        skin.StripHost.ColumnDefinitions[1].Width = new GridLength(Math.Max(aShare, 0.0001), GridUnitType.Star);
        skin.StripHost.ColumnDefinitions[2].Width = new GridLength(Math.Max(vShare, 0.0001), GridUnitType.Star);
        skin.StripHost.ColumnDefinitions[3].Width = new GridLength(Math.Max(trail, 0.0001), GridUnitType.Star);

        skin.MeasureCell.Visibility = s.InvalidateMeasureCount > 0 ? Visibility.Visible : Visibility.Hidden;
        skin.ArrangeCell.Visibility = s.InvalidateArrangeCount > 0 ? Visibility.Visible : Visibility.Hidden;
        skin.VisualCell.Visibility = s.InvalidateVisualCount > 0 ? Visibility.Visible : Visibility.Hidden;

        skin.Numbers.Text = $"M {s.InvalidateMeasureCount}   A {s.InvalidateArrangeCount}   V {s.InvalidateVisualCount}";
    }

    private void RefreshInvalidations()
    {
        if (_invalidationPanel == null) return;

        var entries = LayoutDiagnostics.SnapshotInvalidations();

        if (_invCountPill?.Child is TextBlock pillText)
            pillText.Text = entries.Count.ToString();

        if (entries.Count == 0)
        {
            if (_invEmptyText != null) _invEmptyText.Visibility = Visibility.Visible;
            foreach (var row in _invRowPool) row.Root.Visibility = Visibility.Collapsed;
            return;
        }
        if (_invEmptyText != null) _invEmptyText.Visibility = Visibility.Collapsed;

        int show = Math.Min(entries.Count, InvalidationMaxRows);

        while (_invRowPool.Count < show)
        {
            var skin = InvalidationRowSkin.Build();
            _invalidationPanel.Children.Add(skin.Root);
            _invRowPool.Add(skin);
        }

        // Fill the pool with the most-recent `show` entries, newest first.
        for (int i = 0; i < _invRowPool.Count; i++)
        {
            var skin = _invRowPool[i];
            if (i < show)
            {
                var e = entries[entries.Count - 1 - i];
                skin.Root.Visibility = Visibility.Visible;
                BindInvalidationRow(skin, e);
            }
            else
            {
                skin.Root.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static void BindInvalidationRow(InvalidationRowSkin skin, LayoutDiagnostics.InvalidationEntry e)
    {
        SolidColorBrush accent = e.Kind switch
        {
            LayoutDiagnostics.InvalidationKind.Measure => DevToolsTheme.Info,
            LayoutDiagnostics.InvalidationKind.Arrange => DevToolsTheme.Warning,
            LayoutDiagnostics.InvalidationKind.Visual => DevToolsTheme.Error,
            _ => DevToolsTheme.TextMuted,
        };

        skin.Time.Text = e.Timestamp.ToString("HH:mm:ss.fff");
        if (skin.Pill.Child is TextBlock pillText)
        {
            pillText.Text = e.Kind.ToString().ToUpperInvariant();
            pillText.Foreground = accent;
        }
        skin.Pill.Background = new SolidColorBrush(Color.FromArgb(0x38, accent.Color.R, accent.Color.G, accent.Color.B));
        skin.Element.Text = string.IsNullOrEmpty(e.ElementName) ? e.TypeName : $"{e.TypeName}  #{e.ElementName}";
        skin.Stack.Text = string.IsNullOrEmpty(e.StackSummary) ? "" : $"← {e.StackSummary}";
        skin.Root.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, accent.Color.R, accent.Color.G, accent.Color.B));
    }

    // ── Skin types: immutable UI shells whose inner parts get re-bound ───

    private sealed class MetricBarSkin
    {
        public required TextBlock Label { get; init; }
        public required Grid TrackHost { get; init; }
        public required Border Fill { get; init; }
        public required TextBlock Numbers { get; init; }
        public required Border Root { get; init; }
    }

    private sealed class InvalidationBarSkin
    {
        public required TextBlock Label { get; init; }
        public required Grid StripHost { get; init; }
        public required Border MeasureCell { get; init; }
        public required Border ArrangeCell { get; init; }
        public required Border VisualCell { get; init; }
        public required TextBlock Numbers { get; init; }
        public required Border Root { get; init; }
    }

    private sealed class LayoutStatsRowSkin
    {
        public required Border Root { get; init; }
        public required TextBlock RankText { get; init; }
        public required TextBlock NameText { get; init; }
        public required MetricBarSkin MeasureBar { get; init; }
        public required MetricBarSkin ArrangeBar { get; init; }
        public required InvalidationBarSkin InvalidationBar { get; init; }
        public LayoutDiagnostics.ElementStats? Current { get; set; }

        public static LayoutStatsRowSkin Build(Action<LayoutStatsRowSkin> onReveal)
        {
            var rankText = new TextBlock
            {
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.TextMuted,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var rankChip = new Border
            {
                Background = DevToolsTheme.Chrome,
                BorderBrush = DevToolsTheme.BorderSubtle,
                BorderThickness = DevToolsTheme.ThicknessHairline,
                CornerRadius = new CornerRadius(3),
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Child = rankText,
            };
            var nameText = new TextBlock
            {
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                FontWeight = FontWeights.SemiBold,
                Foreground = DevToolsTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var measureBar = MakeMetricBarSkin("Measure");
            var arrangeBar = MakeMetricBarSkin("Arrange");
            var invalidationBar = MakeInvalidationBarSkin();

            // The reveal callback needs to see the final skin instance, which
            // we haven't constructed yet. Capture a local that gets assigned
            // below so the closure reads the real instance at click time.
            LayoutStatsRowSkin? self = null;
            var reveal = DevToolsUi.Button("Reveal", () =>
            {
                var s = self;
                if (s != null) onReveal(s);
            }, icon: "→");

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(rankChip, 0);
            Grid.SetColumn(nameText, 1);
            Grid.SetColumn(reveal, 2);
            topGrid.Children.Add(rankChip);
            topGrid.Children.Add(nameText);
            topGrid.Children.Add(reveal);

            var body = new StackPanel { Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0) };
            body.Children.Add(measureBar.Root);
            body.Children.Add(arrangeBar.Root);
            body.Children.Add(invalidationBar.Root);

            var whole = new StackPanel();
            whole.Children.Add(topGrid);
            whole.Children.Add(body);

            var card = new Border
            {
                Background = DevToolsTheme.Chrome,
                BorderBrush = DevToolsTheme.BorderSubtle,
                BorderThickness = DevToolsTheme.ThicknessHairline,
                CornerRadius = DevToolsTheme.RadiusBase,
                Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterSm, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
                Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
                Child = whole,
            };

            self = new LayoutStatsRowSkin
            {
                Root = card,
                RankText = rankText,
                NameText = nameText,
                MeasureBar = measureBar,
                ArrangeBar = arrangeBar,
                InvalidationBar = invalidationBar,
            };
            return self;
        }
    }

    private static MetricBarSkin MakeMetricBarSkin(string labelText)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = labelText,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var trackHost = new Grid
        {
            Height = 8,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        trackHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        trackHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fill = new Border { CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(fill, 0);
        trackHost.Children.Add(fill);

        var trackRest = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, DevToolsTheme.BorderStrongColor.R, DevToolsTheme.BorderStrongColor.G, DevToolsTheme.BorderStrongColor.B)),
            CornerRadius = new CornerRadius(2),
        };
        Grid.SetColumn(trackRest, 1);
        trackHost.Children.Add(trackRest);

        Grid.SetColumn(trackHost, 1);
        grid.Children.Add(trackHost);

        var numbers = new TextBlock
        {
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(numbers, 2);
        grid.Children.Add(numbers);

        var root = new Border { Padding = new Thickness(0, 2, 0, 2), Child = grid };
        return new MetricBarSkin
        {
            Label = label,
            TrackHost = trackHost,
            Fill = fill,
            Numbers = numbers,
            Root = root,
        };
    }

    private static InvalidationBarSkin MakeInvalidationBarSkin()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Inv",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var stripHost = new Grid
        {
            Height = 8,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var measureCell = new Border { Background = DevToolsTheme.Info, Visibility = Visibility.Hidden };
        Grid.SetColumn(measureCell, 0);
        stripHost.Children.Add(measureCell);

        var arrangeCell = new Border { Background = DevToolsTheme.Warning, Visibility = Visibility.Hidden };
        Grid.SetColumn(arrangeCell, 1);
        stripHost.Children.Add(arrangeCell);

        var visualCell = new Border { Background = DevToolsTheme.Error, Visibility = Visibility.Hidden };
        Grid.SetColumn(visualCell, 2);
        stripHost.Children.Add(visualCell);

        var trailCell = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, DevToolsTheme.BorderStrongColor.R, DevToolsTheme.BorderStrongColor.G, DevToolsTheme.BorderStrongColor.B)),
            CornerRadius = new CornerRadius(2),
        };
        Grid.SetColumn(trailCell, 3);
        stripHost.Children.Add(trailCell);

        Grid.SetColumn(stripHost, 1);
        grid.Children.Add(stripHost);

        var numbers = new TextBlock
        {
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(numbers, 2);
        grid.Children.Add(numbers);

        var root = new Border { Padding = new Thickness(0, 2, 0, 2), Child = grid };
        return new InvalidationBarSkin
        {
            Label = label,
            StripHost = stripHost,
            MeasureCell = measureCell,
            ArrangeCell = arrangeCell,
            VisualCell = visualCell,
            Numbers = numbers,
            Root = root,
        };
    }

    private sealed class InvalidationRowSkin
    {
        public required Border Root { get; init; }
        public required TextBlock Time { get; init; }
        public required Border Pill { get; init; }
        public required TextBlock Element { get; init; }
        public required TextBlock Stack { get; init; }

        public static InvalidationRowSkin Build()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var time = new TextBlock
            {
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = DevToolsUi.Pill("", DevToolsTheme.TextMuted);
            pill.Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0);
            var element = new TextBlock
            {
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var stack = new TextBlock
            {
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(time, 0);
            Grid.SetColumn(pill, 1);
            Grid.SetColumn(element, 2);
            Grid.SetColumn(stack, 3);
            grid.Children.Add(time);
            grid.Children.Add(pill);
            grid.Children.Add(element);
            grid.Children.Add(stack);

            var root = new Border
            {
                Background = DevToolsTheme.Chrome,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterXS, DevToolsTheme.GutterLg, DevToolsTheme.GutterXS),
                Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, 2),
                Child = grid,
            };

            return new InvalidationRowSkin
            {
                Root = root,
                Time = time,
                Pill = pill,
                Element = element,
                Stack = stack,
            };
        }
    }

    // Legacy helpers kept for other Tab files that still call MakePlainButton.
    // They return the themed DevToolsUi button; type is Border so existing
    // fields typed as Border? continue to accept the return value.
    private static Border MakePlainButton(string label, Action onClick)
        => DevToolsUi.Button(label, onClick);

    private static Border MakeToggleButton(string label, Action onClick)
        => DevToolsUi.Button(label, onClick);

    #endregion

    #region PerfTab

    private TextBlock? _perfBackendText;
    private TextBlock? _perfEngineText;
    private TextBlock? _perfAdapterText;
    private TextBlock? _perfFpsText;
    private TextBlock? _perfFpsHero;
    private TextBlock? _perfFpsSub;
    private StackPanel? _perfGpuPanel;
    private ScrollViewer? _perfGpuScroll;
    private StackPanel? _perfApiPanel;
    private ScrollViewer? _perfApiScroll;
    private Border? _perfGraphHost;
    private DispatcherTimer? _perfRefreshTimer;
    private Image? _perfGraphImage;
    private DevToolsUi.DevToolsButton? _perfEngineAuto;
    private DevToolsUi.DevToolsButton? _perfEngineVello;
    private DevToolsUi.DevToolsButton? _perfEngineImpeller;

    private UIElement BuildPerfTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ──
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };

        _perfBackendText = DevToolsUi.Text("Backend: ?", DevToolsTheme.FontSm, DevToolsTheme.TextPrimary);
        _perfBackendText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfBackendText);

        _perfEngineText = DevToolsUi.Text("Engine: ?", DevToolsTheme.FontSm, DevToolsTheme.TextPrimary);
        _perfEngineText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfEngineText);

        // Adapter: the GPU DXGI actually picked. When iGPU on a hybrid laptop
        // looks slow, the usual culprit isn't iGPU performance — it's that
        // DXGI fell back to WARP (Microsoft Basic Render Driver, CPU
        // software GPU) because the discrete GPU was disabled in Device
        // Manager and the iGPU wasn't visible to DXGI from the active
        // session. This line surfaces the truth: if AdapterType reads
        // "Software", that explains the 30 FPS + input-lag pattern entirely.
        _perfAdapterText = DevToolsUi.Text("Adapter: ?", DevToolsTheme.FontSm, DevToolsTheme.TextPrimary);
        _perfAdapterText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfAdapterText);

        _perfFpsText = DevToolsUi.Text("FPS —", DevToolsTheme.FontSm, DevToolsTheme.Accent, weight: FontWeights.SemiBold);
        _perfFpsText.FontFamily = DevToolsTheme.MonoFont;
        _perfFpsText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfFpsText);

        toolbar.Children.Add(DevToolsUi.VerticalDivider());
        toolbar.Children.Add(DevToolsUi.Muted("Engine:"));
        _perfEngineAuto     = DevToolsUi.Toggle("Auto",     () => SwitchEngine(RenderingEngine.Auto),     false);
        _perfEngineVello    = DevToolsUi.Toggle("Vello",    () => SwitchEngine(RenderingEngine.Vello),    false);
        _perfEngineImpeller = DevToolsUi.Toggle("Impeller", () => SwitchEngine(RenderingEngine.Impeller), false);
        toolbar.Children.Add(_perfEngineAuto);
        toolbar.Children.Add(_perfEngineVello);
        toolbar.Children.Add(_perfEngineImpeller);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Frame graph ──
        _perfGraphImage = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        // Hero readout floated over the "scope screen": a large amber FPS figure
        // with a small unit + average sub-line, like an instrument's primary gauge.
        _perfFpsHero = new TextBlock
        {
            Text = "—",
            FontFamily = DevToolsTheme.DisplayFontLight,
            FontSize = DevToolsTheme.FontHero,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var heroMeta = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(DevToolsTheme.GutterSm + 2, 0, 0, 0),
        };
        heroMeta.Children.Add(DevToolsUi.Eyebrow("FPS"));
        _perfFpsSub = DevToolsUi.Mono("— ms", DevToolsTheme.FontXS, DevToolsTheme.TextMuted);
        heroMeta.Children.Add(_perfFpsSub);
        var heroRow = new StackPanel { Orientation = Orientation.Horizontal };
        heroRow.Children.Add(_perfFpsHero);
        heroRow.Children.Add(heroMeta);
        var heroBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, DevToolsTheme.ChromeColor.R, DevToolsTheme.ChromeColor.G, DevToolsTheme.ChromeColor.B)),
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterSm, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, 0, 0),
            Child = heroRow,
        };

        var scope = new Grid();
        scope.Children.Add(_perfGraphImage);
        scope.Children.Add(heroBadge);
        var scopeFramed = DevToolsUi.CornerTicks(scope);
        scopeFramed.Margin = new Thickness(DevToolsTheme.GutterXS + 1);

        _perfGraphHost = new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            Child = scopeFramed,
            ClipToBounds = true,
        };
        Grid.SetRow(_perfGraphHost, 1);
        root.Children.Add(_perfGraphHost);

        // ── Legend ──
        var legendRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
        };
        legendRow.Children.Add(MakeLegendChip(PerfColorLayout, "Layout"));
        legendRow.Children.Add(MakeLegendChip(PerfColorRender, "Render"));
        legendRow.Children.Add(MakeLegendChip(PerfColorPresent, "Present"));
        legendRow.Children.Add(DevToolsUi.Muted("  · red dashed = 16 ms budget", DevToolsTheme.FontXS));
        Grid.SetRow(legendRow, 2);
        root.Children.Add(legendRow);

        // ── Bottom panel: GPU snapshot (left, fixed-ish) +
        //     per-API call counters / timing (right, fills remainder) ──
        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 220 });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

        _perfGpuPanel = new StackPanel();
        _perfGpuPanel.Children.Add(DevToolsUi.Muted("(no snapshot published)"));
        _perfGpuScroll = new ScrollViewer { Content = _perfGpuPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var gpuCard = DevToolsUi.Panel("GPU Snapshot", _perfGpuScroll, DevToolsTheme.Success);
        gpuCard.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, DevToolsTheme.GutterBase);
        Grid.SetColumn(gpuCard, 0);
        bottomGrid.Children.Add(gpuCard);

        // Draggable divider so the GPU meters can be widened past the default 280px.
        var perfSplitter = new GridSplitter
        {
            Width = 6,
            Background = DevToolsTheme.BorderSubtle,
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(perfSplitter, 1);
        bottomGrid.Children.Add(perfSplitter);

        _perfApiPanel = new StackPanel();
        _perfApiPanel.Children.Add(DevToolsUi.Muted("(waiting for first frame)"));
        _perfApiScroll = new ScrollViewer { Content = _perfApiPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var apiCard = DevToolsUi.Panel("Draw API", _perfApiScroll, DevToolsTheme.Accent);
        apiCard.Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase);
        Grid.SetColumn(apiCard, 2);
        bottomGrid.Children.Add(apiCard);

        Grid.SetRow(bottomGrid, 3);
        root.Children.Add(bottomGrid);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    private static StackPanel MakeLegendChip(Color color, string label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0),
        };
        row.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    partial void OnPerfTabActivated()
    {
        // Turn on per-frame draw-API counters while the Perf tab is visible.
        // Window.RenderFrame's end-of-frame hook publishes them, RefreshPerfStats
        // renders the snapshot. The flag is the only thing gating the recording
        // overhead — outside DevTools we pay nothing.
        RenderDiagnostics.ApiStatsEnabled = true;
        RefreshPerfStats();
        if (_perfRefreshTimer == null)
        {
            _perfRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _perfRefreshTimer.Tick += (_, _) => RefreshPerfStats();
            _perfRefreshTimer.Start();
        }
    }

    private void SwitchEngine(RenderingEngine engine)
    {
        try
        {
            _targetWindow.SetRenderingEngineOverride(engine);
            RefreshPerfStats();
        }
        catch (Exception ex)
        {
            if (_perfEngineText != null)
            {
                _perfEngineText.Text = $"Engine: switch failed — {ex.Message}";
                _perfEngineText.Foreground = DevToolsTheme.Error;
            }
        }
    }

    private void RefreshPerfStats()
    {
        if (_perfBackendText == null) return;
        _perfBackendText.Text = $"Backend  {_targetWindow.CurrentRenderBackend}";
        if (_perfEngineText != null)
        {
            _perfEngineText.Text = $"Engine  {_targetWindow.CurrentRenderingEngine}";
            _perfEngineText.Foreground = DevToolsTheme.TextPrimary;
        }
        if (_perfAdapterText != null)
        {
            // Cheap to call once per perf refresh — context caches the adapter
            // description struct, no DXGI re-enumeration. If the host context
            // is gone or the backend lacks adapter info, leave the placeholder.
            var ctx = Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
            var adapter = ctx?.GetAdapterInfo();
            if (adapter is { } info)
            {
                string typeTag = info.AdapterType switch
                {
                    Jalium.UI.Interop.GpuAdapterType.Discrete => "Discrete",
                    Jalium.UI.Interop.GpuAdapterType.Integrated => "iGPU",
                    Jalium.UI.Interop.GpuAdapterType.Software => "Software/WARP",
                    _ => "?",
                };
                _perfAdapterText.Text = $"Adapter  {typeTag}: {info.Name}";
                // Red-flag the WARP fallback: this is the "iGPU feels slow"
                // pattern that turns out to be a CPU software renderer.
                // Discrete GPU disabled in Device Manager → DXGI walks to
                // Microsoft Basic Render Driver → 30 FPS + input lag.
                _perfAdapterText.Foreground = info.AdapterType == Jalium.UI.Interop.GpuAdapterType.Software
                    ? DevToolsTheme.Error
                    : DevToolsTheme.TextPrimary;
            }
            else
            {
                _perfAdapterText.Text = "Adapter  —";
                _perfAdapterText.Foreground = DevToolsTheme.TextMuted;
            }
        }

        var currentEngine = _targetWindow.CurrentRenderingEngine;
        if (_perfEngineAuto != null)     _perfEngineAuto.IsActive     = currentEngine == RenderingEngine.Auto;
        if (_perfEngineVello != null)    _perfEngineVello.IsActive    = currentEngine == RenderingEngine.Vello;
        if (_perfEngineImpeller != null) _perfEngineImpeller.IsActive = currentEngine == RenderingEngine.Impeller;

        var history = _targetWindow.FrameHistory;
        var buffer = new FrameHistory.Sample[FrameHistory.Capacity];
        int count = history.CopyTo(buffer);
        if (count > 0 && _perfFpsText != null)
        {
            double totalMs = 0;
            double worst = 0;
            for (int i = 0; i < count; i++)
            {
                totalMs += buffer[i].TotalMs;
                if (buffer[i].TotalMs > worst) worst = buffer[i].TotalMs;
            }
            double avg = totalMs / count;
            double fps = avg > 0 ? 1000.0 / avg : 0;
            _perfFpsText.Text = $"FPS {fps:F1}   avg {avg:F1} ms   worst {worst:F1} ms";
            _perfFpsText.Foreground = worst > 16
                ? DevToolsTheme.Warning
                : fps >= 55 ? DevToolsTheme.Success : DevToolsTheme.Accent;

            if (_perfFpsHero != null)
            {
                _perfFpsHero.Text = fps > 0 ? fps.ToString("F0") : "—";
                _perfFpsHero.Foreground = worst > 16
                    ? DevToolsTheme.Warning
                    : fps >= 55 ? DevToolsTheme.Success : DevToolsTheme.Accent;
            }
            if (_perfFpsSub != null) _perfFpsSub.Text = $"avg {avg:F1} ms";
        }
        else if (_perfFpsText != null)
        {
            _perfFpsText.Text = "FPS — (enable F3 HUD to collect samples)";
            _perfFpsText.Foreground = DevToolsTheme.TextMuted;
            if (_perfFpsHero != null) { _perfFpsHero.Text = "—"; _perfFpsHero.Foreground = DevToolsTheme.TextDisabled; }
            if (_perfFpsSub != null) _perfFpsSub.Text = "no samples";
        }

        if (_perfGraphImage != null)
            _perfGraphImage.Source = RenderPerfGraph(buffer.AsSpan(0, count));

        // GPU snapshot + Draw-API panels are rebuilt as real visualisations
        // (stacked bars + hit-rate meters + a data-bar table), not text blobs.
        RebuildGpuPanel(buffer, count);
        RebuildApiPanel();
    }

    // ── GPU snapshot panel (visualised) ──────────────────────────────────

    private static void GpuSection(StackPanel p, string title)
    {
        var e = DevToolsUi.Eyebrow(title);
        e.Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterSm);
        p.Children.Add(e);
    }

    // Colour a hit-rate meter by health: green good, amber so-so, red poor,
    // greyed when there is no traffic yet.
    private static void AddHitMeter(StackPanel p, string label, long hits, long total)
    {
        double frac = total > 0 ? (double)hits / total : 0;
        Brush fill = total == 0 ? DevToolsTheme.TextDisabled
            : frac >= 0.8 ? DevToolsTheme.Success
            : frac >= 0.4 ? DevToolsTheme.Warning
            : DevToolsTheme.Error;
        string val = total == 0 ? "—" : $"{hits}/{total} · {frac * 100:F0}%";
        p.Children.Add(DevToolsUi.MeterBar(label, frac, val, fill));
    }

    private static Brush BreakdownColor(string name) => name switch
    {
        "Path" => DevToolsTheme.Accent,
        "Text" => DevToolsTheme.Info,
        "SdfRect" => DevToolsTheme.Success,
        "Bitmap" => DevToolsTheme.TokenEnum,
        "Backdrop" => DevToolsTheme.TokenKeyword,
        "LiquidGlass" => DevToolsTheme.TokenType,
        _ => DevToolsTheme.TextMuted,
    };

    private void RebuildGpuPanel(FrameHistory.Sample[] buffer, int count)
    {
        if (_perfGpuPanel == null) return;
        var snap = RenderDiagnostics.LatestGpuSnapshot;
        double saved = _perfGpuScroll?.VerticalOffset ?? 0;
        var p = _perfGpuPanel;
        p.Children.Clear();

        if (snap == null)
        {
            p.Children.Add(DevToolsUi.Muted("(no snapshot — backend must call RenderDiagnostics.PublishGpuSnapshot once per frame)"));
            return;
        }

        // 1. Last-frame breakdown — Layout / Render / Present as a stacked bar.
        if (count > 0)
        {
            var last = buffer[count - 1];
            double apiMs = RenderDiagnostics.LatestDrawApiStats?.TotalMs ?? 0;
            // apiMs spans the whole frame's draw-API calls, including EndDraw
            // (which runs in the P segment after MarkRender) and the overlay's
            // own draws — so the managed-overhead gap must be measured against
            // R+P, not R alone. The old `RenderMs - apiMs` read -130 ms under a
            // slow compositor because EndDraw's Present stall lives in P.
            double gap = (last.RenderMs + last.PresentMs) - apiMs;
            GpuSection(p, "LAST FRAME");
            p.Children.Add(DevToolsUi.StackedBar(new (double Value, Brush Color)[]
            {
                (last.LayoutMs,  new SolidColorBrush(PerfColorLayout)),
                (last.RenderMs,  new SolidColorBrush(PerfColorRender)),
                (last.PresentMs, new SolidColorBrush(PerfColorPresent)),
            }));
            p.Children.Add(new Border { Height = DevToolsTheme.GutterSm });
            p.Children.Add(DevToolsUi.KeyValueRow("L / R / P", $"{last.LayoutMs:F1} / {last.RenderMs:F1} / {last.PresentMs:F1} ms"));
            p.Children.Add(DevToolsUi.KeyValueRow("Total frame", $"{last.TotalMs:F1} ms"));
            p.Children.Add(DevToolsUi.KeyValueRow("API / gap", $"{apiMs:F1} / {gap:F1} ms"));
        }

        // 2. GPU breakdown — the hero: hardware-timestamped split by category.
        var timing = RenderDiagnostics.LatestGpuTiming;
        if (timing != null && timing.Valid)
        {
            GpuSection(p, "GPU BREAKDOWN");
            p.Children.Add(DevToolsUi.KeyValueRow("Total GPU", $"{timing.TotalGpuMs:F1} ms · {timing.BatchCount} batches"));
            var cats = new (string Name, long Ns)[]
            {
                ("Path",        timing.PathNs),
                ("Text",        timing.TextNs),
                ("SdfRect",     timing.SdfRectNs),
                ("Bitmap",      timing.BitmapNs),
                ("Backdrop",    timing.BackdropNs),
                ("LiquidGlass", timing.LiquidGlassNs),
                ("Other",       timing.OtherNs),
            };
            Array.Sort(cats, (a, b) => b.Ns.CompareTo(a.Ns));
            double totMs = timing.TotalGpuMs > 0 ? timing.TotalGpuMs : 1.0;

            var segs = new List<(double Value, Brush Color)>();
            foreach (var c in cats)
                if (c.Ns > 0) segs.Add((c.Ns / 1_000_000.0, BreakdownColor(c.Name)));
            p.Children.Add(DevToolsUi.StackedBar(segs, 12));
            p.Children.Add(new Border { Height = DevToolsTheme.GutterSm });

            foreach (var c in cats)
            {
                if (c.Ns <= 0) continue;
                double ms = c.Ns / 1_000_000.0;
                double frac = ms / totMs;
                p.Children.Add(DevToolsUi.MeterBar(c.Name, frac, $"{ms:F1} ms · {frac * 100:F0}%", BreakdownColor(c.Name)));
            }
        }
        else if (timing != null)
        {
            GpuSection(p, "GPU BREAKDOWN");
            p.Children.Add(DevToolsUi.Muted("(waiting for first timestamped frame)"));
        }

        // 3. Text caches — hit-rate meters (instant red/amber/green health).
        var tc = RenderDiagnostics.LatestTextCacheStats;
        if (tc != null && (tc.DrawTextCalls > 0 || tc.LayoutHits + tc.LayoutMisses > 0))
        {
            GpuSection(p, "TEXT CACHES");
            AddHitMeter(p, "Layout", tc.LayoutHits, tc.LayoutHits + tc.LayoutMisses);
            AddHitMeter(p, "Instance", tc.InstanceHits, tc.InstanceHits + tc.InstanceMisses);
            AddHitMeter(p, "Glyph", tc.GlyphRasterHits, tc.GlyphRasterHits + tc.GlyphRasterMisses);
            p.Children.Add(DevToolsUi.KeyValueRow("DrawText / glyphs", $"{tc.DrawTextCalls} / {tc.EmittedGlyphs}"));
            if (tc.AtlasResets > 0)
                p.Children.Add(DevToolsUi.KeyValueRow("Atlas resets", tc.AtlasResets.ToString(), DevToolsTheme.Warning));
        }

        // 4. Path raster cache — hit-rate meters + CPU work.
        var pc = RenderDiagnostics.LatestPathCacheStats;
        if (pc != null)
        {
            long st = pc.StrokeHits + pc.StrokeMisses;
            long ft = pc.FillHits + pc.FillMisses;
            long gt = pc.GeometryHits + pc.GeometryMisses;
            if (st + ft + gt > 0)
            {
                GpuSection(p, "PATH RASTER CACHE");
                if (st > 0) AddHitMeter(p, "Stroke", pc.StrokeHits, st);
                if (ft > 0) AddHitMeter(p, "Fill", pc.FillHits, ft);
                if (gt > 0) AddHitMeter(p, "Geometry", pc.GeometryHits, gt);
                if (pc.FlattenNs > 0 || pc.TriangulateNs > 0)
                {
                    p.Children.Add(DevToolsUi.KeyValueRow("Flatten", $"{pc.FlattenNs / 1_000_000.0:F2} ms"));
                    p.Children.Add(DevToolsUi.KeyValueRow("Triangulate", $"{pc.TriangulateNs / 1_000_000.0:F2} ms"));
                }
            }
        }

        // 5. Retained-mode cache — replay% is the win.
        var rc = RenderDiagnostics.LatestRetainedCacheStats;
        if (rc != null && rc.Records + rc.Replays + rc.Bypasses > 0)
        {
            GpuSection(p, "RETAINED-MODE CACHE");
            AddHitMeter(p, "Replays", rc.Replays, rc.Records + rc.Replays + rc.Bypasses);
            p.Children.Add(DevToolsUi.KeyValueRow("Records / Bypass", $"{rc.Records} / {rc.Bypasses}"));
        }

        // 6. Memory — atlas fill meter + cache/texture sizes.
        GpuSection(p, "MEMORY");
        double atlasFrac = snap.GlyphAtlasSlotsTotal > 0 ? (double)snap.GlyphAtlasSlotsUsed / snap.GlyphAtlasSlotsTotal : 0;
        p.Children.Add(DevToolsUi.MeterBar("Glyph atlas", atlasFrac,
            $"{snap.GlyphAtlasSlotsUsed}/{snap.GlyphAtlasSlotsTotal} · {snap.GlyphAtlasBytes / (1024.0 * 1024.0):F1} MB",
            DevToolsTheme.Info));
        p.Children.Add(DevToolsUi.KeyValueRow("Path cache", $"{snap.PathCacheEntries} · {snap.PathCacheBytes / (1024.0 * 1024.0):F2} MB"));
        p.Children.Add(DevToolsUi.KeyValueRow("Textures", $"{snap.TextureCount} · {snap.TextureBytes / (1024.0 * 1024.0):F2} MB"));

        // 7. Frame pacing — scalar readouts.
        var pacing = RenderDiagnostics.LatestFramePacing;
        if (pacing != null)
        {
            GpuSection(p, "FRAME PACING");
            p.Children.Add(DevToolsUi.KeyValueRow("Begin attempts",
                pacing.BeginFailures == 0 ? $"{pacing.BeginAttempts} clean" : $"{pacing.BeginAttempts} +{pacing.BeginFailures} retries",
                pacing.BeginFailures == 0 ? DevToolsTheme.TextPrimary : DevToolsTheme.Warning));
            if (pacing.SwapBufferCount > 0)
            {
                p.Children.Add(DevToolsUi.KeyValueRow("Swap buffers", pacing.SwapBufferCount.ToString()));
                p.Children.Add(DevToolsUi.KeyValueRow("Waitable wait", $"{pacing.FrameWaitableWaitMs:F1} ms"));
                p.Children.Add(DevToolsUi.KeyValueRow("Fence wait", $"{pacing.FrameGpuWaitMs:F1} ms"));
            }
            if (pacing.LastFramePresentToReadyNs > 0)
                p.Children.Add(DevToolsUi.KeyValueRow("Present to ready", $"{pacing.LastFramePresentToReadyMs:F1} ms"));
        }

        // 8. Bitmap upload.
        var bmp = RenderDiagnostics.LatestBitmapUploadStats;
        if (bmp != null && (bmp.UploadCount > 0 || bmp.FastPathHits > 0))
        {
            GpuSection(p, "BITMAP UPLOAD");
            p.Children.Add(DevToolsUi.KeyValueRow("Uploads", $"{bmp.UploadCount} · {bmp.UploadBytes / (1024.0 * 1024.0):F2} MB"));
            AddHitMeter(p, "FastPath", bmp.FastPathHits, bmp.UploadCount + bmp.FastPathHits);
        }

        // 9. Layout pass.
        var lay = RenderDiagnostics.LatestLayoutPassStats;
        if (lay != null && (lay.MeasureCount > 0 || lay.ArrangeCount > 0))
        {
            GpuSection(p, "LAYOUT");
            p.Children.Add(DevToolsUi.KeyValueRow("Measure", $"{lay.MeasureCount} · {lay.MeasureMs:F1} ms"));
            p.Children.Add(DevToolsUi.KeyValueRow("Arrange", $"{lay.ArrangeCount} · {lay.ArrangeMs:F1} ms"));
            if (lay.Iterations > 1)
                p.Children.Add(DevToolsUi.KeyValueRow("Iterations", $"{lay.Iterations} (thrash)", DevToolsTheme.Warning));
        }

        if (_perfGpuScroll != null) _perfGpuScroll.ScrollToVerticalOffset(saved);
    }

    // ── Draw-API panel (visualised data-bar table) ───────────────────────

    private void RebuildApiPanel()
    {
        if (_perfApiPanel == null) return;
        var apiSnap = RenderDiagnostics.LatestDrawApiStats;
        double saved = _perfApiScroll?.VerticalOffset ?? 0;
        var p = _perfApiPanel;
        p.Children.Clear();

        if (apiSnap == null || apiSnap.Entries.Count == 0)
        {
            p.Children.Add(DevToolsUi.Muted("(waiting for next frame)"));
            return;
        }

        p.Children.Add(DevToolsUi.KeyValueRow($"frame {apiSnap.Timestamp:HH:mm:ss.fff}", $"{apiSnap.TotalMs:F2} ms total", DevToolsTheme.Accent));
        p.Children.Add(new Border { Height = DevToolsTheme.GutterSm });
        p.Children.Add(MakeApiHeaderRow());
        p.Children.Add(new Border { Height = 1, Background = DevToolsTheme.BorderSubtle, Margin = new Thickness(0, 2, 0, 3) });

        var entries = apiSnap.Entries.OrderByDescending(e => e.TotalMs).ToList();
        double maxMs = 0;
        foreach (var e in entries) if (e.TotalMs > maxMs) maxMs = e.TotalMs;
        if (maxMs <= 0) maxMs = 1;

        foreach (var e in entries)
        {
            double frac = e.TotalMs / maxMs;
            Brush msColor = frac >= 0.5 ? DevToolsTheme.Accent : frac >= 0.2 ? DevToolsTheme.Warning : DevToolsTheme.TextPrimary;
            p.Children.Add(MakeApiRow(e.Name, e.Count, e.TotalMs, e.AvgUs, frac, msColor));
        }

        if (_perfApiScroll != null) _perfApiScroll.ScrollToVerticalOffset(saved);
    }

    private static Grid ApiRowGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        return g;
    }

    private static UIElement MakeApiHeaderRow()
    {
        var g = ApiRowGrid();
        g.Children.Add(ColCaption("API", false, 0));
        g.Children.Add(ColCaption("CALLS", true, 1));
        g.Children.Add(ColCaption("MS", true, 2));
        g.Children.Add(ColCaption("AVG µS", true, 3));
        return g;
    }

    // Column caption — uppercase Bahnschrift but NOT letter-tracked, so it stays
    // inside the fixed numeric columns.
    private static TextBlock ColCaption(string text, bool right, int col)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(right ? 0 : 4, 0, right ? 6 : 0, 0),
        };
        Grid.SetColumn(t, col);
        return t;
    }

    private static UIElement MakeApiRow(string name, long calls, double ms, double avgUs, double frac, Brush msColor)
    {
        frac = Math.Clamp(double.IsNaN(frac) ? 0 : frac, 0, 1);

        // Background "data bar": proportional amber fill behind the row showing
        // relative total-ms cost, so the hottest API reads at a glance.
        var bg = new Grid();
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, frac), GridUnitType.Star) });
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - frac), GridUnitType.Star) });
        var fill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x24, DevToolsTheme.AccentColor.R, DevToolsTheme.AccentColor.G, DevToolsTheme.AccentColor.B)),
            CornerRadius = DevToolsTheme.RadiusSm,
        };
        Grid.SetColumn(fill, 0);
        bg.Children.Add(fill);

        var content = ApiRowGrid();
        content.Children.Add(ApiText(name, false, DevToolsTheme.TextPrimary, 0));
        content.Children.Add(ApiText(calls.ToString(), true, DevToolsTheme.TextSecondary, 1));
        content.Children.Add(ApiText(ms.ToString("F2"), true, msColor, 2));
        content.Children.Add(ApiText(avgUs.ToString("F1"), true, DevToolsTheme.TextMuted, 3));

        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.Children.Add(bg);
        row.Children.Add(content);
        return row;
    }

    private static TextBlock ApiText(string text, bool right, Brush color, int col)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(right ? 0 : 4, 1, right ? 6 : 4, 1),
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static readonly Color PerfColorLayout  = DevToolsTheme.InfoColor;
    private static readonly Color PerfColorRender  = DevToolsTheme.AccentColor;
    private static readonly Color PerfColorPresent = DevToolsTheme.SuccessColor;
    private static readonly Color PerfColorBudget  = Color.FromArgb(0xA0, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B);

    // Reused across refreshes — ContentRevision bookkeeping (WriteableBitmap.cs)
    // triggers a native re-upload whenever we write, so we don't need to throw
    // away and re-allocate a new bitmap + native texture each tick.
    private WriteableBitmap? _perfGraphBitmap;
    private byte[]? _perfGraphPixels;

    private ImageSource? RenderPerfGraph(ReadOnlySpan<FrameHistory.Sample> samples)
    {
        // Size bitmap to the control's physical pixel dimensions — Image.Stretch.Fill
        // then becomes a 1:1 blit instead of a ~3× upscale, which is what was
        // turning our single-pixel-wide bars into a soft orange smear.
        double dipW = _perfGraphImage?.RenderSize.Width ?? 0;
        double dipH = _perfGraphImage?.RenderSize.Height ?? 0;
        // First layout pass may not have placed the image yet — fall back to the
        // host border's size.
        if (dipW <= 1 || dipH <= 1)
        {
            dipW = _perfGraphHost?.RenderSize.Width ?? 0;
            dipH = _perfGraphHost?.RenderSize.Height ?? 0;
        }
        // Scale to physical pixels so high-DPI monitors also render sharply.
        double dpiScale = GetDevicePixelScale();
        int width = Math.Max(160, (int)Math.Round(dipW * dpiScale));
        int height = Math.Max(80, (int)Math.Round(dipH * dpiScale));

        if (_perfGraphBitmap == null || _perfGraphBitmap.PixelWidth != width || _perfGraphBitmap.PixelHeight != height)
        {
            _perfGraphBitmap = new WriteableBitmap(width, height, 96, 96, Jalium.UI.Media.PixelFormats.Pbgra32, null);
            _perfGraphPixels = new byte[width * height * 4];
        }
        var bitmap = _perfGraphBitmap!;
        var pixels = _perfGraphPixels!;

        // Background uses Chrome color for consistency with toolbars/cards.
        byte bgB = DevToolsTheme.ChromeColor.B;
        byte bgG = DevToolsTheme.ChromeColor.G;
        byte bgR = DevToolsTheme.ChromeColor.R;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = bgB;
            pixels[i + 1] = bgG;
            pixels[i + 2] = bgR;
            pixels[i + 3] = 255;
        }

        // Horizontal gridlines every quarter of the max range.
        byte gridLum = DevToolsTheme.BorderSubtleColor.R;
        for (int gy = 1; gy <= 3; gy++)
        {
            int y = height * gy / 4;
            for (int x = 0; x < width; x += 2)
                WritePixel(pixels, width, x, y, Color.FromArgb(0x60, gridLum, gridLum, gridLum));
        }

        if (samples.Length > 0)
        {
            double maxMs = 16.0;
            foreach (var s in samples)
                if (s.TotalMs > maxMs) maxMs = s.TotalMs;
            maxMs = Math.Max(maxMs, 1.0);

            // Map each sample to a contiguous column range so a 300-sample buffer
            // still covers the full physical width instead of leaving most of it
            // as background.
            double colsPerSample = (double)width / Math.Max(samples.Length, 1);
            for (int i = 0; i < samples.Length; i++)
            {
                int xStart = (int)Math.Round(i * colsPerSample);
                int xEnd = (int)Math.Round((i + 1) * colsPerSample);
                if (xEnd <= xStart) xEnd = xStart + 1;
                if (xEnd > width) xEnd = width;

                var s = samples[i];
                int lY = height - 1 - (int)((s.LayoutMs / maxMs) * (height - 2));
                int rY = lY - (int)((s.RenderMs / maxMs) * (height - 2));
                int pY = rY - (int)((s.PresentMs / maxMs) * (height - 2));
                lY = Math.Clamp(lY, 1, height - 1);
                rY = Math.Clamp(rY, 1, height - 1);
                pY = Math.Clamp(pY, 1, height - 1);

                for (int x = xStart; x < xEnd; x++)
                {
                    for (int y = height - 1; y >= lY; y--) WritePixel(pixels, width, x, y, PerfColorLayout);
                    for (int y = lY - 1; y >= rY; y--) WritePixel(pixels, width, x, y, PerfColorRender);
                    for (int y = rY - 1; y >= pY; y--) WritePixel(pixels, width, x, y, PerfColorPresent);
                }
            }

            // 16 ms budget line (dashed) — scale dash gap with pixel width so it
            // remains visually dashed at all zoom levels.
            int budgetY = height - 1 - (int)((16.0 / maxMs) * (height - 2));
            budgetY = Math.Clamp(budgetY, 1, height - 1);
            int dashStride = Math.Max(4, (int)(6 * dpiScale));
            for (int x = 0; x < width; x += dashStride)
                WritePixel(pixels, width, x, budgetY, PerfColorBudget);
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return bitmap;
    }

    /// <summary>
    /// Device-to-DIP scale factor for the DevTools window. Falls back to 1.0
    /// when the window hasn't reported DPI yet.
    /// </summary>
    private double GetDevicePixelScale()
    {
        // DevToolsWindow inherits DPI state from the target window host. Use
        // the dispatcher-visible DpiScale if available, otherwise 1.0.
        try
        {
            double s = DpiScale;
            return s > 0.1 ? s : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static void WritePixel(byte[] pixels, int width, int x, int y, Color c)
    {
        if ((uint)x >= (uint)width) return;
        int idx = (y * width + x) * 4;
        if ((uint)idx >= (uint)pixels.Length) return;
        pixels[idx + 0] = c.B;
        pixels[idx + 1] = c.G;
        pixels[idx + 2] = c.R;
        pixels[idx + 3] = c.A == 0 ? (byte)255 : c.A;
    }

    #endregion

    #region ReplTab

    private Terminal? _replTerminal;
    private Border? _replFocusHost;
    private readonly StringBuilder _replLineBuffer = new();
    private readonly Dictionary<string, object?> _replLocals = new(StringComparer.Ordinal);
    private readonly List<string> _replHistory = new();
    private int _replHistoryCursor = -1;
    private bool _replGreeted;

    private const string ReplPrompt = "devtools> ";

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates user input via reflection-based runtime member resolution.")]
    private UIElement BuildReplTab()
    {
        _replTerminal = new Terminal
        {
            AutoStartShell = false,
            IsReadOnly = true,                   // Terminal never processes typed input directly.
            TerminalColumns = 120,
            TerminalRows = 40,
            Focusable = false,                   // Focus stays on the host Border so we can own input.
            Background = new SolidColorBrush(DevToolsTheme.ChromeColor),
            Foreground = new SolidColorBrush(DevToolsTheme.TextPrimaryColor),
            FontFamily = DevToolsTheme.MonoFont,
        };

        _replFocusHost = new Border
        {
            Background = new SolidColorBrush(DevToolsTheme.ChromeColor),
            BorderBrush = new SolidColorBrush(DevToolsTheme.BorderColor),
            BorderThickness = new Thickness(1),
            Focusable = true,
            Child = _replTerminal,
            ClipToBounds = true,
        };

        // Bubble KeyDown/TextInput fire first on the focused element — that's the host Border,
        // not the Terminal. So we receive input here before the Terminal (which in read-only
        // mode would simply ignore character input anyway).
        _replFocusHost.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnReplKeyDown));
        _replFocusHost.AddHandler(UIElement.TextInputEvent, new TextCompositionEventHandler(OnReplTextInput));
        _replFocusHost.MouseDown += (_, _) => _replFocusHost.Focus();

        return MakeTabShell(_replFocusHost);
    }

    partial void OnReplTabActivated()
    {
        EnsureReplGreeting();
        _replLocals["window"] = _targetWindow;
        try { _replLocals["app"] = Application.Current; } catch { }
        _replLocals["root"] = _targetWindow;
        _replFocusHost?.Focus();
    }

    private void EnsureReplGreeting()
    {
        if (_replGreeted || _replTerminal == null) return;
        _replGreeted = true;
        _replTerminal.Write("\x1b[1;33mJalium.UI DevTools REPL\x1b[0m\r\n");
        _replTerminal.Write("Type expressions. ");
        _replTerminal.Write("\x1b[2m$ = selected visual, window, app, root; let name = expr; name.Prop = val;\x1b[0m\r\n");
        _replTerminal.Write("\x1b[2mEnter to run, Backspace to delete, Up/Down history, Ctrl+L clears, Ctrl+C cancels.\x1b[0m\r\n\r\n");
        WritePrompt();
    }

    private void WritePrompt()
    {
        _replTerminal?.Write($"\x1b[1;32m{ReplPrompt}\x1b[0m");
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates user input via reflection-based runtime member resolution.")]
    private void OnReplKeyDown(object? sender, RoutedEventArgs e)
    {
        if (_replTerminal == null || e is not KeyEventArgs ke) return;

        var mods = ke.KeyboardModifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;

        switch (ke.Key)
        {
            case Key.Enter:
                _replTerminal.Write("\r\n");
                RunReplLine(_replLineBuffer.ToString());
                _replLineBuffer.Clear();
                WritePrompt();
                ke.Handled = true;
                return;

            case Key.Back:
                if (_replLineBuffer.Length > 0)
                {
                    _replLineBuffer.Length--;
                    _replTerminal.Write("\b \b");
                }
                ke.Handled = true;
                return;

            case Key.Escape:
                CancelCurrentLine();
                ke.Handled = true;
                return;

            case Key.L when ctrl:
                _replTerminal.Clear();
                _replTerminal.Write("\x1b[1;33mJalium.UI DevTools REPL\x1b[0m\r\n\r\n");
                WritePrompt();
                // Re-echo any partial line the user had typed before Ctrl+L
                if (_replLineBuffer.Length > 0)
                    _replTerminal.Write(_replLineBuffer.ToString());
                ke.Handled = true;
                return;

            case Key.C when ctrl:
                CancelCurrentLine();
                ke.Handled = true;
                return;

            case Key.Up:
                ShowHistory(-1);
                ke.Handled = true;
                return;

            case Key.Down:
                ShowHistory(+1);
                ke.Handled = true;
                return;
        }
    }

    private void OnReplTextInput(object? sender, RoutedEventArgs e)
    {
        if (_replTerminal == null || e is not TextCompositionEventArgs te) return;
        var text = te.Text;
        if (string.IsNullOrEmpty(text)) return;

        // Skip control characters — Enter/Backspace/Escape are handled in KeyDown.
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch < 0x20 || ch == 0x7f) continue;
            sb.Append(ch);
        }
        if (sb.Length == 0) { te.Handled = true; return; }

        var printable = sb.ToString();
        _replLineBuffer.Append(printable);
        _replTerminal.Write(printable);
        te.Handled = true;
    }

    private void CancelCurrentLine()
    {
        if (_replTerminal == null) return;
        _replLineBuffer.Clear();
        _replTerminal.Write("^C\r\n");
        WritePrompt();
    }

    private void ShowHistory(int direction)
    {
        if (_replHistory.Count == 0 || _replTerminal == null) return;

        int newCursor;
        if (direction < 0)
        {
            if (_replHistoryCursor < 0) newCursor = _replHistory.Count - 1;
            else newCursor = Math.Max(0, _replHistoryCursor - 1);
        }
        else
        {
            if (_replHistoryCursor < 0) return;
            newCursor = _replHistoryCursor + 1;
            if (newCursor >= _replHistory.Count) newCursor = -1;
        }

        // Clear existing input visually
        for (int i = 0; i < _replLineBuffer.Length; i++)
            _replTerminal.Write("\b \b");
        _replLineBuffer.Clear();

        _replHistoryCursor = newCursor;
        if (newCursor >= 0 && newCursor < _replHistory.Count)
        {
            var cmd = _replHistory[newCursor];
            _replLineBuffer.Append(cmd);
            _replTerminal.Write(cmd);
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates expressions and assigns to runtime-resolved members via reflection.")]
    private void RunReplLine(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || _replTerminal == null) return;

        _replHistory.Add(code);
        _replHistoryCursor = -1;
        _replLocals["$"] = _selectedVisual;

        try
        {
            var tokens = ReplTokenizer.Tokenize(code);
            int pos = 0;
            while (pos < tokens.Count)
            {
                var stmtTokens = new List<ReplTokenizer.Token>();
                while (pos < tokens.Count && tokens[pos].Kind != ReplTokenizer.TokenKind.Semicolon)
                {
                    stmtTokens.Add(tokens[pos]);
                    pos++;
                }
                if (pos < tokens.Count) pos++;
                if (stmtTokens.Count == 0) continue;
                try
                {
                    var result = EvaluateStatement(stmtTokens);
                    _replTerminal.Write($"\x1b[1;32m=> \x1b[0m\x1b[96m{FormatReplValue(result)}\x1b[0m\r\n");
                }
                catch (Exception ex)
                {
                    _replTerminal.Write($"\x1b[1;31mE: {ex.Message}\x1b[0m\r\n");
                }
            }
        }
        catch (Exception ex)
        {
            _replTerminal.Write($"\x1b[1;31mparse error: {ex.Message}\x1b[0m\r\n");
        }
    }

    private static string FormatReplValue(object? v)
    {
        if (v == null) return "null";
        if (v is string s) return $"\"{s}\"";
        return v.ToString() ?? "<?>";
    }

    // ── Tiny expression interpreter ──────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates expressions and assigns to runtime-resolved members via reflection.")]
    private object? EvaluateStatement(List<ReplTokenizer.Token> tokens)
    {
        if (tokens.Count >= 4 && tokens[0].Kind == ReplTokenizer.TokenKind.Identifier && tokens[0].Text == "let"
            && tokens[1].Kind == ReplTokenizer.TokenKind.Identifier
            && tokens[2].Kind == ReplTokenizer.TokenKind.Assign)
        {
            var name = tokens[1].Text;
            var value = EvaluateExpression(tokens, 3, tokens.Count);
            _replLocals[name] = value;
            return value;
        }

        int assignIndex = FindTopLevelAssign(tokens);
        if (assignIndex > 0)
        {
            var value = EvaluateExpression(tokens, assignIndex + 1, tokens.Count);
            AssignTo(tokens, assignIndex, value);
            return value;
        }

        return EvaluateExpression(tokens, 0, tokens.Count);
    }

    private static int FindTopLevelAssign(List<ReplTokenizer.Token> tokens)
    {
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case ReplTokenizer.TokenKind.LParen: depth++; break;
                case ReplTokenizer.TokenKind.RParen: depth--; break;
                case ReplTokenizer.TokenKind.Assign:
                    if (depth == 0) return i;
                    break;
            }
        }
        return -1;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL assigns to runtime-resolved properties/fields via reflection.")]
    private void AssignTo(List<ReplTokenizer.Token> tokens, int assignIndex, object? value)
    {
        if (assignIndex < 1) throw new InvalidOperationException("Nothing to assign to");
        if (tokens[assignIndex - 1].Kind != ReplTokenizer.TokenKind.Identifier)
            throw new InvalidOperationException("Last token before '=' must be an identifier");
        string lastName = tokens[assignIndex - 1].Text;

        object? host;
        if (assignIndex == 1)
        {
            if (!_replLocals.ContainsKey(lastName))
                throw new InvalidOperationException($"Unknown variable '{lastName}'");
            _replLocals[lastName] = value;
            return;
        }
        if (tokens[assignIndex - 2].Kind != ReplTokenizer.TokenKind.Dot)
            throw new InvalidOperationException("Expected '.' before final member");

        host = EvaluateExpression(tokens, 0, assignIndex - 2);
        if (host == null) throw new InvalidOperationException("Cannot assign member on null");

        var type = host.GetType();
        var prop = type.GetProperty(lastName, BindingFlags.Instance | BindingFlags.Public);
        if (prop != null)
        {
            prop.SetValue(host, CoerceValue(value, prop.PropertyType));
            return;
        }
        var field = type.GetField(lastName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(host, CoerceValue(value, field.FieldType));
            return;
        }
        throw new InvalidOperationException($"No writable member '{lastName}' on {type.Name}");
    }

    private static object? CoerceValue(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;
        if (targetType == typeof(string)) return value.ToString();
        try { return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture); }
        catch { return value; }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates expressions via reflection-based runtime member resolution.")]
    private object? EvaluateExpression(List<ReplTokenizer.Token> tokens, int start, int end)
    {
        if (start >= end) throw new InvalidOperationException("Empty expression");
        object? value = EvaluatePrimary(tokens, ref start);
        while (start < end)
        {
            if (tokens[start].Kind == ReplTokenizer.TokenKind.Dot)
            {
                start++;
                if (start >= end || tokens[start].Kind != ReplTokenizer.TokenKind.Identifier)
                    throw new InvalidOperationException("Expected identifier after '.'");
                string memberName = tokens[start].Text;
                start++;
                value = ReadOrInvokeMember(value, memberName, tokens, ref start, end);
            }
            else
            {
                break;
            }
        }
        return value;
    }

    private object? EvaluatePrimary(List<ReplTokenizer.Token> tokens, ref int pos)
    {
        var tok = tokens[pos];
        switch (tok.Kind)
        {
            case ReplTokenizer.TokenKind.NumberLiteral:
                pos++;
                if (tok.Text.Contains('.')) return double.Parse(tok.Text, CultureInfo.InvariantCulture);
                if (int.TryParse(tok.Text, out var i)) return i;
                return long.Parse(tok.Text, CultureInfo.InvariantCulture);
            case ReplTokenizer.TokenKind.StringLiteral:
                pos++;
                return tok.Text;
            case ReplTokenizer.TokenKind.Identifier:
                pos++;
                switch (tok.Text)
                {
                    case "true": return true;
                    case "false": return false;
                    case "null": return null;
                }
                if (_replLocals.TryGetValue(tok.Text, out var v)) return v;
                throw new InvalidOperationException($"Unknown identifier '{tok.Text}'");
            case ReplTokenizer.TokenKind.Dollar:
                pos++;
                return _selectedVisual;
            default:
                throw new InvalidOperationException($"Unexpected token '{tok.Text}'");
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL reads/invokes runtime-resolved members via reflection.")]
    private object? ReadOrInvokeMember(object? target, string memberName, List<ReplTokenizer.Token> tokens, ref int pos, int end)
    {
        if (target == null)
            throw new InvalidOperationException($"Cannot access '{memberName}' on null");
        bool isCall = pos < end && tokens[pos].Kind == ReplTokenizer.TokenKind.LParen;
        var type = target.GetType();

        if (isCall)
        {
            pos++;
            var args = new List<object?>();
            while (pos < end && tokens[pos].Kind != ReplTokenizer.TokenKind.RParen)
            {
                int argEnd = FindArgEnd(tokens, pos, end);
                args.Add(EvaluateExpression(tokens, pos, argEnd));
                pos = argEnd;
                if (pos < end && tokens[pos].Kind == ReplTokenizer.TokenKind.Comma) pos++;
            }
            if (pos < end && tokens[pos].Kind == ReplTokenizer.TokenKind.RParen) pos++;
            var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                             .FirstOrDefault(m => m.Name == memberName && m.GetParameters().Length == args.Count);
            if (method == null)
                throw new InvalidOperationException($"No method '{memberName}' on {type.Name} matching {args.Count} args");
            var parms = method.GetParameters();
            for (int k = 0; k < args.Count; k++)
                args[k] = CoerceValue(args[k], parms[k].ParameterType);
            return method.Invoke(target, args.ToArray());
        }
        else
        {
            var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (prop != null) return prop.GetValue(target);
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null) return field.GetValue(target);
            throw new InvalidOperationException($"No member '{memberName}' on {type.Name}");
        }
    }

    private static int FindArgEnd(List<ReplTokenizer.Token> tokens, int start, int end)
    {
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            if (tokens[i].Kind == ReplTokenizer.TokenKind.LParen) depth++;
            else if (tokens[i].Kind == ReplTokenizer.TokenKind.RParen) { if (depth == 0) return i; depth--; }
            else if (tokens[i].Kind == ReplTokenizer.TokenKind.Comma && depth == 0) return i;
        }
        return end;
    }

    #endregion

    #region ResourcesTab

    private StackPanel? _resourcesChainPanel;
    private TextBox? _resourcesSearchBox;
    private DevToolsUi.DevToolsButton? _resourcesWinnersOnlyButton;
    private DispatcherTimer? _resourcesSearchTimer;
    private string _resourcesSearchText = string.Empty;
    private bool _resourcesWinnersOnly;

    private UIElement BuildResourcesTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ──
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(DevToolsUi.Muted("Filter:"));

        _resourcesSearchBox = DevToolsUi.TextInput(220, "key substring");
        _resourcesSearchBox.Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0);
        _resourcesSearchBox.TextChanged += (_, _) => ScheduleResourcesRefresh();
        toolbar.Children.Add(_resourcesSearchBox);

        _resourcesWinnersOnlyButton = DevToolsUi.Toggle("Winners only", () =>
        {
            _resourcesWinnersOnly = !_resourcesWinnersOnly;
            if (_resourcesWinnersOnlyButton != null)
                _resourcesWinnersOnlyButton.IsActive = _resourcesWinnersOnly;
            RefreshResourcesChain();
        }, _resourcesWinnersOnly, icon: "⚑");
        toolbar.Children.Add(_resourcesWinnersOnlyButton);

        toolbar.Children.Add(DevToolsUi.Button("Refresh", () => RefreshResourcesChain(), icon: "↻"));
        toolbar.Children.Add(DevToolsUi.Muted("  · winner = nearest scope where the key resolves first."));

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Chain viewport ──
        // The chain content panel is the tracked, refreshable container. We re-parent
        // it (unchanged instance) into an instrument Panel that supplies the framed
        // "RESOURCE MERGE CHAIN" eyebrow header + hairline divider.
        _resourcesChainPanel = new StackPanel();
        var chainPanel = DevToolsUi.Panel("Resource Merge Chain", _resourcesChainPanel);
        chainPanel.Margin = new Thickness(DevToolsTheme.GutterBase);
        var scroll = new ScrollViewer
        {
            Content = chainPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var card = new Border
        {
            Child = scroll,
            ClipToBounds = true,
        };
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    partial void OnResourcesTabActivated()
    {
        RefreshResourcesChain();
    }

    private void ScheduleResourcesRefresh()
    {
        if (_resourcesSearchTimer == null)
        {
            _resourcesSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _resourcesSearchTimer.Tick += (_, _) =>
            {
                _resourcesSearchTimer!.Stop();
                _resourcesSearchText = _resourcesSearchBox?.Text ?? string.Empty;
                RefreshResourcesChain();
            };
        }
        _resourcesSearchTimer.Stop();
        _resourcesSearchTimer.Start();
    }

    private void RefreshResourcesChain()
    {
        if (_resourcesChainPanel == null) return;
        _resourcesChainPanel.Children.Clear();

        // Section title is now carried by the enclosing instrument Panel header.

        // Collect ordered chain: selected element → ancestors → Application.
        var chain = new List<(string ScopeName, ResourceDictionary? Dict)>();
        if (_selectedVisual is FrameworkElement fe)
        {
            FrameworkElement? cur = fe;
            while (cur != null)
            {
                chain.Add(($"{cur.GetType().Name}" + (string.IsNullOrEmpty(cur.Name) ? "" : $" [#{cur.Name}]"), cur.Resources));
                cur = cur.VisualParent as FrameworkElement;
            }
        }
        else
        {
            chain.Add(("Target window", _targetWindow.Resources));
        }
        try
        {
            var app = Application.Current;
            if (app != null)
                chain.Add(("Application", app.Resources));
        }
        catch { /* Application may not exist in all hosting setups */ }

        if (chain.Count == 0)
        {
            var empty = DevToolsUi.Muted("Select an element to view its resource resolution path.");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase);
            _resourcesChainPanel.Children.Add(empty);
            return;
        }

        // Determine winners — the first scope (nearest the element) where each key
        // appears. Later scopes show the key as shadowed so the user can see which
        // declaration actually wins the lookup.
        var winnerScope = new Dictionary<object, int>();
        for (int i = 0; i < chain.Count; i++)
        {
            var dict = chain[i].Dict;
            if (dict == null) continue;
            foreach (var key in dict.Keys)
            {
                if (key == null) continue;
                if (!winnerScope.ContainsKey(key))
                    winnerScope[key] = i;
            }
        }

        // Summary strip: how many keys are there across the chain after filtering.
        int totalKeys = 0, winnerKeys = 0;
        foreach (var (_, dict) in chain)
        {
            if (dict == null) continue;
            totalKeys += dict.Count;
        }
        winnerKeys = winnerScope.Count;

        _resourcesChainPanel.Children.Add(MakeSummaryRow(chain.Count, totalKeys, winnerKeys));

        var subtitle = DevToolsUi.Muted("From the selected element walking up the tree to the Application.", DevToolsTheme.FontXS);
        subtitle.Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterBase);
        _resourcesChainPanel.Children.Add(subtitle);

        for (int depth = 0; depth < chain.Count; depth++)
        {
            var (scope, dict) = chain[depth];
            _resourcesChainPanel.Children.Add(MakeScopeCard(depth, scope, dict, winnerScope));
        }
    }

    private Border MakeSummaryRow(int scopes, int totalKeys, int winnerKeys)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterBase),
        };
        row.Children.Add(DevToolsUi.Pill($"{scopes} scopes", DevToolsTheme.Info));
        row.Children.Add(DevToolsUi.Pill($"{totalKeys} keys total", DevToolsTheme.TextMuted));
        row.Children.Add(DevToolsUi.Pill($"{winnerKeys} distinct", DevToolsTheme.Success));
        if (!string.IsNullOrEmpty(_resourcesSearchText))
            row.Children.Add(DevToolsUi.Pill($"filter: {_resourcesSearchText}", DevToolsTheme.Warning));
        return new Border
        {
            Child = row,
            Padding = new Thickness(0, 0, 0, DevToolsTheme.GutterXS),
        };
    }

    private Border MakeScopeCard(int depth, string scope, ResourceDictionary? dict, Dictionary<object, int> winnerScope)
    {
        var body = new StackPanel();

        // ── Header row ──
        var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm) };
        scopeRow.Children.Add(new TextBlock
        {
            Text = $"[{depth}]",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        scopeRow.Children.Add(new TextBlock
        {
            Text = scope,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        });
        int keyCount = dict?.Count ?? 0;
        scopeRow.Children.Add(DevToolsUi.Pill($"{keyCount} key{(keyCount == 1 ? "" : "s")}", DevToolsTheme.Info));
        if (dict?.MergedDictionaries is { Count: > 0 } merged)
            scopeRow.Children.Add(DevToolsUi.Pill($"{merged.Count} merged", DevToolsTheme.TextMuted));
        body.Children.Add(scopeRow);

        // ── Direct keys ──
        if (dict == null || dict.Count == 0)
        {
            body.Children.Add(DevToolsUi.Muted("(empty)", DevToolsTheme.FontSm));
        }
        else
        {
            int shown = 0;
            foreach (System.Collections.DictionaryEntry kvp in dict)
            {
                if (!MatchesResourceFilter(kvp.Key)) continue;
                bool isWinner = kvp.Key != null
                    && winnerScope.TryGetValue(kvp.Key, out var winnerDepth)
                    && winnerDepth == depth;
                if (_resourcesWinnersOnly && !isWinner) continue;
                body.Children.Add(MakeResourceRow(kvp.Key, kvp.Value, isWinner));
                shown++;
            }
            if (shown == 0 && (!string.IsNullOrEmpty(_resourcesSearchText) || _resourcesWinnersOnly))
            {
                body.Children.Add(DevToolsUi.Muted("(no keys match the current filter)", DevToolsTheme.FontXS));
            }
        }

        // ── Merged dictionaries ──
        if (dict?.MergedDictionaries is { Count: > 0 } mergedDicts)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"↳ MergedDictionaries ({mergedDicts.Count})",
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.TextMuted,
                Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterXS),
            });
            int mi = 0;
            foreach (var m in mergedDicts)
            {
                var chip = new Border
                {
                    Background = DevToolsTheme.Chrome,
                    BorderBrush = DevToolsTheme.BorderSubtle,
                    BorderThickness = DevToolsTheme.ThicknessHairline,
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
                    Margin = new Thickness(DevToolsTheme.GutterLg, 0, 0, DevToolsTheme.GutterXS),
                };
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock
                {
                    Text = $"[{mi}]",
                    FontSize = DevToolsTheme.FontXS,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
                });
                content.Children.Add(DevToolsUi.Pill($"{m.Count} entries", DevToolsTheme.TextMuted));
                string? src = m.Source?.ToString();
                content.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(src) ? "(inline)" : src,
                    FontSize = DevToolsTheme.FontXS,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(DevToolsTheme.GutterSm, 0, 0, 0),
                });
                chip.Child = content;
                body.Children.Add(chip);
                mi++;
            }
        }

        return new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase),
            Child = body,
        };
    }

    private Border MakeResourceRow(object? key, object? value, bool isWinner)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });                        // type chip
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });    // key
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });                        // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });      // value preview

        var chip = MakeValueTypeChip(value);
        Grid.SetColumn(chip, 0);
        grid.Children.Add(chip);

        var keyText = new TextBlock
        {
            Text = key?.ToString() ?? "<null>",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = isWinner ? DevToolsTheme.TokenProperty : DevToolsTheme.TextMuted,
            FontWeight = isWinner ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0),
        };
        Grid.SetColumn(keyText, 1);
        grid.Children.Add(keyText);

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        var valueView = MakeValuePreview(value);
        Grid.SetColumn(valueView, 3);
        grid.Children.Add(valueView);

        var accentBar = isWinner
            ? new SolidColorBrush(Color.FromArgb(0x80, DevToolsTheme.SuccessColor.R, DevToolsTheme.SuccessColor.G, DevToolsTheme.SuccessColor.B))
            : new SolidColorBrush(Color.FromArgb(0x30, DevToolsTheme.TextMutedColor.R, DevToolsTheme.TextMutedColor.G, DevToolsTheme.TextMutedColor.B));

        return new Border
        {
            Background = isWinner ? DevToolsTheme.RowAlt : null,
            BorderBrush = accentBar,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
            Margin = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    private static UIElement MakeValueTypeChip(object? value) => value switch
    {
        null                    => SimpleChip("∅", DevToolsTheme.TextMuted),
        SolidColorBrush scb     => ColorChip(scb.Color),
        LinearGradientBrush     => SimpleChip("↘", DevToolsTheme.Accent),
        RadialGradientBrush     => SimpleChip("◎", DevToolsTheme.Accent),
        Brush                   => SimpleChip("▦", DevToolsTheme.Accent),
        Jalium.UI.Style         => SimpleChip("S", DevToolsTheme.Warning),
        ControlTemplate         => SimpleChip("T", DevToolsTheme.Warning),
        DataTemplate            => SimpleChip("D", DevToolsTheme.Warning),
        Thickness               => SimpleChip("⊞", DevToolsTheme.Info),
        double                  => SimpleChip("#", DevToolsTheme.TokenNumber),
        int                     => SimpleChip("#", DevToolsTheme.TokenNumber),
        float                   => SimpleChip("#", DevToolsTheme.TokenNumber),
        long                    => SimpleChip("#", DevToolsTheme.TokenNumber),
        bool                    => SimpleChip("✓", DevToolsTheme.TokenBool),
        string                  => SimpleChip("\u201C", DevToolsTheme.TokenString),
        Enum                    => SimpleChip("E", DevToolsTheme.Accent),
        _                       => SimpleChip("?", DevToolsTheme.TextMuted),
    };

    private static Border SimpleChip(string glyph, Brush fg)
    {
        return new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = DevToolsTheme.Control,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
    }

    private static Border ColorChip(Color color)
    {
        return new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color),
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    private static UIElement MakeValuePreview(object? value)
    {
        switch (value)
        {
            case null:
                return new TextBlock
                {
                    Text = "<null>",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                };

            case SolidColorBrush scb:
                return ColorValue(scb.Color);

            case Style style:
                return StyleValue(style);

            case ControlTemplate ct:
                return new TextBlock
                {
                    Text = $"ControlTemplate · TargetType={ct.TargetType?.Name ?? "?"}",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TokenType,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

            case DataTemplate dt:
                return new TextBlock
                {
                    Text = $"DataTemplate · DataType={dt.DataType?.ToString() ?? "?"}",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TokenType,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

            default:
                Brush fg = value switch
                {
                    string => DevToolsTheme.TokenString,
                    Enum   => DevToolsTheme.TokenEnum,
                    bool   => DevToolsTheme.TokenBool,
                    double or int or float or long => DevToolsTheme.TokenNumber,
                    _ => DevToolsTheme.TextPrimary,
                };
                return new TextBlock
                {
                    Text = value is string s ? $"\"{s}\"" : value.ToString() ?? "",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
        }
    }

    private static StackPanel ColorValue(Color color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(color),
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenString,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static StackPanel StyleValue(Style style)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = "Style",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenType,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (style.TargetType != null)
        {
            row.Children.Add(new TextBlock
            {
                Text = $"  TargetType={style.TargetType.Name}",
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.Accent,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        int setterCount = style.Setters?.Count ?? 0;
        row.Children.Add(DevToolsUi.Pill($"{setterCount} setter{(setterCount == 1 ? "" : "s")}", DevToolsTheme.TextMuted));
        int triggerCount = style.Triggers?.Count ?? 0;
        if (triggerCount > 0)
            row.Children.Add(DevToolsUi.Pill($"{triggerCount} trigger{(triggerCount == 1 ? "" : "s")}", DevToolsTheme.Warning));
        if (style.BasedOn != null)
            row.Children.Add(DevToolsUi.Pill($"BasedOn={style.BasedOn.TargetType?.Name ?? "?"}", DevToolsTheme.Info));
        return row;
    }

    private bool MatchesResourceFilter(object? key)
    {
        if (string.IsNullOrEmpty(_resourcesSearchText)) return true;
        var s = key?.ToString();
        if (s == null) return false;
        return s.IndexOf(_resourcesSearchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion

    #region StyleTemplateXaml

    private void AppendStyleXamlViewer(Style style)
    {
        _propertiesPanel.Children.Add(MakeRevealXamlButton("View style XAML", () => BuildStyleXaml(style)));
    }

    private void AppendTemplateXamlViewer(FrameworkElement fe)
    {
        if (fe is Control ctrl && ctrl.Template != null)
            _propertiesPanel.Children.Add(MakeRevealXamlButton("View ControlTemplate XAML", () => BuildTemplateXaml(ctrl)));
    }

    private Border MakeRevealXamlButton(string label, Func<string> xamlProvider)
    {
        var btn = new Border
        {
            Background = DevToolsTheme.AccentSoft,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xB4, DevToolsTheme.AccentColor.R, DevToolsTheme.AccentColor.G, DevToolsTheme.AccentColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 11,
                FontFamily = DevToolsTheme.DisplayFont,
                Foreground = DevToolsTheme.Accent,
            },
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        btn.MouseDown += (_, _) =>
        {
            try
            {
                var xaml = xamlProvider();
                ShowXamlPopup(label, xaml);
            }
            catch (Exception ex)
            {
                ShowXamlPopup(label, $"(failed to render XAML: {ex.Message})");
            }
        };
        return btn;
    }

    private void ShowXamlPopup(string title, string xaml)
    {
        var popup = new Window
        {
            Title = "DevTools — " + title,
            Width = 640,
            Height = 520,
            SystemBackdrop = WindowBackdropType.Mica,
            Background = DevToolsTheme.Chrome,
        };

        var tb = new TextBox
        {
            Text = xaml,
            AcceptsReturn = true,
            FontSize = 12,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextPrimary,
            Background = DevToolsTheme.Surface,
            BorderBrush = DevToolsTheme.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(6),
            IsReadOnly = true,
        };
        var scroll = new ScrollViewer
        {
            Content = tb,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        popup.Content = scroll;
        popup.Show();
    }

    private static string BuildStyleXaml(Style style)
    {
        var sb = new StringBuilder();
        sb.Append("<Style");
        if (style.TargetType != null)
            sb.Append(" TargetType=\"").Append(style.TargetType.Name).Append('"');
        if (style.BasedOn != null && style.BasedOn.TargetType != null)
            sb.Append(" BasedOn=\"{StaticResource ").Append(style.BasedOn.TargetType.Name).Append("Style}\"");
        sb.AppendLine(">");

        foreach (var setter in style.Setters.OfType<Setter>())
        {
            var name = setter.Property?.Name ?? "?";
            var value = setter.Value switch
            {
                null => "{x:Null}",
                string s => s,
                SolidColorBrush scb => $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}",
                _ => setter.Value.ToString() ?? "",
            };
            sb.Append("  <Setter Property=\"").Append(name).Append("\" Value=\"").Append(EscapeXml(value)).AppendLine("\" />");
        }

        if (style.Triggers.Count > 0)
        {
            sb.AppendLine("  <Style.Triggers>");
            foreach (var trig in style.Triggers)
            {
                sb.Append("    <!-- Trigger: ").Append(trig.GetType().Name).AppendLine(" -->");
            }
            sb.AppendLine("  </Style.Triggers>");
        }

        sb.AppendLine("</Style>");
        return sb.ToString();
    }

    private static string BuildTemplateXaml(Control ctrl)
    {
        var template = ctrl.Template;
        if (template == null) return "(no template)";
        var sb = new StringBuilder();
        sb.Append("<ControlTemplate");
        if (template.TargetType != null)
            sb.Append(" TargetType=\"").Append(template.TargetType.Name).Append('"');
        sb.AppendLine(">");

        // Walk the live visual tree of the control — the template has been instantiated,
        // so the first templated child is a reasonable approximation of the template body.
        if (ctrl.VisualChildrenCount > 0 && ctrl.GetVisualChild(0) is Visual root)
        {
            WriteTemplateNode(root, 1, sb);
        }
        else
        {
            sb.AppendLine("  <!-- template not yet applied -->");
        }

        sb.AppendLine("</ControlTemplate>");
        return sb.ToString();
    }

    private static void WriteTemplateNode(Visual visual, int depth, StringBuilder sb)
    {
        var indent = new string(' ', depth * 2);
        string typeName = visual.GetType().Name;
        sb.Append(indent).Append('<').Append(typeName);

        if (visual is FrameworkElement fe)
        {
            if (!string.IsNullOrEmpty(fe.Name))
                sb.Append(" Name=\"").Append(EscapeXml(fe.Name)).Append('"');
        }

        int childCount = visual.VisualChildrenCount;
        if (childCount == 0)
        {
            sb.AppendLine(" />");
            return;
        }
        sb.AppendLine(">");
        for (int i = 0; i < childCount; i++)
        {
            if (visual.GetVisualChild(i) is Visual c)
                WriteTemplateNode(c, depth + 1, sb);
        }
        sb.Append(indent).Append("</").Append(typeName).AppendLine(">");
    }

    #endregion

    #region Tabs

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

    #endregion

    #region ToolsTab

    private TextBlock? _rulerStatusText;
    private TextBlock? _pickerStatusText;
    private TextBlock? _exportStatusText;
    private TextBlock? _screenshotStatusText;

    // Result panes — hidden while empty, populated when the user completes an
    // action so they can review the measurement / sample at leisure.
    private Border? _rulerResultHost;
    private Border? _pickerResultHost;

    private bool _rulerActive;
    private Point? _rulerStart;
    private bool _colorPickerActive;

    private DevToolsUi.DevToolsButton? _overdrawButton;
    private DevToolsUi.DevToolsButton? _dirtyRegionsButton;
    private DevToolsUi.DevToolsButton? _focusToggleButton;

    // Single toggle buttons — style flips between Primary (idle) and Danger (active).
    private DevToolsUi.DevToolsButton? _rulerToggleButton;
    private DevToolsUi.DevToolsButton? _pickerToggleButton;

    private UIElement BuildToolsTab()
    {
        var root = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase) };

        // Tab intro header
        root.Children.Add(new TextBlock
        {
            Text = DevToolsUi.Tracked("TOOLS"),
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterXS),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Interactive helpers for poking at the target window.",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterLg),
        });

        // ── Ruler / measure ──
        _rulerStatusText = DevToolsUi.Muted("Click two points inside the target window to measure distance.");
        _rulerResultHost = new Border { Visibility = Visibility.Collapsed };
        _rulerToggleButton = DevToolsUi.Button("Start ruler", ToggleRuler, DevToolsUi.ButtonStyle.Primary, icon: "▶");
        root.Children.Add(MakeToolCard(
            icon: "📏",
            title: DevToolsUi.Tracked("RULER · MEASURE"),
            description: "Measure pixel distance between two points on the target window. Hold Shift while picking the second point to snap to 0° / 45° / 90°.",
            accent: DevToolsTheme.Info,
            actions: new[] { _rulerToggleButton },
            resultHost: _rulerResultHost,
            status: _rulerStatusText));

        // ── Color picker ──
        _pickerStatusText = DevToolsUi.Muted("Click on the target window to sample the screen pixel at that position.");
        _pickerResultHost = new Border { Visibility = Visibility.Collapsed };
        _pickerToggleButton = DevToolsUi.Button("Pick color", ToggleColorPicker, DevToolsUi.ButtonStyle.Primary, icon: "▶");
        root.Children.Add(MakeToolCard(
            icon: "◉",
            title: DevToolsUi.Tracked("COLOR PICKER"),
            description: "Sample any on-screen pixel inside the target window; result is shown in status with the hex value.",
            accent: DevToolsTheme.TokenKeyword,
            actions: new[] { _pickerToggleButton },
            resultHost: _pickerResultHost,
            status: _pickerStatusText));

        // XAML export + Screenshot both live on the right-click context menu
        // inside the Inspector tree — no card in the Tools tab.
        _exportStatusText = null;
        _screenshotStatusText = null;

        // ── Render overlays ──
        _overdrawButton     = DevToolsUi.Toggle("Overdraw",      () => ToggleOverlayMode(RenderDiagnostics.OverlayMode.Overdraw), false, icon: "⚙");
        _dirtyRegionsButton = DevToolsUi.Toggle("Dirty regions", () => ToggleOverlayMode(RenderDiagnostics.OverlayMode.DirtyRegions), false, icon: "▤");
        var overlayHint = DevToolsUi.Muted("Requires native backends to call RenderDiagnostics.RecordDraw / RecordDirtyRegion.", DevToolsTheme.FontXS);
        root.Children.Add(MakeToolCard(
            icon: "▦",
            title: DevToolsUi.Tracked("RENDER OVERLAYS"),
            description: "Paint the target window with diagnostics — overdraw heatmap or dirty-region outlines.",
            accent: DevToolsTheme.Warning,
            actions: new UIElement[] { _overdrawButton, _dirtyRegionsButton },
            resultHost: null,
            status: overlayHint));

        // ── Focus visualization ──
        _focusToggleButton = DevToolsUi.Toggle("Focus overlay", ToggleFocusOverlay, false, icon: "◎");
        _focusStatusText = DevToolsUi.Muted("Highlights the currently focused element.");
        root.Children.Add(MakeToolCard(
            icon: "◎",
            title: DevToolsUi.Tracked("FOCUS VISUALIZATION"),
            description: "Draw a ring around whichever element currently owns keyboard focus.",
            accent: DevToolsTheme.Success,
            actions: new UIElement[] { _focusToggleButton },
            resultHost: null,
            status: _focusStatusText));

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            ClipToBounds = true,
        };
    }

    /// <summary>
    /// A polished tool card: left accent bar, circular icon chip, title + muted
    /// description, button row, an optional result-visualisation host (shown
    /// after the user acts), and a bottom status hint with an info glyph.
    /// </summary>
    private static Border MakeToolCard(string icon, string title, string description,
        SolidColorBrush accent, UIElement[] actions, Border? resultHost, TextBlock status)
    {
        // ── Icon chip ──
        var iconChip = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0x28, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = icon,
                FontSize = 18,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };

        // ── Title ──
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = DevToolsTheme.FontLg,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
        };

        // ── Description ──
        var descText = new TextBlock
        {
            Text = description,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            Margin = new Thickness(0, 2, 0, DevToolsTheme.GutterBase),
        };

        // ── Buttons ──
        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in actions) buttonsRow.Children.Add(b);

        // ── Status hint with info glyph ──
        status.Margin = new Thickness(0, 0, 0, 0);
        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, 0),
        };
        statusRow.Children.Add(new TextBlock
        {
            Text = "ⓘ",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        statusRow.Children.Add(status);

        // ── Body (title + description + buttons + result + status) ──
        var body = new StackPanel();
        body.Children.Add(titleText);
        body.Children.Add(descText);
        body.Children.Add(buttonsRow);
        if (resultHost != null)
        {
            resultHost.Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, 0);
            body.Children.Add(resultHost);
        }
        body.Children.Add(statusRow);

        // ── Two-column layout: icon chip | body ──
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(iconChip, 0);
        Grid.SetColumn(body, 1);
        grid.Children.Add(iconChip);
        grid.Children.Add(body);

        // Outer card with an accent bar on the left.
        return new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, DevToolsTheme.RadiusBase.TopRight, DevToolsTheme.RadiusBase.BottomRight, 0),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg),
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterBase),
            Child = grid,
        };
    }

    partial void OnToolsTabActivated()
    {
        // nothing persistent to refresh
    }

    private TextBlock _focusStatusText = new();

    // ── Ruler ────────────────────────────────────────────────────────────

    private void ToggleRuler()
    {
        if (_rulerActive) DeactivateRuler();
        else ActivateRuler();
    }

    private void UpdateRulerButton()
    {
        if (_rulerToggleButton == null) return;
        if (_rulerActive)
        {
            _rulerToggleButton.Label = "Stop ruler";
            _rulerToggleButton.SetIcon("■");
            _rulerToggleButton.Style = DevToolsUi.ButtonStyle.Danger;
        }
        else
        {
            _rulerToggleButton.Label = "Start ruler";
            _rulerToggleButton.SetIcon("▶");
            _rulerToggleButton.Style = DevToolsUi.ButtonStyle.Primary;
        }
    }

    private void ActivateRuler()
    {
        if (_rulerActive) return;
        _rulerActive = true;
        _rulerStart = null;
        _targetWindow.PreviewMouseDown += OnRulerTargetMouseDown;
        _targetWindow.PreviewMouseMove += OnRulerTargetMouseMove;
        _overlay?.BeginRuler();
        if (_rulerResultHost != null) _rulerResultHost.Visibility = Visibility.Collapsed;
        if (_rulerStatusText != null)
            _rulerStatusText.Text = "Ruler: click first point, move to preview, click again to commit.";
        UpdateRulerButton();
    }

    private void DeactivateRuler()
    {
        if (!_rulerActive) return;
        _rulerActive = false;
        _targetWindow.PreviewMouseDown -= OnRulerTargetMouseDown;
        _targetWindow.PreviewMouseMove -= OnRulerTargetMouseMove;
        _overlay?.EndRuler();
        if (_rulerStatusText != null)
            _rulerStatusText.Text = "Ruler stopped.";
        UpdateRulerButton();
    }

    private void OnRulerTargetMouseMove(object? sender, RoutedEventArgs e)
    {
        if (e is not Input.MouseEventArgs me) return;
        if (_overlay == null || _rulerStart == null) return;
        // Live preview while the user hunts for the second point.
        bool shift = (me.KeyboardModifiers & Input.ModifierKeys.Shift) != 0;
        var pt = shift ? SnapRulerToAxis(_rulerStart.Value, me.Position) : me.Position;
        _overlay.SetRulerPreviewEnd(pt);
    }

    private void OnRulerTargetMouseDown(object? sender, RoutedEventArgs e)
    {
        if (e is not Input.MouseButtonEventArgs me) return;
        if (me.ChangedButton != Input.MouseButton.Left) return;
        var rawPt = me.Position;
        bool shift = (me.KeyboardModifiers & Input.ModifierKeys.Shift) != 0;

        if (_rulerStart == null || _overlay?.RulerEndCommitted == true)
        {
            // First click of a new measurement — start fresh.
            _rulerStart = rawPt;
            _overlay?.SetRulerStart(rawPt);
            if (_rulerResultHost != null) _rulerResultHost.Visibility = Visibility.Collapsed;
            if (_rulerStatusText != null)
                _rulerStatusText.Text = $"First point ({rawPt.X:F0}, {rawPt.Y:F0}). Hold Shift to constrain to 0°/45°/90°. Click again to commit.";
        }
        else
        {
            // Second click — commit the measurement (axis-snapped if Shift is held)
            // and keep it visible until the user clicks again to start a new one.
            var p0 = _rulerStart.Value;
            var pt = shift ? SnapRulerToAxis(p0, rawPt) : rawPt;
            double dx = pt.X - p0.X;
            double dy = pt.Y - p0.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            _overlay?.CommitRulerEnd(pt);
            if (_rulerStatusText != null)
                _rulerStatusText.Text = shift ? "Measurement committed · ⇧ snapped to axis." : "Measurement committed. Click again for a new one.";
            UpdateRulerResult(p0, pt, dx, dy, dist, angle);
        }
        me.Handled = true;
    }

    private void UpdateRulerResult(Point p0, Point p1, double dx, double dy, double dist, double angleDeg)
    {
        if (_rulerResultHost == null) return;

        // ── Big distance number ──
        var distRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        distRow.Children.Add(new TextBlock
        {
            Text = dist.ToString("F1"),
            FontSize = 28,
            FontFamily = DevToolsTheme.MonoFont,
            FontWeight = FontWeights.Bold,
            Foreground = DevToolsTheme.Info,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        distRow.Children.Add(new TextBlock
        {
            Text = "px",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(4, 0, 0, 6),
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        // ── Metric chips: Δx · Δy · angle ──
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        chips.Children.Add(MakeMetricChip("Δx", $"{dx:F0}", DevToolsTheme.Info));
        chips.Children.Add(MakeMetricChip("Δy", $"{dy:F0}", DevToolsTheme.Info));
        chips.Children.Add(MakeMetricChip("∠", $"{angleDeg:F1}°", DevToolsTheme.Accent));

        // ── Coordinate line ──
        var coordText = new TextBlock
        {
            Text = $"({p0.X:F0}, {p0.Y:F0})  →  ({p1.X:F0}, {p1.Y:F0})",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0),
        };

        var body = new StackPanel();
        body.Children.Add(distRow);
        body.Children.Add(chips);
        body.Children.Add(coordText);

        _rulerResultHost.Background = new SolidColorBrush(Color.FromArgb(
            0x18, DevToolsTheme.InfoColor.R, DevToolsTheme.InfoColor.G, DevToolsTheme.InfoColor.B));
        _rulerResultHost.BorderBrush = new SolidColorBrush(Color.FromArgb(
            0x70, DevToolsTheme.InfoColor.R, DevToolsTheme.InfoColor.G, DevToolsTheme.InfoColor.B));
        _rulerResultHost.BorderThickness = new Thickness(1);
        _rulerResultHost.CornerRadius = DevToolsTheme.RadiusBase;
        _rulerResultHost.Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
        _rulerResultHost.Child = body;
        _rulerResultHost.Visibility = Visibility.Visible;
    }

    private static Border MakeMetricChip(string label, string value, SolidColorBrush accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.MonoFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(
                0x24, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x60, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(DevToolsTheme.GutterBase, 2, DevToolsTheme.GutterBase, 2),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            Child = row,
        };
    }

    /// <summary>
    /// Snap a raw cursor position to the nearest 0° / 45° / 90° / 135° axis
    /// anchored at <paramref name="start"/>. The length of the vector is kept so
    /// that shift-drag produces a straight line in the expected direction.
    /// </summary>
    private static Point SnapRulerToAxis(Point start, Point raw)
    {
        double dx = raw.X - start.X;
        double dy = raw.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5) return start;

        // Bucket the angle into 45° steps.
        const double step = Math.PI / 4;
        double angle = Math.Atan2(dy, dx);
        double snapped = Math.Round(angle / step) * step;
        return new Point(
            start.X + len * Math.Cos(snapped),
            start.Y + len * Math.Sin(snapped));
    }

    // ── Color picker ─────────────────────────────────────────────────────

    private void ToggleColorPicker()
    {
        if (_colorPickerActive) DeactivateColorPicker();
        else ActivateColorPicker();
    }

    private void UpdatePickerButton()
    {
        if (_pickerToggleButton == null) return;
        if (_colorPickerActive)
        {
            _pickerToggleButton.Label = "Stop picker";
            _pickerToggleButton.SetIcon("■");
            _pickerToggleButton.Style = DevToolsUi.ButtonStyle.Danger;
        }
        else
        {
            _pickerToggleButton.Label = "Pick color";
            _pickerToggleButton.SetIcon("▶");
            _pickerToggleButton.Style = DevToolsUi.ButtonStyle.Primary;
        }
    }

    private void ActivateColorPicker()
    {
        if (_colorPickerActive) return;
        _colorPickerActive = true;
        _targetWindow.PreviewMouseDown += OnColorPickerTargetMouseDown;
        if (_pickerResultHost != null) _pickerResultHost.Visibility = Visibility.Collapsed;
        if (_pickerStatusText != null)
            _pickerStatusText.Text = "Color picker: click anywhere on the target window…";
        UpdatePickerButton();
    }

    private void DeactivateColorPicker()
    {
        if (!_colorPickerActive) return;
        _colorPickerActive = false;
        _targetWindow.PreviewMouseDown -= OnColorPickerTargetMouseDown;
        if (_pickerStatusText != null)
            _pickerStatusText.Text = "Color picker stopped.";
        UpdatePickerButton();
    }

    private void OnColorPickerTargetMouseDown(object? sender, RoutedEventArgs e)
    {
        if (e is not Input.MouseButtonEventArgs me) return;
        if (me.ChangedButton != Input.MouseButton.Left) return;
        try
        {
            var color = PickScreenPixel(me.Position);
            if (color.HasValue)
            {
                if (_pickerStatusText != null)
                    _pickerStatusText.Text = $"Sampled pixel at ({me.Position.X:F0}, {me.Position.Y:F0}). Click again to sample a different pixel.";
                UpdatePickerResult(color.Value, me.Position);
            }
            else
            {
                if (_pickerStatusText != null)
                    _pickerStatusText.Text = "Color picker: sampling failed (platform not supported).";
            }
        }
        catch (Exception ex)
        {
            if (_pickerStatusText != null)
                _pickerStatusText.Text = $"Color picker error: {ex.Message}";
        }
        finally
        {
            me.Handled = true;
        }
    }

    private void UpdatePickerResult(Color color, Point samplePoint)
    {
        if (_pickerResultHost == null) return;

        string hex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        var (h, s, l) = ColorToHsl(color);

        // ── Big swatch ──
        var swatch = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = DevToolsTheme.RadiusBase,
            Background = new SolidColorBrush(color),
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
        };

        // ── Right-side details ──
        var details = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterLg, 0, 0, 0) };

        // HEX (big)
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        hexRow.Children.Add(new TextBlock
        {
            Text = hex,
            FontSize = 20,
            FontFamily = DevToolsTheme.MonoFont,
            FontWeight = FontWeights.Bold,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        hexRow.Children.Add(new TextBlock
        {
            Text = "  HEX",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(DevToolsTheme.GutterSm, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        details.Children.Add(hexRow);

        // Channel chips: R G B A
        var channels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0) };
        channels.Children.Add(MakeChannelChip("R", color.R, DevToolsTheme.Error));
        channels.Children.Add(MakeChannelChip("G", color.G, DevToolsTheme.Success));
        channels.Children.Add(MakeChannelChip("B", color.B, DevToolsTheme.Info));
        channels.Children.Add(MakeChannelChip("A", color.A, DevToolsTheme.TextMuted));
        details.Children.Add(channels);

        // HSL line
        details.Children.Add(new TextBlock
        {
            Text = $"HSL  {h:F0}° · {s * 100:F0}% · {l * 100:F0}%",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextSecondary,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0),
        });

        // Source pixel coordinate (small)
        details.Children.Add(new TextBlock
        {
            Text = $"at ({samplePoint.X:F0}, {samplePoint.Y:F0})",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 2, 0, 0),
        });

        // Copy HEX button
        var copyBtn = DevToolsUi.Button("Copy HEX", () =>
        {
            try { WpfClipboard.SetText(hex); } catch { /* clipboard may not be available */ }
        }, icon: "⧉");
        copyBtn.Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0);
        details.Children.Add(copyBtn);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(swatch, 0);
        Grid.SetColumn(details, 1);
        grid.Children.Add(swatch);
        grid.Children.Add(details);

        var accent = DevToolsTheme.TokenKeyword;
        _pickerResultHost.Background = new SolidColorBrush(Color.FromArgb(
            0x18, accent.Color.R, accent.Color.G, accent.Color.B));
        _pickerResultHost.BorderBrush = new SolidColorBrush(Color.FromArgb(
            0x70, accent.Color.R, accent.Color.G, accent.Color.B));
        _pickerResultHost.BorderThickness = new Thickness(1);
        _pickerResultHost.CornerRadius = DevToolsTheme.RadiusBase;
        _pickerResultHost.Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
        _pickerResultHost.Child = grid;
        _pickerResultHost.Visibility = Visibility.Visible;
    }

    private static Border MakeChannelChip(string label, byte value, SolidColorBrush accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = value.ToString(),
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(
                0x22, accent.Color.R, accent.Color.G, accent.Color.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(DevToolsTheme.GutterBase, 2, DevToolsTheme.GutterBase, 2),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            Child = row,
        };
    }

    private static (double H, double S, double L) ColorToHsl(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        double h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    private Color? PickScreenPixel(Point windowPoint)
    {
        if (!OperatingSystem.IsWindows())
        {
            byte[] pixels = CaptureTargetWindowPixels(out int width, out int height);
            return TrySampleBgraPixel(
                pixels, width, height, _targetWindow.DpiScale, windowPoint,
                out Color color)
                ? color
                : null;
        }
        var hwnd = _targetWindow.Handle;
        if (hwnd == nint.Zero) return null;
        var pt = new POINT { X = (int)windowPoint.X, Y = (int)windowPoint.Y };
        ClientToScreen(hwnd, ref pt);
        var dc = GetDC(nint.Zero);
        if (dc == nint.Zero) return null;
        try
        {
            uint rgb = GetPixel(dc, pt.X, pt.Y);
            byte r = (byte)(rgb & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)((rgb >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }
        finally
        {
            ReleaseDC(nint.Zero, dc);
        }
    }

    internal static bool TrySampleBgraPixel(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        double dpiScale,
        Point positionDip,
        out Color color)
    {
        color = default;
        if (width <= 0 || height <= 0 ||
            !double.IsFinite(dpiScale) || dpiScale <= 0 ||
            !double.IsFinite(positionDip.X) || !double.IsFinite(positionDip.Y))
        {
            return false;
        }

        long requiredBytes = (long)width * height * 4;
        if (requiredBytes > pixels.Length)
            return false;

        int x = (int)Math.Floor(positionDip.X * dpiScale);
        int y = (int)Math.Floor(positionDip.Y * dpiScale);
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return false;

        int offset = checked((y * width + x) * 4);
        color = Color.FromArgb(
            pixels[offset + 3],
            pixels[offset + 2],
            pixels[offset + 1],
            pixels[offset]);
        return true;
    }

    // ── XAML export ──────────────────────────────────────────────────────

    // Export / screenshot handlers live in DevToolsWindow.ElementContextMenu.cs —
    // they are invoked from the right-click menu on Inspector / Logical nodes.

    private static string BuildXamlFromVisual(Visual visual, bool recurse, int depth)
    {
        var sb = new StringBuilder();
        WriteXaml(visual, recurse, depth, sb);
        return sb.ToString();
    }

    private static void WriteXaml(Visual visual, bool recurse, int depth, StringBuilder sb)
    {
        string indent = new string(' ', depth * 2);
        string typeName = visual.GetType().Name;
        sb.Append(indent).Append('<').Append(typeName);

        if (visual is FrameworkElement fe)
        {
            if (!string.IsNullOrEmpty(fe.Name))
                sb.Append(" Name=\"").Append(EscapeXml(fe.Name)).Append("\"");
            AppendAttrIfSet(sb, fe, FrameworkElement.WidthProperty, "Width", v => v is double d && !double.IsNaN(d));
            AppendAttrIfSet(sb, fe, FrameworkElement.HeightProperty, "Height", v => v is double d && !double.IsNaN(d));
            AppendAttrIfSet(sb, fe, FrameworkElement.MarginProperty, "Margin", v => v is Thickness t && (t.Left != 0 || t.Top != 0 || t.Right != 0 || t.Bottom != 0));
            AppendAttrIfSet(sb, fe, FrameworkElement.HorizontalAlignmentProperty, "HorizontalAlignment", v => v is HorizontalAlignment ha && ha != HorizontalAlignment.Stretch);
            AppendAttrIfSet(sb, fe, FrameworkElement.VerticalAlignmentProperty, "VerticalAlignment", v => v is VerticalAlignment va && va != VerticalAlignment.Stretch);
        }

        var children = GetRenderableChildren(visual);
        bool hasChildren = recurse && children.Count > 0;
        if (!hasChildren)
        {
            sb.AppendLine(" />");
            return;
        }
        sb.AppendLine(">");
        foreach (var child in children)
            WriteXaml(child, recurse, depth + 1, sb);
        sb.Append(indent).Append("</").Append(typeName).AppendLine(">");
    }

    private static List<Visual> GetRenderableChildren(Visual visual)
    {
        var list = new List<Visual>();
        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            if (visual.GetVisualChild(i) is Visual c)
                list.Add(c);
        }
        return list;
    }

    private static void AppendAttrIfSet(StringBuilder sb, DependencyObject dObj, DependencyProperty dp, string attrName, Func<object?, bool> isInteresting)
    {
        try
        {
            var source = DependencyPropertyHelper.GetValueSource(dObj, dp);
            if (source.BaseValueSource != BaseValueSource.Local) return;
            var val = dObj.GetValue(dp);
            if (val == null || !isInteresting(val)) return;
            sb.Append(' ').Append(attrName).Append("=\"").Append(EscapeXml(val.ToString() ?? "")).Append("\"");
        }
        catch
        {
            // Skip attributes whose getter throws.
        }
    }

    private static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    // ── Render overlay toggles ───────────────────────────────────────────

    private void ToggleOverlayMode(RenderDiagnostics.OverlayMode mode)
    {
        RenderDiagnostics.Mode = RenderDiagnostics.Mode == mode
            ? RenderDiagnostics.OverlayMode.None
            : mode;
        if (_overdrawButton != null)
            _overdrawButton.IsActive = RenderDiagnostics.Mode == RenderDiagnostics.OverlayMode.Overdraw;
        if (_dirtyRegionsButton != null)
            _dirtyRegionsButton.IsActive = RenderDiagnostics.Mode == RenderDiagnostics.OverlayMode.DirtyRegions;
        _targetWindow.RequestFullInvalidation();
        _targetWindow.InvalidateWindow();
    }

    // ── Focus overlay ────────────────────────────────────────────────────

    private bool _focusOverlayEnabled;
    private Threading.DispatcherTimer? _focusOverlayTimer;

    private void ToggleFocusOverlay()
    {
        _focusOverlayEnabled = !_focusOverlayEnabled;
        if (_focusToggleButton != null)
            _focusToggleButton.IsActive = _focusOverlayEnabled;
        if (_focusStatusText != null)
            _focusStatusText.Text = _focusOverlayEnabled
                ? "Focus overlay: highlighting the focused element each frame."
                : "Highlights the currently focused element.";
        if (_focusOverlayEnabled)
        {
            if (_focusOverlayTimer == null)
            {
                _focusOverlayTimer = new Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _focusOverlayTimer.Tick += (_, _) => UpdateFocusOverlay();
                _focusOverlayTimer.Start();
            }
            UpdateFocusOverlay();
        }
        else
        {
            _focusOverlayTimer?.Stop();
            _focusOverlayTimer = null;
            _overlay?.HighlightElement(null);
        }
    }

    private void UpdateFocusOverlay()
    {
        try
        {
            var focused = FocusService.FocusedElement as UIElement;
            _overlay?.HighlightElement(focused);
        }
        catch { /* ignore */ }
    }

    // ── Win32 P/Invoke (screenshot + color picker) ────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint hdc, nint hbmp, uint start, uint cLines, byte[]? bits, ref BITMAPINFO lpbi, uint usage);

    private static byte[] CaptureHwndPixels(nint hwnd, int width, int height)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

        nint windowDc = GetDC(hwnd);
        nint memDc = CreateCompatibleDC(windowDc);
        nint bitmap = CreateCompatibleBitmap(windowDc, width, height);
        nint oldBmp = SelectObject(memDc, bitmap);
        try
        {
            // PrintWindow with PW_RENDERFULLCONTENT (0x2) grabs DirectComposition / GPU content.
            PrintWindow(hwnd, memDc, 0x2);
            var bi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top-down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };
            byte[] pixels = new byte[width * height * 4];
            GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref bi, 0);
            return pixels;
        }
        finally
        {
            SelectObject(memDc, oldBmp);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(hwnd, windowDc);
        }
    }

    private static void WritePngFromBgra(string path, byte[] bgra, int width, int height)
    {
        // Jalium's PngBitmapEncoder does not yet emit bytes, so we write a minimal PNG
        // (signature + IHDR + single IDAT + IEND) here. Pixels arrive as BGRA; PNG wants RGBA.
        using var fs = File.Create(path);
        Span<byte> sig = stackalloc byte[8] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        fs.Write(sig);

        // IHDR
        Span<byte> ihdr = stackalloc byte[13];
        WriteUInt32BE(ihdr, 0, (uint)width);
        WriteUInt32BE(ihdr, 4, (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WritePngChunk(fs, "IHDR", ihdr);

        // IDAT — raw filter=0 per scanline, then zlib (adler32 + deflate)
        int rowBytes = width * 4;
        byte[] rawWithFilters = new byte[(rowBytes + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int srcOffset = y * rowBytes;
            int dstOffset = y * (rowBytes + 1);
            rawWithFilters[dstOffset] = 0;
            for (int x = 0; x < width; x++)
            {
                int srcPx = srcOffset + x * 4;
                int dstPx = dstOffset + 1 + x * 4;
                // BGRA → RGBA, un-premultiply not needed: source is either BGRA from GetDIBits (already straight)
                // or Pbgra32 which we treat as straight BGRA for a screenshot visualisation.
                rawWithFilters[dstPx + 0] = bgra[srcPx + 2];
                rawWithFilters[dstPx + 1] = bgra[srcPx + 1];
                rawWithFilters[dstPx + 2] = bgra[srcPx + 0];
                rawWithFilters[dstPx + 3] = bgra[srcPx + 3];
            }
        }

        using var zlibStream = new MemoryStream();
        WriteZlib(zlibStream, rawWithFilters);
        WritePngChunk(fs, "IDAT", zlibStream.ToArray());

        WritePngChunk(fs, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteZlib(Stream output, byte[] raw)
    {
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }
        uint adler = Adler32(raw);
        Span<byte> a = stackalloc byte[4];
        WriteUInt32BE(a, 0, adler);
        output.Write(a);
    }

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }

    private static void WritePngChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteUInt32BE(len, 0, (uint)data.Length);
        s.Write(len);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        s.Write(typeBytes);

        s.Write(data);

        uint crc = Crc32(typeBytes, data);
        Span<byte> crcSpan = stackalloc byte[4];
        WriteUInt32BE(crcSpan, 0, crc);
        s.Write(crcSpan);
    }

    private static readonly uint[] s_crcTable = CreateCrcTable();

    private static uint[] CreateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFF;
        for (int i = 0; i < a.Length; i++)
            c = s_crcTable[(c ^ a[i]) & 0xFF] ^ (c >> 8);
        for (int i = 0; i < b.Length; i++)
            c = s_crcTable[(c ^ b[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static void WriteUInt32BE(Span<byte> buffer, int offset, uint value)
    {
        buffer[offset + 0] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }

    #endregion

    #region UiaTab

    private TreeView? _uiaTreeView;
    private StackPanel? _uiaDetailsPanel;
    private readonly Queue<UiaBuildTask> _uiaPendingBuild = new();
    private DispatcherTimer? _uiaBuildTimer;
    private bool _uiaTreeBuilt;

    /// <summary>
    /// TreeView container carrying a reference back to its <see cref="AutomationPeer"/>
    /// so the tree selection and context-menu can map a visible row to its peer.
    /// </summary>
    private sealed class UiaTreeViewItem : TreeViewItem
    {
        public AutomationPeer Peer { get; }
        public UiaTreeViewItem(AutomationPeer peer)
        {
            Peer = peer;
            Header = DescribeUiaPeer(peer);
        }
    }

    private sealed class UiaBuildTask
    {
        public UiaBuildTask(UiaTreeViewItem item, AutomationPeer peer, int level)
        {
            Item = item;
            Peer = peer;
            Level = level;
        }
        public UiaTreeViewItem Item { get; }
        public AutomationPeer Peer { get; }
        public int Level { get; }
    }

    private UIElement BuildUiaTab()
    {
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Toolbar
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(DevToolsUi.Button("Refresh", BuildUiaTree, DevToolsUi.ButtonStyle.Primary, icon: "↻"));
        toolbar.Children.Add(DevToolsUi.Muted("Click a peer to see its properties and supported patterns."));
        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        outer.Children.Add(toolbarBar);

        // Split: tree | details
        var grid = new Grid { Margin = new Thickness(DevToolsTheme.GutterBase) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _uiaTreeView = new TreeView
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Margin = new Thickness(0),
        };
        _uiaTreeView.SelectedItemChanged += OnUiaTreeSelectionChanged;
        Grid.SetColumn(_uiaTreeView, 0);
        grid.Children.Add(_uiaTreeView);

        var splitter = new GridSplitter
        {
            Background = DevToolsTheme.BorderSubtle,
            ResizeDirection = GridResizeDirection.Columns,
            Margin = new Thickness(DevToolsTheme.GutterXS, 0, DevToolsTheme.GutterXS, 0),
            Width = 2,
        };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        _uiaDetailsPanel = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterLg) };
        _uiaDetailsPanel.Children.Add(DevToolsUi.Muted("Activate the tab to enumerate the AutomationPeer tree."));
        var right = new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Child = new ScrollViewer
            {
                Content = _uiaDetailsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            ClipToBounds = true,
        };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Grid.SetRow(grid, 1);
        outer.Children.Add(grid);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = outer,
            ClipToBounds = true,
        };
    }

    partial void OnUiaTabActivated()
    {
        if (_uiaTreeBuilt) return;
        BuildUiaTree();
    }

    private void BuildUiaTree()
    {
        if (_uiaTreeView == null) return;
        _uiaTreeBuilt = true;
        _uiaTreeView.Items.Clear();
        _uiaPendingBuild.Clear();

        var rootPeer = _targetWindow.GetAutomationPeer();
        if (rootPeer == null)
        {
            // TreeView expects own-container TreeViewItems; render a leaf explanation
            // row so the empty state keeps the overall style consistent.
            var empty = new TreeViewItem { Header = "Window has no AutomationPeer (OnCreateAutomationPeer returned null)." };
            _uiaTreeView.Items.Add(empty);
            return;
        }

        // Build the root up-front just like the Inspector: attach metadata before
        // adding to the TreeView, then expand. Children are filled asynchronously
        // by a dispatcher timer so the VSP container pipeline stays stable.
        var root = new UiaTreeViewItem(rootPeer);
        root.ParentTreeView = _uiaTreeView;
        root.Level = 0;
        _uiaTreeView.Items.Add(root);
        root.IsExpanded = true;

        _uiaPendingBuild.Enqueue(new UiaBuildTask(root, rootPeer, 0));
        ScheduleUiaBuild();
    }

    private void ScheduleUiaBuild()
    {
        if (_uiaPendingBuild.Count == 0) return;
        if (_uiaBuildTimer == null)
        {
            _uiaBuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _uiaBuildTimer.Tick += OnUiaBuildTimerTick;
        }
        if (!_uiaBuildTimer.IsEnabled)
            _uiaBuildTimer.Start();
    }

    private const int UiaBuildNodeBatch = 8;
    private const int UiaBuildChildBatch = 48;

    private void OnUiaBuildTimerTick(object? sender, EventArgs e)
    {
        _uiaBuildTimer?.Stop();

        int processedNodes = 0;
        int processedChildren = 0;
        while (processedNodes < UiaBuildNodeBatch &&
               processedChildren < UiaBuildChildBatch &&
               _uiaPendingBuild.Count > 0)
        {
            var task = _uiaPendingBuild.Dequeue();

            List<AutomationPeer> children;
            try { children = task.Peer.GetChildren() ?? new List<AutomationPeer>(); }
            catch { children = new List<AutomationPeer>(); }

            var childItems = new List<TreeViewItem>();
            foreach (var child in children)
            {
                if (child == null) continue;
                var childItem = new UiaTreeViewItem(child);
                childItems.Add(childItem);
                _uiaPendingBuild.Enqueue(new UiaBuildTask(childItem, child, task.Level + 1));
                processedChildren++;
                if (processedChildren >= UiaBuildChildBatch) break;
            }

            if (childItems.Count > 0)
                task.Item.AddChildItems(childItems);

            processedNodes++;
        }

        if (_uiaPendingBuild.Count > 0) ScheduleUiaBuild();
    }

    private void OnUiaTreeSelectionChanged(object? sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (e.NewValue is UiaTreeViewItem item)
            ShowUiaDetails(item.Peer);
    }

    private static string DescribeUiaPeer(AutomationPeer peer)
    {
        string name = SafeGet(() => peer.GetName()) ?? "";
        string role = SafeGet(() => peer.GetAutomationControlType().ToString()) ?? "?";
        string cls = SafeGet(() => peer.GetClassName()) ?? peer.GetType().Name;
        return string.IsNullOrEmpty(name) ? $"{cls}  ({role})" : $"{cls}  ({role})  \"{name}\"";
    }

    private static string? SafeGet(Func<string> fn)
    {
        try { return fn(); }
        catch { return null; }
    }

    private void ShowUiaDetails(AutomationPeer peer)
    {
        if (_uiaDetailsPanel == null) return;
        _uiaDetailsPanel.Children.Clear();

        void PropertyRow(string label, string value, Brush? valueBrush = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.TokenProperty,
                MinWidth = 140,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = valueBrush ?? DevToolsTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _uiaDetailsPanel.Children.Add(row);
        }

        _uiaDetailsPanel.Children.Add(DevToolsUi.SectionHeading("Properties"));
        try { PropertyRow("Name", string.IsNullOrEmpty(peer.GetName()) ? "(none)" : $"\"{peer.GetName()}\"", DevToolsTheme.TokenString); }
        catch (Exception ex) { PropertyRow("Name", $"<error: {ex.Message}>", DevToolsTheme.Error); }
        try { PropertyRow("ControlType", peer.GetAutomationControlType().ToString(), DevToolsTheme.TokenEnum); } catch { }
        try { PropertyRow("ClassName", peer.GetClassName()); } catch { }
        try { PropertyRow("IsEnabled", peer.IsEnabled().ToString(), DevToolsTheme.TokenBool); } catch { }
        try { PropertyRow("IsKeyboardFocusable", peer.IsKeyboardFocusable().ToString(), DevToolsTheme.TokenBool); } catch { }
        try { PropertyRow("HasKeyboardFocus", peer.HasKeyboardFocus().ToString(), DevToolsTheme.TokenBool); } catch { }
        try { PropertyRow("BoundingRectangle", peer.GetBoundingRectangle().ToString(), DevToolsTheme.TokenNumber); } catch { }

        _uiaDetailsPanel.Children.Add(DevToolsUi.SectionHeading("Supported patterns"));
        var patternsRow = new WrapPanel();
        int patternCount = 0;
        foreach (PatternInterface pat in Enum.GetValues<PatternInterface>())
        {
            object? impl = null;
            try { impl = peer.GetPattern(pat); } catch { }
            if (impl == null) continue;
            patternsRow.Children.Add(DevToolsUi.Pill(pat.ToString(), DevToolsTheme.Success));
            patternCount++;
        }
        if (patternCount == 0)
        {
            _uiaDetailsPanel.Children.Add(DevToolsUi.Muted("(no patterns exposed)"));
        }
        else
        {
            _uiaDetailsPanel.Children.Add(patternsRow);
        }
    }

    #endregion

    #region ValueSource

    private static readonly SolidColorBrush BrushSourceLocal = new(DevToolsTheme.AccentColor);
    private static readonly SolidColorBrush BrushSourceStyle = new(DevToolsTheme.WarningColor);
    private static readonly SolidColorBrush BrushSourceTemplate = new(DevToolsTheme.InfoColor);
    private static readonly SolidColorBrush BrushSourceInherited = new(DevToolsTheme.TokenKeywordColor);
    private static readonly SolidColorBrush BrushSourceDefault = new(DevToolsTheme.TextMutedColor);
    private static readonly SolidColorBrush BrushSourceAnimated = new(DevToolsTheme.SuccessColor);

    private void AppendValueSourceBadge(DependencyObject target, DependencyProperty property)
    {
        try
        {
            if (_propertiesPanel.Children.Count == 0) return;
            var last = _propertiesPanel.Children[_propertiesPanel.Children.Count - 1];
            if (last is not StackPanel row) return;

            var source = DependencyPropertyHelper.GetValueSource(target, property);
            string labelText = source.BaseValueSource.ToString();

            Brush brush = source.BaseValueSource switch
            {
                BaseValueSource.Local => BrushSourceLocal,
                BaseValueSource.Style => BrushSourceStyle,
                BaseValueSource.DefaultStyle => BrushSourceStyle,
                BaseValueSource.StyleTrigger => BrushSourceStyle,
                BaseValueSource.DefaultStyleTrigger => BrushSourceStyle,
                BaseValueSource.ImplicitStyleReference => BrushSourceStyle,
                BaseValueSource.ParentTemplate => BrushSourceTemplate,
                BaseValueSource.ParentTemplateTrigger => BrushSourceTemplate,
                BaseValueSource.TemplateTrigger => BrushSourceTemplate,
                BaseValueSource.Inherited => BrushSourceInherited,
                BaseValueSource.Default => BrushSourceDefault,
                _ => BrushSourceDefault,
            };

            var sb = new System.Text.StringBuilder();
            sb.Append("  · ").Append(labelText);
            if (source.IsAnimated) sb.Append(" · anim");
            if (source.IsExpression) sb.Append(" · bound");
            if (source.IsCoerced) sb.Append(" · coerced");

            var badge = new TextBlock
            {
                Text = sb.ToString(),
                FontSize = DevToolsTheme.FontXS,
                Foreground = source.IsAnimated ? BrushSourceAnimated : brush,
                Margin = new Thickness(4, 2, 0, 0),
            };
            row.Children.Add(badge);
        }
        catch
        {
            // ValueSource is diagnostic-only — never fail the inspector if lookup errors.
        }
    }

    #endregion
}

internal static class ReplTokenizer
{
    internal enum TokenKind
    {
        Identifier, NumberLiteral, StringLiteral,
        Dot, LParen, RParen, Comma, Semicolon, Assign, Dollar,
    }

    internal readonly struct Token
    {
        public Token(TokenKind kind, string text) { Kind = kind; Text = text; }
        public TokenKind Kind { get; }
        public string Text { get; }
    }

    internal static List<Token> Tokenize(string code)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '$') { tokens.Add(new Token(TokenKind.Dollar, "$")); i++; continue; }
            if (c == '.') { tokens.Add(new Token(TokenKind.Dot, ".")); i++; continue; }
            if (c == '(') { tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue; }
            if (c == ',') { tokens.Add(new Token(TokenKind.Comma, ",")); i++; continue; }
            if (c == ';') { tokens.Add(new Token(TokenKind.Semicolon, ";")); i++; continue; }
            if (c == '=')
            {
                tokens.Add(new Token(TokenKind.Assign, "="));
                i++;
                continue;
            }
            if (c == '"')
            {
                int end = i + 1;
                var sb = new StringBuilder();
                while (end < code.Length && code[end] != '"')
                {
                    if (code[end] == '\\' && end + 1 < code.Length)
                    {
                        sb.Append(code[end + 1] switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            '"' => '"',
                            '\\' => '\\',
                            _ => code[end + 1],
                        });
                        end += 2;
                    }
                    else
                    {
                        sb.Append(code[end]);
                        end++;
                    }
                }
                if (end >= code.Length) throw new InvalidOperationException("Unterminated string literal");
                tokens.Add(new Token(TokenKind.StringLiteral, sb.ToString()));
                i = end + 1;
                continue;
            }
            if (char.IsDigit(c) || (c == '-' && i + 1 < code.Length && char.IsDigit(code[i + 1])))
            {
                int end = i + 1;
                while (end < code.Length && (char.IsDigit(code[end]) || code[end] == '.')) end++;
                tokens.Add(new Token(TokenKind.NumberLiteral, code.Substring(i, end - i)));
                i = end;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int end = i + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_')) end++;
                tokens.Add(new Token(TokenKind.Identifier, code.Substring(i, end - i)));
                i = end;
                continue;
            }
            throw new InvalidOperationException($"Unexpected character '{c}' at position {i}");
        }
        return tokens;
    }
}
