using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Template-created text surface for PasswordBox. The owner retains all editing and password state.</summary>
public sealed class PasswordBoxView : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
        nameof(Owner), typeof(PasswordBox), typeof(PasswordBoxView), new PropertyMetadata(null, (d, _) =>
        {
            var view = (PasswordBoxView)d;
            view.InvalidateMeasure();
            view.InvalidateVisual();
        }));

    public PasswordBox? Owner
    {
        get => (PasswordBox?)GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => Owner?.MeasurePassword(availableSize) ?? default;
    protected override void OnRender(DrawingContext drawingContext) => Owner?.RenderPasswordContent(drawingContext);
}
