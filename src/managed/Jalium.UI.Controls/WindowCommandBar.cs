namespace Jalium.UI.Controls;

/// <summary>Displays application commands beside an optional native-window title.</summary>
[TemplatePart(Name = "PART_LeftCommandsHost", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_RightCommandsHost", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_TitleText", Type = typeof(TextBlock))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "TitleVisible", GroupName = "TitleStates")]
[TemplateVisualState(Name = "TitleHidden", GroupName = "TitleStates")]
[TemplateVisualState(Name = "HasLeftCommands", GroupName = "LeftCommandStates")]
[TemplateVisualState(Name = "NoLeftCommands", GroupName = "LeftCommandStates")]
[TemplateVisualState(Name = "HasRightCommands", GroupName = "RightCommandStates")]
[TemplateVisualState(Name = "NoRightCommands", GroupName = "RightCommandStates")]
public class WindowCommandBar : Control
{
    public static readonly DependencyProperty LeftCommandsProperty = DependencyProperty.Register(
        nameof(LeftCommands), typeof(object), typeof(WindowCommandBar), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty RightCommandsProperty = DependencyProperty.Register(
        nameof(RightCommands), typeof(object), typeof(WindowCommandBar), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(WindowCommandBar), new PropertyMetadata(string.Empty, OnPresentationChanged));
    public static readonly DependencyProperty IsTitleVisibleProperty = DependencyProperty.Register(
        nameof(IsTitleVisible), typeof(bool), typeof(WindowCommandBar), new PropertyMetadata(false, OnPresentationChanged));

    /// <summary>Gets or sets the content in the leading command area.</summary>
    public object? LeftCommands { get => GetValue(LeftCommandsProperty); set => SetValue(LeftCommandsProperty, value); }
    /// <summary>Gets or sets the content in the trailing command area.</summary>
    public object? RightCommands { get => GetValue(RightCommandsProperty); set => SetValue(RightCommandsProperty, value); }
    /// <summary>Gets or sets the caption displayed when the native host requests one.</summary>
    public string Title { get => (string?)GetValue(TitleProperty) ?? string.Empty; set => SetValue(TitleProperty, value); }
    /// <summary>Gets or sets whether the caption is displayed in the leading area.</summary>
    public bool IsTitleVisible { get => (bool)GetValue(IsTitleVisibleProperty)!; set => SetValue(IsTitleVisibleProperty, value); }

    private ContentPresenter? _leftHost, _rightHost;

    public WindowCommandBar() => DefaultStyleKey = typeof(WindowCommandBar);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _leftHost = GetTemplateChild("PART_LeftCommandsHost") as ContentPresenter;
        _rightHost = GetTemplateChild("PART_RightCommandsHost") as ContentPresenter;
        UpdateVisualStates();
    }

    internal override void OnTemplateContentClearing()
    {
        // A retired presenter must release caller-owned controls and its old
        // template binding before a replacement presenter adopts those controls.
        _leftHost?.ClearValue(ContentPresenter.ContentProperty);
        _rightHost?.ClearValue(ContentPresenter.ContentProperty);
        _leftHost = _rightHost = null;
        base.OnTemplateContentClearing();
    }

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        var bar = (WindowCommandBar)d;
        bar.InvalidateMeasure();
        bar.UpdateVisualStates();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateVisualStates();
    }

    private void UpdateVisualStates()
    {
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsTitleVisible ? "TitleVisible" : "TitleHidden", false);
        VisualStateManager.GoToState(this, LeftCommands is null ? "NoLeftCommands" : "HasLeftCommands", false);
        VisualStateManager.GoToState(this, RightCommands is null ? "NoRightCommands" : "HasRightCommands", false);
    }
}
