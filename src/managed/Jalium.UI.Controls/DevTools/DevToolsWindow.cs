using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

/// <summary>
/// Developer tools window for inspecting the visual tree and element properties.
/// Features: syntax-highlighted values, color swatches, inline editing, font preview,
/// toolbar, element picker, search, breadcrumb, box model, grid/style/resource/binding inspectors.
/// </summary>
public class DevToolsWindow : Window
{
    private const int SearchRefreshDelayMilliseconds = 150;
    private const int TreeBuildNodeBatchSize = 8;
    private const int TreeBuildChildBatchSize = 48;

    private readonly Window _targetWindow;
    private readonly Grid _mainGrid;
    private readonly TreeView _visualTreeView;
    private readonly StackPanel _propertiesPanel;
    private readonly UIElement _propertiesScrollViewer;
    private readonly TextBox _searchTextBox;
    private readonly DispatcherTimer _searchRefreshTimer;
    private readonly DispatcherTimer _treeBuildTimer;
    private Visual? _selectedVisual;
    private DevToolsOverlay? _overlay;
    private int _rowIndex;

    // Toolbar state
    private bool _isPickerActive;
    private Border? _pickerButton;

    // All tree items for search/expand/collapse
    private readonly List<DevToolsTreeViewItem> _allTreeItems = new();
    private readonly Queue<PendingTreeBuildNode> _pendingTreeBuild = new();

