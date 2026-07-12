using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an item in a MenuFlyout that a user can change between two states, checked or unchecked.
/// </summary>
public class ToggleMenuFlyoutItem : MenuFlyoutItem
{
    private static readonly SolidColorBrush s_defaultCheckGlyphBrush = new(Color.FromRgb(255, 255, 255));

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsChecked dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(ToggleMenuFlyoutItem),
            new PropertyMetadata(false));

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
        var dc = drawingContext;
        base.OnRender(drawingContext);

        // Draw check mark when checked
        if (IsChecked)
        {
            var checkBrush = ResolveCheckGlyphBrush();
            var checkText = new Jalium.UI.Media.FormattedText(
                "\u2713", FontFamily.Source, 14) { Foreground = checkBrush };
            dc.DrawText(checkText, new Point(8, (RenderSize.Height - 14) / 2));
        }
    }

    private Brush ResolveCheckGlyphBrush()
    {
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
