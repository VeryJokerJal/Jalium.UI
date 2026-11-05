using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a message to the user with an optional close button.
/// Used for showing informational, success, warning, or error messages.
/// </summary>
[TemplatePart(Name = "PART_CloseButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Content", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_Action", Type = typeof(ContentPresenter))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Open", GroupName = "OpenStates")]
[TemplateVisualState(Name = "Closed", GroupName = "OpenStates")]
[TemplateVisualState(Name = "Information", GroupName = "SeverityStates")]
[TemplateVisualState(Name = "Success", GroupName = "SeverityStates")]
[TemplateVisualState(Name = "Warning", GroupName = "SeverityStates")]
[TemplateVisualState(Name = "Error", GroupName = "SeverityStates")]
[TemplateVisualState(Name = "IconVisible", GroupName = "IconStates")]
[TemplateVisualState(Name = "IconHidden", GroupName = "IconStates")]
[TemplateVisualState(Name = "DefaultIcon", GroupName = "IconSourceStates")]
[TemplateVisualState(Name = "CustomIcon", GroupName = "IconSourceStates")]
[TemplateVisualState(Name = "Closable", GroupName = "CloseStates")]
[TemplateVisualState(Name = "NotClosable", GroupName = "CloseStates")]
[TemplateVisualState(Name = "HasTitle", GroupName = "TitleStates")]
[TemplateVisualState(Name = "NoTitle", GroupName = "TitleStates")]
[TemplateVisualState(Name = "HasMessage", GroupName = "MessageStates")]
[TemplateVisualState(Name = "NoMessage", GroupName = "MessageStates")]
[TemplateVisualState(Name = "HasAction", GroupName = "ActionStates")]
[TemplateVisualState(Name = "NoAction", GroupName = "ActionStates")]
public class InfoBar : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.InfoBarAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(InfoBar),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Message dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(InfoBar),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Severity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar),
            new PropertyMetadata(InfoBarSeverity.Informational, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(InfoBar),
            new PropertyMetadata(true, OnIsOpenChanged));

    /// <summary>
    /// Identifies the IsClosable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsClosableProperty =
        DependencyProperty.Register(nameof(IsClosable), typeof(bool), typeof(InfoBar),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsIconVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsIconVisibleProperty =
        DependencyProperty.Register(nameof(IsIconVisible), typeof(bool), typeof(InfoBar),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ActionButton dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ActionButtonProperty =
        DependencyProperty.Register(nameof(ActionButton), typeof(ButtonBase), typeof(InfoBar),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    #endregion

    /// <summary>Identifies the optional custom severity icon source.</summary>
    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(nameof(IconSource), typeof(IconSource), typeof(InfoBar), new PropertyMetadata(null, OnVisualPropertyChanged));
    /// <summary>Gets or sets a custom icon while preserving IsIconVisible and the severity presentation.</summary>
    public IconSource? IconSource { get => (IconSource?)GetValue(IconSourceProperty); set => SetValue(IconSourceProperty, value); }

    #region Routed Events

    /// <summary>
    /// Identifies the Closed routed event.
    /// </summary>
    public static readonly RoutedEvent ClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(Closed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(InfoBar));

    /// <summary>
    /// Identifies the CloseButtonClick routed event.
    /// </summary>
    public static readonly RoutedEvent CloseButtonClickEvent =
        EventManager.RegisterRoutedEvent(nameof(CloseButtonClick), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(InfoBar));

    /// <summary>
    /// Occurs when the InfoBar is closed.
    /// </summary>
    public event RoutedEventHandler Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    /// <summary>
    /// Occurs when the close button is clicked.
    /// </summary>
    public event RoutedEventHandler CloseButtonClick
    {
        add => AddHandler(CloseButtonClickEvent, value);
        remove => RemoveHandler(CloseButtonClickEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the title text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the message text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Gets or sets the severity level of the InfoBar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty)!;
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the InfoBar is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the InfoBar shows a close button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty)!;
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the severity icon is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsIconVisible
    {
        get => (bool)GetValue(IsIconVisibleProperty)!;
        set => SetValue(IsIconVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional action button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ButtonBase? ActionButton
    {
        get => (ButtonBase?)GetValue(ActionButtonProperty);
        set => SetValue(ActionButtonProperty, value);
    }

    #endregion

    private ButtonBase? _closeButton;
    private ContentPresenter? _contentHost, _actionHost;

    static InfoBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(InfoBar), new PropertyMetadata(typeof(InfoBar)));
        VisibilityProperty.OverrideMetadata(typeof(InfoBar), new PropertyMetadata(Visibility.Visible, null,
            (owner, value) => ((InfoBar)owner).IsOpen ? value : Visibility.Collapsed));
    }

    /// <summary>Initializes a message control whose presentation is supplied entirely by its template.</summary>
    public InfoBar() { }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _closeButton = GetTemplateChild("PART_CloseButton") as ButtonBase;
        if (_closeButton is not null) _closeButton.Click += OnCloseClick;
        _contentHost = GetTemplateChild("PART_Content") as ContentPresenter;
        _actionHost = GetTemplateChild("PART_Action") as ContentPresenter;
        UpdateVisualStates();
    }

    /// <inheritdoc />
    protected override void OnTemplateChanging() { ReleaseParts(); base.OnTemplateChanging(); }

    private void ReleaseParts()
    {
        if (_closeButton is not null) _closeButton.Click -= OnCloseClick;
        _closeButton = null; ReleaseContent(_contentHost); ReleaseContent(_actionHost); _contentHost = _actionHost = null;
    }
    private static void ReleaseContent(ContentPresenter? presenter)
    {
        if (presenter is null) return;
        presenter.ClearBinding(ContentPresenter.ContentProperty); presenter.ClearValue(ContentPresenter.ContentProperty);
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => TryClose();

    /// <summary>Closes an enabled, open and closable message, raises both close events, and preserves its IsOpen binding.</summary>
    public bool TryClose()
    {
        if (!IsEnabled || !IsOpen || !IsClosable) return false;
        RaiseEvent(new RoutedEventArgs(CloseButtonClickEvent, this)); SetCurrentValue(IsOpenProperty, false); return true;
    }

    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateVisualStates(); }

    private void UpdateVisualStates()
    {
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsOpen ? "Open" : "Closed", false);
        VisualStateManager.GoToState(this, Severity switch { InfoBarSeverity.Success => "Success", InfoBarSeverity.Warning => "Warning", InfoBarSeverity.Error => "Error", _ => "Information" }, false);
        VisualStateManager.GoToState(this, IsIconVisible ? "IconVisible" : "IconHidden", false);
        VisualStateManager.GoToState(this, IconSource is null ? "DefaultIcon" : "CustomIcon", false);
        VisualStateManager.GoToState(this, IsClosable ? "Closable" : "NotClosable", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Title) ? "NoTitle" : "HasTitle", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Message) ? "NoMessage" : "HasMessage", false);
        VisualStateManager.GoToState(this, ActionButton is null ? "NoAction" : "HasAction", false);
    }
    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (InfoBar)d; bar.CoerceValue(VisibilityProperty); bar.InvalidateMeasure(); bar.UpdateVisualStates();
        if (!(bool)e.NewValue!) bar.RaiseEvent(new RoutedEventArgs(ClosedEvent, bar));
    }
    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (InfoBar)d; bar.InvalidateMeasure(); bar.UpdateVisualStates();
    }
    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (InfoBar)d; bar.InvalidateMeasure(); bar.UpdateVisualStates();
    }
}

/// <summary>
/// Specifies the severity level of an InfoBar.
/// </summary>
public enum InfoBarSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Informational,

    /// <summary>
    /// Success message.
    /// </summary>
    Success,

    /// <summary>
    /// Warning message.
    /// </summary>
    Warning,

    /// <summary>
    /// Error message.
    /// </summary>
    Error
}
