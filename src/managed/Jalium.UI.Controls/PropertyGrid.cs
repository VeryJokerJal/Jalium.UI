using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>A templated object inspector with categorized properties and replaceable row/editor templates.</summary>
[TemplatePart(Name = "PART_SearchTextBox", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_CategorizedButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_AlphabeticalButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_PropertiesList", Type = typeof(ItemsControl))]
[TemplateVisualState(Name = "Categorized", GroupName = "SortStates")]
[TemplateVisualState(Name = "Alphabetical", GroupName = "SortStates")]
[TemplateVisualState(Name = "SearchVisible", GroupName = "SearchStates")]
[TemplateVisualState(Name = "SearchHidden", GroupName = "SearchStates")]
[TemplateVisualState(Name = "ToolbarVisible", GroupName = "ToolbarStates")]
[TemplateVisualState(Name = "ToolbarHidden", GroupName = "ToolbarStates")]
[TemplateVisualState(Name = "DescriptionVisible", GroupName = "DescriptionStates")]
[TemplateVisualState(Name = "DescriptionHidden", GroupName = "DescriptionStates")]
[TemplateVisualState(Name = "Empty", GroupName = "DataStates")]
[TemplateVisualState(Name = "Ready", GroupName = "DataStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class PropertyGrid : Control
{
    static PropertyGrid() => DefaultStyleKeyProperty.OverrideMetadata(typeof(PropertyGrid), new PropertyMetadata(typeof(PropertyGrid)));
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.PropertyGridAutomationPeer(this);
    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedObject dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SelectedObjectProperty =
        DependencyProperty.Register(nameof(SelectedObject), typeof(object), typeof(PropertyGrid),
            new PropertyMetadata(null, OnSelectedObjectChanged));

    /// <summary>
    /// Identifies the SelectedObjects dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SelectedObjectsProperty =
        DependencyProperty.Register(nameof(SelectedObjects), typeof(IEnumerable), typeof(PropertyGrid),
            new PropertyMetadata(null, OnSelectedObjectsChanged));

    /// <summary>
    /// Identifies the SortMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SortModeProperty =
        DependencyProperty.Register(nameof(SortMode), typeof(PropertyGridSortMode), typeof(PropertyGrid),
            new FrameworkPropertyMetadata(PropertyGridSortMode.Categorized, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewPropertyChanged));

    /// <summary>
    /// Identifies the ShowSearchBox dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowSearchBoxProperty =
        DependencyProperty.Register(nameof(ShowSearchBox), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(true, OnStatePropertyChanged));

    /// <summary>
    /// Identifies the ShowDescription dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowDescriptionProperty =
        DependencyProperty.Register(nameof(ShowDescription), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(true, OnStatePropertyChanged));

    /// <summary>
    /// Identifies the ShowToolBar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowToolBarProperty =
        DependencyProperty.Register(nameof(ShowToolBar), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(true, OnStatePropertyChanged));

    /// <summary>
    /// Identifies the SearchText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(PropertyGrid),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewPropertyChanged));

    /// <summary>
    /// Identifies the SelectedProperty dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SelectedPropertyProperty =
        DependencyProperty.Register(nameof(SelectedProperty), typeof(PropertyItem), typeof(PropertyGrid),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedPropertyChanged));

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the NameColumnWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty NameColumnWidthProperty =
        DependencyProperty.Register(nameof(NameColumnWidth), typeof(double), typeof(PropertyGrid),
            new PropertyMetadata(150.0), value => value is double width && double.IsFinite(width) && width >= 0);

    /// <summary>
    /// Identifies the CategoryHeaderBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CategoryHeaderBackgroundProperty =
        DependencyProperty.Register(nameof(CategoryHeaderBackground), typeof(Brush), typeof(PropertyGrid),
            new PropertyMetadata(null, OnStatePropertyChanged));

    /// <summary>
    /// Identifies the CategoryHeaderForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CategoryHeaderForegroundProperty =
        DependencyProperty.Register(nameof(CategoryHeaderForeground), typeof(Brush), typeof(PropertyGrid),
            new PropertyMetadata(null, OnStatePropertyChanged));

    /// <summary>
    /// Identifies the PropertyNameForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty PropertyNameForegroundProperty =
        DependencyProperty.Register(nameof(PropertyNameForeground), typeof(Brush), typeof(PropertyGrid),
            new PropertyMetadata(null, OnStatePropertyChanged));

    /// <summary>
    /// Identifies the PropertyFilter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PropertyFilterProperty =
        DependencyProperty.Register(nameof(PropertyFilter), typeof(Predicate<PropertyItem>), typeof(PropertyGrid),
            new PropertyMetadata(null, OnViewPropertyChanged));

    /// <summary>
    /// Identifies the AutoGenerateProperties dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AutoGeneratePropertiesProperty =
        DependencyProperty.Register(nameof(AutoGenerateProperties), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(true, OnDiscoveryChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the PropertyValueChanged routed event.
    /// </summary>
    public static readonly RoutedEvent PropertyValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(PropertyValueChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<PropertyValueChangedEventArgs>), typeof(PropertyGrid));

    /// <summary>
    /// Identifies the SelectedPropertyChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedPropertyChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedPropertyChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<SelectedPropertyChangedEventArgs>), typeof(PropertyGrid));

    /// <summary>
    /// Identifies the PropertyEditingStarted routed event.
    /// </summary>
    public static readonly RoutedEvent PropertyEditingStartedEvent =
        EventManager.RegisterRoutedEvent(nameof(PropertyEditingStarted), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PropertyGrid));

    /// <summary>
    /// Identifies the PropertyEditingEnded routed event.
    /// </summary>
    public static readonly RoutedEvent PropertyEditingEndedEvent =
        EventManager.RegisterRoutedEvent(nameof(PropertyEditingEnded), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PropertyGrid));

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event EventHandler<PropertyValueChangedEventArgs> PropertyValueChanged
    {
        add => AddHandler(PropertyValueChangedEvent, value);
        remove => RemoveHandler(PropertyValueChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the selected property changes.
    /// </summary>
    public event EventHandler<SelectedPropertyChangedEventArgs> SelectedPropertyChanged
    {
        add => AddHandler(SelectedPropertyChangedEvent, value);
        remove => RemoveHandler(SelectedPropertyChangedEvent, value);
    }

    /// <summary>
    /// Occurs when property editing starts.
    /// </summary>
    public event RoutedEventHandler PropertyEditingStarted
    {
        add => AddHandler(PropertyEditingStartedEvent, value);
        remove => RemoveHandler(PropertyEditingStartedEvent, value);
    }

    /// <summary>
    /// Occurs when property editing ends.
    /// </summary>
    public event RoutedEventHandler PropertyEditingEnded
    {
        add => AddHandler(PropertyEditingEndedEvent, value);
        remove => RemoveHandler(PropertyEditingEndedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the object whose properties are displayed.
    /// </summary>
    public object? SelectedObject
    {
        get => GetValue(SelectedObjectProperty);
        set => SetValue(SelectedObjectProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of objects whose common properties are displayed.
    /// </summary>
    public IEnumerable? SelectedObjects
    {
        get => (IEnumerable?)GetValue(SelectedObjectsProperty);
        set => SetValue(SelectedObjectsProperty, value);
    }

    /// <summary>
    /// Gets or sets the sort mode (categorized or alphabetical).
    /// </summary>
    public PropertyGridSortMode SortMode
    {
        get => (PropertyGridSortMode)GetValue(SortModeProperty)!;
        set => SetValue(SortModeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the search box is visible.
    /// </summary>
    public bool ShowSearchBox
    {
        get => (bool)GetValue(ShowSearchBoxProperty)!;
        set => SetValue(ShowSearchBoxProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the description area is visible.
    /// </summary>
    public bool ShowDescription
    {
        get => (bool)GetValue(ShowDescriptionProperty)!;
        set => SetValue(ShowDescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the toolbar is visible.
    /// </summary>
    public bool ShowToolBar
    {
        get => (bool)GetValue(ShowToolBarProperty)!;
        set => SetValue(ShowToolBarProperty, value);
    }

    /// <summary>
    /// Gets or sets the current search text for filtering properties.
    /// </summary>
    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty)!;
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected property item.
    /// </summary>
    public PropertyItem? SelectedProperty
    {
        get => (PropertyItem?)GetValue(SelectedPropertyProperty);
        set => SetValue(SelectedPropertyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the property grid is in read-only mode.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the property name column.
    /// </summary>
    public double NameColumnWidth
    {
        get => (double)GetValue(NameColumnWidthProperty)!;
        set => SetValue(NameColumnWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for category headers.
    /// </summary>
    public Brush? CategoryHeaderBackground
    {
        get => (Brush?)GetValue(CategoryHeaderBackgroundProperty);
        set => SetValue(CategoryHeaderBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush for category headers.
    /// </summary>
    public Brush? CategoryHeaderForeground
    {
        get => (Brush?)GetValue(CategoryHeaderForegroundProperty);
        set => SetValue(CategoryHeaderForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush for property names.
    /// </summary>
    public Brush? PropertyNameForeground
    {
        get => (Brush?)GetValue(PropertyNameForegroundProperty);
        set => SetValue(PropertyNameForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets a filter predicate for properties.
    /// </summary>
    public Predicate<PropertyItem>? PropertyFilter
    {
        get => (Predicate<PropertyItem>?)GetValue(PropertyFilterProperty);
        set => SetValue(PropertyFilterProperty, value);
    }

    /// <summary>
    /// Gets or sets whether properties are automatically generated via reflection.
    /// </summary>
    public bool AutoGenerateProperties
    {
        get => (bool)GetValue(AutoGeneratePropertiesProperty)!;
        set => SetValue(AutoGeneratePropertiesProperty, value);
    }

    #endregion

    public static readonly DependencyProperty PropertyTemplateProperty = DependencyProperty.Register(
        nameof(PropertyTemplate), typeof(DataTemplate), typeof(PropertyGrid), new PropertyMetadata(null, OnViewPropertyChanged));
    public DataTemplate? PropertyTemplate { get => (DataTemplate?)GetValue(PropertyTemplateProperty); set => SetValue(PropertyTemplateProperty, value); }
    public static readonly DependencyProperty CategoryTemplateProperty = DependencyProperty.Register(
        nameof(CategoryTemplate), typeof(DataTemplate), typeof(PropertyGrid), new PropertyMetadata(null, OnViewPropertyChanged));
    public DataTemplate? CategoryTemplate { get => (DataTemplate?)GetValue(CategoryTemplateProperty); set => SetValue(CategoryTemplateProperty, value); }
    public static readonly DependencyProperty EditorTemplateProperty = DependencyProperty.Register(
        nameof(EditorTemplate), typeof(DataTemplate), typeof(PropertyGrid), new PropertyMetadata(null, OnEditorTemplatesChanged));
    public DataTemplate? EditorTemplate { get => (DataTemplate?)GetValue(EditorTemplateProperty); set => SetValue(EditorTemplateProperty, value); }
    public static readonly DependencyProperty EditorTemplateSelectorProperty = DependencyProperty.Register(
        nameof(EditorTemplateSelector), typeof(DataTemplateSelector), typeof(PropertyGrid), new PropertyMetadata(null, OnEditorTemplatesChanged));
    public DataTemplateSelector? EditorTemplateSelector { get => (DataTemplateSelector?)GetValue(EditorTemplateSelectorProperty); set => SetValue(EditorTemplateSelectorProperty, value); }
    private static readonly DependencyPropertyKey EntriesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Entries), typeof(ReadOnlyObservableCollection<object>), typeof(PropertyGrid), new PropertyMetadata(null));
    public static readonly DependencyProperty EntriesProperty = EntriesPropertyKey.DependencyProperty;
    public ReadOnlyObservableCollection<object> Entries => (ReadOnlyObservableCollection<object>)GetValue(EntriesProperty)!;
    private static readonly DependencyPropertyKey PropertiesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Properties), typeof(ReadOnlyObservableCollection<PropertyItem>), typeof(PropertyGrid), new PropertyMetadata(null));
    public static readonly DependencyProperty PropertiesProperty = PropertiesPropertyKey.DependencyProperty;
    public ReadOnlyObservableCollection<PropertyItem> Properties => (ReadOnlyObservableCollection<PropertyItem>)GetValue(PropertiesProperty)!;
    private static readonly DependencyPropertyKey VisiblePropertiesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(VisibleProperties), typeof(ReadOnlyObservableCollection<PropertyItem>), typeof(PropertyGrid), new PropertyMetadata(null));
    public static readonly DependencyProperty VisiblePropertiesProperty = VisiblePropertiesPropertyKey.DependencyProperty;
    public ReadOnlyObservableCollection<PropertyItem> VisibleProperties => (ReadOnlyObservableCollection<PropertyItem>)GetValue(VisiblePropertiesProperty)!;
    private static readonly DependencyPropertyKey EntryTemplateSelectorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(EntryTemplateSelector), typeof(DataTemplateSelector), typeof(PropertyGrid), new PropertyMetadata(null));
    public static readonly DependencyProperty EntryTemplateSelectorProperty = EntryTemplateSelectorPropertyKey.DependencyProperty;
    public DataTemplateSelector EntryTemplateSelector => (DataTemplateSelector)GetValue(EntryTemplateSelectorProperty)!;
    private static readonly DependencyPropertyKey IsEmptyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEmpty), typeof(bool), typeof(PropertyGrid), new PropertyMetadata(true));
    public static readonly DependencyProperty IsEmptyProperty = IsEmptyPropertyKey.DependencyProperty;
    public bool IsEmpty => (bool)GetValue(IsEmptyProperty)!;
    private static readonly DependencyPropertyKey PropertyCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PropertyCount), typeof(int), typeof(PropertyGrid), new PropertyMetadata(0));
    public static readonly DependencyProperty PropertyCountProperty = PropertyCountPropertyKey.DependencyProperty;
    public int PropertyCount => (int)GetValue(PropertyCountProperty)!;
    private static readonly DependencyPropertyKey SelectedPropertyNamePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedPropertyName), typeof(string), typeof(PropertyGrid), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SelectedPropertyNameProperty = SelectedPropertyNamePropertyKey.DependencyProperty;
    public string SelectedPropertyName => (string)GetValue(SelectedPropertyNameProperty)!;
    private static readonly DependencyPropertyKey SelectedPropertyDescriptionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedPropertyDescription), typeof(string), typeof(PropertyGrid), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SelectedPropertyDescriptionProperty = SelectedPropertyDescriptionPropertyKey.DependencyProperty;
    public string SelectedPropertyDescription => (string)GetValue(SelectedPropertyDescriptionProperty)!;
    private static readonly DependencyPropertyKey EditorRevisionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(EditorRevision), typeof(int), typeof(PropertyGrid), new PropertyMetadata(0));
    public static readonly DependencyProperty EditorRevisionProperty = EditorRevisionPropertyKey.DependencyProperty;
    public int EditorRevision => (int)GetValue(EditorRevisionProperty)!;

    private readonly ObservableCollection<PropertyItem> _properties = new();
    private readonly ObservableCollection<PropertyItem> _visibleProperties = new();
    private readonly ObservableCollection<object> _entries = new();
    private readonly Dictionary<string, PropertyGridCategory> _categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, Func<PropertyItem, PropertyGrid, FrameworkElement>> _customEditors = new();
    private readonly Dictionary<PropertyItem, WeakReference<PropertyGridEditor>> _editors = new();
    private readonly Dictionary<PropertyItem, WeakReference<PropertyGridRow>> _rows = new();
    private readonly Dictionary<PropertyItem, string> _pendingEdits = new();
    private TextBox? _searchBox;
    private ButtonBase? _categorizedButton;
    private ButtonBase? _alphabeticalButton;
    private ItemsControl? _propertiesList;
    private bool _hasTemplate;

    public PropertyGrid()
    {
        SetValue(PropertiesPropertyKey, new ReadOnlyObservableCollection<PropertyItem>(_properties));
        SetValue(VisiblePropertiesPropertyKey, new ReadOnlyObservableCollection<PropertyItem>(_visibleProperties));
        SetValue(EntriesPropertyKey, new ReadOnlyObservableCollection<object>(_entries));
        SetValue(EntryTemplateSelectorPropertyKey, new EntrySelector(this));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _hasTemplate = true;
        _searchBox = GetTemplateChild("PART_SearchTextBox") as TextBox;
        _categorizedButton = GetTemplateChild("PART_CategorizedButton") as ButtonBase;
        _alphabeticalButton = GetTemplateChild("PART_AlphabeticalButton") as ButtonBase;
        _propertiesList = GetTemplateChild("PART_PropertiesList") as ItemsControl;
        if (_categorizedButton != null) _categorizedButton.Click += OnCategorized;
        if (_alphabeticalButton != null) _alphabeticalButton.Click += OnAlphabetical;
        UpdateStates();
    }

    protected override void OnTemplateChanging()
    {
        _hasTemplate = false;
        RetireEditors(preserve: true);
        _rows.Clear();
        if (_categorizedButton != null) _categorizedButton.Click -= OnCategorized;
        if (_alphabeticalButton != null) _alphabeticalButton.Click -= OnAlphabetical;
        if (_searchBox != null) BindingOperations.ClearBinding(_searchBox, TextBox.TextProperty);
        if (_propertiesList != null)
        {
            BindingOperations.ClearBinding(_propertiesList, ItemsControl.ItemsSourceProperty);
            BindingOperations.ClearBinding(_propertiesList, ItemsControl.ItemTemplateSelectorProperty);
            _propertiesList.SetCurrentValue(ItemsControl.ItemsSourceProperty, null);
        }
        _searchBox = null;
        _categorizedButton = _alphabeticalButton = null;
        _propertiesList = null;
        base.OnTemplateChanging();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }

    public void RegisterCustomEditor(Type propertyType, Func<PropertyItem, PropertyGrid, FrameworkElement> editorFactory)
    {
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(editorFactory);
        _customEditors[propertyType] = editorFactory;
        IncrementEditorRevision();
    }
    internal Func<PropertyItem, PropertyGrid, FrameworkElement>? GetCustomEditor(Type propertyType) => _customEditors.GetValueOrDefault(propertyType);

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "PropertyGrid inspects runtime objects; applications must preserve their public property metadata and converters under AOT.")]
    public void RefreshProperties()
    {
        RetireEditors(preserve: false);
        _pendingEdits.Clear();
        _rows.Clear();
        _categories.Clear();
        _properties.Clear();
        SetCurrentValue(SelectedPropertyProperty, null);
        if (SelectedObject is { } instance && AutoGenerateProperties)
        {
            foreach (var info in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (info.GetIndexParameters().Length > 0 || info.GetMethod?.IsPublic != true || info.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false) continue;
                _properties.Add(new PropertyItem(instance, info));
            }
        }
        RebuildEntries();
    }

    private void RebuildEntries()
    {
        RetireEditors(preserve: true);
        _rows.Clear();
        _entries.Clear();
        _visibleProperties.Clear();
        var query = SearchText?.Trim() ?? string.Empty;
        var visible = _properties.Where(item => (PropertyFilter?.Invoke(item) ?? true) && (query.Length == 0
            || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var item in visible) _visibleProperties.Add(item);
        if (SortMode == PropertyGridSortMode.Categorized)
        {
            foreach (var group in visible.GroupBy(item => item.Category ?? "Misc", StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => string.Equals(group.Key, "Misc", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!_categories.TryGetValue(group.Key, out var category)) _categories[group.Key] = category = new PropertyGridCategory(group.Key);
                category.SetProperties(group);
                _entries.Add(category);
            }
        }
        else foreach (var item in visible) _entries.Add(item);
        SetValue(PropertyCountPropertyKey, visible.Length);
        SetValue(IsEmptyPropertyKey, visible.Length == 0);
        if (SelectedProperty is { } selected && !visible.Contains(GetRootProperty(selected))) SetCurrentValue(SelectedPropertyProperty, null);
        foreach (var key in _pendingEdits.Keys.Where(item => !visible.Contains(GetRootProperty(item))).ToArray()) _pendingEdits.Remove(key);
        UpdateStates();
    }

    private static PropertyItem GetRootProperty(PropertyItem item)
    {
        while (item.ParentProperty != null) item = item.ParentProperty;
        return item;
    }
    internal bool OwnsProperty(PropertyItem item)
    {
        while (item.ParentProperty is { } parent)
        {
            if (!parent.SubProperties.Contains(item)) return false;
            item = parent;
        }
        return _properties.Contains(item);
    }
    internal void RegisterEditor(PropertyGridEditor editor)
    {
        if (editor.Property is not { } item || !OwnsProperty(item)) return;
        _editors[item] = new(editor);
        editor.ActivateOwner();
        if (_pendingEdits.Remove(item, out var text)) editor.RestoreEdit(text);
    }
    internal void UnregisterEditor(PropertyGridEditor editor, PropertyItem? previous = null)
    {
        if ((previous ?? editor.Property) is { } item && _editors.TryGetValue(item, out var weak) && weak.TryGetTarget(out var current) && ReferenceEquals(editor, current)) _editors.Remove(item);
    }
    internal void RegisterRow(PropertyGridRow row)
    {
        if (row.Property is { } item && OwnsProperty(item)) _rows[item] = new(row);
    }
    internal void UnregisterRow(PropertyGridRow row, PropertyItem? previous = null)
    {
        if ((previous ?? row.Property) is { } item && _rows.TryGetValue(item, out var weak) && weak.TryGetTarget(out var current) && ReferenceEquals(row, current)) _rows.Remove(item);
    }
    private void RetireEditors(bool preserve)
    {
        foreach (var weak in _editors.Values.ToArray())
        {
            if (!weak.TryGetTarget(out var editor)) continue;
            if (preserve && editor.IsEditing && editor.Property is { } item) _pendingEdits[item] = editor.EditText;
            editor.Retire();
        }
        _editors.Clear();
    }
    internal bool MoveSelection(PropertyGridRow current, int delta)
    {
        var rows = _rows.Values.Select(weak => weak.TryGetTarget(out var row) ? row : null)
            .Where(row => row?.IsVisible == true && row.ActualHeight > 0)
            .Cast<PropertyGridRow>().OrderBy(row => row.TranslatePoint(new Point(0, 0), this).Y).ToArray();
        var index = Array.IndexOf(rows, current) + delta;
        if (index < 0 || index >= rows.Length) return false;
        var target = rows[index];
        SetCurrentValue(SelectedPropertyProperty, target.Property);
        return target.Focus();
    }
    internal void CommitPropertyValue(PropertyItem item, object? oldValue)
    {
        if (!OwnsProperty(item) || Equals(oldValue, item.Value)) return;
        RaiseEvent(new PropertyValueChangedEventArgs(PropertyValueChangedEvent, item.Name, oldValue, item.Value));
    }
    internal void NotifyEditingStarted(PropertyItem item)
    {
        SetCurrentValue(SelectedPropertyProperty, item);
        RaiseEvent(new RoutedEventArgs(PropertyEditingStartedEvent, this));
    }
    internal void NotifyEditingEnded() => RaiseEvent(new RoutedEventArgs(PropertyEditingEndedEvent, this));
    private void IncrementEditorRevision() => SetValue(EditorRevisionPropertyKey, EditorRevision + 1);
    private void OnCategorized(object sender, RoutedEventArgs e) => SetCurrentValue(SortModeProperty, PropertyGridSortMode.Categorized);
    private void OnAlphabetical(object sender, RoutedEventArgs e) => SetCurrentValue(SortModeProperty, PropertyGridSortMode.Alphabetical);
    private static void OnSelectedObjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGrid)d).RefreshProperties();
    private static void OnDiscoveryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGrid)d).RefreshProperties();
    private static void OnSelectedObjectsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        object? first = null;
        if (e.NewValue is IEnumerable values)
        {
            var enumerator = values.GetEnumerator();
            try { if (enumerator.MoveNext()) first = enumerator.Current; }
            finally { (enumerator as IDisposable)?.Dispose(); }
        }
        grid.SetCurrentValue(SelectedObjectProperty, first);
    }
    private static void OnViewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGrid)d).RebuildEntries();
    private static void OnStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGrid)d).UpdateStates();
    private static void OnEditorTemplatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGrid)d).IncrementEditorRevision();
    private static void OnSelectedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        var item = e.NewValue as PropertyItem;
        grid.SetValue(SelectedPropertyNamePropertyKey, item?.DisplayName ?? string.Empty);
        grid.SetValue(SelectedPropertyDescriptionPropertyKey, item?.Description ?? string.Empty);
        grid.UpdateStates();
        grid.RaiseEvent(new SelectedPropertyChangedEventArgs(SelectedPropertyChangedEvent, e.OldValue as PropertyItem, item));
    }
    private void UpdateStates()
    {
        if (!_hasTemplate) return;
        VisualStateManager.GoToState(this, SortMode == PropertyGridSortMode.Categorized ? "Categorized" : "Alphabetical", false);
        VisualStateManager.GoToState(this, ShowSearchBox ? "SearchVisible" : "SearchHidden", false);
        VisualStateManager.GoToState(this, ShowToolBar ? "ToolbarVisible" : "ToolbarHidden", false);
        VisualStateManager.GoToState(this, ShowDescription && !string.IsNullOrWhiteSpace(SelectedPropertyDescription) ? "DescriptionVisible" : "DescriptionHidden", true);
        VisualStateManager.GoToState(this, IsEmpty ? "Empty" : "Ready", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    private sealed class EntrySelector(PropertyGrid owner) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object? item, DependencyObject? container) => item is PropertyGridCategory ? owner.CategoryTemplate : owner.PropertyTemplate;
    }
}
