using System.Collections.Concurrent;
using System.Collections.ObjectModel;
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
using BitmapFrame = Jalium.UI.Media.Imaging.BitmapFrame;
using BitmapSource = Jalium.UI.Media.Imaging.BitmapSource;
using PngBitmapEncoder = Jalium.UI.Media.Imaging.PngBitmapEncoder;

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
    private readonly DevToolsInspector _inspector;
    private readonly InspectorTreeList _visualTreeView;
    private readonly DevToolsDetailsList _propertiesPanel;
    private readonly List<DevToolsDependencyPropertyModel> _propertyModels = new();
    private readonly DispatcherTimer _searchRefreshTimer;
    private Visual? _selectedVisual;
    private DevToolsOverlay? _overlay;
    private readonly RelayCommand _fontDialogCommand;

    // Inspector tree view mode (visual vs logical vs flat). The segmented toggle
    // in the inspector toolbar flips this; RefreshVisualTree rebuilds accordingly.
    internal enum InspectorViewMode
    {
        Visual = 0,
        Logical = 1,
        Flat = 2,
    }
    private InspectorViewMode _inspectorViewMode = InspectorViewMode.Visual;

    private void SetInspectorViewMode(InspectorViewMode mode)
    {
        if (_inspectorViewMode == mode) return;
        _inspectorViewMode = mode;
        _inspector.SetCurrentValue(DevToolsInspector.SelectedViewProperty, (DevToolsInspectorView)mode);
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

    // ── Inspector flat virtualized list state ──
    private readonly BulkObservableCollection<InspectorRow> _rows = new();
    private readonly Dictionary<Visual, InspectorRow> _rowByVisual = new();
    private readonly HashSet<Visual> _expandedVisuals = new();
    private string? _activeFilter;

    // ── Palette (re-exported from DevToolsTheme for legacy call sites in this file) ──
    // Property-category legend — a muted, graphite-friendly categorical palette
    // (mapped onto the instrument tokens where one fits) so category bullets stay
    // distinguishable without reintroducing neon devtools-blue.
    private const double NameWidth = 145;
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<DependencyPropertyInspectorEntry>> s_dependencyPropertyCache = new();

    private sealed record DependencyPropertyInspectorEntry(DependencyProperty Property, DevToolsPropertyCategory Category, string? DisplayName = null);

    protected override bool CanOpenDevTools => false;

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevToolsWindow includes a REPL and inspector that reflect on user types.")]
    public DevToolsWindow(Window targetWindow)
    {
        _fontDialogCommand = new RelayCommand(ExecuteFontDialog, CanExecuteFontDialog);
        _targetWindow = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));
        LayoutDiagnostics.StackCaptureEnricher ??= LayoutDiagnostics.CreateInvalidationEntryWithStack;

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

        _visualTreeView = new InspectorTreeList
        {
            ItemsSource = _rows,
            Style = DevToolsThemeResources.GetStyle("DevTools.InspectorTree"),
            ToggleExpand = ToggleExpand,
        };
        _visualTreeView.SelectionChanged += OnInspectorSelectionChanged;
        _visualTreeView.AddHandler(UIElement.PreviewMouseUpEvent, new Input.MouseButtonEventHandler(OnVisualTreeRightClick));
        _propertiesPanel = new DevToolsDetailsList();
        _inspector = new DevToolsInspector
        {
            TreeContent = _visualTreeView,
            DetailsContent = _propertiesPanel,
            IsDetailsEmpty = true,
            RefreshCommand = new RelayCommand(RefreshVisualTree),
            PickCommand = new RelayCommand(() => { if (_isPickerActive) DeactivatePicker(); else ActivatePicker(); }),
            ExpandCommand = new RelayCommand(ExpandAll),
            CollapseCommand = new RelayCommand(CollapseAll),
            CopyCommand = new RelayCommand(CopyElementInfo),
            DeleteCommand = new RelayCommand(DeleteSelectedElement),
            UndoCommand = new RelayCommand(UndoDelete),
        };
        _inspector.SearchTextChanged += OnSearchTextChanged;
        _inspector.SelectedViewChanged += (_, _) => SetInspectorViewMode((InspectorViewMode)_inspector.SelectedView);
        UpdateMutationButtons();
        Content = BuildTabLayout();

        _searchRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SearchRefreshDelayMilliseconds)
        };
        _searchRefreshTimer.Tick += OnSearchRefreshTimerTick;

        RefreshVisualTree();

        _overlay = new DevToolsOverlay(_targetWindow);
        _targetWindow.DevToolsOverlay = _overlay;

        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        Closed += OnDevToolsClosed;
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Toolbar
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void DeleteSelectedElement()
    {
        if (_selectedVisual != null && CanDeleteElement(_selectedVisual, out _))
            DeleteElement(_selectedVisual);
    }

    private void UpdateMutationButtons()
    {
        string reason = "Select a removable element";
        bool canDelete = _selectedVisual != null && CanDeleteElement(_selectedVisual, out reason);
        _inspector.CanDelete = canDelete;
        _inspector.DeleteToolTip = canDelete ? "Delete selected element (Delete)" : reason ?? "Select a removable element";
        _inspector.CanUndo = _deleteRecord != null;
        _inspector.UndoToolTip = _deleteRecord == null ? "Nothing to restore" : $"Restore {_deleteRecord.Label} (Ctrl+Z)";
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Element Picker Mode
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    internal void ActivatePicker()
    {
        StopToolsInteractions();
        _isPickerActive = true;
        _inspector.IsPicking = true;

        _targetWindow.PreviewMouseMove += OnTargetPreviewMouseMove;
        _targetWindow.PreviewMouseDown += OnTargetPreviewMouseDown;
    }

    private void DeactivatePicker()
    {
        _isPickerActive = false;
        _inspector.IsPicking = false;

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
        if (_workspace != null)
            _workspace.SetCurrentValue(DevToolsWorkspace.SelectedPageProperty, DevToolsPage.Inspector);

        // Normalize: clear any active filter + force Visual mode so every visual is
        // locatable, then rebuild the flat row list before locating the target.
        _inspector.SearchText = "";
        _inspectorViewMode = InspectorViewMode.Visual;
        _inspector.SelectedView = DevToolsInspectorView.Visual;
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
        var filter = _inspector.SearchText?.Trim() ?? "";
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

    private void OnDevToolsClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        ClearPropertyModels();
        StopAllRefreshTimers();
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
        if (ReferenceEquals(_targetWindow.DevToolsOverlay, _overlay))
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
            if (_rulerActive || _colorPickerActive)
            {
                DeactivateRuler(); DeactivateColorPicker();
                e.Handled = true;
            }
            else if (_isPickerActive)
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
        else if (e.Key == Key.Delete && _selectedVisual != null && CanDeleteElement(_selectedVisual, out _))
        {
            DeleteSelectedElement();
            e.Handled = true;
        }
        else if (e.IsControlDown)
        {
            switch (e.Key)
            {
                case Key.F:
                    _inspector.FocusSearch();
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
                case Key.Z:
                    if (_deleteRecord != null)
                    {
                        UndoDelete();
                        e.Handled = true;
                    }
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

        var filter = _inspector.SearchText?.Trim() ?? "";
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
            UpdateMutationButtons();
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
        using var ignoredCreation = DiagnosticsScope.BeginIgnoredCreation();
        _inspector.IsDetailsEmpty = visual == null;
        ClearPropertyModels();
        _propertiesPanel.Items.Clear();

        if (visual == null)
        {
            _inspector.InvalidateMeasure();
            return;
        }

        try
        {
            UpdatePropertiesPanelCore(visual);
        }
        catch (Exception ex)
        {
            _propertiesPanel.Items.Add(new DevToolsDetailRow { Label = "Error", ValueText = $"{ex.GetType().Name}: {ex.Message}", Foreground = DevToolsTheme.Error });
        }

        // Force the ScrollViewer to re-measure after content changed
        _inspector.InvalidateMeasure();
        InvalidateWindow();
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "AddBindingInspector 'enumerates static DependencyProperty fields on FrameworkElement subtypes via reflection.' DevTools is an opt-in developer inspector control (UseDevTools()); consumers that inspect their own FrameworkElement subtypes under trimming/AOT must keep those types' static DependencyProperty fields preserved. That preservation is the documented consumer responsibility, not a defect of this site.")]
    private void UpdatePropertiesPanelCore(Visual visual)
    {
        AddElementSummary(visual);
        AddBreadcrumb(visual);
        if (visual is FrameworkElement box) AddBoxModel(box);
        AddCategorizedDependencyPropertyInspector(visual);
        AddElementDiagnostics(visual);
        if (visual is not FrameworkElement element) return;
        if (element is Grid grid) AddGridDefinitions(grid);
        try { if (element.Style is { } style) AddStyleInspector(style); } catch { }
        try { if (element.ResourcesOrNull is { Count: > 0 } resources) AddResourceInspector(resources); } catch { }
        try { AddBindingInspector(element); } catch { }
        try { AppendTemplateXamlViewer(element); } catch { }
    }

    private void AddElementDiagnostics(Visual visual)
    {
        var items = new List<DevToolsDetailItem>();
        if (visual is UIElement ui)
        {
            items.Add(new() { Name = "DesiredSize", ValueText = $"{ui.DesiredSize.Width:F1} × {ui.DesiredSize.Height:F1}", ValueKind = DevToolsResourceValueKind.Number });
            var bounds = ui.VisualBounds;
            items.Add(new() { Name = "VisualBounds", ValueText = $"({bounds.X:F1}, {bounds.Y:F1}) {bounds.Width:F1} × {bounds.Height:F1}", ValueKind = DevToolsResourceValueKind.Number });
        }
        if (visual is FrameworkElement { DataContext: { } dataContext })
            items.Add(new() { Name = "DataContext Type", ValueText = dataContext.GetType().Name, ValueKind = DevToolsResourceValueKind.Style });
        if (visual is Border border) items.Add(DevToolsDetailItem.FromValue("Child", border.Child?.GetType().Name));
        if (visual is ContentControl { Content: { } content } && visual is not NavigationView)
            items.Add(new() { Name = "Content Type", ValueText = content.GetType().Name, ValueKind = DevToolsResourceValueKind.Style });
        if (visual is Panel panel) items.Add(DevToolsDetailItem.FromValue("Children", panel.Children.Count));
        if (visual is Grid grid)
        {
            items.Add(DevToolsDetailItem.FromValue("Rows", grid.RowDefinitions.Count));
            items.Add(DevToolsDetailItem.FromValue("Columns", grid.ColumnDefinitions.Count));
        }
        if (visual is ItemsControl control) items.Add(DevToolsDetailItem.FromValue("Items.Count", control.Items.Count));
        if (visual is Control fontControl) items.Add(DevToolsDetailItem.FromValue("FontWeight.OpenType", fontControl.FontWeight.ToOpenTypeWeight()));
        else if (visual is TextBlock text) items.Add(DevToolsDetailItem.FromValue("FontWeight.OpenType", text.FontWeight.ToOpenTypeWeight()));
        if (visual is ToggleSwitch toggle)
        {
            if (toggle.OnContent == null) items.Add(DevToolsDetailItem.FromValue("On label", "On"));
            if (toggle.OffContent == null) items.Add(DevToolsDetailItem.FromValue("Off label", "Off"));
        }
        if (items.Count > 0) AddDetailSection("Diagnostics", items);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "GetCategorizedDependencyProperties 'enumerates static DependencyProperty fields on the target runtime type via reflection.' DevTools is an opt-in developer inspector control (UseDevTools()); consumers that inspect their own runtime types under trimming/AOT must keep those types' static DependencyProperty fields preserved. That preservation is the documented consumer responsibility, not a defect of this site.")]
    private void AddCategorizedDependencyPropertyInspector(DependencyObject dependencyObject)
    {
        var entries = GetCategorizedDependencyProperties(dependencyObject.GetType()).ToDictionary(entry => entry.Property);
        if (dependencyObject is Visual { VisualParent: Grid })
        {
            AddAttached(Grid.RowProperty, "Grid.Row"); AddAttached(Grid.ColumnProperty, "Grid.Column");
            AddAttached(Grid.RowSpanProperty, "Grid.RowSpan"); AddAttached(Grid.ColumnSpanProperty, "Grid.ColumnSpan");
        }
        else if (dependencyObject is Visual { VisualParent: Canvas })
        {
            AddAttached(Canvas.LeftProperty, "Canvas.Left"); AddAttached(Canvas.TopProperty, "Canvas.Top");
            AddAttached(Canvas.RightProperty, "Canvas.Right"); AddAttached(Canvas.BottomProperty, "Canvas.Bottom");
        }
        void AddAttached(DependencyProperty property, string name) => entries[property] = new(property, DevToolsPropertyCategory.Layout, name);
        if (entries.Count == 0)
        {
            return;
        }

        AddSection($"Properties by Category ({entries.Count})");

        foreach (var group in entries.Values
                     .GroupBy(static entry => entry.Category)
                     .OrderBy(static group => GetCategorySortOrder(group.Key)))
        {
            var models = group.OrderBy(static entry => entry.DisplayName ?? entry.Property.Name, StringComparer.Ordinal)
                .Select(entry => new DevToolsDependencyPropertyModel(dependencyObject, entry.Property, entry.Category.ToString(), entry.DisplayName)).ToArray();
            _propertyModels.AddRange(models);
            _propertiesPanel.Items.Add(new DevToolsPropertyGroup { Category = group.Key, ItemsSource = models, NameColumnWidth = NameWidth, FontDialogCommand = Platform.PlatformFactory.IsWindows ? _fontDialogCommand : null });
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

    private void ClearPropertyModels()
    {
        foreach (var model in _propertyModels) model.Dispose();
        _propertyModels.Clear();
    }



    private void AddElementSummary(Visual visual)
    {
        int depth = 0;
        for (var ancestor = visual.VisualParent; ancestor != null; ancestor = ancestor.VisualParent) depth++;
        var type = visual.GetType();
        var summary = new DevToolsElementSummary
        {
            TypeName = type.Name,
            BaseTypeName = type.BaseType is { } baseType && baseType != typeof(object) ? baseType.Name : string.Empty,
            ShowStatistics = visual is UIElement,
            Depth = depth,
            ChildrenCount = visual is UIElement ? visual.VisualChildrenCount : 0,
            DescendantCount = visual is UIElement ? CountDescendants(visual) : 0,
            Margin = new Thickness(8, 8, 4, 2),
        };
        if (visual is FrameworkElement element)
            summary.SetBinding(DevToolsElementSummary.ElementNameProperty, new Binding(nameof(FrameworkElement.Name)) { Source = element, Mode = BindingMode.OneWay });
        _propertiesPanel.Items.Add(summary);
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
        for (Visual? current = visual; current != null; current = current.VisualParent) ancestors.Add(current);
        ancestors.Reverse();
        var view = new DevToolsBreadcrumb
        {
            ItemsSource = ancestors.Select((ancestor, index) => new DevToolsBreadcrumbNode(ancestor, index == ancestors.Count - 1, index > 0)).ToArray(),
            Margin = new Thickness(8, 0, 4, 6),
        };
        view.NavigationRequested += (_, args) => RevealInInspector(args.Target);
        _propertiesPanel.Items.Add(view);
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Box Model Diagram (Margin > Border > Padding > Content)
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void AddBoxModel(FrameworkElement element)
    {
        Thickness border = element is Control control ? control.BorderThickness : element is Border frame ? frame.BorderThickness : default;
        Thickness padding = element is Control control2 ? control2.Padding : element is Border frame2 ? frame2.Padding : default;
        if (element.Margin == default && border == default && padding == default) return;

        AddSection("Box Model");
        var view = new DevToolsBoxModel
        {
            Margin = new Thickness(8, 2, 4, 8),
        };
        void Bind(DependencyProperty property, string path) => view.SetBinding(property, new Binding(path) { Source = element, Mode = BindingMode.OneWay });
        Bind(DevToolsBoxModel.ElementMarginProperty, nameof(FrameworkElement.Margin));
        Bind(DevToolsBoxModel.ElementWidthProperty, nameof(FrameworkElement.ActualWidth));
        Bind(DevToolsBoxModel.ElementHeightProperty, nameof(FrameworkElement.ActualHeight));
        if (element is Control or Border)
        {
            Bind(DevToolsBoxModel.ElementBorderThicknessProperty, nameof(Control.BorderThickness));
            Bind(DevToolsBoxModel.ElementPaddingProperty, nameof(Control.Padding));
        }
        _propertiesPanel.Items.Add(view);
    }

    private DevToolsDetailSection AddDetailSection(string title, IEnumerable<DevToolsDetailItem> items, bool showCount = false)
    {
        var section = new DevToolsDetailSection { Title = title, ItemsSource = items.ToArray(), ShowCount = showCount };
        _propertiesPanel.Items.Add(section);
        return section;
    }

    private void AddGridDefinitions(Grid grid)
    {
        if (grid.RowDefinitions.Count > 0)
            AddDetailSection("Row Definitions", grid.RowDefinitions.Select((definition, index) => new DevToolsDetailItem
            {
                Name = $"Row[{index}]", ValueText = definition.Height.ToString(), ValueKind = DevToolsResourceValueKind.Enum,
                DetailText = $"Actual: {definition.ActualHeight:F1}"
            }));
        if (grid.ColumnDefinitions.Count > 0)
            AddDetailSection("Column Definitions", grid.ColumnDefinitions.Select((definition, index) => new DevToolsDetailItem
            {
                Name = $"Col[{index}]", ValueText = definition.Width.ToString(), ValueKind = DevToolsResourceValueKind.Enum,
                DetailText = $"Actual: {definition.ActualWidth:F1}"
            }));
    }

    private void AddStyleInspector(Style style)
    {
        var items = new List<DevToolsDetailItem>();
        if (style.TargetType != null) items.Add(new() { Name = "TargetType", ValueText = style.TargetType.Name, ValueKind = DevToolsResourceValueKind.Style });
        if (style.BasedOn != null) items.Add(new() { Name = "BasedOn", ValueText = style.BasedOn.TargetType?.Name ?? "(unknown)", ValueKind = DevToolsResourceValueKind.Style });
        foreach (var setter in style.Setters.OfType<Setter>())
            items.Add(DevToolsDetailItem.FromValue(setter.Property?.Name ?? "?", setter.Value));
        AddDetailSection("Style", items);
        AppendStyleXamlViewer(style);
    }

    private void AddResourceInspector(System.Collections.IDictionary resources)
    {
        var items = new List<DevToolsDetailItem>();
        foreach (var key in resources.Keys)
        {
            try { items.Add(DevToolsDetailItem.FromValue(key?.ToString() ?? "?", resources[key!])); }
            catch (Exception error) { items.Add(new() { Name = key?.GetType().Name ?? "?", ValueText = error.Message }); }
        }
        AddDetailSection("Resources", items, showCount: true);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools binding inspector enumerates static DependencyProperty fields on FrameworkElement subtypes via reflection.")]
    private void AddBindingInspector(FrameworkElement element)
    {
        var items = new List<DevToolsDetailItem>();
        foreach (var property in EnumerateDependencyProperties(element.GetType()).Distinct())
        {
            try
            {
                var binding = BindingOperations.GetBindingBase(element, property);
                if (binding == null) continue;
                var value = binding switch
                {
                    Binding ordinary => ordinary.Path?.Path ?? "(no path)",
                    MultiBinding multi => string.Join(" + ", multi.Bindings.Select(BindingPath)),
                    PriorityBinding priority => string.Join(" | ", priority.Bindings.Select(BindingPath)),
                    _ => binding.GetType().Name,
                };
                var detail = binding switch
                {
                    Binding ordinary => ordinary.Mode.ToString(),
                    MultiBinding multi => $"MultiBinding · {multi.Mode} · {multi.Bindings.Count}",
                    PriorityBinding priority => $"PriorityBinding · {priority.Bindings.Count}",
                    _ => binding.GetType().Name,
                };
                items.Add(new() { Name = property.Name, ValueText = value, DetailText = detail, ValueKind = DevToolsResourceValueKind.Reference });
            }
            catch { /* One unavailable diagnostic binding must not hide other properties. */ }
        }
        if (items.Count > 0) AddDetailSection("Bindings", items, showCount: true);
        static string BindingPath(BindingBase binding) => binding is Binding ordinary ? ordinary.Path?.Path ?? "(no path)" : binding.GetType().Name;
    }

    private void AddSection(string name) => _propertiesPanel.Items.Add(new DevToolsHeading { Text = name });

    private bool CanExecuteFontDialog(object? value) => !_isClosing && Platform.PlatformFactory.IsWindows &&
        value is DevToolsDependencyPropertyModel model && _propertyModels.Contains(model) && model.TryGetTarget(out var target) &&
        (target is TextBlock && model.Property == TextBlock.FontFamilyProperty || target is Control && model.Property == Control.FontFamilyProperty);

    private void ExecuteFontDialog(object? value)
    {
        if (!CanExecuteFontDialog(value) || value is not DevToolsDependencyPropertyModel model || !model.TryGetTarget(out var target)) return;
        var family = target.GetValue(model.Property) as FontFamily;
        ShowFontDialogFor(target, family?.Source);
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

        }
        catch
        {
            // DevTools should never crash the host because a font edit failed.
        }
    }

    // 鈹€鈹€ 12. Font weight (rendered with that weight) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
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

    public new void CloseDevTools()
    {
        if (_isClosing) return;
        // The Closed handler owns cleanup after every Closing handler accepts.
        // Cancellation leaves the current tool session and overlay intact.
        Close();
    }

    #region BindingsTab

    private DevToolsBindingsView? _bindingsView;
    private readonly ObservableCollection<DevToolsBindingItem> _bindingItems = new();
    private readonly ObservableCollection<DevToolsBindingEventItem> _bindingEvents = new();
    private readonly List<DevToolsBindingItem> _bindingItemPool = new();
    private readonly List<DevToolsBindingEventItem> _bindingEventPool = new();
    private DispatcherTimer? _bindingsRefreshTimer;
    private const int BindingEventMaxRows = 120;

    private UIElement BuildBindingsTab()
    {
        _bindingsView = new DevToolsBindingsView
        {
            BindingsSource = _bindingItems,
            EventsSource = _bindingEvents,
            ToggleRecordingCommand = new RelayCommand(_ =>
            {
                if (BindingDiagnostics.IsRecording) BindingDiagnostics.StopRecording();
                else { BindingDiagnostics.StartRecording(); StartBindingsRefreshTimer(); }
                ReflectBindingsRecordingState();
            }),
            ResetCommand = new RelayCommand(_ => { BindingDiagnostics.Reset(); RefreshBindingsOverview(); RefreshBindingsEvents(); }),
            RevealTargetCommand = new RelayCommand(value =>
            {
                if (value is DevToolsBindingItem item && item.TargetReference?.TryGetTarget(out var target) == true) RevealInInspector(target);
            }, value => value is DevToolsBindingItem item && item.TargetReference?.TryGetTarget(out _) == true),
            RevealSourceCommand = new RelayCommand(value =>
            {
                if (value is DevToolsBindingItem item && item.SourceReference?.TryGetTarget(out var source) == true) RevealInInspector(source);
            }, value => value is DevToolsBindingItem item && item.SourceReference?.TryGetTarget(out _) == true)
        };
        return _bindingsView;
    }

    partial void OnBindingsTabActivated()
    {
        ReflectBindingsRecordingState(); RefreshBindingsOverview(); RefreshBindingsEvents();
        StartBindingsRefreshTimer();
    }

    private void StartBindingsRefreshTimer()
    {
        if (_bindingsRefreshTimer != null) return;
        // Errors are published even while recording is off. Keep a slow idle
        // refresh while this page is active so those failures remain visible.
        _bindingsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BindingDiagnostics.IsRecording ? 400 : 1000) };
        _bindingsRefreshTimer.Tick += OnBindingsRefreshTimerTick;
        _bindingsRefreshTimer.Start();
    }

    private void OnBindingsRefreshTimerTick(object? sender, EventArgs e)
    {
        ReflectBindingsRecordingState(); RefreshBindingsOverview(); RefreshBindingsEvents();
    }
    private void StopBindingsRefreshTimer() { _bindingsRefreshTimer?.Stop(); _bindingsRefreshTimer = null; }
    private void ReflectBindingsRecordingState()
    {
        if (_bindingsView != null) _bindingsView.IsRecording = BindingDiagnostics.IsRecording;
        var interval = TimeSpan.FromMilliseconds(BindingDiagnostics.IsRecording ? 400 : 1000);
        if (_bindingsRefreshTimer != null && _bindingsRefreshTimer.Interval != interval) _bindingsRefreshTimer.Interval = interval;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "DevTools is opt-in; consumers preserve inspected DependencyProperty static fields under trimming/AOT.")]
    private void RefreshBindingsOverview()
    {
        if (_bindingsView == null) return;
        int count = 0;
        if (_selectedVisual is FrameworkElement target)
        {
            _bindingsView.SelectedElementName = string.IsNullOrEmpty(target.Name) ? target.GetType().Name : $"{target.GetType().Name}  #{target.Name}";
            foreach (var property in EnumerateDependencyProperties(target.GetType()).Distinct())
            {
                var expression = target.GetBindingExpression(property);
                if (expression == null) continue;
                if (count == _bindingItemPool.Count) _bindingItemPool.Add(new DevToolsBindingItem());
                var item = _bindingItemPool[count];
                item.Update(target, property, expression);
                if (count == _bindingItems.Count) _bindingItems.Add(item);
                count++;
            }
        }
        else _bindingsView.SelectedElementName = string.Empty;
        while (_bindingItems.Count > count) _bindingItems.RemoveAt(_bindingItems.Count - 1);
    }

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
        if (_bindingsView == null) return;
        var entries = BindingDiagnostics.Snapshot();
        _bindingsView.EventCount = entries.Count;
        int count = Math.Min(entries.Count, BindingEventMaxRows);
        for (int i = 0; i < count; i++)
        {
            if (i == _bindingEventPool.Count) _bindingEventPool.Add(new DevToolsBindingEventItem());
            var item = _bindingEventPool[i];
            item.Update(entries[entries.Count - 1 - i]);
            if (i == _bindingEvents.Count) _bindingEvents.Add(item);
        }
        while (_bindingEvents.Count > count) _bindingEvents.RemoveAt(_bindingEvents.Count - 1);
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
                SetToolStatus($"Action failed: {ex.Message}", isError: true);
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
            SetToolStatus($"Saved XAML to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus($"Export failed: {ex.Message}", isError: true);
        }
    }

    private void CopyVisualXamlToClipboard(Visual visual)
    {
        try
        {
            string xaml = BuildXamlFromVisual(visual, recurse: true, 0);
            WpfClipboard.SetText(xaml);
            SetToolStatus("XAML copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetToolStatus($"Copy failed: {ex.Message}", isError: true);
        }
    }

    // ── Screenshot ───────────────────────────────────────────────────────

    private void SaveWholeWindowScreenshot()
    {
        try
        {
            if (!TryGetTargetScreenshotSize(out int w, out int h, out string? error))
            {
                SetToolStatus(error ?? "Target window is not capturable.", isError: true);
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
            SetToolStatus($"Saved to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus($"Screenshot failed: {ex.Message}", isError: true);
        }
    }

    private void SaveElementScreenshot(Visual visual)
    {
        if (visual is not UIElement ui)
        {
            SetToolStatus("Selected visual is not a UIElement.", isError: true);
            return;
        }
        try
        {
            if (!TryGetTargetScreenshotSize(out int w, out int h, out string? error))
            {
                SetToolStatus(error ?? "Target window is not capturable.", isError: true);
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
                SetToolStatus("Selected element is outside the target window or has zero size.", isError: true);
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
            SetToolStatus($"Saved element crop to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus($"Screenshot failed: {ex.Message}", isError: true);
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

    private void SetToolStatus(string message, bool isError = false)
    {
        if (_toolsView == null) return;
        _toolsView.LastError = isError ? message : string.Empty;
        if (!isError) _toolsView.StatusText = message;
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
        UpdateMutationButtons();
    }

    /// <summary>
    /// Restores the most-recently-deleted element and re-selects it. Invoked through
    /// <see cref="Defer"/>. The undo slot is consumed only after restoration succeeds;
    /// a transient container failure therefore remains retryable instead of silently
    /// discarding the user's only recovery action.
    /// </summary>
    private void UndoDelete()
    {
        var record = _deleteRecord;
        if (record == null) return;

        UIElement? revealed = record.Restore();
        _deleteRecord = null;
        RefreshVisualTree();

        if (revealed != null && revealed.VisualParent != null)
            RevealInInspector(revealed);

        UpdateMutationButtons();
    }

    #endregion

    #region EventsTab

    private DevToolsEventsView? _eventsView;
    private DispatcherTimer? _eventsRefreshTimer;
    private readonly ObservableCollection<DevToolsRoutedEventItem> _eventItems = new();
    private readonly Dictionary<RoutedEventDiagnostics.RoutedEventEntry, DevToolsRoutedEventItem> _eventItemByEntry = new(ReferenceEqualityComparer.Instance);
    private const int EventsMaxVisibleRows = 80;

    private UIElement BuildEventsTab()
    {
        _eventsView = new DevToolsEventsView
        {
            EventsSource = _eventItems,
            FilterText = string.Join(", ", RoutedEventDiagnostics.GetFilter()),
            ToggleRecordingCommand = new RelayCommand(_ =>
            {
                if (RoutedEventDiagnostics.IsRecording) { RoutedEventDiagnostics.StopRecording(); StopEventsRefreshTimer(); }
                else { RoutedEventDiagnostics.StartRecording(); StartEventsRefreshTimer(); }
                ReflectEventsRecordingState();
            }),
            ResetCommand = new RelayCommand(_ => { RoutedEventDiagnostics.Reset(); ResetEventsGraph(); }),
            ApplyFilterCommand = new RelayCommand(value => ApplyEventsFilter(value as string ?? string.Empty)),
            RevealNodeCommand = new RelayCommand(value =>
            {
                if (value is DevToolsEventNodeItem item && item.VisualReference?.TryGetTarget(out var visual) == true) RevealInInspector(visual);
            }, value => value is DevToolsEventNodeItem item && item.CanReveal && item.VisualReference?.TryGetTarget(out _) == true)
        };
        return _eventsView;
    }

    partial void OnEventsTabActivated()
    {
        ReflectEventsRecordingState(); RefreshEventsGraph();
        if (RoutedEventDiagnostics.IsRecording) StartEventsRefreshTimer();
    }

    private void StartEventsRefreshTimer()
    {
        if (_eventsRefreshTimer != null) return;
        _eventsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _eventsRefreshTimer.Tick += OnEventsRefreshTimerTick;
        _eventsRefreshTimer.Start();
    }
    private void OnEventsRefreshTimerTick(object? sender, EventArgs e)
    {
        ReflectEventsRecordingState(); RefreshEventsGraph();
        if (!RoutedEventDiagnostics.IsRecording) StopEventsRefreshTimer();
    }
    private void StopEventsRefreshTimer() { _eventsRefreshTimer?.Stop(); _eventsRefreshTimer = null; }
    private void ReflectEventsRecordingState() { if (_eventsView != null) _eventsView.IsRecording = RoutedEventDiagnostics.IsRecording; }
    private static void ApplyEventsFilter(string text) => RoutedEventDiagnostics.SetFilter(text
        .Split(new[] { ',', ';', ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(name => name.Trim()).Where(name => name.Length > 0));

    private void ResetEventsGraph()
    {
        _eventItemByEntry.Clear();
        if (_eventItems.Count > 0) _eventItems.Clear();
        if (_eventsView != null) _eventsView.EventCount = 0;
    }

    private void RefreshEventsGraph()
    {
        if (_eventsView == null) return;
        var entries = RoutedEventDiagnostics.Snapshot();
        _eventsView.EventCount = entries.Count;
        int count = Math.Min(entries.Count, EventsMaxVisibleRows);
        if (count == 0) { ResetEventsGraph(); return; }
        for (int i = 0; i < count; i++)
        {
            var entry = entries[entries.Count - 1 - i];
            if (!_eventItemByEntry.TryGetValue(entry, out var item))
            {
                item = new DevToolsRoutedEventItem(); _eventItemByEntry.Add(entry, item);
            }
            item.Update(entry);
            if (i < _eventItems.Count && ReferenceEquals(_eventItems[i], item)) continue;
            int previous = _eventItems.IndexOf(item);
            if (previous >= 0) _eventItems.Move(previous, i);
            else _eventItems.Insert(i, item);
        }
        while (_eventItems.Count > count)
        {
            var retired = _eventItems[^1];
            _eventItems.RemoveAt(_eventItems.Count - 1);
            if (retired.Source != null) _eventItemByEntry.Remove(retired.Source);
        }
    }

    #endregion

    #region LayoutTab

    private DevToolsLayoutProfiler? _layoutProfiler;
    private DispatcherTimer? _layoutRefreshTimer;
    private readonly ObservableCollection<DevToolsLayoutStatsItem> _layoutStats = new();
    private readonly ObservableCollection<DevToolsLayoutInvalidationItem> _layoutInvalidations = new();
    // Keep presentation models stable between snapshots. Realized containers and
    // their templates are retained by the virtualizing lists in the profiler.
    private readonly List<DevToolsLayoutStatsItem> _statsRowPool = new();
    private readonly List<DevToolsLayoutInvalidationItem> _invRowPool = new();
    private const int StatsTopN = 30;
    private const int InvalidationMaxRows = 120;

    private UIElement BuildLayoutTab()
    {
        var profiler = new DevToolsLayoutProfiler
        {
            StatsSource = _layoutStats,
            InvalidationSource = _layoutInvalidations,
            ToggleRecordingCommand = new RelayCommand(ToggleLayoutRecording),
            ResetCommand = new RelayCommand(() =>
            {
                LayoutDiagnostics.Reset();
                RefreshLayoutStats();
                RefreshInvalidations();
            }),
            ToggleStackCaptureCommand = new RelayCommand(() =>
            {
                LayoutDiagnostics.CaptureStackTraces = !LayoutDiagnostics.CaptureStackTraces;
                ReflectLayoutRecordingState();
            }),
            CycleSortCommand = new RelayCommand(() =>
            {
                if (_layoutProfiler is { } view)
                    view.SetCurrentValue(DevToolsLayoutProfiler.SortModeProperty,
                        (DevToolsLayoutSortMode)(((int)view.SortMode + 1) % 3));
            }),
            RevealCommand = new RelayCommand(parameter =>
            {
                if (parameter is DevToolsLayoutStatsItem { Source: { } source } &&
                    source.ElementRef.TryGetTarget(out var element))
                    RevealInInspector(element);
            }),
        };
        profiler.SortModeChanged += (_, _) => RefreshLayoutStats();
        _layoutProfiler = profiler;
        ReflectLayoutRecordingState();
        return profiler;
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
        _layoutRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _layoutRefreshTimer.Tick += (_, _) =>
        {
            // Template realization and new pooled models belong to DevTools,
            // and must never appear in the target window's layout samples.
            using var scope = DiagnosticsScope.BeginIgnoredCreation();
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
        if (_layoutProfiler is not { } view) return;
        view.IsRecording = LayoutDiagnostics.IsRecording;
        view.IsStackCaptureEnabled = LayoutDiagnostics.CaptureStackTraces;
    }

    private void RefreshLayoutStats()
    {
        if (_layoutProfiler is not { } view) return;
        var stats = LayoutDiagnostics.SnapshotStats();
        Func<LayoutDiagnostics.ElementStats, double> key = view.SortMode switch
        {
            DevToolsLayoutSortMode.Measure => s => s.MeasureTotalMicros,
            DevToolsLayoutSortMode.Arrange => s => s.ArrangeTotalMicros,
            _ => s => (double)s.InvalidateMeasureCount + s.InvalidateArrangeCount + s.InvalidateVisualCount,
        };
        var top = SelectTopN(stats, key, StatsTopN);
        double maximum = top.Count > 0 ? Math.Max(1, key(top[0])) : 1;
        while (_statsRowPool.Count < top.Count) _statsRowPool.Add(new DevToolsLayoutStatsItem());
        for (int i = 0; i < top.Count; i++)
            _statsRowPool[i].Update(top[i], i + 1, view.SortMode, maximum);
        while (_layoutStats.Count > top.Count) _layoutStats.RemoveAt(_layoutStats.Count - 1);
        while (_layoutStats.Count < top.Count) _layoutStats.Add(_statsRowPool[_layoutStats.Count]);
        view.FooterText = stats.Count > top.Count ? $"+{stats.Count - top.Count} more elements tracked" : string.Empty;
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

    private void RefreshInvalidations()
    {
        if (_layoutProfiler is not { } view) return;
        var entries = LayoutDiagnostics.SnapshotInvalidations();
        view.InvalidationCount = entries.Count;
        int count = Math.Min(entries.Count, InvalidationMaxRows);
        while (_invRowPool.Count < count) _invRowPool.Add(new DevToolsLayoutInvalidationItem());
        for (int i = 0; i < count; i++)
            _invRowPool[i].Update(entries[entries.Count - 1 - i]);
        while (_layoutInvalidations.Count > count) _layoutInvalidations.RemoveAt(_layoutInvalidations.Count - 1);
        while (_layoutInvalidations.Count < count) _layoutInvalidations.Add(_invRowPool[_layoutInvalidations.Count]);
    }

    #endregion

    #region PerfTab

    private DevToolsPerformanceView? _perfView;
    private DispatcherTimer? _perfRefreshTimer;
    private DevToolsPerformanceCapture? _perfLastCapture;
    private long _perfLastGraphStamp = -1;
    private int _perfLastGraphCount = -1;
    private readonly FrameHistory.Sample[] _perfSampleBuffer = new FrameHistory.Sample[FrameHistory.Capacity];
    private readonly System.Collections.ObjectModel.ObservableCollection<DevToolsPerformanceMetric> _perfMetrics = [];
    private readonly System.Collections.ObjectModel.ObservableCollection<DevToolsDrawApiItem> _perfApiItems = [];

    private UIElement BuildPerfTab()
    {
        _perfView = new DevToolsPerformanceView
        {
            MetricsSource = _perfMetrics, ApiSource = _perfApiItems,
            EngineCommand = new RelayCommand(value => { if (value is RenderingEngine engine) SwitchEngine(engine); }),
        };
        return _perfView;
    }

    partial void OnPerfTabActivated()
    {
        RenderDiagnostics.ApiStatsEnabled = true;
        RenderDiagnostics.StatsOwner = _targetWindow;
        _perfLastCapture = null; _perfLastGraphStamp = -1; _perfLastGraphCount = -1;
        RefreshPerfStats();
        if (_perfRefreshTimer == null)
        {
            _perfRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _perfRefreshTimer.Tick += (_, _) => RefreshPerfStats();
        }
        _perfRefreshTimer.Start();
    }

    private void OnPerfTabDeactivated()
    {
        _perfRefreshTimer?.Stop();
        if (ReferenceEquals(RenderDiagnostics.StatsOwner, _targetWindow))
        {
            RenderDiagnostics.ApiStatsEnabled = false;
            RenderDiagnostics.StatsOwner = null;
        }
    }

    private void SwitchEngine(RenderingEngine engine)
    {
        _targetWindow.SetRenderingEngineOverride(engine);
        RefreshPerfStats();
    }

    private void RefreshPerfStats()
    {
        if (_perfView == null) return;
        var backend = _targetWindow.CurrentRenderBackend;
        bool software = backend == RenderBackend.Software;
        _perfView.BackendText = backend.ToString();
        _perfView.ActualEngineText = software ? "CPU Raster" : _targetWindow.CurrentRenderingEngine.ToString();
        _perfView.IsEngineSwitchEnabled = backend != RenderBackend.Auto && !software;
        _perfView.HasAdapterWarning = false;
        if (software) _perfView.AdapterText = "CPU / system memory";
        else if (backend == RenderBackend.Auto) _perfView.AdapterText = "—";
        else
        {
            var adapter = Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(backend)?.GetAdapterInfo();
            if (adapter is { } info)
            {
                string kind = info.AdapterType switch
                {
                    Jalium.UI.Interop.GpuAdapterType.Discrete => "Discrete",
                    Jalium.UI.Interop.GpuAdapterType.Integrated => "iGPU",
                    Jalium.UI.Interop.GpuAdapterType.Software => "Software/WARP",
                    _ => "GPU",
                };
                _perfView.AdapterText = kind + ": " + info.Name;
                _perfView.HasAdapterWarning = info.AdapterType == Jalium.UI.Interop.GpuAdapterType.Software;
            }
            else _perfView.AdapterText = "—";
        }

        int count = _targetWindow.FrameHistory.CopyTo(_perfSampleBuffer);
        long stamp = count > 0 ? _perfSampleBuffer[count - 1].TimestampTicks : 0;
        bool framesChanged = stamp != _perfLastGraphStamp || count != _perfLastGraphCount;
        if (framesChanged) _perfView.SamplesSource = _perfSampleBuffer.AsSpan(0, count).ToArray();
        var samples = _perfView.SamplesSource ?? Array.Empty<FrameHistory.Sample>();
        _perfView.FrameSummary = DevToolsFrameSummary.Create(samples, Stopwatch.GetTimestamp(), _perfView.FrameBudgetMs);
        var capture = DevToolsPerformanceCapture.Read();
        if (framesChanged || capture != _perfLastCapture) DevToolsPerformanceSnapshotBuilder.UpdateRenderer(_perfMetrics, samples, capture);
        if (!ReferenceEquals(capture.Api, _perfLastCapture?.Api))
        {
            DevToolsPerformanceSnapshotBuilder.UpdateApi(_perfApiItems, capture.Api);
            _perfView.ApiTimeText = capture.Api == null ? "—" : capture.Api.Timestamp.ToString("HH:mm:ss.fff");
            _perfView.ApiTotalText = capture.Api == null ? "—" : $"{capture.Api.TotalMs:F2} ms total";
        }
        _perfLastCapture = capture; _perfLastGraphStamp = stamp; _perfLastGraphCount = count;
    }

    #endregion

    #region ReplTab

    private DevToolsReplView? _replView;
    private readonly Dictionary<string, object?> _replLocals = new(StringComparer.Ordinal);

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates user input via reflection-based runtime member resolution.")]
    private UIElement BuildReplTab()
    {
        _replView = new DevToolsReplView { ExecuteCommand = new RelayCommand(code => RunReplLine(code as string ?? string.Empty)) };
        return _replView;
    }

    partial void OnReplTabActivated()
    {
        _replLocals["window"] = _targetWindow;
        _replLocals["app"] = Application.Current;
        _replLocals["root"] = _targetWindow;
        _replView?.FocusInput();
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates expressions and assigns to runtime-resolved members via reflection.")]
    private void RunReplLine(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || _replView == null) return;

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
                    _replView.WriteResult(FormatReplValue(result));
                }
                catch (Exception ex)
                {
                    _replView.WriteError(ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _replView.WriteError("Parse error: " + ex.Message);
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

    private DevToolsResourcesView? _resourcesView;
    private DispatcherTimer? _resourcesSearchTimer;
    private readonly ObservableCollection<DevToolsResourceItem> _resourceItems = new();
    private readonly Dictionary<DevToolsResourceRowIdentity, DevToolsResourceItem> _resourceItemByIdentity = new();

    private UIElement BuildResourcesTab()
    {
        _resourcesView = new DevToolsResourcesView
        {
            ItemsSource = _resourceItems,
            RefreshCommand = new RelayCommand(_ => RefreshResourcesChain()),
            ToggleWinnersOnlyCommand = new RelayCommand(_ =>
            {
                if (_resourcesView == null) return;
                _resourcesView.SetCurrentValue(DevToolsResourcesView.IsWinnersOnlyProperty, !_resourcesView.IsWinnersOnly);
                RefreshResourcesChain();
            })
        };
        _resourcesView.FilterChanged += OnResourcesFilterChanged;
        return _resourcesView;
    }

    partial void OnResourcesTabActivated() => RefreshResourcesChain();

    private void OnResourcesFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _resourcesView) || _workspace?.SelectedPage != DevToolsPage.Resources) return;
        if (_resourcesSearchTimer == null)
        {
            _resourcesSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _resourcesSearchTimer.Tick += OnResourcesSearchTick;
        }
        _resourcesSearchTimer.Stop(); _resourcesSearchTimer.Start();
    }
    private void OnResourcesSearchTick(object? sender, EventArgs e) => RefreshResourcesChain();
    private void StopResourcesRefreshTimer() { _resourcesSearchTimer?.Stop(); _resourcesSearchTimer = null; }

    private void RefreshResourcesChain()
    {
        StopResourcesRefreshTimer();
        if (_resourcesView == null) return;
        var snapshot = DevToolsResourceSnapshot.Capture(_selectedVisual as FrameworkElement ?? _targetWindow,
            Application.Current, _resourcesView.FilterText, _resourcesView.IsWinnersOnly);
        var retained = new HashSet<DevToolsResourceRowIdentity>();
        for (int index = 0; index < snapshot.Rows.Count; index++)
        {
            var data = snapshot.Rows[index]; retained.Add(data.Identity);
            if (!_resourceItemByIdentity.TryGetValue(data.Identity, out var item))
            {
                item = new DevToolsResourceItem(); _resourceItemByIdentity.Add(data.Identity, item);
            }
            item.Update(data);
            if (index < _resourceItems.Count && ReferenceEquals(_resourceItems[index], item)) continue;
            int previous = _resourceItems.IndexOf(item);
            if (previous >= 0) _resourceItems.Move(previous, index);
            else _resourceItems.Insert(index, item);
        }
        while (_resourceItems.Count > snapshot.Rows.Count) _resourceItems.RemoveAt(_resourceItems.Count - 1);
        foreach (var retired in _resourceItemByIdentity.Keys.Where(identity => !retained.Contains(identity)).ToArray())
            _resourceItemByIdentity.Remove(retired);
        _resourcesView.ScopeCount = snapshot.ScopeCount;
        _resourcesView.DeclarationCount = snapshot.DeclarationCount;
        _resourcesView.WinnerCount = snapshot.WinnerCount;
    }

    #endregion

    #region StyleTemplateXaml

    private void AppendStyleXamlViewer(Style style)
    {
        AddRevealXamlButton("View style XAML", () => BuildStyleXaml(style));
    }

    private void AppendTemplateXamlViewer(FrameworkElement fe)
    {
        if (fe is Control ctrl && ctrl.Template != null)
            AddRevealXamlButton("View ControlTemplate XAML", () => BuildTemplateXaml(ctrl));
    }

    private void AddRevealXamlButton(string label, Func<string> xamlProvider)
    {
        var button = new DevToolsActionButton { Label = label, Margin = new Thickness(8,4,8,4), HorizontalAlignment = HorizontalAlignment.Left };
        // ButtonBase evaluates CanExecute when the command is assigned. Attach
        // first so the initial ownership check enables this current action.
        _propertiesPanel.Items.Add(button);
        button.Command = new RelayCommand(() =>
        {
            try { ShowXamlPopup(label, xamlProvider()); }
            catch (Exception error) { ShowXamlPopup(label, $"(failed to render XAML: {error.Message})"); }
        }, () => !_isClosing && _propertiesPanel.Items.Contains(button));
    }

    private void ShowXamlPopup(string title, string xaml)
    {
        new Window
        {
            Title = "DevTools — " + title, Width = 640, Height = 520,
            SystemBackdrop = WindowBackdropType.Mica, Background = DevToolsTheme.Chrome,
            Content = new DevToolsXamlView { Text = xaml }
        }.Show();
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

    private DevToolsWorkspace? _workspace;

    // Legacy brush aliases (read from the central theme so existing partial
    // files continue to compile).

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools tab layout includes the REPL which evaluates user input via reflection.")]
    private UIElement BuildTabLayout()
    {
        _workspace = new DevToolsWorkspace
        {
            InspectorContent = _inspector,
            LayoutContent = BuildLayoutTab(),
            EventsContent = BuildEventsTab(),
            BindingsContent = BuildBindingsTab(),
            ResourcesContent = BuildResourcesTab(),
            PerformanceContent = BuildPerfTab(),
            AutomationContent = BuildUiaTab(),
            ToolsContent = BuildToolsTab(),
            ReplContent = BuildReplTab(),
        };
        _workspace.SelectedPageChanged += OnWorkspacePageChanged;
        return _workspace;
    }

    private void OnWorkspacePageChanged(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _workspace) || e is not DevToolsPageChangedEventArgs change) return;
        using var scope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();
        DeactivateTab(change.OldPage);
        switch (change.NewPage)
        {
            case DevToolsPage.Layout: OnLayoutTabActivated(); break;
            case DevToolsPage.Events: OnEventsTabActivated(); break;
            case DevToolsPage.Bindings: OnBindingsTabActivated(); break;
            case DevToolsPage.Performance: OnPerfTabActivated(); break;
            case DevToolsPage.Automation: OnUiaTabActivated(); break;
            case DevToolsPage.Resources: OnResourcesTabActivated(); break;
            case DevToolsPage.Tools: OnToolsTabActivated(); break;
            case DevToolsPage.Repl: OnReplTabActivated(); break;
        }
    }

    private void DeactivateTab(DevToolsPage page)
    {
        switch (page)
        {
            case DevToolsPage.Performance: OnPerfTabDeactivated(); break;
            case DevToolsPage.Layout: StopLayoutRefreshTimer(); break;
            case DevToolsPage.Events: StopEventsRefreshTimer(); break;
            case DevToolsPage.Bindings: StopBindingsRefreshTimer(); break;
            case DevToolsPage.Resources: StopResourcesRefreshTimer(); break;
            case DevToolsPage.Automation: StopUiaBuild(); break;
            case DevToolsPage.Tools: StopToolsInteractions(); break;
        }
    }

    /// <summary>
    /// Stops every background refresh this window owns. Closing used to stop only the
    /// search timer, so a DevTools window that had visited Perf / Layout / Events kept
    /// its timers alive after being closed — each tick still rebuilding UI for a dead
    /// window, and the static CompositionTarget / timer callbacks kept the whole
    /// DevTools element tree (and the target window it references) rooted for good.
    /// </summary>
    private void StopAllRefreshTimers()
    {
        _searchRefreshTimer.Stop();
        _perfRefreshTimer?.Stop();
        StopResourcesRefreshTimer();
        StopUiaBuild(cancel: true);
        StopToolsInteractions();
        StopLayoutRefreshTimer();
        StopEventsRefreshTimer();
        StopBindingsRefreshTimer();
        OnPerfTabDeactivated();
    }

    private static DevToolsSurface MakeTabShell(UIElement content) => new() { Kind = DevToolsSurfaceKind.TabShell, Content = content };

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

    private DevToolsToolsView? _toolsView;
    private RelayCommand? _toolsCommand;
    private bool _rulerActive;
    private Point? _rulerStart;
    private bool _rulerCommitted;
    private bool _colorPickerActive;
    private bool _focusOverlayEnabled;

    private UIElement BuildToolsTab()
    {
        _toolsCommand = new RelayCommand(ExecuteToolRequest, request =>
            !_isClosing && !IsCloseRequestedForPlatformTermination && _workspace?.SelectedPage == DevToolsPage.Tools && request is DevToolsToolRequest);
        return _toolsView = new DevToolsToolsView
        {
            ActionCommand = _toolsCommand,
            OverlayMode = RenderDiagnostics.Mode,
        };
    }

    partial void OnToolsTabActivated()
    {
        if (_toolsView != null) _toolsView.OverlayMode = RenderDiagnostics.Mode;
        _toolsCommand?.RaiseCanExecuteChanged();
    }

    private void ExecuteToolRequest(object? parameter)
    {
        if (parameter is not DevToolsToolRequest request) return;
        switch (request.Action)
        {
            case DevToolsToolAction.ToggleRuler: ToggleRuler(); break;
            case DevToolsToolAction.ToggleColorPicker: ToggleColorPicker(); break;
            case DevToolsToolAction.ToggleOverdraw: ToggleOverlayMode(RenderDiagnostics.OverlayMode.Overdraw); break;
            case DevToolsToolAction.ToggleDirtyRegions: ToggleOverlayMode(RenderDiagnostics.OverlayMode.DirtyRegions); break;
            case DevToolsToolAction.ToggleFocus: ToggleFocusOverlay(); break;
            case DevToolsToolAction.CopyArgb:
                if (request.Parameter is string text) { WpfClipboard.SetText(text); SetToolStatus("Copied ARGB color"); }
                break;
        }
    }

    private void SyncToolMode()
    {
        if (_toolsView != null)
            _toolsView.ToolMode = _rulerActive ? DevToolsToolMode.Ruler : _colorPickerActive ? DevToolsToolMode.ColorPicker : DevToolsToolMode.None;
    }

    private void StopToolsInteractions()
    {
        DeactivateRuler(); DeactivateColorPicker();
        if (_focusOverlayEnabled) ToggleFocusOverlay();
        _toolsCommand?.RaiseCanExecuteChanged();
    }

    private void OnToolTargetPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((!_rulerActive && !_colorPickerActive) || e.Key != Key.Escape) return;
        DeactivateRuler(); DeactivateColorPicker(); e.Handled = true;
    }

    private void ToggleRuler() { if (_rulerActive) DeactivateRuler(); else ActivateRuler(); }

    private void ActivateRuler()
    {
        if (_rulerActive) return;
        DeactivateColorPicker();
        if (_isPickerActive) DeactivatePicker();
        _rulerActive = true; _rulerStart = null; _rulerCommitted = false;
        _targetWindow.PreviewMouseDown += OnRulerTargetMouseDown;
        _targetWindow.PreviewMouseMove += OnRulerTargetMouseMove;
        _targetWindow.PreviewKeyDown += OnToolTargetPreviewKeyDown;
        _overlay?.BeginRuler();
        if (_toolsView != null)
        {
            _toolsView.Measurement = null; _toolsView.HasRulerError = false;
            _toolsView.RulerStatusText = "Choose the first point";
        }
        SyncToolMode();
    }

    private void DeactivateRuler()
    {
        if (!_rulerActive) return;
        _rulerActive = false; _rulerStart = null; _rulerCommitted = false;
        _targetWindow.PreviewMouseDown -= OnRulerTargetMouseDown;
        _targetWindow.PreviewMouseMove -= OnRulerTargetMouseMove;
        _targetWindow.PreviewKeyDown -= OnToolTargetPreviewKeyDown;
        _overlay?.EndRuler();
        if (_toolsView != null) { _toolsView.HasRulerError = false; _toolsView.RulerStatusText = "Ruler stopped"; }
        SyncToolMode();
    }

    private void OnRulerTargetMouseMove(object? sender, RoutedEventArgs e)
    {
        if (!_rulerActive || _rulerStart == null || _rulerCommitted || e is not MouseEventArgs mouse) return;
        var raw = mouse.GetPosition(_targetWindow);
        if (!DevToolsRulerMeasurement.IsFinite(raw)) return;
        var point = raw;
        if ((mouse.KeyboardModifiers & ModifierKeys.Shift) != 0)
        {
            if (!DevToolsRulerMeasurement.TrySnap(_rulerStart.Value, raw, out var snapped)) return;
            point = snapped;
        }
        _overlay?.SetRulerPreviewEnd(point);
    }

    private void OnRulerTargetMouseDown(object? sender, RoutedEventArgs e)
    {
        if (!_rulerActive || e is not MouseButtonEventArgs mouse || mouse.ChangedButton != MouseButton.Left) return;
        mouse.Handled = true;
        var raw = mouse.GetPosition(_targetWindow);
        if (!DevToolsRulerMeasurement.IsFinite(raw))
        {
            if (_toolsView != null) { _toolsView.HasRulerError = true; _toolsView.RulerStatusText = "Invalid coordinates"; }
            return;
        }
        if (_rulerStart == null || _rulerCommitted)
        {
            _rulerStart = raw; _rulerCommitted = false; _overlay?.SetRulerStart(raw);
            if (_toolsView != null)
            {
                _toolsView.Measurement = null; _toolsView.HasRulerError = false;
                _toolsView.RulerStatusText = "Choose the second point · Shift to snap";
            }
            return;
        }
        bool snap = (mouse.KeyboardModifiers & ModifierKeys.Shift) != 0;
        var end = raw;
        if (snap)
        {
            if (!DevToolsRulerMeasurement.TrySnap(_rulerStart.Value, raw, out var snapped))
            {
                if (_toolsView != null) { _toolsView.HasRulerError = true; _toolsView.RulerStatusText = "Cannot snap measurement"; }
                return;
            }
            end = snapped;
        }
        if (!DevToolsRulerMeasurement.TryCreate(_rulerStart.Value, end, snap, out var measurement))
        {
            if (_toolsView != null) { _toolsView.HasRulerError = true; _toolsView.RulerStatusText = "Measurement unavailable"; }
            return;
        }
        _rulerCommitted = true; _overlay?.CommitRulerEnd(end);
        if (_toolsView != null)
        {
            _toolsView.Measurement = measurement; _toolsView.HasRulerError = false;
            _toolsView.RulerStatusText = "Measured · choose another point to restart";
        }
    }

    private void ToggleColorPicker() { if (_colorPickerActive) DeactivateColorPicker(); else ActivateColorPicker(); }

    private void ActivateColorPicker()
    {
        if (_colorPickerActive) return;
        DeactivateRuler();
        if (_isPickerActive) DeactivatePicker();
        _colorPickerActive = true;
        _targetWindow.PreviewMouseDown += OnColorPickerTargetMouseDown;
        _targetWindow.PreviewKeyDown += OnToolTargetPreviewKeyDown;
        if (_toolsView != null)
        {
            _toolsView.ColorSample = null; _toolsView.HasPickerError = false;
            _toolsView.PickerStatusText = "Choose a pixel in the target";
        }
        SyncToolMode();
    }

    private void DeactivateColorPicker()
    {
        if (!_colorPickerActive) return;
        _colorPickerActive = false;
        _targetWindow.PreviewMouseDown -= OnColorPickerTargetMouseDown;
        _targetWindow.PreviewKeyDown -= OnToolTargetPreviewKeyDown;
        if (_toolsView != null) { _toolsView.HasPickerError = false; _toolsView.PickerStatusText = "Color picker stopped"; }
        SyncToolMode();
    }

    private void OnColorPickerTargetMouseDown(object? sender, RoutedEventArgs e)
    {
        if (!_colorPickerActive || e is not MouseButtonEventArgs mouse || mouse.ChangedButton != MouseButton.Left) return;
        mouse.Handled = true;
        try
        {
            var position = mouse.GetPosition(_targetWindow);
            if (!DevToolsRulerMeasurement.IsFinite(position))
            {
                if (_toolsView != null) { _toolsView.HasPickerError = true; _toolsView.PickerStatusText = "Invalid coordinates"; }
                return;
            }
            var color = PickScreenPixel(position);
            if (color.HasValue && DevToolsColorSample.TryCreate(color.Value, position, out var sample))
            {
                if (_toolsView != null)
                {
                    _toolsView.ColorSample = sample; _toolsView.HasPickerError = false;
                    _toolsView.PickerStatusText = "Sampled · choose another pixel to update";
                }
            }
            else if (_toolsView != null) { _toolsView.HasPickerError = true; _toolsView.PickerStatusText = "Sampling unavailable"; }
        }
        catch (Exception error)
        {
            if (_toolsView != null) { _toolsView.HasPickerError = true; _toolsView.PickerStatusText = error.Message; }
        }
    }

    private void ToggleOverlayMode(RenderDiagnostics.OverlayMode mode)
    {
        RenderDiagnostics.Mode = RenderDiagnostics.Mode == mode ? RenderDiagnostics.OverlayMode.None : mode;
        if (_toolsView != null) _toolsView.OverlayMode = RenderDiagnostics.Mode;
        _targetWindow.RequestFullInvalidation(); _targetWindow.InvalidateWindow();
    }

    private void ToggleFocusOverlay()
    {
        _focusOverlayEnabled = !_focusOverlayEnabled;
        var handler = new KeyboardFocusChangedEventHandler(OnToolsTargetFocusChanged);
        if (_focusOverlayEnabled)
        {
            _targetWindow.AddHandler(FocusService.GotKeyboardFocusEvent, handler, true);
            _targetWindow.AddHandler(FocusService.LostKeyboardFocusEvent, handler, true);
            _targetWindow.Activated += OnToolsTargetActivated;
            _targetWindow.Deactivated += OnToolsTargetDeactivated;
            UpdateFocusOverlay(FocusService.FocusedElement as UIElement);
        }
        else
        {
            _targetWindow.RemoveHandler(FocusService.GotKeyboardFocusEvent, handler);
            _targetWindow.RemoveHandler(FocusService.LostKeyboardFocusEvent, handler);
            _targetWindow.Activated -= OnToolsTargetActivated;
            _targetWindow.Deactivated -= OnToolsTargetDeactivated;
            UpdateFocusOverlay(null);
        }
        if (_toolsView != null) _toolsView.IsFocusTracking = _focusOverlayEnabled;
    }

    private void OnToolsTargetFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_focusOverlayEnabled && !_isClosing) UpdateFocusOverlay(e.NewFocus as UIElement);
    }
    private void OnToolsTargetActivated(object? sender, EventArgs e)
    {
        if (_focusOverlayEnabled && !_isClosing) UpdateFocusOverlay(FocusService.FocusedElement as UIElement);
    }
    private void OnToolsTargetDeactivated(object? sender, EventArgs e)
    {
        if (_focusOverlayEnabled && !_isClosing) UpdateFocusOverlay(null);
    }

    private void UpdateFocusOverlay(UIElement? focused)
    {
        if (focused != null && !InputRoute.IsWithin(focused, _targetWindow)) focused = null;
        _overlay?.HighlightElement(focused);
        if (_toolsView != null)
            _toolsView.FocusedElementText = focused == null ? "No focused element" : focused is FrameworkElement { Name.Length: > 0 } named ? $"{focused.GetType().Name} · {named.Name}" : focused.GetType().Name;
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

    // Native capture helpers shared with Inspector actions.

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
            Jalium.UI.Interop.Win32.Win32GdiMethods.DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(hwnd, windowDc);
        }
    }

    private static void WritePngFromBgra(string path, byte[] bgra, int width, int height)
    {
        var source = BitmapSource.Create(
            width, height, 96, 96, PixelFormat.Bgra32, null, bgra, checked(width * 4));
        var encoder = new PngBitmapEncoder { Frames = { BitmapFrame.Create(source) } };
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    #endregion

    #region UiaTab

    private DevToolsAutomationView? _uiaView;
    private readonly ObservableCollection<DevToolsAutomationItem> _uiaRoots = new();
    private DevToolsAutomationTreeBuilder? _uiaBuilder;
    private DispatcherTimer? _uiaBuildTimer;
    private bool _uiaTreeBuilt;
    private bool _uiaSettingSelection;
    private object? _uiaRestoreIdentity;

    private UIElement BuildUiaTab()
    {
        _uiaView = new DevToolsAutomationView { ItemsSource = _uiaRoots, RefreshCommand = new RelayCommand(_ => BuildUiaTree()) };
        _uiaView.SelectedItemChanged += OnUiaSelectionChanged;
        return _uiaView;
    }
    partial void OnUiaTabActivated()
    {
        if (!_uiaTreeBuilt) BuildUiaTree();
        else ScheduleUiaBuild();
    }
    private void BuildUiaTree()
    {
        if (_uiaView == null) return;
        StopUiaBuild(); _uiaBuilder?.Cancel();
        var expansion = new Dictionary<object, bool>();
        var pending = new Stack<DevToolsAutomationItem>(_uiaRoots);
        var visited = new HashSet<DevToolsAutomationItem>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            if (node.Identity != null) expansion[node.Identity] = node.IsExpanded;
            foreach (var child in node.Children) pending.Push(child);
        }
        var selectionIdentity = _uiaView.SelectedItem?.Identity;
        _uiaRoots.Clear(); _uiaTreeBuilt = true;
        _uiaRestoreIdentity = selectionIdentity;
        var peer = _targetWindow.GetAutomationPeer();
        if (peer == null)
        {
            _uiaBuilder = null; _uiaView.NodeCount = 0; _uiaView.IssueCount = 0;
            _uiaView.IsBuilding = false; _uiaView.StatusText = "No provider"; return;
        }
        _uiaBuilder = new DevToolsAutomationTreeBuilder(peer, expansion, _uiaRestoreIdentity);
        _uiaRoots.Add(_uiaBuilder.Root); SetUiaSelection(_uiaBuilder.Root);
        UpdateUiaBuildState(); ScheduleUiaBuild();
    }
    private void SetUiaSelection(DevToolsAutomationItem item)
    {
        _uiaSettingSelection = true;
        try { _uiaView?.SetCurrentValue(DevToolsAutomationView.SelectedItemProperty, item); }
        finally { _uiaSettingSelection = false; }
    }
    private void OnUiaSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _uiaView)) return;
        if (!_uiaSettingSelection) _uiaRestoreIdentity = null;
        if (_uiaView?.SelectedItem is { } item) DevToolsAutomationTreeBuilder.RefreshDetails(item);
    }
    private void ScheduleUiaBuild()
    {
        if (_uiaBuilder?.IsBuilding != true || _workspace?.SelectedPage != DevToolsPage.Automation) return;
        if (_uiaBuildTimer == null)
        {
            _uiaBuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _uiaBuildTimer.Tick += OnUiaBuildTimerTick;
        }
        _uiaBuildTimer.Start();
    }
    private void OnUiaBuildTimerTick(object? sender, EventArgs e)
    {
        StopUiaBuild();
        if (_workspace?.SelectedPage != DevToolsPage.Automation) return;
        _uiaBuilder?.ProcessBatch(); UpdateUiaBuildState(); ScheduleUiaBuild();
    }
    private void UpdateUiaBuildState()
    {
        if (_uiaView == null || _uiaBuilder == null) return;
        _uiaView.NodeCount = _uiaBuilder.NodeCount; _uiaView.IssueCount = _uiaBuilder.IssueCount;
        _uiaView.IsBuilding = _uiaBuilder.IsBuilding;
        _uiaView.StatusText = _uiaBuilder.IsBuilding ? "Reading peers" : _uiaBuilder.IsLimited ? "Limit reached" : "Ready";
        if (_uiaRestoreIdentity != null && ReferenceEquals(_uiaRestoreIdentity, _uiaBuilder.SelectedItem?.Identity))
        {
            SetUiaSelection(_uiaBuilder.SelectedItem!); _uiaRestoreIdentity = null;
        }
    }
    private void StopUiaBuild(bool cancel = false)
    {
        _uiaBuildTimer?.Stop();
        if (!cancel) return;
        _uiaBuilder?.Cancel(); _uiaBuilder = null; _uiaTreeBuilt = false;
        _uiaRestoreIdentity = null; _uiaRoots.Clear();
        if (_uiaView != null) { _uiaView.SelectedItem = null; _uiaView.IsBuilding = false; }
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
