using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button control.
/// </summary>
/// <remarks>
/// Button uses a ControlTemplate for rendering. The default template consists of a Border
/// containing a ContentPresenter. Visual states (hover, pressed, disabled) are handled
/// by triggers in the template.
/// </remarks>
public class Button : ButtonBase
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the IsDefault dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDefaultProperty =
        DependencyProperty.Register(nameof(IsDefault), typeof(bool), typeof(Button),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsCancel dependency property.
    /// </summary>
    public static readonly DependencyProperty IsCancelProperty =
        DependencyProperty.Register(nameof(IsCancel), typeof(bool), typeof(Button),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether this is the default button.
    /// </summary>
    public bool IsDefault
    {
        get => (bool)(GetValue(IsDefaultProperty) ?? false);
        set => SetValue(IsDefaultProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this is the cancel button.
    /// </summary>
    public bool IsCancel
    {
        get => (bool)(GetValue(IsCancelProperty) ?? false);
        set => SetValue(IsCancelProperty, value);
    }

    #endregion

    // Rendering is handled by the ControlTemplate defined in Generic.jalxaml
    // The template uses Border for background/border and ContentPresenter for content
}
