using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>Reusable content and chrome for non-menu flyouts.</summary>
[TemplatePart(Name = "PART_Root", Type = typeof(Border))]
[TemplatePart(Name = "PART_ContentPresenter", Type = typeof(ContentPresenter))]
public class FlyoutPresenter : ContentControl
{
    private static readonly DependencyPropertyKey IsOpenPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsOpen), typeof(bool), typeof(FlyoutPresenter), new PropertyMetadata(false));
    public static readonly DependencyProperty IsOpenProperty = IsOpenPropertyKey.DependencyProperty;
    public bool IsOpen => (bool)GetValue(IsOpenProperty)!;

    internal event EventHandler? CloseRequested;
    static FlyoutPresenter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyoutPresenter), new PropertyMetadata(typeof(FlyoutPresenter)));
        BackgroundProperty.OverrideMetadata(typeof(FlyoutPresenter), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(45, 45, 48))));
        BorderBrushProperty.OverrideMetadata(typeof(FlyoutPresenter), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(67, 67, 70))));
        BorderThicknessProperty.OverrideMetadata(typeof(FlyoutPresenter), new PropertyMetadata(new Thickness(1)));
        PaddingProperty.OverrideMetadata(typeof(FlyoutPresenter), new PropertyMetadata(new Thickness(4)));
        CornerRadiusProperty.OverrideMetadata(typeof(FlyoutPresenter), new PropertyMetadata(new CornerRadius(8)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateVisualState();
    }

    internal void SetOpen(bool open)
    {
        SetValue(IsOpenPropertyKey, open);
        UpdateVisualState();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue && IsOpen)
            CloseRequested?.Invoke(this, EventArgs.Empty);
        UpdateVisualState();
    }

    protected override void OnDataContextChanged(object? oldValue, object? newValue)
    {
        base.OnDataContextChanged(oldValue, newValue);
        if (Content is FrameworkElement content)
            ReactivateBindingsRecursive(content);
    }

    private void UpdateVisualState()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsOpen ? "Open" : "Closed", false);
    }
}
