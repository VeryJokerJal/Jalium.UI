using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an item in a MenuFlyout that a user can change between two states, checked or unchecked.
/// </summary>
public sealed class ToggleMenuFlyoutItem : MenuFlyoutItem
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the IsChecked dependency property.
    /// </summary>
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(ToggleMenuFlyoutItem),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the ToggleMenuFlyoutItem is checked.
    /// </summary>
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
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnToggleMouseDown));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        // Draw check mark when checked
        if (IsChecked)
        {
            var checkBrush = new Jalium.UI.Media.SolidColorBrush(
                Jalium.UI.Media.Color.FromRgb(255, 255, 255));
            var checkText = new Jalium.UI.Media.FormattedText(
                "\u2713", FontFamily, 14) { Foreground = checkBrush }; // ✓
            dc.DrawText(checkText, new Point(8, (RenderSize.Height - 14) / 2));
        }
    }

    private void OnToggleMouseDown(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;
        IsChecked = !IsChecked;
        // Note: the base MenuFlyoutItem.OnMouseDownHandler will raise Click and execute Command
    }
}
