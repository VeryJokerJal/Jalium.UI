using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button control that can switch states and be displayed in a CommandBar.
/// </summary>
public sealed class AppBarToggleButton : ToggleButton, ICommandBarElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(IconElement), typeof(AppBarToggleButton),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Label dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(AppBarToggleButton),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the LabelPosition dependency property.
    /// </summary>
    public static readonly DependencyProperty LabelPositionProperty =
        DependencyProperty.Register(nameof(LabelPosition), typeof(CommandBarLabelPosition), typeof(AppBarToggleButton),
            new PropertyMetadata(CommandBarLabelPosition.Default));

    /// <summary>
    /// Identifies the IsCompact dependency property.
    /// </summary>
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(AppBarToggleButton),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the DynamicOverflowOrder dependency property.
    /// </summary>
    public static readonly DependencyProperty DynamicOverflowOrderProperty =
        DependencyProperty.Register(nameof(DynamicOverflowOrder), typeof(int), typeof(AppBarToggleButton),
            new PropertyMetadata(0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the graphic content of the app bar toggle button.
    /// </summary>
    public IconElement? Icon
    {
        get => (IconElement?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets the text label displayed on the app bar toggle button.
    /// </summary>
    public string Label
    {
        get => (string?)GetValue(LabelProperty) ?? string.Empty;
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating the placement and visibility of the label.
    /// </summary>
    public CommandBarLabelPosition LabelPosition
    {
        get => (CommandBarLabelPosition)(GetValue(LabelPositionProperty) ?? CommandBarLabelPosition.Default);
        set => SetValue(LabelPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the element is shown in its compact representation.
    /// </summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty)!;
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>
    /// Gets or sets the priority of this element's dynamic overflow behavior.
    /// </summary>
    public int DynamicOverflowOrder
    {
        get => (int)GetValue(DynamicOverflowOrderProperty)!;
        set => SetValue(DynamicOverflowOrderProperty, value);
    }

    #endregion
}
