using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "RootBorder", Type = typeof(Border))]
[TemplatePart(Name = "PART_Icon", Type = typeof(SymbolIcon))]
[TemplatePart(Name = "PART_TitleText", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_MessageText", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_ContentPresenter", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_ActionHost", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_CloseButton", Type = typeof(Button))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Open", GroupName = "OpenStates")]
[TemplateVisualState(Name = "Closed", GroupName = "OpenStates")]
[TemplateVisualState(Name = "Information", GroupName = "SeverityStates")]
[TemplateVisualState(Name = "Success", GroupName = "SeverityStates")]
[TemplateVisualState(Name = "Warning", GroupName = "SeverityStates")]
[TemplateVisualState(Name = "Error", GroupName = "SeverityStates")]
public partial class ToastNotificationItem
{
    /// <summary>Identifies the brush used for the severity icon.</summary>
    public static readonly DependencyProperty IconForegroundProperty = DependencyProperty.Register(
        nameof(IconForeground), typeof(Brush), typeof(ToastNotificationItem), new PropertyMetadata(null));

    /// <summary>Gets or sets the brush used for the severity icon.</summary>
    public Brush? IconForeground { get => (Brush?)GetValue(IconForegroundProperty); set => SetValue(IconForegroundProperty, value); }

    private Button? _closeButton;
    private ContentPresenter? _actionHost;
    private ButtonBase? _subscribedAction;

    public override void OnApplyTemplate()
    {
        UnsubscribeTemplateHandlers(); base.OnApplyTemplate();
        _closeButton = GetTemplateChild("PART_CloseButton") as Button;
        _actionHost = GetTemplateChild("PART_ActionHost") as ContentPresenter;
        if (_closeButton is not null) _closeButton.Click += OnCloseClick;
        UpdateActionSubscription(); UpdateVisualStates();
    }

    internal override void OnTemplateContentClearing()
    {
        UnsubscribeTemplateHandlers();
        _actionHost?.ClearValue(ContentPresenter.ContentProperty);
        _actionHost = null; _closeButton = null;
        base.OnTemplateContentClearing();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue); UpdateVisualStates();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (!IsOpen || !IsClosable || !IsEnabled) return;
        RaiseEvent(new RoutedEventArgs(CloseButtonClickEvent, this)); Dismiss();
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (IsOpen && IsEnabled && _subscribedAction?.IsEnabled == true)
            RaiseEvent(new RoutedEventArgs(ActionButtonClickEvent, this));
    }

    private void UnsubscribeTemplateHandlers()
    {
        if (_closeButton is not null) _closeButton.Click -= OnCloseClick;
        if (_subscribedAction is not null) _subscribedAction.Click -= OnActionClick;
        _subscribedAction = null;
    }

    private void UpdateActionSubscription()
    {
        if (_subscribedAction is not null) _subscribedAction.Click -= OnActionClick;
        _subscribedAction = _actionHost is null ? null : ActionButton;
        if (_subscribedAction is not null) _subscribedAction.Click += OnActionClick;
    }

    private static void OnActionButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toast = (ToastNotificationItem)d;
        toast.UpdateActionSubscription(); toast.InvalidateMeasure(); toast.UpdateVisualStates();
    }

    private void UpdateVisualStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsOpen ? "Open" : "Closed", false);
        VisualStateManager.GoToState(this, Severity switch
        {
            ToastSeverity.Success => "Success", ToastSeverity.Warning => "Warning",
            ToastSeverity.Error => "Error", _ => "Information",
        }, false);
    }
}
