namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Displays a set of related items in a Ribbon control.
/// </summary>
[ContentProperty("Items")]
public class RibbonGallery : ItemsControl
{
    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(RibbonGallery),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the SelectedValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(RibbonGallery),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the selected item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of columns.
    /// </summary>
    public int MinColumnCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of columns.
    /// </summary>
    public int MaxColumnCount { get; set; } = int.MaxValue;

    /// <summary>
    /// Gets or sets whether the gallery can be filtered by user input.
    /// </summary>
    public bool CanUserFilter { get; set; }

    /// <summary>
    /// Occurs when the selection changes.
    /// </summary>
    public event RoutedEventHandler? SelectionChanged;

    /// <summary>
    /// Raises the SelectionChanged event.
    /// </summary>
    protected void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this, new RoutedEventArgs());
    }
}

/// <summary>
/// Represents a category within a RibbonGallery.
/// </summary>
[ContentProperty("Items")]
public class RibbonGalleryCategory : ItemsControl
{
    /// <summary>
    /// Gets or sets the header of the category.
    /// </summary>
    public object? Header { get; set; }

    /// <summary>
    /// Gets or sets the minimum number of columns for this category.
    /// </summary>
    public int MinColumnCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of columns for this category.
    /// </summary>
    public int MaxColumnCount { get; set; } = int.MaxValue;
}

/// <summary>
/// Represents an item within a RibbonGalleryCategory.
/// </summary>
public class RibbonGalleryItem : ContentControl
{
    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(RibbonGalleryItem),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets or sets whether this item is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets the key tip text.
    /// </summary>
    public string? KeyTip { get; set; }
}
