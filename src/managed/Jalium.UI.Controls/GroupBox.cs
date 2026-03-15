using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a frame around a group of controls with an optional caption.
/// </summary>
public class GroupBox : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.GroupBoxAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(GroupBox),
            new PropertyMetadata(null, OnHeaderChanged));

    /// <summary>
    /// Identifies the HeaderBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderBackgroundProperty =
        DependencyProperty.Register(nameof(HeaderBackground), typeof(Brush), typeof(GroupBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the header area.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Brush? HeaderBackground
    {
        get => (Brush?)GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupBox"/> class.
    /// </summary>
    public GroupBox()
    {
        UseTemplateContentManagement();
    }

    #endregion

    #region Template Parts

    private Border? _headerBorder;
    private Border? _contentBorder;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _headerBorder = GetTemplateChild("PART_HeaderBorder") as Border;
        _contentBorder = GetTemplateChild("PART_ContentBorder") as Border;
        UpdateHeaderVisibility();
        UpdateHeaderBackground();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GroupBox groupBox)
        {
            groupBox.UpdateHeaderVisibility();
            groupBox.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GroupBox groupBox)
        {
            groupBox.UpdateHeaderBackground();
            groupBox.InvalidateVisual();
        }
    }

    #endregion

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == BackgroundProperty)
            UpdateHeaderBackground();
    }

    private void UpdateHeaderVisibility()
    {
        if (_headerBorder != null)
            _headerBorder.Visibility = Header != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHeaderBackground()
    {
        if (_headerBorder != null)
            _headerBorder.Background = HeaderBackground ?? Background;
    }
}
