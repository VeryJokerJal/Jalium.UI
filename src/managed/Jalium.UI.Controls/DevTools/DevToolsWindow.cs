using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
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
    private static readonly SolidColorBrush BrushAccent = new(ThemeColors.Accent);
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

        toolbar.Children.Add(MakeToolbarButton("Refresh", "F5", OnRefreshClick,
            "M670.783 229.290c-47.677-26.908-102.166-41.658-159.109-41.658-178.952 0-324.022 145.069-324.022 324.022 0 33.553-27.200 60.754-60.754 60.754s-60.754-27.200-60.754-60.754c0-246.059 199.470-445.530 445.530-445.530 89.863 0 175.407 26.802 247.370 74.905l68.914-68.914c8.258-8.259 21.966-7.940 30.617 0.710 4.022 4.022 6.413 9.366 6.677 14.928l10.908 229.367c0.853 17.940-12.982 32.500-30.902 32.522a32.715 32.715 0 0 1-1.583-0.037l-229.367-10.908c-11.960-0.569-21.644-10.715-21.630-22.661 0.007-5.555 2.152-10.794 5.991-14.634l72.111-72.111z m-318.218 564.726c47.677 26.908 102.166 41.659 159.109 41.659 178.952 0 324.022-145.069 324.022-324.022 0-33.553 27.200-60.754 60.754-60.754s60.754 27.200 60.754 60.754c0 246.059-199.470 445.530-445.530 445.530-89.863 0-175.407-26.802-247.370-74.905l-68.914 68.914c-8.258 8.258-21.966 7.941-30.617-0.710-4.022-4.022-6.413-9.367-6.677-14.928l-10.908-229.367c-0.853-17.940 12.982-32.500 30.902-32.522a31.497 31.497 0 0 1 1.584 0.037l229.367 10.909c11.960 0.569 21.644 10.714 21.630 22.661-0.006 5.555-2.152 10.794-5.991 14.633l-72.112 72.110z"));

        _pickerButton = MakeToolbarButton("Pick", "Ctrl+Shift+C", OnPickerClick,
            "M702.932 777.584c-11.692-3.161-23.743 3.758-26.903 15.45-3.165 11.698 3.758 23.744 15.451 26.903 73.027 19.747 105.712 47.546 105.712 66.333 0 14.494-19.804 37.052-75.398 56.907-55.701 19.895-130.205 30.845-209.791 30.845-79.586 0-154.099-10.95-209.799-30.845-55.593-19.855-75.396-42.413-75.396-56.907 0-18.793 32.702-46.602 105.761-66.348 11.698-3.161 18.618-15.201 15.456-26.899-3.159-11.692-15.199-18.617-26.897-15.455-89.119 24.084-138.198 62.687-138.198 108.702 0 26.869 17.359 47.165 31.924 59.459 17.477 14.762 41.905 27.805 72.594 38.766 60.318 21.541 140.063 33.407 224.549 33.407 84.493 0 164.238-11.872 224.556-33.407 30.689-10.961 55.115-24.005 72.595-38.766 14.564-12.295 31.924-32.590 31.924-59.459 0-46-49.056-84.603-138.137-108.686zM422.048 826.725c23.419 26.842 56.222 42.223 90.118 42.223h0.005c34.028 0 66.802-15.506 90.080-42.569 7.059-7.696 67.087-73.789 127.916-160.351 88.146-125.430 132.837-229.369 132.837-308.923 0-47.367-9.283-93.344-27.597-136.640-17.684-41.803-42.986-79.343-75.212-111.563-32.219-32.218-69.751-57.526-111.554-75.202-43.302-18.315-89.278-27.603-136.639-27.603-47.367 0-93.344 9.289-136.641 27.603-41.802 17.678-79.342 42.985-111.561 75.202-32.219 32.226-57.526 69.759-75.202 111.563-18.315 43.297-27.603 89.273-27.603 136.640 0 79.552 44.724 183.524 132.929 309.029 61.247 87.146 121.682 153.568 128.124 160.590zM375.908 347.290c0-77.421 62.989-140.405 140.402-140.405 77.421 0 140.404 62.986 140.404 140.405 0 77.415-62.982 140.404-140.404 140.404-77.414 0-140.402-62.989-140.402-140.404z");
        toolbar.Children.Add(_pickerButton);

        toolbar.Children.Add(MakeToolbarButton("Expand", "Ctrl+E", OnExpandAllClick,
            "M947.2 916.48l-30.72-199.68c0-15.36-15.36-30.72-30.72-30.72-5.12 0-10.24 0-15.36 5.12-5.12 0-5.12 5.12-10.24 5.12l-51.2 51.2-194.56-199.68c-10.24-10.24-25.6-10.24-35.84 0l-35.84 35.84c-10.24 10.24-10.24 25.6 0 35.84l199.68 199.68-51.2 51.2c-5.12 5.12-5.12 5.12-5.12 10.24-5.12 5.12-5.12 10.24-5.12 15.36 0 15.36 15.36 30.72 30.72 30.72l194.56 25.6h5.12c10.24 0 20.48-5.12 20.48-15.36 10.24-10.24 15.36-10.24 15.36-20.48z m-506.88-368.64c-10.24-10.24-25.6-10.24-35.84 0L204.8 747.52l-51.2-51.2c0-5.12 0-10.24-5.12-10.24s-5.12-5.12-15.36-5.12c-15.36 0-30.72 15.36-30.72 30.72l-30.72 199.68c0 5.12 5.12 15.36 5.12 15.36 5.12 5.12 15.36 15.36 25.6 15.36h5.12l199.68-25.6c15.36 0 30.72-15.36 30.72-30.72 0-5.12 0-10.24-5.12-15.36 0-5.12-5.12-5.12-5.12-10.24l-51.2-51.2L476.16 614.4c10.24-10.24 10.24-25.6 0-35.84 0 5.12-35.84-30.72-35.84-30.72z m506.88-440.32c0-5.12-5.12-15.36-5.12-15.36-5.12-5.12-15.36-15.36-20.48-15.36h-5.12L716.8 102.4c-15.36 0-30.72 15.36-30.72 30.72 0 5.12 0 10.24 5.12 15.36 0 5.12 5.12 5.12 5.12 10.24l51.2 51.2-194.56 194.56c-10.24 10.24-10.24 25.6 0 35.84l35.84 35.84c10.24 10.24 25.6 10.24 35.84 0l194.56-204.8 51.2 51.2c5.12 5.12 5.12 5.12 15.36 5.12 5.12 0 5.12 5.12 10.24 5.12 10.24 0 25.6-10.24 25.6-25.6l25.6-199.68z m-670.72 102.4l51.2-51.2c0-5.12 5.12-5.12 5.12-10.24 5.12-5.12 5.12-10.24 5.12-15.36 0-15.36-15.36-30.72-30.72-30.72l-199.68-25.6c-10.24 0-15.36 5.12-20.48 15.36-5.12 5.12-10.24 5.12-10.24 15.36L107.52 307.2c0 15.36 15.36 30.72 30.72 30.72 5.12 0 5.12 0 15.36-5.12l5.12-5.12 51.2-51.2L409.6 476.16c10.24 10.24 25.6 10.24 35.84 0l35.84-35.84c10.24-10.24 10.24-25.6 0-35.84-5.12 0-204.8-194.56-204.8-194.56z"));
        toolbar.Children.Add(MakeToolbarButton("Collapse", "Ctrl+Shift+E", OnCollapseAllClick,
            "M398.409143 172.763429c-3.218286 0-11.190857 0-14.409143 3.218285-3.218286 0-6.436571 3.218286-11.190857 6.436572l-57.636572 57.563428L95.963429 20.772571C84.845714 9.581714 67.291429 9.581714 57.563429 20.772571L17.627429 59.245714c-11.190857 11.190857-11.190857 28.818286 0 38.4l223.963428 224.036572-57.563428 57.563428c-3.218286 3.218286-6.436571 6.436571-6.436572 11.264-3.218286 3.145143-3.218286 11.190857-3.218286 14.336 0 17.627429 14.409143 32.036571 32.036572 32.036572l219.209143 27.209143h3.145143a30.281143 30.281143 0 0 0 25.6-14.409143c3.218286-6.436571 6.436571-11.190857 6.436571-20.845715L428.763429 204.8a29.842286 29.842286 0 0 0-30.354286-32.036571zM592.018286 460.8h3.145143l224.036571-28.818286c17.554286 0 32.036571-14.409143 32.036571-31.963428 0-3.218286 0-11.190857-3.218285-14.409143 0-3.218286-3.218286-6.436571-6.436572-11.190857l-57.563428-56.027429 220.818285-220.818286c11.190857-11.190857 11.190857-28.745143 0-38.4l-38.4-38.4c-11.264-11.190857-28.818286-11.190857-38.4 0L705.536 239.981714l-57.636571-57.563428c-3.145143-3.218286-6.363429-6.436571-11.190858-6.436572-3.145143 0-6.363429-3.218286-14.409142-3.218285a32.109714 32.109714 0 0 0-31.963429 32.036571l-32.036571 224.036571c0 6.363429 3.218286 14.336 6.436571 17.554286 9.581714 8.045714 17.554286 14.409143 27.209143 14.409143zM428.836571 563.2h-3.218285l-219.209143 28.818286a32.109714 32.109714 0 0 0-32.036572 31.963428c0 3.218286 0 11.190857 3.218286 14.409143 0 3.218286 3.218286 6.436571 6.436572 11.190857l57.563428 57.636572-220.818286 219.209143c-11.190857 11.190857-11.190857 28.745143 0 38.4l38.4 38.4c11.190857 11.190857 28.818286 11.190857 38.4 0l222.427429-219.209143 57.563429 57.563428c3.218286 3.218286 6.436571 6.436571 14.409142 6.436572 3.218286 0 6.436571 3.145143 11.264 3.145143 17.554286 0 31.963429-14.336 31.963429-31.963429l28.818286-224.036571a29.110857 29.110857 0 0 0-6.436572-17.554286c-11.190857-8.045714-17.554286-14.409143-28.818285-14.409143z m577.536 361.618286L782.409143 708.754286l57.563428-57.636572c3.218286-3.218286 6.436571-6.363429 6.436572-11.190857 3.218286-3.218286 3.218286-11.190857 3.218286-14.409143a32.109714 32.109714 0 0 0-32.036572-31.963428L596.845714 561.590857h-3.145143c-11.190857 0-17.627429 6.436571-25.6 14.409143-3.218286 6.436571-6.436571 11.190857-6.436571 17.554286l32.036571 224.036571c0 17.554286 14.409143 32.036571 31.963429 32.036572 3.218286 0 6.436571 0 14.409143-3.218286 3.218286 0 6.436571-3.218286 11.190857-6.436572l57.636571-57.563428 219.136 220.818286c11.264 11.190857 28.818286 11.190857 38.473143 0l38.4-38.4a29.622857 29.622857 0 0 0 1.536-40.009143z"));
        toolbar.Children.Add(MakeToolbarButton("Copy", "Ctrl+C", OnCopyClick,
            "M890.197 41.984H347.179C296.832 41.984 256 81.195 256 129.536v75.861h86.016V156.928c0-16.128 13.611-29.184 30.379-29.184h492.16c16.768 0 30.379 13.056 30.379 29.184v539.349c0 7.68-3.2 15.061-8.96 20.437a30.379 30.379 0 0 1-21.461 8.149H733.867v85.12h156.245c50.133 0 90.88-38.827 91.179-86.955V129.536c0-48.341-40.832-87.552-91.093-87.552z M676.864 214.016H133.845C83.499 214.016 42.667 253.227 42.667 301.568v75.861h86.016V328.96c0-16.128 13.611-29.184 30.379-29.184h492.16c16.768 0 30.379 13.056 30.379 29.184v539.349c0 7.68-3.2 15.061-8.96 20.437a30.379 30.379 0 0 1-21.461 8.149H520.533v85.12h156.245c50.133 0 90.88-38.827 91.179-86.955V301.568c0.043-48.341-40.789-87.552-91.093-87.552z M42.709 301.568v593.493c0.299 48.128 41.045 86.955 91.179 86.955H546.133v-85.12H159.445a30.379 30.379 0 0 1-21.461-8.149 27.947 27.947 0 0 1-8.96-20.437V328.96c0-16.128 13.611-29.184 30.379-29.184h492.16c16.768 0 30.379 13.056 30.379 29.184v48.469H768V301.568c0-48.341-40.832-87.552-91.179-87.552H133.803c-50.304 0-91.136 39.211-91.093 87.552z M256 384.683h298.667a42.667 42.667 0 0 1 0 85.333H256a42.667 42.667 0 0 1 0-85.333zM256 555.349h298.667a42.667 42.667 0 0 1 0 85.333H256a42.667 42.667 0 0 1 0-85.333zM256 726.016h298.667a42.667 42.667 0 0 1 0 85.333H256a42.667 42.667 0 0 1 0-85.333z"));

        return new Border
        {
            Background = BrushToolbarBg,
            BorderBrush = BrushToolbarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar
        };
    }

    private static Border MakeToolbarButton(string label, string shortcut, RoutedEventHandler handler, string? iconPathData = null)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal };

        if (iconPathData != null)
        {
            var icon = new ShapePath
            {
                Data = iconPathData,
                Fill = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                Stretch = Stretch.Uniform,
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            inner.Children.Add(icon);
        }

        inner.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        inner.Children.Add(new TextBlock
        {
            Text = shortcut,
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
            VerticalAlignment = VerticalAlignment.Center
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
        // First, try to find in already-built items
        foreach (var item in _allTreeItems)
        {
            if (item.Visual == visual)
            {
                ExpandAncestors(item);
                item.IsSelected = true;
                return;
            }
        }

        // Not found — tree is lazily built and this node hasn't been created yet.
        // Build the ancestor chain from root to target, then force-expand along that path.
        var ancestorChain = new List<Visual>();
        for (Visual? v = visual; v != null; v = v.VisualParent)
        {
            ancestorChain.Add(v);
            if (v == _targetWindow) break;
        }
        if (ancestorChain.Count == 0 || ancestorChain[^1] != _targetWindow) return;
        ancestorChain.Reverse(); // [_targetWindow, child, ..., visual]

        // Find root tree item
        DevToolsTreeViewItem? current = null;
        foreach (var item in _allTreeItems)
        {
            if (item.Visual == _targetWindow) { current = item; break; }
        }
        if (current == null) return;

        // Build a set for O(1) lookup of visuals in the ancestor chain
        var ancestorSet = new HashSet<Visual>(ancestorChain);

        // Walk from root toward target, force-building children at each level
        while (current.Visual != visual)
        {
            current.IsExpanded = true;
            EnsureChildrenBuilt(current);

            // Find the child whose visual is in the ancestor chain (i.e. on the path to target)
            DevToolsTreeViewItem? next = null;
            foreach (var child in current.Items)
            {
                if (child is DevToolsTreeViewItem dti && ancestorSet.Contains(dti.Visual))
                {
                    next = dti;
                    break;
                }
            }
            if (next == null) return;
            current = next;
        }

        ExpandAncestors(current);
        current.IsSelected = true;
    }

    /// <summary>
    /// Forces immediate creation of child tree items for a node that may still be in the pending build queue.
    /// </summary>
    private void EnsureChildrenBuilt(DevToolsTreeViewItem parentItem)
    {
        var remaining = _pendingTreeBuild.Count;
        var requeue = new List<PendingTreeBuildNode>(remaining);
        bool found = false;

        for (int i = 0; i < remaining; i++)
        {
            var node = _pendingTreeBuild.Dequeue();
            if (node.Item == parentItem && !found)
            {
                found = true;
                var childCount = node.Visual.VisualChildrenCount;
                var childItems = new List<TreeViewItem>(childCount - node.NextChildIndex);
                while (node.NextChildIndex < childCount)
                {
                    var child = node.Visual.GetVisualChild(node.NextChildIndex);
                    node.NextChildIndex++;
                    if (child == null) continue;

                    var childItem = new DevToolsTreeViewItem(child);
                    PrepareTreeItem(childItem, node.Level + 1);
                    childItems.Add(childItem);
                    _allTreeItems.Add(childItem);
                    _pendingTreeBuild.Enqueue(new PendingTreeBuildNode(childItem, child, node.Level + 1));
                }
                parentItem.AddChildItems(childItems);
            }
            else
            {
                requeue.Add(node);
            }
        }

        foreach (var node in requeue)
            _pendingTreeBuild.Enqueue(node);
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
                    AddBrush("Foreground", tb.Foreground, v => ForceSetValue(tb, TextBlock.ForegroundProperty, v));
                    AddNum("FontSize", tb.FontSize, "F1", v => ForceSetValue(tb, TextBlock.FontSizeProperty, (double)v));
                    AddFontFamily("FontFamily", tb.FontFamily);
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
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
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
                        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
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
    private void AddFontFamily(string name, string? fontFamily)
    {
        var row = Row(name);
        var display = fontFamily ?? "(default)";
        row.Children.Add(new TextBlock
        {
            Text = $"\"{display}\"",
            Foreground = BrushString,
            FontSize = 11,
            FontFamily = fontFamily ?? FrameworkElement.DefaultFontFamilyName
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
