using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that a user can select and clear.
/// </summary>
public class CheckBox : ToggleButton
{
    private Shapes.Path? _checkMark;
    private Border? _indeterminateMark;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckBox"/> class.
    /// </summary>
    public CheckBox()
    {
        // CheckBox uses ControlTemplate for visual appearance (inherited from ButtonBase)
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _checkMark = GetTemplateChild("CheckMark") as Shapes.Path;
        _indeterminateMark = GetTemplateChild("IndeterminateMark") as Border;
        UpdateCheckGlyphs();
    }

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return new CheckBoxAutomationPeer(this);
    }

    /// <inheritdoc />
    protected override void OnIsCheckedChanged(bool? oldValue, bool? newValue)
    {
        base.OnIsCheckedChanged(oldValue, newValue);
        UpdateCheckGlyphs();
    }

    private void UpdateCheckGlyphs()
    {
        if (_checkMark == null || _indeterminateMark == null)
        {
            return;
        }

        if (IsChecked == true)
        {
            _checkMark.Opacity = 1;
            _indeterminateMark.Opacity = 0;
        }
        else if (IsChecked == null)
        {
            _checkMark.Opacity = 0;
            _indeterminateMark.Opacity = 1;
        }
        else
        {
            _checkMark.Opacity = 0;
            _indeterminateMark.Opacity = 0;
        }
    }
}
