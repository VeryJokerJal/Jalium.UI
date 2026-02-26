using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents the base class for an icon UI element.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.IconElement.
/// </summary>
public abstract class IconElement : FrameworkElement
{
    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(IconElement),
            new PropertyMetadata(null, OnForegroundChanged));

    /// <summary>
    /// Gets or sets the foreground brush for the icon.
    /// When null, inherits from the parent visual tree.
    /// </summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Resolves the effective foreground brush by walking up the visual tree.
    /// </summary>
    protected Brush GetEffectiveForeground()
    {
        if (Foreground != null)
            return Foreground;

        // Walk up to find Foreground from a parent Control
        Visual? current = VisualParent;
        while (current != null)
        {
            if (current is Control control && control.Foreground != null)
                return control.Foreground;
            current = current.VisualParent;
        }

        return new SolidColorBrush(Color.FromRgb(255, 255, 255));
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IconElement icon)
            icon.InvalidateVisual();
    }
}