    // 鈹€鈹€ Syntax highlighting palette (VS Code Dark+ inspired) 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
    private static readonly SolidColorBrush BrushString = new(Color.FromRgb(206, 145, 120));     // #CE9178
    private static readonly SolidColorBrush BrushNumber = new(Color.FromRgb(181, 206, 168));     // #B5CEA8
    private static readonly SolidColorBrush BrushBool = new(Color.FromRgb(86, 156, 214));        // #569CD6
    private static readonly SolidColorBrush BrushEnum = new(Color.FromRgb(220, 220, 170));       // #DCDCAA
    private static readonly SolidColorBrush BrushNull = new(Color.FromRgb(128, 128, 128));       // #808080
    private static readonly SolidColorBrush BrushThickness = new(Color.FromRgb(78, 201, 176));   // #4EC9B0
    private static readonly SolidColorBrush BrushPropName = new(Color.FromRgb(156, 220, 254));   // #9CDCFE
    private static readonly SolidColorBrush BrushSection = new(Color.FromRgb(78, 201, 176));     // #4EC9B0
    private static readonly SolidColorBrush BrushType = new(Color.FromRgb(86, 156, 214));        // #569CD6
    private static readonly SolidColorBrush BrushKeyword = new(Color.FromRgb(197, 134, 192));    // #C586C0
    private static readonly SolidColorBrush BrushEditBg = new(Color.FromRgb(30, 30, 30));
    private static readonly SolidColorBrush BrushEditBorder = new(Color.FromRgb(60, 60, 80));
    private static readonly SolidColorBrush BrushSwatchBorder = new(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush BrushRowAlt = new(Color.FromRgb(36, 36, 36));
    private static readonly SolidColorBrush BrushToolbarBg = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush BrushToolbarBorder = new(Color.FromRgb(63, 63, 70));
    private static readonly SolidColorBrush BrushAccent = new(Color.FromRgb(0, 120, 215));
    private static readonly SolidColorBrush BrushBreadcrumbSep = new(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush BrushBoxMargin = new(Color.FromArgb(180, 255, 180, 100));
    private static readonly SolidColorBrush BrushBoxBorder = new(Color.FromArgb(180, 255, 220, 100));
    private static readonly SolidColorBrush BrushBoxPadding = new(Color.FromArgb(180, 140, 200, 140));
    private static readonly SolidColorBrush BrushBoxContent = new(Color.FromArgb(180, 100, 160, 220));
    private static readonly SolidColorBrush BrushBoxLabel = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush BrushCategoryFramework = new(Color.FromRgb(94, 196, 255));
    private static readonly SolidColorBrush BrushCategoryLayout = new(Color.FromRgb(152, 195, 121));
    private static readonly SolidColorBrush BrushCategoryAppearance = new(Color.FromRgb(255, 178, 102));
    private static readonly SolidColorBrush BrushCategoryTypography = new(Color.FromRgb(244, 166, 232));
    private static readonly SolidColorBrush BrushCategoryContent = new(Color.FromRgb(97, 175, 239));
    private static readonly SolidColorBrush BrushCategoryItems = new(Color.FromRgb(86, 182, 194));
    private static readonly SolidColorBrush BrushCategoryData = new(Color.FromRgb(229, 192, 123));
    private static readonly SolidColorBrush BrushCategoryInput = new(Color.FromRgb(224, 108, 117));
    private static readonly SolidColorBrush BrushCategoryBehavior = new(Color.FromRgb(198, 120, 221));
    private static readonly SolidColorBrush BrushCategoryState = new(Color.FromRgb(163, 190, 140));
    private static readonly SolidColorBrush BrushCategoryOther = new(Color.FromRgb(171, 178, 191));
    private const double NameWidth = 145;
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<DependencyPropertyInspectorEntry>> s_dependencyPropertyCache = new();

    private sealed record DependencyPropertyInspectorEntry(DependencyProperty Property, DevToolsPropertyCategory Category);

    protected override bool CanOpenDevTools => false;

    public DevToolsWindow(Window targetWindow)
    {
        _targetWindow = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));

        Title = $"DevTools - {targetWindow.Title}";
        Width = 780;
        Height = 820;
        SystemBackdrop = WindowBackdropType.Mica;
        Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));

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
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            BorderBrush = BrushToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4, 6, 4)
        };
        _searchTextBox.TextChanged += OnSearchTextChanged;
        Grid.SetRow(_searchTextBox, 0);
        leftGrid.Children.Add(_searchTextBox);

        var treeView = new TreeView
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            Margin = new Thickness(4)
        };
        treeView.SelectedItemChanged += OnVisualTreeSelectionChanged;
        Grid.SetRow(treeView, 1);
        leftGrid.Children.Add(treeView);

        var splitter = new GridSplitter
        {
            Width = 6,
            Margin = new Thickness(0, 4, 0, 4),
            Background = new SolidColorBrush(Color.FromArgb(255, 64, 64, 64)),
            ResizeDirection = GridResizeDirection.Columns
        };
        Grid.SetColumn(splitter, 1);
        contentGrid.Children.Add(splitter);
        // 鈹€鈹€ Right column: properties panel 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
        var propertiesPanel = new StackPanel
        {
            Margin = new Thickness(4)
        };
        var scrollViewer = new ScrollViewer
        {
            Content = propertiesPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var rightBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40)),
            Child = scrollViewer,
            ClipToBounds = true
        };
        Grid.SetColumn(rightBorder, 2);
        contentGrid.Children.Add(rightBorder);
        Content = _mainGrid;

        _visualTreeView = treeView;
        _propertiesScrollViewer = scrollViewer;
        _propertiesPanel = propertiesPanel;

        // Add placeholder text so the right pane has initial content
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = "Select an element to inspect",
            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            FontSize = 12,
            Margin = new Thickness(8, 16, 8, 8)
        });

        _searchRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SearchRefreshDelayMilliseconds)
        };
        _searchRefreshTimer.Tick += OnSearchRefreshTimerTick;

        _treeBuildTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _treeBuildTimer.Tick += OnTreeBuildTimerTick;

        RefreshVisualTree();

        _overlay = new DevToolsOverlay(_targetWindow);
        _targetWindow.DevToolsOverlay = _overlay;

        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        Loaded += OnDevToolsLoaded;
        Closing += OnDevToolsClosing;
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Toolbar
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private Border CreateToolbar()
    {
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 2, 4, 2)
        };

        toolbar.Children.Add(MakeToolbarButton("\u21bb Refresh", "F5", OnRefreshClick));

        _pickerButton = MakeToolbarButton("\u2316 Pick", "Ctrl+Shift+C", OnPickerClick);
        toolbar.Children.Add(_pickerButton);

        toolbar.Children.Add(MakeToolbarButton("\u25bc Expand", "Ctrl+E", OnExpandAllClick));
        toolbar.Children.Add(MakeToolbarButton("\u25b6 Collapse", "Ctrl+Shift+E", OnCollapseAllClick));
        toolbar.Children.Add(MakeToolbarButton("\u2398 Copy", "Ctrl+C", OnCopyClick));

        return new Border
        {
            Background = BrushToolbarBg,
            BorderBrush = BrushToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar
        };
    }

    private static Border MakeToolbarButton(string label, string shortcut, RoutedEventHandler handler)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        inner.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 0, 4, 0)
        });
        inner.Children.Add(new TextBlock
        {
            Text = shortcut,
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
        });

        var btn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(3)
        };
        btn.Child = inner;
        btn.MouseDown += (s, e) => handler(s, e);
        return btn;
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
            _pickerButton.BorderBrush = BrushAccent;

        _targetWindow.PreviewMouseMove += OnTargetPreviewMouseMove;
        _targetWindow.PreviewMouseDown += OnTargetPreviewMouseDown;
    }

    private void DeactivatePicker()
    {
        _isPickerActive = false;
        if (_pickerButton != null)
            _pickerButton.BorderBrush = null;

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
        {
            SelectVisualInTree(hit);
            _selectedVisual = hit;
            UpdatePropertiesPanel(hit);
            _overlay?.HighlightElement(hit as UIElement);
        }

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

            // Skip OverlayLayer 鈥?it covers the entire window and would always be hit
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
    /// Selects the tree item corresponding to the given visual.
    /// </summary>
    private void SelectVisualInTree(Visual visual)
    {
        foreach (var item in _allTreeItems)
        {
            if (item.Visual == visual)
            {
                // Expand ancestors
                ExpandAncestors(item);
                item.IsSelected = true;
                return;
            }
        }
    }

    private static void ExpandAncestors(TreeViewItem item)
    {
        var parent = item.VisualParent;
        while (parent != null)
        {
            if (parent is TreeViewItem parentItem)
            {
                parentItem.IsExpanded = true;
            }
            parent = parent.VisualParent;
        }
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Search / Filter
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        var filter = _searchTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(filter))
        {
            RefreshVisualTree();
        }
        else
        {
            _treeBuildTimer.Stop();
            _pendingTreeBuild.Clear();
            RestartSearchRefreshTimer();
        }
    }

    private DevToolsTreeViewItem? CreateFilteredTreeViewItem(Visual visual, string filter)
    {
        bool selfMatches = MatchesSearch(visual, filter);

        var item = new DevToolsTreeViewItem(visual);
        _allTreeItems.Add(item);

        bool hasMatchingChild = false;
        int childCount = visual.VisualChildrenCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                var childItem = CreateFilteredTreeViewItem(child, filter);
                if (childItem != null)
                {
                    item.Items.Add(childItem);
                    hasMatchingChild = true;
                }
            }
        }

        if (selfMatches || hasMatchingChild)
        {
            if (hasMatchingChild)
                item.IsExpanded = true;
            return item;
        }

        _allTreeItems.Remove(item);
        return null;
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

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Expand / Collapse All
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

    private void ExpandAll()
    {
        foreach (var item in _allTreeItems)
            item.IsExpanded = true;
    }

    private void CollapseAll()
    {
        foreach (var item in _allTreeItems)
            item.IsExpanded = false;
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
        _treeBuildTimer.Stop();
        _pendingTreeBuild.Clear();

        if (_isPickerActive)
            DeactivatePicker();
        _targetWindow.DevToolsOverlay = null;
        _overlay = null;
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
        _treeBuildTimer.Stop();
        _pendingTreeBuild.Clear();
        _visualTreeView.Items.Clear();
        _allTreeItems.Clear();

        var filter = _searchTextBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(filter))
        {
            var rootItem = CreateFilteredTreeViewItem(_targetWindow, filter);
            if (rootItem != null)
            {
                AttachTreeItemToView(rootItem, 0);
                _visualTreeView.Items.Add(rootItem);
                rootItem.IsExpanded = true;
            }

            return;
        }

        var root = new DevToolsTreeViewItem(_targetWindow);
        PrepareTreeItem(root, 0);
        _allTreeItems.Add(root);
        _visualTreeView.Items.Add(root);
        root.IsExpanded = true;
        _pendingTreeBuild.Enqueue(new PendingTreeBuildNode(root, _targetWindow, 0));
        ScheduleTreeBuild();
    }

    private void OnVisualTreeSelectionChanged(object? sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (e.NewValue is DevToolsTreeViewItem treeItem)
        {
            _selectedVisual = treeItem.Visual;
            UpdatePropertiesPanel(_selectedVisual);
            _overlay?.HighlightElement(_selectedVisual as UIElement);
        }
    }

    /// <summary>
    /// Ensures TreeViewItem ownership metadata is wired when items are added as direct containers.
    /// </summary>
    private void AttachTreeItemToView(TreeViewItem item, int level)
    {
        PrepareTreeItem(item, level);

        foreach (var child in item.Items)
        {
            if (child is TreeViewItem childItem)
            {
                AttachTreeItemToView(childItem, level + 1);
            }
        }
    }

    private void PrepareTreeItem(TreeViewItem item, int level)
    {
        item.ParentTreeView = _visualTreeView;
        item.Level = level;
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

    private void OnDevToolsLoaded(object? sender, EventArgs e)
    {
        ScheduleTreeBuild(deferToDispatcher: true);
    }

    private void ScheduleTreeBuild(bool deferToDispatcher = false)
    {
        if (_isClosing || _pendingTreeBuild.Count == 0 || Handle == nint.Zero)
        {
            return;
        }

        void StartTimer()
        {
            if (!_isClosing && _pendingTreeBuild.Count > 0 && !_treeBuildTimer.IsEnabled)
            {
                _treeBuildTimer.Start();
            }
        }

        if (deferToDispatcher)
        {
            Dispatcher.BeginInvoke(StartTimer);
        }
        else
        {
            StartTimer();
        }
    }

    private void OnTreeBuildTimerTick(object? sender, EventArgs e)
    {
        _treeBuildTimer.Stop();

        if (_isClosing)
        {
            _pendingTreeBuild.Clear();
            return;
        }

        int processedNodes = 0;
        int processedChildren = 0;
        while (processedNodes < TreeBuildNodeBatchSize &&
               processedChildren < TreeBuildChildBatchSize &&
               _pendingTreeBuild.Count > 0)
        {
            var pendingNode = _pendingTreeBuild.Dequeue();
            var childCount = pendingNode.Visual.VisualChildrenCount;
            if (pendingNode.NextChildIndex >= childCount)
            {
                processedNodes++;
                continue;
            }

            var childItems = new List<TreeViewItem>(Math.Min(childCount - pendingNode.NextChildIndex, TreeBuildChildBatchSize));
            while (pendingNode.NextChildIndex < childCount &&
                   processedChildren < TreeBuildChildBatchSize)
            {
                var child = pendingNode.Visual.GetVisualChild(pendingNode.NextChildIndex);
                pendingNode.NextChildIndex++;
                if (child == null)
                {
                    continue;
                }

                var childItem = new DevToolsTreeViewItem(child);
                PrepareTreeItem(childItem, pendingNode.Level + 1);
                childItems.Add(childItem);
                _allTreeItems.Add(childItem);
                _pendingTreeBuild.Enqueue(new PendingTreeBuildNode(childItem, child, pendingNode.Level + 1));
                processedChildren++;
            }

            pendingNode.Item.AddChildItems(childItems);
            if (pendingNode.NextChildIndex < childCount)
            {
                _pendingTreeBuild.Enqueue(pendingNode);
            }

            processedNodes++;
        }

        if (_pendingTreeBuild.Count > 0)
        {
            ScheduleTreeBuild();
        }
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
            Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 0)),
            FontSize = 13,
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
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                FontSize = 11,
                Margin = new Thickness(8, 4, 4, 4)
            });
        }

        // Force the ScrollViewer to re-measure after content changed
        _propertiesScrollViewer.InvalidateMeasure();
        InvalidateWindow();
    }

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
            AddEnum("Visibility", uiElement.Visibility, v => uiElement.Visibility = (Visibility)v);
            AddBool("IsEnabled", uiElement.IsEnabled, v => uiElement.IsEnabled = v);
            AddNum("Opacity", uiElement.Opacity, "F2", v => uiElement.Opacity = v);
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
                    AddBrush("Background", control.Background, v => control.Background = v);
                    AddBrush("Foreground", control.Foreground, v => control.Foreground = v);
                    AddBrush("BorderBrush", control.BorderBrush, v => control.BorderBrush = v);
                    AddThickness("BorderThickness", control.BorderThickness, v => control.BorderThickness = v);
                    AddThickness("Padding", control.Padding, v => control.Padding = v);
                    AddStr("CornerRadius", control.CornerRadius.ToString());

                    AddSection("Typography");
                    AddNum("FontSize", control.FontSize, "F1", v => control.FontSize = v);
                    AddFontFamily("FontFamily", control.FontFamily);
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
                    AddBrush("Foreground", tb.Foreground, v => tb.Foreground = v);
                    AddNum("FontSize", tb.FontSize, "F1", v => tb.FontSize = v);
                    AddFontFamily("FontFamily", tb.FontFamily);
                    AddFontWeight("FontWeight", tb.FontWeight);
                }

                // 鈹€鈹€ Border 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€
                if (fe is Border border)
                {
                    AddSection("Border");
                    AddBrush("Background", border.Background, v => border.Background = v);
                    AddBrush("BorderBrush", border.BorderBrush, v => border.BorderBrush = v);
                    AddThickness("BorderThickness", border.BorderThickness, v => border.BorderThickness = v);
                    AddThickness("Padding", border.Padding, v => border.Padding = v);
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
            }
        }
    }

    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
    //  Element Statistics
    // 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

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
        _propertiesPanel.Children.Add(new TextBlock
        {
            Text = $"\u25cf {category} ({count})",
            Foreground = GetCategoryBrush(category),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 8, 4, 1)
        });
    }

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
                AddFormattedDependencyPropertyValue(property.Name, value, nameBrush);
                break;
        }
    }

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
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
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
                    Text = " > ",
                    Foreground = BrushBreadcrumbSep,
                    FontSize = 10
                });
            }

            var ancestor = ancestors[i];
            var isLast = (i == ancestors.Count - 1);
            var crumb = new TextBlock
            {
                Text = ancestor.GetType().Name,
                FontSize = 10,
                Foreground = isLast
                    ? BrushAccent
                    : new SolidColorBrush(Color.FromRgb(160, 160, 160))
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
                    SelectVisualInTree(target);
                    _selectedVisual = target;
                    UpdatePropertiesPanel(target);
                    _overlay?.HighlightElement(target as UIElement);
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
            Width = 10,
            Height = 10,
            Background = color,
            Margin = new Thickness(0, 0, 4, 0),
            CornerRadius = new CornerRadius(2)
        });
        inner.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160))
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
                var setter = style.Setters[i];
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
            Text = $"\u25b8 {name}",
            Foreground = BrushSection,
            FontSize = 11,
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
                    var values = Enum.GetValues(value.GetType());
                    int index = Array.IndexOf(values, value);
                    setter(values.GetValue((index + 1) % values.Length)!);
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
                Margin = new Thickness(0, 1, 6, 1)
            };
            row.Children.Add(swatch);

            string hex = c.A < 255
                ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            if (setter != null)
            {
                var tb = new TextBox
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
        }
        else if (brush == null)
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
    private void AddFontFamily(string name, string? fontFamily)
    {
        var row = Row(name);
        var display = fontFamily ?? "(default)";
        row.Children.Add(new TextBlock
        {
            Text = $"\"{display}\"",
            Foreground = BrushString,
            FontSize = 11,
            FontFamily = fontFamily ?? "Segoe UI"
        });
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

        _targetWindow.DevToolsOverlay = null;
        _overlay = null;

        Close();
    }
}

/// <summary>
/// Custom TreeViewItem for displaying visual tree nodes with enhanced display.
/// Shows child count and dimensions for container elements.
/// </summary>
internal sealed class DevToolsTreeViewItem : TreeViewItem
{
    public Visual Visual { get; }

    public DevToolsTreeViewItem(Visual visual)
    {
        Visual = visual;
        Header = GetVisualDisplayName(visual);
        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    }

    private static string GetVisualDisplayName(Visual visual)
    {
        var typeName = visual.GetType().Name;
        var suffix = "";

        // Append dimensions for FrameworkElements
        if (visual is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
        {
            suffix = $" {fe.ActualWidth:F0}\u00d7{fe.ActualHeight:F0}";
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
}

internal sealed class PendingTreeBuildNode
{
    public PendingTreeBuildNode(DevToolsTreeViewItem item, Visual visual, int level)
    {
        Item = item;
        Visual = visual;
        Level = level;
    }

    public DevToolsTreeViewItem Item { get; }

    public Visual Visual { get; }

    public int Level { get; }

    public int NextChildIndex { get; set; }
}
