using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using WpfClipboard = global::Jalium.UI.Clipboard;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays JSON data in an interactive tree view with
/// search, expand/collapse, path copying, and optional inline editing.
/// </summary>
[TemplatePart(Name = "PART_SearchBox", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_TreeView", Type = typeof(TreeView))]
[TemplatePart(Name = "PART_ExpandAllButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_CollapseAllButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_CopyPathButton", Type = typeof(ButtonBase))]
[TemplateVisualState(Name = "Empty", GroupName = "DataStates")]
[TemplateVisualState(Name = "Ready", GroupName = "DataStates")]
[TemplateVisualState(Name = "Error", GroupName = "DataStates")]
[TemplateVisualState(Name = "Filtered", GroupName = "FilterStates")]
[TemplateVisualState(Name = "Unfiltered", GroupName = "FilterStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class JsonTreeViewer : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.JsonTreeViewerAutomationPeer(this);
    }

    static JsonTreeViewer() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(JsonTreeViewer), new PropertyMetadata(typeof(JsonTreeViewer)));

    // Fallback colors for consumers that use the data API without a theme.
    private static readonly SolidColorBrush s_defaultObjectBrush = new(Color.FromRgb(86, 156, 214));
    private static readonly SolidColorBrush s_defaultArrayBrush = new(Color.FromRgb(184, 215, 163));
    private static readonly SolidColorBrush s_defaultStringBrush = new(Color.FromRgb(206, 145, 120));
    private static readonly SolidColorBrush s_defaultNumberBrush = new(Color.FromRgb(181, 206, 168));
    private static readonly SolidColorBrush s_defaultBooleanBrush = new(Color.FromRgb(86, 156, 214));
    private static readonly SolidColorBrush s_defaultNullBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_defaultKeyBrush = new(Color.FromRgb(156, 220, 254));

    #region Dependency Properties

    /// <summary>
    /// Identifies the JsonText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(JsonTreeViewer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnJsonTextChanged));

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
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedNodeChanged));

    /// <summary>
    /// Identifies the SearchText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(JsonTreeViewer),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSearchTextChanged));

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
            new PropertyMetadata(20.0), value => value is double size && double.IsFinite(size) && size >= 0);

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
            new PropertyMetadata(100, OnTreePropertyChanged), value => value is int depth && depth >= 0);

    /// <summary>
    /// Identifies the ObjectBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ObjectBrushProperty =
        DependencyProperty.Register(nameof(ObjectBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ArrayBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ArrayBrushProperty =
        DependencyProperty.Register(nameof(ArrayBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the StringBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StringBrushProperty =
        DependencyProperty.Register(nameof(StringBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the NumberBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NumberBrushProperty =
        DependencyProperty.Register(nameof(NumberBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the BooleanBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BooleanBrushProperty =
        DependencyProperty.Register(nameof(BooleanBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the NullBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NullBrushProperty =
        DependencyProperty.Register(nameof(NullBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the KeyBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty KeyBrushProperty =
        DependencyProperty.Register(nameof(KeyBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the BracketBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BracketBrushProperty =
        DependencyProperty.Register(nameof(BracketBrush), typeof(Brush), typeof(JsonTreeViewer),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ShowTypeIndicators dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowTypeIndicatorsProperty =
        DependencyProperty.Register(nameof(ShowTypeIndicators), typeof(bool), typeof(JsonTreeViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the ShowItemCount dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowItemCountProperty =
        DependencyProperty.Register(nameof(ShowItemCount), typeof(bool), typeof(JsonTreeViewer),
            new PropertyMetadata(true));

    /// <summary>Identifies the template used for each JSON node's header.</summary>
    public static readonly DependencyProperty NodeTemplateProperty = DependencyProperty.Register(
        nameof(NodeTemplate), typeof(DataTemplate), typeof(JsonTreeViewer), new PropertyMetadata(null));
    /// <summary>Identifies the style used for generated tree containers.</summary>
    public static readonly DependencyProperty NodeContainerStyleProperty = DependencyProperty.Register(
        nameof(NodeContainerStyle), typeof(Style), typeof(JsonTreeViewer), new PropertyMetadata(null));
    /// <summary>Identifies the brush used to highlight search matches.</summary>
    public static readonly DependencyProperty SearchHighlightBrushProperty = DependencyProperty.Register(
        nameof(SearchHighlightBrush), typeof(Brush), typeof(JsonTreeViewer), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(StatusText), typeof(string), typeof(JsonTreeViewer), new PropertyMetadata("No JSON loaded"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ParseErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ParseError), typeof(string), typeof(JsonTreeViewer), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ParseErrorProperty = ParseErrorPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasSelectionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasSelection), typeof(bool), typeof(JsonTreeViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty HasSelectionProperty = HasSelectionPropertyKey.DependencyProperty;

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
        private set => SetValue(RootNodePropertyKey, value);
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

    /// <summary>Gets or sets the node display template. The header's data context is a JsonTreeNode.</summary>
    public DataTemplate? NodeTemplate { get => (DataTemplate?)GetValue(NodeTemplateProperty); set => SetValue(NodeTemplateProperty, value); }
    public Style? NodeContainerStyle { get => (Style?)GetValue(NodeContainerStyleProperty); set => SetValue(NodeContainerStyleProperty, value); }
    public Brush? SearchHighlightBrush { get => (Brush?)GetValue(SearchHighlightBrushProperty); set => SetValue(SearchHighlightBrushProperty, value); }
    public string StatusText => (string)GetValue(StatusTextProperty)!;
    public string ParseError => (string)GetValue(ParseErrorProperty)!;
    public bool HasSelection => (bool)GetValue(HasSelectionProperty)!;

    #endregion

    #region Private Fields

    private TextBox? _searchBox;
    private TreeView? _treeView;
    private ButtonBase? _expandButton;
    private ButtonBase? _collapseButton;
    private ButtonBase? _copyButton;
    private readonly Dictionary<JsonTreeNode, TreeViewItem> _nodeToItemMap = new();
    private readonly Dictionary<JsonTreeNode, WeakReference<JsonTreeNodePresenter>> _nodePresenters = new();
    private bool _isBuildingTree;
    private bool _hasTemplate;
    private JsonTreeNode? _pendingEditNode;
    private string? _pendingEditText;
    private string? _nodeEditJson;
    private static readonly JsonWriterOptions NodeWriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    #endregion

    public JsonTreeViewer()
    {
        Focusable = true;
        SetCurrentValue(TransitionPropertyProperty, TransitionPropertyCollection.None());
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(NodeValueEditedEvent, new EventHandler<JsonTreeViewerNodeValueEditedEventArgs>(OnNodeValueEdited));
    }

    #region Template

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _hasTemplate = true;
        _searchBox = GetTemplateChild("PART_SearchBox") as TextBox;
        _treeView = GetTemplateChild("PART_TreeView") as TreeView;
        _expandButton = GetTemplateChild("PART_ExpandAllButton") as ButtonBase;
        _collapseButton = GetTemplateChild("PART_CollapseAllButton") as ButtonBase;
        _copyButton = GetTemplateChild("PART_CopyPathButton") as ButtonBase;
        if (_treeView != null)
        {
            _treeView.SelectedItemChanged += OnTreeViewSelectedItemChanged;
            _treeView.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(OnNodeExpanded));
            _treeView.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(OnNodeCollapsed));
        }
        if (_expandButton != null) _expandButton.Click += OnExpandAllClick;
        if (_collapseButton != null) _collapseButton.Click += OnCollapseAllClick;
        if (_copyButton != null) _copyButton.Click += OnCopyPathClick;
        BuildTreeItems(preserveEdit: false);
        UpdateStatusBar();
    }

    protected override void OnTemplateChanging()
    {
        _hasTemplate = false;
        SavePendingEdit();
        if (_searchBox != null) BindingOperations.ClearBinding(_searchBox, TextBox.TextProperty);
        if (_treeView != null)
        {
            _treeView.SelectedItemChanged -= OnTreeViewSelectedItemChanged;
            _treeView.RemoveHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(OnNodeExpanded));
            _treeView.RemoveHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(OnNodeCollapsed));
        }
        if (_expandButton != null) _expandButton.Click -= OnExpandAllClick;
        if (_collapseButton != null) _collapseButton.Click -= OnCollapseAllClick;
        if (_copyButton != null) _copyButton.Click -= OnCopyPathClick;
        ClearTreeItems();
        _searchBox = null;
        _treeView = null;
        _expandButton = _collapseButton = _copyButton = null;
        base.OnTemplateChanging();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnJsonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JsonTreeViewer viewer)
        {
            if (viewer._nodeEditJson != null && string.Equals(viewer._nodeEditJson, e.NewValue as string, StringComparison.Ordinal)) return;
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

    private static void OnTreePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((JsonTreeViewer)d).BuildTreeItems();

    private static void OnSelectedNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (JsonTreeViewer)d;
        viewer.SyncSelection();
        viewer.SetValue(HasSelectionPropertyKey, viewer.SelectedNode != null);
        viewer.RaiseEvent(new JsonTreeViewerSelectedNodeChangedEventArgs(
            SelectedNodeChangedEvent, e.OldValue as JsonTreeNode, e.NewValue as JsonTreeNode));
    }

    #endregion

    #region JSON Parsing

    /// <summary>
    /// Parses the given JSON string and builds the node tree.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    private void ParseJson(string? json)
    {
        _pendingEditNode = null;
        _pendingEditText = null;
        SetValue(ParseErrorPropertyKey, string.Empty);
        SetCurrentValue(SelectedNodeProperty, null);
        RootNode = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                RootNode = JsonParser.Parse(json);
                ExpandToDepth(ExpandDepth, RootNode);
                ApplyFilter();
            }
            catch (JsonException ex) { SetValue(ParseErrorPropertyKey, ex.Message); }
        }
        BuildTreeItems(preserveEdit: false);
        UpdateStatusBar();
    }

    #endregion

    #region Tree Building

    private void SavePendingEdit()
    {
        foreach (var entry in _nodePresenters.Values)
        {
            if (entry.TryGetTarget(out var presenter) && presenter.IsEditing)
            {
                _pendingEditNode = presenter.Node;
                _pendingEditText = presenter.EditText;
                presenter.CancelEdit();
            }
        }
    }

    private void ClearTreeItems()
    {
        foreach (var entry in _nodePresenters.Values)
            if (entry.TryGetTarget(out var presenter)) presenter.CancelEdit();
        _nodePresenters.Clear();
        foreach (var item in _nodeToItemMap.Values)
        {
            BindingOperations.ClearBinding(item, TreeViewItem.IsExpandedProperty);
            BindingOperations.ClearBinding(item, TreeViewItem.HeaderTemplateProperty);
            BindingOperations.ClearBinding(item, TreeViewItem.IndentSizeProperty);
            BindingOperations.ClearBinding(item, StyleProperty);
            item.Header = null;
            item.Tag = null;
            item.Items.Clear();
        }
        _nodeToItemMap.Clear();
        _treeView?.Items.Clear();
    }

    private void BuildTreeItems(bool preserveEdit = true)
    {
        if (_treeView == null) { UpdateStatusBar(); return; }
        if (_isBuildingTree) return;
        _isBuildingTree = true;
        try
        {
            if (preserveEdit) SavePendingEdit();
            ClearTreeItems();
            if (RootNode is { IsVisible: true } root)
            {
                var rootItem = CreateTreeViewItem(root, 0);
                if (rootItem != null) _treeView.Items.Add(rootItem);
            }
            if (SelectedNode != null && !_nodeToItemMap.ContainsKey(SelectedNode))
                SetCurrentValue(SelectedNodeProperty, null);
            SyncSelection();
            if (_pendingEditNode != null && !_nodeToItemMap.ContainsKey(_pendingEditNode))
            {
                _pendingEditNode = null;
                _pendingEditText = null;
            }
        }
        finally { _isBuildingTree = false; }
        UpdateStatusBar();
    }

    private TreeViewItem? CreateTreeViewItem(JsonTreeNode node, int currentDepth)
    {
        if (currentDepth > MaxRenderDepth) return null;
        var item = new TreeViewItem { Header = node, Tag = node };
        item.SetBinding(StyleProperty, new Binding(nameof(NodeContainerStyle)) { Source = this });
        item.SetBinding(TreeViewItem.HeaderTemplateProperty, new Binding(nameof(NodeTemplate)) { Source = this });
        item.SetBinding(TreeViewItem.IndentSizeProperty, new Binding(nameof(IndentSize)) { Source = this });
        item.SetBinding(TreeViewItem.IsExpandedProperty, new Binding(nameof(JsonTreeNode.IsExpanded)) { Source = node, Mode = BindingMode.TwoWay });
        _nodeToItemMap[node] = item;
        foreach (var child in node.Children)
        {
            if (!child.IsVisible) continue;
            var childItem = CreateTreeViewItem(child, currentDepth + 1);
            if (childItem != null) item.Items.Add(childItem);
        }
        return item;
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
        ApplyFilter();
        BuildTreeItems();
    }

    private void ApplyFilter()
    {
        if (RootNode == null) return;
        if (string.IsNullOrWhiteSpace(SearchText)) ClearFilter(RootNode);
        else MarkFilterMatches(RootNode, SearchText.Trim());
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
        WpfClipboard.SetText(path);
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
    public bool BeginEditNode(JsonTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!IsEditable || !IsEnabled) return false;
        if (!_nodePresenters.TryGetValue(node, out var reference) || !reference.TryGetTarget(out var presenter)) return false;
        foreach (var other in _nodePresenters.Values)
            if (other.TryGetTarget(out var row) && !ReferenceEquals(row, presenter)) row.CancelEdit();
        return presenter.BeginEdit();
    }

    internal void RegisterNodePresenter(JsonTreeNodePresenter presenter)
    {
        if (presenter.Node is not { } node) return;
        _nodePresenters[node] = new(presenter);
        if (ReferenceEquals(node, _pendingEditNode) && presenter.IsEditable && presenter.BeginEdit())
        {
            presenter.SetCurrentValue(JsonTreeNodePresenter.EditTextProperty, _pendingEditText ?? string.Empty);
            _pendingEditNode = null;
            _pendingEditText = null;
        }
    }

    internal void UnregisterNodePresenter(JsonTreeNode? node, JsonTreeNodePresenter presenter)
    {
        if (node != null && _nodePresenters.TryGetValue(node, out var reference)
            && reference.TryGetTarget(out var current) && ReferenceEquals(current, presenter)) _nodePresenters.Remove(node);
    }

    internal void FocusNode(JsonTreeNode node)
    {
        if (_nodeToItemMap.TryGetValue(node, out var item)) item.Focus();
    }

    #endregion

    #region Event Handlers

    private void OnNodeValueEdited(object? sender, JsonTreeViewerNodeValueEditedEventArgs e)
    {
        if (RootNode == null || !_nodeToItemMap.ContainsKey(e.Node)) return;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, NodeWriterOptions)) WriteNode(writer, RootNode);
        _nodeEditJson = Encoding.UTF8.GetString(stream.ToArray());
        try { SetCurrentValue(JsonTextProperty, _nodeEditJson); }
        finally { _nodeEditJson = null; }
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonTreeNode node)
    {
        switch (node.NodeType)
        {
            case JsonNodeType.Object:
                writer.WriteStartObject();
                foreach (var child in node.Children)
                {
                    writer.WritePropertyName(child.Key ?? string.Empty);
                    WriteNode(writer, child);
                }
                writer.WriteEndObject();
                break;
            case JsonNodeType.Array:
                writer.WriteStartArray();
                foreach (var child in node.Children) WriteNode(writer, child);
                writer.WriteEndArray();
                break;
            case JsonNodeType.String: writer.WriteStringValue(node.Value as string); break;
            case JsonNodeType.Boolean: writer.WriteBooleanValue(node.Value is true); break;
            case JsonNodeType.Null: writer.WriteNullValue(); break;
            case JsonNodeType.Number:
                // Typed writes participate in the writer's indentation and retain
                // normal numeric values. Raw text covers larger parsed JSON numbers.
                switch (node.Value)
                {
                    case long integer: writer.WriteNumberValue(integer); break;
                    case int integer: writer.WriteNumberValue(integer); break;
                    case double real: writer.WriteNumberValue(real); break;
                    case float real: writer.WriteNumberValue(real); break;
                    case decimal real: writer.WriteNumberValue(real); break;
                    default: writer.WriteRawValue(Convert.ToString(node.Value, CultureInfo.InvariantCulture) ?? "0"); break;
                }
                break;
        }
    }

    private void OnExpandAllClick(object sender, RoutedEventArgs e) => ExpandAll();
    private void OnCollapseAllClick(object sender, RoutedEventArgs e) => CollapseAll();
    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (SelectedNode != null) CopyPathToClipboard(SelectedNode);
    }
    private void OnNodeExpanded(object sender, RoutedEventArgs e) => OnNodeToggled(e, true);
    private void OnNodeCollapsed(object sender, RoutedEventArgs e) => OnNodeToggled(e, false);
    private void OnNodeToggled(RoutedEventArgs e, bool expanded)
    {
        if (_isBuildingTree || e.OriginalSource is not TreeViewItem { Tag: JsonTreeNode node } item
            || !_nodeToItemMap.TryGetValue(node, out var current) || !ReferenceEquals(item, current)) return;
        node.IsExpanded = expanded;
        RaiseEvent(new JsonTreeViewerNodeToggleEventArgs(expanded ? NodeExpandedEvent : NodeCollapsedEvent, node));
    }
    private void SyncSelection()
    {
        var target = SelectedNode != null && _nodeToItemMap.TryGetValue(SelectedNode, out var item) ? item : null;
        _treeView?.SelectItem(target);
    }
    private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (_isBuildingTree) return;
        var node = e.NewValue is TreeViewItem item ? item.Tag as JsonTreeNode : e.NewValue as JsonTreeNode;
        SetCurrentValue(SelectedNodeProperty, node);
    }

    #endregion

    #region Status Bar

    /// <summary>
    /// Updates the status bar with current tree information.
    /// </summary>
    private void UpdateStatusBar()
    {
        string statusText;

        if (!string.IsNullOrEmpty(ParseError))
        {
            statusText = $"Parse error: {ParseError}";
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

        SetValue(StatusTextPropertyKey, statusText);
        UpdateStates();
    }

    private void UpdateStates()
    {
        if (!_hasTemplate) return;
        VisualStateManager.GoToState(this, !string.IsNullOrEmpty(ParseError) ? "Error" : RootNode == null ? "Empty" : "Ready", false);
        VisualStateManager.GoToState(this, string.IsNullOrWhiteSpace(SearchText) ? "Unfiltered" : "Filtered", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
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
        if (e.OriginalSource is TextBox) return;
        bool command = e.IsControlDown || (OperatingSystem.IsMacOS() && (e.KeyboardModifiers & ModifierKeys.Windows) != 0);
        if (e.Key == Key.C && command && SelectedNode != null)
        {
            CopyPathToClipboard(SelectedNode);
            e.Handled = true;
        }
        // F2 starts inline editing
        else if (e.Key == Key.F2 && IsEditable && SelectedNode != null)
        {
            e.Handled = BeginEditNode(SelectedNode);
        }
        // Ctrl+F focuses the search box
        else if (e.Key == Key.F && command)
        {
            _searchBox?.Focus();
            e.Handled = true;
        }
    }

    #endregion
}
