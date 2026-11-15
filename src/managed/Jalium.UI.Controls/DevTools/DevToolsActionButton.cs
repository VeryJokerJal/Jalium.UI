using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;

namespace Jalium.UI.Controls.DevTools;

/// <summary>The semantic emphasis of a developer-tools action.</summary>
public enum DevToolsButtonVariant
{
    Default,
    Primary,
    Danger,
}

/// <summary>A keyboard, command and automation enabled action with a replaceable theme template.</summary>
[TemplateVisualState(Name = "Normal", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "Hover", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "Pressed", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "Active", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "Primary", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "PrimaryHover", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "PrimaryPressed", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "Danger", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "DangerHover", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "DangerPressed", GroupName = "AppearanceStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Enabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "IconVisible", GroupName = "IconStates")]
[TemplateVisualState(Name = "IconHidden", GroupName = "IconStates")]
public class DevToolsActionButton : Button
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DevToolsActionButton), new PropertyMetadata(string.Empty, OnLabelChanged));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(DevToolsActionButton), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(DevToolsButtonVariant), typeof(DevToolsActionButton), new PropertyMetadata(DevToolsButtonVariant.Default));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(DevToolsActionButton), new PropertyMetadata(false));
    public static readonly DependencyProperty IsToggleProperty = DependencyProperty.Register(
        nameof(IsToggle), typeof(bool), typeof(DevToolsActionButton), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey DisplayLabelPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayLabel), typeof(string), typeof(DevToolsActionButton), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayLabelProperty = DisplayLabelPropertyKey.DependencyProperty;

    static DevToolsActionButton() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DevToolsActionButton), new PropertyMetadata(typeof(DevToolsActionButton)));

    public string Label { get => (string?)GetValue(LabelProperty) ?? string.Empty; set => SetValue(LabelProperty, value); }
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public DevToolsButtonVariant Variant { get => (DevToolsButtonVariant)GetValue(VariantProperty)!; set => SetValue(VariantProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty)!; set => SetValue(IsActiveProperty, value); }
    /// <summary>Whether activation toggles IsActive and exposes the UI Automation Toggle pattern.</summary>
    public bool IsToggle { get => (bool)GetValue(IsToggleProperty)!; set => SetValue(IsToggleProperty, value); }
    public string DisplayLabel => (string)GetValue(DisplayLabelProperty)!;

    public void SetIcon(string? glyph) => SetCurrentValue(IconProperty, glyph);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates();
    }

    protected override void OnClick()
    {
        if (IsToggle) SetCurrentValue(IsActiveProperty, !IsActive);
        base.OnClick();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsToggleProperty) UIElementAutomationPeer.FromElement(this)?.InvalidatePeer();
        if (e.Property == IsActiveProperty && IsToggle && UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationProperty.ToggleStateProperty,
                (bool)e.OldValue! ? ToggleState.On : ToggleState.Off,
                (bool)e.NewValue! ? ToggleState.On : ToggleState.Off);
        if (e.Property == VariantProperty || e.Property == IsActiveProperty || e.Property == IconProperty ||
            e.Property == IsMouseOverProperty || e.Property == IsPressedProperty || e.Property == IsEnabledProperty ||
            e.Property == IsKeyboardFocusedProperty)
            UpdateStates();
    }

    private void UpdateStates()
    {
        string interaction = IsPressed ? "Pressed" : IsMouseOver ? "Hover" : string.Empty;
        string appearance = Variant switch
        {
            DevToolsButtonVariant.Primary => "Primary" + interaction,
            DevToolsButtonVariant.Danger => "Danger" + interaction,
            _ => IsActive ? "Active" : interaction.Length == 0 ? "Normal" : interaction,
        };
        VisualStateManager.GoToState(this, appearance, false);
        VisualStateManager.GoToState(this, IsEnabled ? "Enabled" : "Disabled", false);
        VisualStateManager.GoToState(this, IsKeyboardFocused && IsEnabled ? "Focused" : "Unfocused", false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Icon) ? "IconHidden" : "IconVisible", false);
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DevToolsActionButton)d).SetValue(DisplayLabelPropertyKey, ((string?)e.NewValue ?? string.Empty).ToUpperInvariant());

    protected override AutomationPeer OnCreateAutomationPeer() => new ActionAutomationPeer(this);

    private sealed class ActionAutomationPeer(DevToolsActionButton owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        protected override string GetClassNameCore() => nameof(DevToolsActionButton);
        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name
            ? name : owner.Label.Length == 0 ? base.GetNameCore() : owner.Label;
        protected override object? GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Toggle && owner.IsToggle ? this : base.GetPatternCore(patternInterface);
        public ToggleState ToggleState => owner.IsActive ? ToggleState.On : ToggleState.Off;
        public void Toggle()
        {
            if (!owner.IsToggle || !IsEnabled()) throw new InvalidOperationException("Cannot toggle this action.");
            owner.PerformClick();
        }
    }
}
