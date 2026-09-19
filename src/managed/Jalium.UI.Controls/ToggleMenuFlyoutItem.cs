using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an item in a MenuFlyout that a user can change between two states, checked or unchecked.
/// </summary>
public class ToggleMenuFlyoutItem : MenuFlyoutItem
{
    private static readonly SolidColorBrush s_defaultCheckGlyphBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush s_defaultDisabledCheckGlyphBrush = new(Color.FromRgb(90, 90, 90));
    private static readonly Geometry s_checkGeometry = Geometry.Parse("M2,8 L6,12 L14,4");

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsChecked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(ToggleMenuFlyoutItem),
            new PropertyMetadata(false, (d, _) => ((ToggleMenuFlyoutItem)d).InvalidateVisual()));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the ToggleMenuFlyoutItem is checked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty)!;
        set => SetValue(IsCheckedProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the ToggleMenuFlyoutItem class.
    /// </summary>
    public ToggleMenuFlyoutItem()
    {
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (IsChecked && RenderSize.Width > 0 && RenderSize.Height > 0)
        {
            // Use a vector in the reserved leading icon column, independent of the label's font.
            var checkPen = new Pen(ResolveCheckGlyphBrush(), 1.5);
            drawingContext.PushTransform(new TranslateTransform(12, (RenderSize.Height - 16) / 2));
            drawingContext.DrawGeometry(null, checkPen, s_checkGeometry);
            drawingContext.Pop();
        }
    }

    private Brush ResolveCheckGlyphBrush()
    {
        if (!IsEnabled)
        {
            return TryFindResource("OneTextDisabled") as Brush
                ?? TryFindResource("TextDisabled") as Brush
                ?? s_defaultDisabledCheckGlyphBrush;
        }

        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TextPrimary") as Brush
            ?? Foreground
            ?? s_defaultCheckGlyphBrush;
    }

    /// <inheritdoc />
    protected override void OnItemInvoking()
    {
        IsChecked = !IsChecked;
    }
}
