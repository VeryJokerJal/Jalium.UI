using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a frame around a group of controls with an optional caption.
/// </summary>
public class GroupBox : HeaderedContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.GroupBoxAutomationPeer(this);
    }

    #region Dependency Properties

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

    private Panel? _headerBorder;
    private Border? _contentBorder;

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _headerBorder = GetTemplateChild("PART_HeaderBorder") as Panel;
        _contentBorder = GetTemplateChild("PART_ContentBorder") as Border;
        UpdateHeaderVisibility();
        UpdateHeaderBackground();
    }

    #endregion

    #region Property Changed Callbacks

    /// <inheritdoc />
    protected override void OnHeaderChanged(object? oldHeader, object? newHeader)
    {
        base.OnHeaderChanged(oldHeader, newHeader);
        UpdateHeaderVisibility();
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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

    /// <inheritdoc />
    protected override void OnAccessKey(Jalium.UI.Input.AccessKeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnAccessKey(e);
        if (!e.IsMultiple)
        {
            MoveFocus(new Jalium.UI.Input.TraversalRequest(
                Jalium.UI.Input.FocusNavigationDirection.First));
        }
    }
}
