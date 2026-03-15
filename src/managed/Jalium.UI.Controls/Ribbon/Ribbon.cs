using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ribbon;

/// <summary>
/// Displays a Ribbon user interface.
/// </summary>
[ContentProperty("Items")]
public class Ribbon : ItemsControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Ribbon),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the IsMinimized dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsMinimizedProperty =
        DependencyProperty.Register(nameof(IsMinimized), typeof(bool), typeof(Ribbon),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(Ribbon),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectedIndex dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(Ribbon),
            new PropertyMetadata(0));

    #endregion

    /// <summary>
    /// Gets or sets the title of the Ribbon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the ribbon is minimized.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsMinimized
    {
        get => (bool)GetValue(IsMinimizedProperty);
        set => SetValue(IsMinimizedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the drop-down is open when minimized.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected tab index.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets the application menu.
    /// </summary>
    public RibbonApplicationMenu? ApplicationMenu { get; set; }

    /// <summary>
    /// Gets or sets the quick access toolbar.
    /// </summary>
    public RibbonQuickAccessToolBar? QuickAccessToolBar { get; set; }

    /// <summary>
    /// Gets or sets the help pane content.
    /// </summary>
    public object? HelpPaneContent { get; set; }
}

/// <summary>
/// Represents a tab on a Ribbon control.
/// </summary>
[ContentProperty("Items")]
public class RibbonTab : ItemsControl
{
    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(RibbonTab),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(RibbonTab),
            new PropertyMetadata(false));

    /// <summary>
    /// Gets or sets the header of the tab.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this tab is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets the contextual tab group header.
    /// </summary>
    public object? ContextualTabGroupHeader { get; set; }
}

/// <summary>
/// Represents a group of controls within a RibbonTab.
/// </summary>
[ContentProperty("Items")]
public class RibbonGroup : ItemsControl
{
    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(RibbonGroup),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the header of the group.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the small image source for the group.
    /// </summary>
    public ImageSource? SmallImageSource { get; set; }

    /// <summary>
    /// Gets or sets the large image source for the group.
    /// </summary>
    public ImageSource? LargeImageSource { get; set; }
}
