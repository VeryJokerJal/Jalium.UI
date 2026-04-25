using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays the properties of an object for editing, similar to the WinForms PropertyGrid.
/// Supports categorized and alphabetical views, search filtering, custom editors,
/// and read-only mode.
/// </summary>
public class PropertyGrid : Control
{
    private readonly PropertyEditorSelector _editorSelector = new();
    private readonly Dictionary<Type, Func<PropertyItem, PropertyGrid, FrameworkElement>> _customEditors = new();
    private readonly List<PropertyItem> _allPropertyItems = new();
    private readonly Dictionary<PropertyItem, FrameworkElement> _propertyRowMap = new();

    private Border? _toolBar;
    private Border? _searchBoxBorder;
    private TextBox? _searchBox;
    private Button? _categorizedButton;
    private Button? _alphabeticalButton;
    private ScrollViewer? _propertiesHost;
    private StackPanel? _propertiesPanel;
    private Border? _descriptionArea;
    private TextBlock? _descriptionTitle;
    private TextBlock? _descriptionText;

    // Default row brushes (used only when theme resources can't be resolved).
    private static readonly Brush s_defaultRowHoverBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
    private static readonly Brush s_defaultRowSelectedBrush = new SolidColorBrush(Color.FromArgb(56, 0x1E, 0x79, 0x3F));
    private static readonly Brush s_defaultSelectionAccentBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x79, 0x3F));

    // Description area animation (height + opacity, non-linear cubic ease-out).
    private const double DescriptionExpandMs = 260.0;
    private const double DescriptionCollapseMs = 180.0;
    // Floor keeps layout stable even if measurement briefly yields 0 (e.g. before
    // the first layout pass). The real target is always the measured DesiredSize.
    private const double DescriptionMinHeight = 36.0;

    private DispatcherTimer? _descriptionAnimationTimer;
    private readonly Stopwatch _descriptionStopwatch = new();
    private double _descriptionAnimationDurationMs = DescriptionExpandMs;
    private double _descriptionStartHeight;
    private double _descriptionStartOpacity;
    private double _descriptionTargetHeight;
    private double _descriptionTargetOpacity;
    private bool _descriptionVisible;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.PropertyGridAutomationPeer(this);
    }

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
            new PropertyMetadata(PropertyGridSortMode.Categorized, OnViewPropertyChanged));

    /// <summary>
    /// Identifies the ShowSearchBox dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowSearchBoxProperty =
        DependencyProperty.Register(nameof(ShowSearchBox), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowDescription dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowDescriptionProperty =
        DependencyProperty.Register(nameof(ShowDescription), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowToolBar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty ShowToolBarProperty =
        DependencyProperty.Register(nameof(ShowToolBar), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SearchText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(PropertyGrid),
            new PropertyMetadata(string.Empty, OnSearchTextChanged));

    /// <summary>
    /// Identifies the SelectedProperty dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SelectedPropertyProperty =
        DependencyProperty.Register(nameof(SelectedProperty), typeof(PropertyItem), typeof(PropertyGrid),
            new PropertyMetadata(null, OnSelectedPropertyChanged));

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(PropertyGrid),
            new PropertyMetadata(false, OnViewPropertyChanged));

    /// <summary>
    /// Identifies the NameColumnWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty NameColumnWidthProperty =
        DependencyProperty.Register(nameof(NameColumnWidth), typeof(double), typeof(PropertyGrid),
            new PropertyMetadata(150.0, OnViewPropertyChanged));

    /// <summary>
    /// Identifies the CategoryHeaderBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CategoryHeaderBackgroundProperty =
        DependencyProperty.Register(nameof(CategoryHeaderBackground), typeof(Brush), typeof(PropertyGrid),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CategoryHeaderForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CategoryHeaderForegroundProperty =
        DependencyProperty.Register(nameof(CategoryHeaderForeground), typeof(Brush), typeof(PropertyGrid),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the PropertyNameForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty PropertyNameForegroundProperty =
        DependencyProperty.Register(nameof(PropertyNameForeground), typeof(Brush), typeof(PropertyGrid),
            new PropertyMetadata(null, OnVisualPropertyChanged));

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
            new PropertyMetadata(true, OnViewPropertyChanged));

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

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Unhook old search box / buttons
        if (_searchBox != null)
        {
            _searchBox.TextChanged -= OnSearchBoxTextChanged;
        }
        if (_categorizedButton != null)
        {
            _categorizedButton.Click -= OnCategorizedButtonClick;
        }
        if (_alphabeticalButton != null)
        {
            _alphabeticalButton.Click -= OnAlphabeticalButtonClick;
        }

        _toolBar = GetTemplateChild("PART_ToolBar") as Border;
        _searchBoxBorder = GetTemplateChild("PART_SearchBox") as Border;
        _searchBox = GetTemplateChild("PART_SearchTextBox") as TextBox;
        _categorizedButton = GetTemplateChild("PART_CategorizedButton") as Button;
        _alphabeticalButton = GetTemplateChild("PART_AlphabeticalButton") as Button;
        _propertiesHost = GetTemplateChild("PART_PropertiesHost") as ScrollViewer;
        _propertiesPanel = GetTemplateChild("PART_PropertiesPanel") as StackPanel;
        _descriptionArea = GetTemplateChild("PART_DescriptionArea") as Border;
        _descriptionTitle = GetTemplateChild("PART_PropertyNameText") as TextBlock;
        _descriptionText = GetTemplateChild("PART_PropertyDescriptionText") as TextBlock;

        if (_searchBox != null)
        {
            _searchBox.TextChanged += OnSearchBoxTextChanged;
            _searchBox.Text = SearchText ?? string.Empty;
        }
        if (_categorizedButton != null)
        {
            _categorizedButton.Click += OnCategorizedButtonClick;
        }
        if (_alphabeticalButton != null)
        {
            _alphabeticalButton.Click += OnAlphabeticalButtonClick;
        }

        // Reset description area to collapsed state; animation will open it
        // when a property with a description becomes selected.
        if (_descriptionArea != null)
        {
            _descriptionArea.Height = 0;
            _descriptionArea.Opacity = 0;
            _descriptionArea.Visibility = Visibility.Collapsed;
        }
        _descriptionVisible = false;

        UpdateSortModeButtons();
        UpdateVisibility();
        RebuildView();
    }

    private void OnCategorizedButtonClick(object? sender, RoutedEventArgs e)
    {
        SortMode = PropertyGridSortMode.Categorized;
    }

    private void OnAlphabeticalButtonClick(object? sender, RoutedEventArgs e)
    {
        SortMode = PropertyGridSortMode.Alphabetical;
    }

    private void UpdateSortModeButtons()
    {
        var activeStyle = TryFindResource("PropertyGridToolbarButtonActiveStyle") as Style;
        var inactiveStyle = TryFindResource("PropertyGridToolbarButtonStyle") as Style;
        var isCategorized = SortMode == PropertyGridSortMode.Categorized;

        if (_categorizedButton != null)
        {
            _categorizedButton.Style = isCategorized ? activeStyle : inactiveStyle;
        }
        if (_alphabeticalButton != null)
        {
            _alphabeticalButton.Style = isCategorized ? inactiveStyle : activeStyle;
        }
    }

    #endregion

    #region Property Change Callbacks

    private static void OnSelectedObjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        grid.OnSelectedObjectChanged();
    }

    private static void OnSelectedObjectsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        // When SelectedObjects is set, use the first object
        if (e.NewValue is IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            if (enumerator.MoveNext())
            {
                grid.SelectedObject = enumerator.Current;
                return;
            }
        }
        grid.SelectedObject = null;
    }

    private static void OnViewPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        grid.UpdateSortModeButtons();
        grid.RebuildView();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        grid.UpdateVisibility();
        grid.RebuildView();
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        grid.FilterProperties((string?)e.NewValue ?? string.Empty);
    }

    private static void OnSelectedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (PropertyGrid)d;
        var oldProp = e.OldValue as PropertyItem;
        var newProp = e.NewValue as PropertyItem;
        grid.OnPropertyRowSelected(oldProp, newProp);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers a custom editor factory for properties of the specified type.
    /// </summary>
    public void RegisterCustomEditor(Type propertyType, Func<PropertyItem, PropertyGrid, FrameworkElement> editorFactory)
    {
        _customEditors[propertyType] = editorFactory;
    }

    /// <summary>
    /// Rebuilds the property view from the current selected object.
    /// </summary>
    public void RefreshProperties()
    {
        OnSelectedObjectChanged();
    }

    /// <summary>
    /// Commits a property value change and raises the <see cref="PropertyValueChanged"/> event.
    /// </summary>
    internal void CommitPropertyValue(PropertyItem item, object? oldValue)
    {
        RaiseEvent(new PropertyValueChangedEventArgs(
            PropertyValueChangedEvent, item.Name, oldValue, item.Value));
    }

    /// <summary>
    /// Gets a custom editor factory for the specified type, if registered.
    /// </summary>
    internal Func<PropertyItem, PropertyGrid, FrameworkElement>? GetCustomEditor(Type propertyType)
    {
        _customEditors.TryGetValue(propertyType, out var factory);
        return factory;
    }

    /// <summary>
    /// Creates a property row (Grid with name label and editor) for the given property item.
    /// This is also used by <see cref="PropertyEditorSelector"/> for expandable sub-properties.
    /// </summary>
    internal FrameworkElement CreatePropertyRow(PropertyItem item)
    {
        return CreatePropertyRowInternal(item);
    }

    #endregion

    #region Private Methods

    private void OnSelectedObjectChanged()
    {
        _allPropertyItems.Clear();
        _propertyRowMap.Clear();

        var obj = SelectedObject;
        if (obj != null && AutoGenerateProperties)
        {
            BuildPropertyItems(obj);
        }

        SelectedProperty = null;
        RebuildView();
    }

    private void BuildPropertyItems(object obj)
    {
        var type = obj.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            // Skip indexer properties
            if (prop.GetIndexParameters().Length > 0)
                continue;

            // Respect BrowsableAttribute
            var browsable = prop.GetCustomAttribute<BrowsableAttribute>();
            if (browsable?.Browsable == false)
                continue;

            // Skip properties that can't be read
            if (!prop.CanRead)
                continue;

            var item = new PropertyItem(obj, prop);

            // Apply the external filter
            var filter = PropertyFilter;
            if (filter != null && !filter(item))
                continue;

            _allPropertyItems.Add(item);
        }
    }

    private void EnsurePropertiesPanel()
    {
        if (_propertiesPanel != null) return;

        // Fallback: create full layout programmatically when no template is applied
        _propertiesPanel = new StackPanel { Orientation = Orientation.Vertical };
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scrollViewer.Content = _propertiesPanel;

        // Description area at the bottom
        _descriptionArea = new Border
        {
            MinHeight = 50,
            Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 4, 8, 4)
        };

        // Combine into a vertical layout
        var rootPanel = new Grid();
        rootPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scrollViewer, 0);
        Grid.SetRow(_descriptionArea, 1);
        rootPanel.Children.Add(scrollViewer);
        rootPanel.Children.Add(_descriptionArea);

        _fallbackRoot = rootPanel;
        AddVisualChild(_fallbackRoot);
    }

    private FrameworkElement? _fallbackRoot;

    /// <inheritdoc />
    public override int VisualChildrenCount => _fallbackRoot != null && Template == null ? 1 : base.VisualChildrenCount;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (_fallbackRoot != null && Template == null && index == 0) return _fallbackRoot;
        return base.GetVisualChild(index);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_fallbackRoot != null && Template == null)
        {
            _fallbackRoot.Measure(availableSize);
            return _fallbackRoot.DesiredSize;
        }
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_fallbackRoot != null && Template == null)
        {
            _fallbackRoot.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            return finalSize;
        }
        return base.ArrangeOverride(finalSize);
    }

    private void RebuildView()
    {
        EnsurePropertiesPanel();
        if (_propertiesPanel == null)
            return;

        _propertiesPanel.Children.Clear();
        _propertyRowMap.Clear();

        if (_allPropertyItems.Count == 0)
            return;

        if (SortMode == PropertyGridSortMode.Categorized)
        {
            BuildCategorizedView();
        }
        else
        {
            BuildAlphabeticalView();
        }

        // Apply current search filter
        var searchText = SearchText;
        if (!string.IsNullOrEmpty(searchText))
        {
            FilterProperties(searchText);
        }
    }

    private void BuildCategorizedView()
    {
        if (_propertiesPanel == null)
            return;

        // Group by category
        var groups = new Dictionary<string, List<PropertyItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _allPropertyItems)
        {
            var category = item.Category ?? "Misc";
            if (!groups.TryGetValue(category, out var list))
            {
                list = new List<PropertyItem>();
                groups[category] = list;
            }
            list.Add(item);
        }

        // Sort categories alphabetically, but "Misc" goes last
        var sortedCategories = groups.Keys.OrderBy(c =>
            string.Equals(c, "Misc", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var categoryExpanderStyle = TryFindResource("PropertyGridCategoryExpanderStyle") as Style;

        foreach (var category in sortedCategories)
        {
            var items = groups[category];
            items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            var categoryPanel = new StackPanel { Orientation = Orientation.Vertical };

            foreach (var item in items)
            {
                var row = CreatePropertyRowInternal(item);
                _propertyRowMap[item] = row;
                categoryPanel.Children.Add(row);
            }

            var headerText = new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (CategoryHeaderForeground != null)
            {
                headerText.Foreground = CategoryHeaderForeground;
            }
            else if (TryFindResource("TextFillColorSecondaryBrush") is Brush secondary)
            {
                headerText.Foreground = secondary;
            }

            var expander = new Expander
            {
                Header = headerText,
                IsExpanded = true,
                Content = categoryPanel,
                Margin = new Thickness(0, 0, 0, 2)
            };

            if (categoryExpanderStyle != null)
            {
                expander.Style = categoryExpanderStyle;
            }

            if (CategoryHeaderBackground != null)
            {
                expander.HeaderBackground = CategoryHeaderBackground;
            }

            _propertiesPanel.Children.Add(expander);
        }
    }

    private void BuildAlphabeticalView()
    {
        if (_propertiesPanel == null)
            return;

        var sorted = _allPropertyItems
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in sorted)
        {
            var row = CreatePropertyRowInternal(item);
            _propertyRowMap[item] = row;
            _propertiesPanel.Children.Add(row);
        }
    }

    private FrameworkElement CreatePropertyRowInternal(PropertyItem item)
    {
        // Row container: Border supports IsMouseOver-style background; we use
        // MouseEnter/Leave handlers since Border's style system already applies
        // other visual settings in themes.
        var rowBorder = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 4, 0)
        };

        var grid = new Grid();

        // Column 0: 2px accent bar (visible only when selected)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Pixel) });
        // Column 1: name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameColumnWidth, GridUnitType.Pixel) });
        // Column 2: editor
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accentBar = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 3, 4, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(accentBar, 0);
        grid.Children.Add(accentBar);

        var nameLabel = new TextBlock
        {
            Text = item.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 4, 8, 4),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        if (PropertyNameForeground != null)
        {
            nameLabel.Foreground = PropertyNameForeground;
        }
        else if (TryFindResource("TextFillColorSecondaryBrush") is Brush secondary)
        {
            nameLabel.Foreground = secondary;
        }

        Grid.SetColumn(nameLabel, 1);
        grid.Children.Add(nameLabel);

        var editor = _editorSelector.CreateEditor(item, this);
        Grid.SetColumn(editor, 2);
        grid.Children.Add(editor);

        rowBorder.Child = grid;

        // Store the accent bar so UpdateRowHighlights can toggle it.
        rowBorder.Tag = accentBar;

        // Hover + selection wiring
        rowBorder.AddHandler(MouseEnterEvent, new MouseEventHandler((_, _) =>
        {
            if (!ReferenceEquals(SelectedProperty, item))
            {
                rowBorder.Background = ResolveRowHoverBrush();
            }
        }));

        rowBorder.AddHandler(MouseLeaveEvent, new MouseEventHandler((_, _) =>
        {
            if (!ReferenceEquals(SelectedProperty, item))
            {
                rowBorder.Background = Brushes.Transparent;
            }
        }));

        rowBorder.AddHandler(MouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            SelectedProperty = item;
            RaiseEvent(new RoutedEventArgs(PropertyEditingStartedEvent, this));
            e.Handled = true;
        }));

        editor.LostFocus += (_, _) =>
        {
            RaiseEvent(new RoutedEventArgs(PropertyEditingEndedEvent, this));
        };

        return rowBorder;
    }

    private Brush ResolveRowHoverBrush()
        => TryFindResource("SubtleFillColorSecondaryBrush") as Brush ?? s_defaultRowHoverBrush;

    private Brush ResolveRowSelectedBrush()
        => TryFindResource("PropertyGridSelectionBackground") as Brush
           ?? TryFindResource("SubtleFillColorTertiaryBrush") as Brush
           ?? s_defaultRowSelectedBrush;

    private Brush ResolveSelectionAccentBrush()
        => TryFindResource("AccentBrush") as Brush ?? s_defaultSelectionAccentBrush;

    private void FilterProperties(string searchText)
    {
        if (_propertiesPanel == null)
            return;

        var isEmpty = string.IsNullOrWhiteSpace(searchText);

        foreach (var kvp in _propertyRowMap)
        {
            var item = kvp.Key;
            var row = kvp.Value;

            if (isEmpty)
            {
                row.Visibility = Visibility.Visible;
            }
            else
            {
                var matches = item.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                           || item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                           || (item.Category?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true);
                row.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void OnPropertyRowSelected(PropertyItem? oldProperty, PropertyItem? newProperty)
    {
        // Highlight the selected row
        UpdateRowHighlights(oldProperty, newProperty);

        // Update description area
        UpdateDescription(newProperty);

        // Raise event
        RaiseEvent(new SelectedPropertyChangedEventArgs(
            SelectedPropertyChangedEvent, oldProperty, newProperty));
    }

    private void UpdateRowHighlights(PropertyItem? oldProperty, PropertyItem? newProperty)
    {
        if (oldProperty != null && _propertyRowMap.TryGetValue(oldProperty, out var oldRow))
        {
            if (oldRow is Border oldBorder)
            {
                // Preserve hover state if the pointer is still over the row.
                oldBorder.Background = oldBorder.IsMouseOver
                    ? ResolveRowHoverBrush()
                    : Brushes.Transparent;
                if (oldBorder.Tag is Border oldAccent)
                {
                    oldAccent.Background = Brushes.Transparent;
                }
            }
        }

        if (newProperty != null && _propertyRowMap.TryGetValue(newProperty, out var newRow))
        {
            if (newRow is Border newBorder)
            {
                newBorder.Background = ResolveRowSelectedBrush();
                if (newBorder.Tag is Border newAccent)
                {
                    newAccent.Background = ResolveSelectionAccentBrush();
                }
            }
        }
    }

    private void UpdateDescription(PropertyItem? property)
    {
        if (_descriptionArea == null)
            return;

        var description = property?.Description;
        var hasDescription = ShowDescription
                             && property != null
                             && !string.IsNullOrWhiteSpace(description);

        if (hasDescription)
        {
            EnsureDescriptionChildren();

            if (_descriptionTitle != null)
                _descriptionTitle.Text = property!.DisplayName;
            if (_descriptionText != null)
                _descriptionText.Text = description!;
        }
        else
        {
            if (_descriptionTitle != null)
                _descriptionTitle.Text = string.Empty;
            if (_descriptionText != null)
                _descriptionText.Text = string.Empty;
        }

        AnimateDescription(hasDescription);
    }

    /// <summary>
    /// Animates the description area's height + opacity with a non-linear
    /// cubic ease-out, hiding it entirely when there's nothing to show.
    /// The target height is measured from the actual content so longer
    /// descriptions get taller panels and shorter descriptions shrink.
    /// </summary>
    private void AnimateDescription(bool show)
    {
        if (_descriptionArea == null)
            return;

        // Capture current visual state so re-triggered animations blend smoothly.
        var currentHeight = double.IsNaN(_descriptionArea.Height) ? _descriptionArea.ActualHeight : _descriptionArea.Height;
        var currentOpacity = _descriptionArea.Opacity;

        double targetHeight;
        double targetOpacity;

        if (show)
        {
            _descriptionArea.Visibility = Visibility.Visible;
            targetHeight = MeasureDescriptionNaturalHeight();
            targetOpacity = 1.0;
        }
        else
        {
            targetHeight = 0.0;
            targetOpacity = 0.0;
        }

        // Fast path: already at target and no animation in flight.
        if (show == _descriptionVisible
            && _descriptionAnimationTimer?.IsEnabled != true
            && Math.Abs(currentHeight - targetHeight) < 0.5
            && Math.Abs(currentOpacity - targetOpacity) < 0.01)
        {
            return;
        }

        _descriptionVisible = show;
        _descriptionAnimationDurationMs = show ? DescriptionExpandMs : DescriptionCollapseMs;
        _descriptionStartHeight = currentHeight;
        _descriptionStartOpacity = currentOpacity;
        _descriptionTargetHeight = targetHeight;
        _descriptionTargetOpacity = targetOpacity;

        if (Math.Abs(_descriptionStartHeight - _descriptionTargetHeight) < 0.5
            && Math.Abs(_descriptionStartOpacity - _descriptionTargetOpacity) < 0.01)
        {
            // Already at target — snap without running the timer.
            ApplyDescriptionFinalState();
            return;
        }

        // Prime the starting frame so the first tick doesn't jump when the
        // panel was previously sized to Auto/NaN.
        _descriptionArea.Height = currentHeight;
        _descriptionArea.Opacity = currentOpacity;

        _descriptionStopwatch.Restart();

        if (_descriptionAnimationTimer == null)
        {
            _descriptionAnimationTimer = new DispatcherTimer
            {
                Interval = CompositionTarget.FrameInterval
            };
            _descriptionAnimationTimer.Tick += OnDescriptionAnimationTick;
        }
        _descriptionAnimationTimer.Start();
    }

    private double MeasureDescriptionNaturalHeight()
    {
        if (_descriptionArea == null)
            return DescriptionMinHeight;

        _descriptionArea.Height = double.NaN;
        _descriptionArea.InvalidateMeasure();

        var availableWidth = _descriptionArea.ActualWidth > 0
            ? _descriptionArea.ActualWidth
            : (_descriptionArea.VisualParent is FrameworkElement parent && parent.ActualWidth > 0
                ? parent.ActualWidth
                : double.PositiveInfinity);

        _descriptionArea.Measure(new Size(availableWidth, double.PositiveInfinity));
        var desired = _descriptionArea.DesiredSize.Height;

        return desired > 0.0 ? desired : DescriptionMinHeight;
    }

    private void ApplyDescriptionFinalState()
    {
        if (_descriptionArea == null)
            return;

        _descriptionAnimationTimer?.Stop();
        _descriptionStopwatch.Stop();

        if (_descriptionVisible)
        {
            // Auto-sized — content changes (new description text) will reflow naturally.
            _descriptionArea.Height = double.NaN;
            _descriptionArea.Opacity = 1.0;
        }
        else
        {
            _descriptionArea.Height = 0.0;
            _descriptionArea.Opacity = 0.0;
            _descriptionArea.Visibility = Visibility.Collapsed;
        }
    }

    private void OnDescriptionAnimationTick(object? sender, EventArgs e)
    {
        if (_descriptionArea == null)
        {
            _descriptionAnimationTimer?.Stop();
            return;
        }

        var elapsed = _descriptionStopwatch.Elapsed.TotalMilliseconds;
        var t = Math.Min(1.0, elapsed / Math.Max(1.0, _descriptionAnimationDurationMs));

        // Non-linear: cubic ease-out on expand, cubic ease-in on collapse.
        var eased = _descriptionVisible ? EaseOutCubic(t) : EaseInCubic(t);

        var height = _descriptionStartHeight + (_descriptionTargetHeight - _descriptionStartHeight) * eased;
        var opacity = _descriptionStartOpacity + (_descriptionTargetOpacity - _descriptionStartOpacity) * eased;

        _descriptionArea.Height = height;
        _descriptionArea.Opacity = opacity;

        if (t >= 1.0)
        {
            ApplyDescriptionFinalState();
        }
    }

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);

    private static double EaseInCubic(double t) => t * t * t;

    private void EnsureDescriptionChildren()
    {
        if (_descriptionTitle != null || _descriptionArea == null)
            return;

        var panel = new StackPanel { Orientation = Orientation.Vertical };

        _descriptionTitle = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 3)
        };

        if (TryFindResource("TextFillColorPrimaryBrush") is Brush primary)
        {
            _descriptionTitle.Foreground = primary;
        }

        _descriptionText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11
        };

        if (TryFindResource("TextFillColorSecondaryBrush") is Brush secondary)
        {
            _descriptionText.Foreground = secondary;
        }
        else
        {
            _descriptionText.Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        }

        panel.Children.Add(_descriptionTitle);
        panel.Children.Add(_descriptionText);

        _descriptionArea.Child = panel;
    }

    private void UpdateVisibility()
    {
        if (_toolBar != null)
            _toolBar.Visibility = ShowToolBar ? Visibility.Visible : Visibility.Collapsed;
        if (_searchBoxBorder != null)
            _searchBoxBorder.Visibility = ShowSearchBox ? Visibility.Visible : Visibility.Collapsed;
        else if (_searchBox != null)
            _searchBox.Visibility = ShowSearchBox ? Visibility.Visible : Visibility.Collapsed;

        // Description area visibility is driven by AnimateDescription based on
        // both ShowDescription and whether the selected property has text to show.
        UpdateDescription(SelectedProperty);
    }

    private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchBox != null)
        {
            SearchText = _searchBox.Text;
        }
    }

    #endregion
}
