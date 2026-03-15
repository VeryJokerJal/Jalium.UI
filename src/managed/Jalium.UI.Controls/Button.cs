using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button control.
/// Uses ControlTemplate for rendering - visual appearance is defined in Button.jalxaml.
/// </summary>
public class Button : ButtonBase
{
    #region Automation

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return new ButtonAutomationPeer(this);
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsDefault dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDefaultProperty =
        DependencyProperty.Register(nameof(IsDefault), typeof(bool), typeof(Button),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsCancel dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCancelProperty =
        DependencyProperty.Register(nameof(IsCancel), typeof(bool), typeof(Button),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether this is the default button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDefault
    {
        get => (bool)GetValue(IsDefaultProperty)!;
        set => SetValue(IsDefaultProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this is the cancel button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCancel
    {
        get => (bool)GetValue(IsCancelProperty)!;
        set => SetValue(IsCancelProperty, value);
    }

    #endregion
}
