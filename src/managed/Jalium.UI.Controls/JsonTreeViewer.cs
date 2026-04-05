using System.Text.Json;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays JSON data in an interactive tree view with
/// search, expand/collapse, path copying, and optional inline editing.
/// </summary>
public class JsonTreeViewer : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.JsonTreeViewerAutomationPeer(this);
    }

    // Default type-indicator brushes
    private static readonly SolidColorBrush s_defaultObjectBrush = new(Color.FromRgb(86, 156, 214));
    private static readonly SolidColorBrush s_defaultArrayBrush = new(Color.FromRgb(184, 215, 163));
    private static readonly SolidColorBrush s_defaultStringBrush = new(Color.FromRgb(206, 145, 120));
    private static readonly SolidColorBrush s_defaultNumberBrush = new(Color.FromRgb(181, 206, 168));
    private static readonly SolidColorBrush s_defaultBooleanBrush = new(Color.FromRgb(86, 156, 214));
    private static readonly SolidColorBrush s_defaultNullBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_defaultKeyBrush = new(Color.FromRgb(156, 220, 254));
    private static readonly SolidColorBrush s_defaultBracketBrush = new(Color.FromRgb(180, 180, 180));
    private static readonly SolidColorBrush s_searchHighlightBrush = new(Color.FromArgb(80, 255, 200, 0));

    #region Dependency Properties

    /// <summary>
    /// Identifies the JsonText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnJsonTextChanged));

    /// <summary>
    /// Identifies the RootNode read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey RootNodePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(RootNode), typeof(JsonTreeNode), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the RootNode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty RootNodeProperty = RootNodePropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the SelectedNode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedNodeProperty =
        DependencyProperty.Register(nameof(SelectedNode), typeof(JsonTreeNode), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the SearchText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(JsonTreeViewer),
            new PropertyMetadata(string.Empty, OnSearchTextChanged));

    /// <summary>
    /// Identifies the IsEditable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEditableProperty =
        DependencyProperty.Register(nameof(IsEditable), typeof(bool), typeof(JsonTreeViewer),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IndentSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty IndentSizeProperty =
        DependencyProperty.Register(nameof(IndentSize), typeof(double), typeof(JsonTreeViewer),
            new PropertyMetadata(20.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ExpandDepth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ExpandDepthProperty =
        DependencyProperty.Register(nameof(ExpandDepth), typeof(int), typeof(JsonTreeViewer),
            new PropertyMetadata(2, OnExpandDepthChanged));

    /// <summary>
    /// Identifies the MaxRenderDepth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty MaxRenderDepthProperty =
        DependencyProperty.Register(nameof(MaxRenderDepth), typeof(int), typeof(JsonTreeViewer),
            new PropertyMetadata(100));

    /// <summary>
    /// Identifies the ObjectBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ObjectBrushProperty =
        DependencyProperty.Register(nameof(ObjectBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ArrayBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ArrayBrushProperty =
        DependencyProperty.Register(nameof(ArrayBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StringBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StringBrushProperty =
        DependencyProperty.Register(nameof(StringBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NumberBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NumberBrushProperty =
        DependencyProperty.Register(nameof(NumberBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BooleanBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BooleanBrushProperty =
        DependencyProperty.Register(nameof(BooleanBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NullBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NullBrushProperty =
        DependencyProperty.Register(nameof(NullBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the KeyBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty KeyBrushProperty =
        DependencyProperty.Register(nameof(KeyBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BracketBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BracketBrushProperty =
        DependencyProperty.Register(nameof(BracketBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowTypeIndicators dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowTypeIndicatorsProperty =
        DependencyProperty.Register(nameof(ShowTypeIndicators), typeof(bool), typeof(JsonTreeViewer),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowItemCount dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowItemCountProperty =
        DependencyProperty.Register(nameof(ShowItemCount), typeof(bool), typeof(JsonTreeViewer),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the SelectedNodeChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedNodeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedNodeChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<JsonTreeViewerSelectedNodeChangedEventArgs>), typeof(JsonTreeViewer));

    /// <summary>
    /// Identifies the NodeExpanded routed event.
    /// </summary>
    public static readonly RoutedEvent NodeExpandedEvent =
        EventManager.RegisterRoutedEvent(nameof(NodeExpanded), RoutingStrategy.Bubble,
            typeof(EventHandler<JsonTreeViewerNodeToggleEventArgs>), typeof(JsonTreeViewer));

    /// <summary>
    /// Identifies the NodeCollapsed routed event.
    /// </summary>
    public static readonly RoutedEvent NodeCollapsedEvent =
        EventManager.RegisterRoutedEvent(nameof(NodeCollapsed), RoutingStrategy.Bubble,
            typeof(EventHandler<JsonTreeViewerNodeToggleEventArgs>), typeof(JsonTreeViewer));

    /// <summary>
    /// Identifies the NodeValueEdited routed event.
    /// </summary>
    public static readonly RoutedEvent NodeValueEditedEvent =
        EventManager.RegisterRoutedEvent(nameof(NodeValueEdited), RoutingStrategy.Bubble,
            typeof(EventHandler<JsonTreeViewerNodeValueEditedEventArgs>), typeof(JsonTreeViewer));

    /// <summary>
    /// Identifies the PathCopied routed event.
    /// </summary>
    public static readonly RoutedEvent PathCopiedEvent =
        EventManager.RegisterRoutedEvent(nameof(PathCopied), RoutingStrategy.Bubble,
            typeof(EventHandler<JsonTreeViewerPathCopiedEventArgs>), typeof(JsonTreeViewer));

    /// <summary>
    /// Occurs when the selected node changes.
    /// </summary>
    public event EventHandler<JsonTreeViewerSelectedNodeChangedEventArgs> SelectedNodeChanged
    {
        add => AddHandler(SelectedNodeChangedEvent, value);
        remove => RemoveHandler(SelectedNodeChangedEvent, value);
    }

    /// <summary>
    /// Occurs when a node is expanded.
    /// </summary>
    public event EventHandler<JsonTreeViewerNodeToggleEventArgs> NodeExpanded
    {
        add => AddHandler(NodeExpandedEvent, value);
        remove => RemoveHandler(NodeExpandedEvent, value);
    }

    /// <summary>
    /// Occurs when a node is collapsed.
    /// </summary>
    public event EventHandler<JsonTreeViewerNodeToggleEventArgs> NodeCollapsed
    {
        add => AddHandler(NodeCollapsedEvent, value);
        remove => RemoveHandler(NodeCollapsedEvent, value);
    }

    /// <summary>
    /// Occurs when a node's value is edited.
    /// </summary>
    public event EventHandler<JsonTreeViewerNodeValueEditedEventArgs> NodeValueEdited
    {
        add => AddHandler(NodeValueEditedEvent, value);
        remove => RemoveHandler(NodeValueEditedEvent, value);
    }

    /// <summary>
    /// Occurs when a node's path is copied to the clipboard.
    /// </summary>
    public event EventHandler<JsonTreeViewerPathCopiedEventArgs> PathCopied
    {
        add => AddHandler(PathCopiedEvent, value);
        remove => RemoveHandler(PathCopiedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the JSON text to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? JsonText
    {
        get => (string?)GetValue(JsonTextProperty);
        set => SetValue(JsonTextProperty, value);
    }

    /// <summary>
    /// Gets the root node of the parsed JSON tree.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public JsonTreeNode? RootNode
    {
        get => (JsonTreeNode?)GetValue(RootNodeProperty);
        private set => SetValue(RootNodePropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected node.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public JsonTreeNode? SelectedNode
    {
        get => (JsonTreeNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    /// <summary>
    /// Gets or sets the search/filter text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string SearchText
    {
        get => (string)(GetValue(SearchTextProperty) ?? string.Empty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>
    /// Gets or sets whether node values can be edited inline.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty)!;
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>
    /// Gets or sets the indent size in pixels for each tree level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double IndentSize
    {
        get => (double)GetValue(IndentSizeProperty)!;
        set => SetValue(IndentSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the default depth to which nodes are expanded when JSON is loaded.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public int ExpandDepth
    {
        get => (int)GetValue(ExpandDepthProperty)!;
        set => SetValue(ExpandDepthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum depth of nodes to render in the tree.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public int MaxRenderDepth
    {
        get => (int)GetValue(MaxRenderDepthProperty)!;
        set => SetValue(MaxRenderDepthProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON object type indicators and values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ObjectBrush
    {
        get => (Brush?)GetValue(ObjectBrushProperty);
        set => SetValue(ObjectBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON array type indicators and values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ArrayBrush
    {
        get => (Brush?)GetValue(ArrayBrushProperty);
        set => SetValue(ArrayBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON string values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? StringBrush
    {
        get => (Brush?)GetValue(StringBrushProperty);
        set => SetValue(StringBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON number values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NumberBrush
    {
        get => (Brush?)GetValue(NumberBrushProperty);
        set => SetValue(NumberBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON boolean values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BooleanBrush
    {
        get => (Brush?)GetValue(BooleanBrushProperty);
        set => SetValue(BooleanBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON null values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NullBrush
    {
        get => (Brush?)GetValue(NullBrushProperty);
        set => SetValue(NullBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON property keys.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? KeyBrush
    {
        get => (Brush?)GetValue(KeyBrushProperty);
        set => SetValue(KeyBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for JSON brackets and braces.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BracketBrush
    {
        get => (Brush?)GetValue(BracketBrushProperty);
        set => SetValue(BracketBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether type indicator icons are shown next to nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowTypeIndicators
    {
        get => (bool)GetValue(ShowTypeIndicatorsProperty)!;
        set => SetValue(ShowTypeIndicatorsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether item counts are shown for object and array nodes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowItemCount
    {
        get => (bool)GetValue(ShowItemCountProperty)!;
        set => SetValue(ShowItemCountProperty, value);
    }

    #endregion

    #region Private Fields

    private TextBox? _searchBox;
    private TreeView? _treeView;
    private Border? _statusBar;
    private StackPanel? _toolBar;

    // Maps JsonTreeNode -> TreeViewItem for fast lookup
    private readonly Dictionary<JsonTreeNode, TreeViewItem> _nodeToItemMap = new();

    private bool _isBuildingTree;
    private string? _parseError;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonTreeViewer"/> class.
    /// </summary>
    public JsonTreeViewer()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        // Unhook old event handlers
        if (_searchBox != null)
        {
            _searchBox.RemoveHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnSearchBoxTextChanged));
        }

        if (_treeView != null)
        {
            _treeView.SelectedItemChanged -= OnTreeViewSelectedItemChanged;
        }

        base.OnApplyTemplate();

        _searchBox = GetTemplateChild("PART_SearchBox") as TextBox;
        _treeView = GetTemplateChild("PART_TreeView") as TreeView;
        _statusBar = GetTemplateChild("PART_StatusBar") as Border;
        _toolBar = GetTemplateChild("PART_ToolBar") as StackPanel;

        // Hook up search box text changed
        if (_searchBox != null)
        {
            _searchBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(OnSearchBoxTextChanged));
        }

        // Hook up tree view selection changed
        if (_treeView != null)
        {
            _treeView.SelectedItemChanged += OnTreeViewSelectedItemChanged;
        }

        // Rebuild tree if we already have parsed data
        if (RootNode != null)
        {
            BuildTreeItems();
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnJsonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonTreeViewer viewer)
        {
            viewer.ParseJson((string?)e.NewValue);
        }
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonTreeViewer viewer)
        {
            viewer.FilterNodes((string?)e.NewValue ?? string.Empty);
        }
    }

    private static void OnExpandDepthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonTreeViewer viewer && viewer.RootNode != null)
        {
            viewer.ExpandToDepth((int)e.NewValue!);
        }
    }

    private new static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonTreeViewer viewer)
        {
            viewer.InvalidateVisual();
            // Rebuild tree items so brush changes take effect
            viewer.BuildTreeItems();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonTreeViewer viewer)
        {
            viewer.InvalidateMeasure();
        }
    }

    #endregion

    #region JSON Parsing

    /// <summary>
    /// Parses the given JSON string and builds the node tree.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    private void ParseJson(string? json)
    {
        _parseError = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            RootNode = null;
            _nodeToItemMap.Clear();
            if (_treeView != null)
            {
                _treeView.Items.Clear();
            }
            UpdateStatusBar();
            return;
        }

        try
        {
            var root = JsonParser.Parse(json);
            RootNode = root;
            ExpandToDepth(ExpandDepth, root);
            BuildTreeItems();
        }
        catch (JsonException ex)
        {
            _parseError = ex.Message;
            RootNode = null;
            _nodeToItemMap.Clear();
            if (_treeView != null)
            {
                _treeView.Items.Clear();
            }
        }

        UpdateStatusBar();
    }

    #endregion

    #region Tree Building

    /// <summary>
    /// Populates the TreeView from the current node tree.
    /// </summary>
    private void EnsureTreeView()
    {
        if (_treeView != null) return;

        // Fallback: create TreeView programmatically when no template is applied
        _treeView = new TreeView { BorderThickness = new Thickness(0) };
        _treeView.SelectedItemChanged += OnTreeViewSelectedItemChanged;
        AddVisualChild(_treeView);
    }

    /// <inheritdoc />
    public override int VisualChildrenCount => _treeView != null && Template == null ? 1 : base.VisualChildrenCount;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (_treeView != null && Template == null && index == 0) return _treeView;
        return base.GetVisualChild(index);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_treeView != null && Template == null)
        {
            _treeView.Measure(availableSize);
            return _treeView.DesiredSize;
        }
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_treeView != null && Template == null)
        {
            _treeView.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            return finalSize;
        }
        return base.ArrangeOverride(finalSize);
    }

    private void BuildTreeItems()
    {
        EnsureTreeView();
        if (_treeView == null)
            return;

        _isBuildingTree = true;

        try
        {
            _treeView.Items.Clear();
            _nodeToItemMap.Clear();

            if (RootNode == null)
                return;

            var rootItem = CreateTreeViewItem(RootNode, 0);
            if (rootItem != null)
            {
                _treeView.Items.Add(rootItem);
            }
        }
        finally
        {
            _isBuildingTree = false;
        }

        UpdateStatusBar();
    }

    /// <summary>
    /// Creates a TreeViewItem for the given node, recursively building children
    /// up to <see cref="MaxRenderDepth"/>.
    /// </summary>
    private TreeViewItem? CreateTreeViewItem(JsonTreeNode node, int currentDepth)
    {
        if (currentDepth > MaxRenderDepth)
            return null;

        var item = new TreeViewItem
        {
            Header = CreateNodeHeader(node),
            IsExpanded = node.IsExpanded,
            Tag = node
        };

        _nodeToItemMap[node] = item;

        // Wire up expand/collapse tracking
        item.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler((s, e) =>
        {
            if (e.OriginalSource == s && item.Tag is JsonTreeNode n)
            {
                n.IsExpanded = true;
                RaiseEvent(new JsonTreeViewerNodeToggleEventArgs(NodeExpandedEvent, n));
            }
        }));

        item.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler((s, e) =>
        {
            if (e.OriginalSource == s && item.Tag is JsonTreeNode n)
            {
                n.IsExpanded = false;
                RaiseEvent(new JsonTreeViewerNodeToggleEventArgs(NodeCollapsedEvent, n));
            }
        }));

        // Add children
        foreach (var child in node.Children)
        {
            if (!child.IsVisible)
                continue;

            var childItem = CreateTreeViewItem(child, currentDepth + 1);
            if (childItem != null)
            {
                item.Items.Add(childItem);
            }
        }

        // Set visibility based on filter state
        if (!node.IsVisible)
        {
            item.Visibility = Visibility.Collapsed;
        }

        return item;
    }

    /// <summary>
    /// Creates the visual header content for a tree node, consisting of a type indicator,
    /// key label, and value label with appropriate coloring.
    /// </summary>
    /// <param name="node">The JSON node to create a header for.</param>
    /// <returns>A <see cref="StackPanel"/> containing the header elements.</returns>
    private StackPanel CreateNodeHeader(JsonTreeNode node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Type indicator
        if (ShowTypeIndicators)
        {
            var typeIndicator = new TextBlock
            {
                Text = GetTypeIndicatorText(node.NodeType),
                Foreground = GetBrushForType(node.NodeType),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(typeIndicator);
        }

        // Key label
        if (node.Key != null)
        {
            var keyBlock = new TextBlock
            {
                Text = node.Key,
                Foreground = KeyBrush ?? s_defaultKeyBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Highlight search matches on the key
            if (node.IsMatchedBySearch)
            {
                var highlight = new Border
                {
                    Background = s_searchHighlightBrush,
                    Child = keyBlock
                };
                panel.Children.Add(highlight);
            }
            else
            {
                panel.Children.Add(keyBlock);
            }

            // Colon separator
            var colonBlock = new TextBlock
            {
                Text = ": ",
                Foreground = BracketBrush ?? s_defaultBracketBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(colonBlock);
        }

        // Value / bracket display
        switch (node.NodeType)
        {
            case JsonNodeType.Object:
            {
                var openBrace = new TextBlock
                {
                    Text = "{",
                    Foreground = BracketBrush ?? s_defaultBracketBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(openBrace);

                if (ShowItemCount)
                {
                    var countBlock = new TextBlock
                    {
                        Text = $" {node.ChildCount} properties ",
                        Foreground = GetBrushForType(JsonNodeType.Object),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11
                    };
                    panel.Children.Add(countBlock);
                }

                var closeBrace = new TextBlock
                {
                    Text = "}",
                    Foreground = BracketBrush ?? s_defaultBracketBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(closeBrace);
                break;
            }

            case JsonNodeType.Array:
            {
                var openBracket = new TextBlock
                {
                    Text = "[",
                    Foreground = BracketBrush ?? s_defaultBracketBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(openBracket);

                if (ShowItemCount)
                {
                    var countBlock = new TextBlock
                    {
                        Text = $" {node.ChildCount} items ",
                        Foreground = GetBrushForType(JsonNodeType.Array),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11
                    };
                    panel.Children.Add(countBlock);
                }

                var closeBracket = new TextBlock
                {
                    Text = "]",
                    Foreground = BracketBrush ?? s_defaultBracketBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(closeBracket);
                break;
            }

            default:
            {
                var valueBlock = new TextBlock
                {
                    Text = node.DisplayValue,
                    Foreground = GetBrushForType(node.NodeType),
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (node.IsMatchedBySearch)
                {
                    var highlight = new Border
                    {
                        Background = s_searchHighlightBrush,
                        Child = valueBlock
                    };
                    panel.Children.Add(highlight);
                }
                else
                {
                    panel.Children.Add(valueBlock);
                }
                break;
            }
        }

        return panel;
    }

    /// <summary>
    /// Returns a short text indicator for the given JSON node type.
    /// </summary>
    private static string GetTypeIndicatorText(JsonNodeType nodeType)
    {
        return nodeType switch
        {
            JsonNodeType.Object => "{}",
            JsonNodeType.Array => "[]",
            JsonNodeType.String => "ab",
            JsonNodeType.Number => "#",
            JsonNodeType.Boolean => "tf",
            JsonNodeType.Null => "--",
            _ => "?"
        };
    }

    /// <summary>
    /// Resolves the appropriate brush for the given JSON node type,
    /// preferring user-specified dependency property values over defaults.
    /// </summary>
    /// <param name="nodeType">The JSON node type.</param>
    /// <returns>The brush to use for rendering.</returns>
    public Brush GetBrushForType(JsonNodeType nodeType)
    {
        return nodeType switch
        {
            JsonNodeType.Object => ObjectBrush ?? s_defaultObjectBrush,
            JsonNodeType.Array => ArrayBrush ?? s_defaultArrayBrush,
            JsonNodeType.String => StringBrush ?? s_defaultStringBrush,
            JsonNodeType.Number => NumberBrush ?? s_defaultNumberBrush,
            JsonNodeType.Boolean => BooleanBrush ?? s_defaultBooleanBrush,
            JsonNodeType.Null => NullBrush ?? s_defaultNullBrush,
            _ => Foreground ?? s_defaultKeyBrush
        };
    }

    #endregion

    #region Search / Filtering

    /// <summary>
    /// Filters nodes in the tree to show only those matching the search text.
    /// Matching is case-insensitive on both keys and values. Parent nodes of
    /// matches are automatically shown and expanded.
    /// </summary>
    /// <param name="searchText">The text to search for. Empty string shows all nodes.</param>
    private void FilterNodes(string searchText)
    {
        if (RootNode == null)
            return;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            // Clear filter: show all nodes, restore original expansion state
            ClearFilter(RootNode);
            BuildTreeItems();
            return;
        }

        var search = searchText.Trim();

        // Mark visibility on all nodes
        MarkFilterMatches(RootNode, search);

        // Rebuild tree with filter applied
        BuildTreeItems();
    }

    /// <summary>
    /// Recursively marks nodes as visible/invisible based on search text.
    /// Returns true if this node or any descendant matches.
    /// </summary>
    private bool MarkFilterMatches(JsonTreeNode node, string searchText)
    {
        bool keyMatches = node.Key != null &&
            node.Key.Contains(searchText, StringComparison.OrdinalIgnoreCase);

        bool valueMatches = node.Value != null &&
            node.Value.ToString()!.Contains(searchText, StringComparison.OrdinalIgnoreCase);

        bool directMatch = keyMatches || valueMatches;
        node.IsMatchedBySearch = directMatch;

        bool anyChildMatches = false;
        foreach (var child in node.Children)
        {
            if (MarkFilterMatches(child, searchText))
                anyChildMatches = true;
        }

        bool isVisible = directMatch || anyChildMatches;
        node.IsVisible = isVisible;

        // Auto-expand nodes that have matching descendants
        if (anyChildMatches && !directMatch)
        {
            node.IsExpanded = true;
        }

        return isVisible;
    }

    /// <summary>
    /// Clears all filter state, restoring visibility on all nodes.
    /// </summary>
    private void ClearFilter(JsonTreeNode node)
    {
        node.IsVisible = true;
        node.IsMatchedBySearch = false;

        foreach (var child in node.Children)
        {
            ClearFilter(child);
        }
    }

    #endregion

    #region Expand / Collapse

    /// <summary>
    /// Expands all nodes up to the specified depth.
    /// </summary>
    /// <param name="depth">The maximum depth to expand (0 = root only).</param>
    public void ExpandToDepth(int depth)
    {
        if (RootNode != null)
        {
            ExpandToDepth(depth, RootNode);
            BuildTreeItems();
        }
    }

    /// <summary>
    /// Expands nodes in the tree up to the specified depth, starting from the given node.
    /// </summary>
    private void ExpandToDepth(int depth, JsonTreeNode node)
    {
        node.IsExpanded = node.Depth < depth;

        foreach (var child in node.Children)
        {
            ExpandToDepth(depth, child);
        }
    }

    /// <summary>
    /// Expands all nodes in the tree.
    /// </summary>
    public void ExpandAll()
    {
        if (RootNode != null)
        {
            SetExpandedRecursive(RootNode, true);
            BuildTreeItems();
        }
    }

    /// <summary>
    /// Collapses all nodes in the tree.
    /// </summary>
    public void CollapseAll()
    {
        if (RootNode != null)
        {
            SetExpandedRecursive(RootNode, false);
            BuildTreeItems();
        }
    }

    private void SetExpandedRecursive(JsonTreeNode node, bool expanded)
    {
        if (node.NodeType is JsonNodeType.Object or JsonNodeType.Array)
        {
            node.IsExpanded = expanded;
        }

        foreach (var child in node.Children)
        {
            SetExpandedRecursive(child, expanded);
        }
    }

    #endregion

    #region Clipboard

    /// <summary>
    /// Copies the JSONPath of the given node to the system clipboard.
    /// </summary>
    /// <param name="node">The node whose path to copy.</param>
    public void CopyPathToClipboard(JsonTreeNode node)
    {
        var path = node.Path;
        Clipboard.SetText(path);
        RaiseEvent(new JsonTreeViewerPathCopiedEventArgs(PathCopiedEvent, node, path));
    }

    #endregion

    #region Inline Editing

    /// <summary>
    /// Begins inline editing of a leaf node's value.
    /// Only applicable when <see cref="IsEditable"/> is true and the node is a
    /// primitive type (string, number, boolean, null).
    /// </summary>
    /// <param name="node">The node to edit.</param>
    internal void BeginEditNode(JsonTreeNode node)
    {
        if (!IsEditable)
            return;

        if (node.NodeType is JsonNodeType.Object or JsonNodeType.Array)
            return;

        if (!_nodeToItemMap.TryGetValue(node, out var treeViewItem))
            return;

        var oldValue = node.Value;

        // Create an inline TextBox for editing
        var editBox = new TextBox
        {
            Text = node.Value?.ToString() ?? "",
            MinWidth = 80
        };

        // Replace the header with the edit box
        var originalHeader = treeViewItem.Header;

        var editPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // Keep the key portion
        if (node.Key != null)
        {
            var keyBlock = new TextBlock
            {
                Text = $"{node.Key}: ",
                Foreground = KeyBrush ?? s_defaultKeyBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            editPanel.Children.Add(keyBlock);
        }

        editPanel.Children.Add(editBox);
        treeViewItem.Header = editPanel;

        editBox.Focus();
        editBox.SelectAll();

        // Commit on Enter, cancel on Escape
        editBox.AddHandler(KeyDownEvent, new KeyEventHandler((s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitEdit(node, editBox.Text, oldValue, treeViewItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Restore original header
                treeViewItem.Header = originalHeader;
                e.Handled = true;
            }
        }));

        // Also commit on lost focus
        editBox.LostFocus += (s, e) =>
        {
            if (treeViewItem.Header == editPanel)
            {
                CommitEdit(node, editBox.Text, oldValue, treeViewItem);
            }
        };
    }

    private void CommitEdit(JsonTreeNode node, string newText, object? oldValue, TreeViewItem item)
    {
        object? newValue = node.NodeType switch
        {
            JsonNodeType.Number when long.TryParse(newText, out long l) => l,
            JsonNodeType.Number when double.TryParse(newText, out double d) => d,
            JsonNodeType.Boolean when bool.TryParse(newText, out bool b) => b,
            JsonNodeType.Null when string.Equals(newText, "null", StringComparison.OrdinalIgnoreCase) => null,
            JsonNodeType.String => newText,
            _ => newText
        };

        node.Value = newValue;

        // Rebuild the header for this item
        item.Header = CreateNodeHeader(node);

        // Raise the edited event
        RaiseEvent(new JsonTreeViewerNodeValueEditedEventArgs(NodeValueEditedEvent, node, oldValue, newValue));
    }

    #endregion

    #region Event Handlers

    private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchBox != null)
        {
            SearchText = _searchBox.Text;
        }
    }

    private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (_isBuildingTree)
            return;

        var oldNode = SelectedNode;
        JsonTreeNode? newNode = null;

        if (e.NewValue is TreeViewItem selectedItem && selectedItem.Tag is JsonTreeNode node)
        {
            newNode = node;
        }

        SelectedNode = newNode;

        RaiseEvent(new JsonTreeViewerSelectedNodeChangedEventArgs(
            SelectedNodeChangedEvent, oldNode, newNode));
    }

    #endregion

    #region Status Bar

    /// <summary>
    /// Updates the status bar with current tree information.
    /// </summary>
    private void UpdateStatusBar()
    {
        if (_statusBar == null)
            return;

        string statusText;

        if (_parseError != null)
        {
            statusText = $"Parse error: {_parseError}";
        }
        else if (RootNode == null)
        {
            statusText = "No JSON loaded";
        }
        else
        {
            int totalNodes = CountNodes(RootNode);
            statusText = $"Nodes: {totalNodes}";

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                int visibleNodes = CountVisibleNodes(RootNode);
                statusText += $" | Showing: {visibleNodes}";
            }
        }

        // Try to find or create a TextBlock in the status bar
        if (_statusBar.Child is TextBlock statusBlock)
        {
            statusBlock.Text = statusText;
        }
        else
        {
            var textBlock = new TextBlock
            {
                Text = statusText,
                Margin = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusBar.Child = textBlock;
        }
    }

    private static int CountNodes(JsonTreeNode node)
    {
        int count = 1;
        foreach (var child in node.Children)
        {
            count += CountNodes(child);
        }
        return count;
    }

    private static int CountVisibleNodes(JsonTreeNode node)
    {
        if (!node.IsVisible)
            return 0;

        int count = 1;
        foreach (var child in node.Children)
        {
            count += CountVisibleNodes(child);
        }
        return count;
    }

    #endregion

    #region Keyboard

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        // Ctrl+C on a selected node copies its path
        if (e.Key == Key.C && e.IsControlDown && SelectedNode != null)
        {
            CopyPathToClipboard(SelectedNode);
            e.Handled = true;
        }
        // F2 starts inline editing
        else if (e.Key == Key.F2 && IsEditable && SelectedNode != null)
        {
            BeginEditNode(SelectedNode);
            e.Handled = true;
        }
        // Ctrl+F focuses the search box
        else if (e.Key == Key.F && e.IsControlDown)
        {
            _searchBox?.Focus();
            e.Handled = true;
        }
    }

    #endregion
}
