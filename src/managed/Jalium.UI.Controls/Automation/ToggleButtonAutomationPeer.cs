using Jalium.UI.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Exposes ToggleButton types to UI Automation.
/// </summary>
public class ToggleButtonAutomationPeer : ButtonBaseAutomationPeer, IToggleProvider
{
    /// <summary>
    /// Initializes a new instance of the ToggleButtonAutomationPeer class.
    /// </summary>
    /// <param name="owner">The ToggleButton that is associated with this peer.</param>
    public ToggleButtonAutomationPeer(ToggleButton owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the ToggleButton owner.
    /// </summary>
    protected ToggleButton ToggleButtonOwner => (ToggleButton)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Button;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(ToggleButton);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Toggle)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    #region IToggleProvider

    /// <summary>
    /// Gets the toggle state of the control.
    /// </summary>
    public ToggleState ToggleState
    {
        get
        {
            var isChecked = ToggleButtonOwner.IsChecked;

            if (isChecked == true)
                return ToggleState.On;
            else if (isChecked == false)
                return ToggleState.Off;
            else
                return ToggleState.Indeterminate;
        }
    }

    /// <summary>
    /// Cycles through the toggle states of the control.
    /// </summary>
    public void Toggle()
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException("Cannot toggle a disabled control.");
        }

        var oldState = ToggleState;

        // Perform the toggle
        ToggleButtonOwner.PerformClick();

        var newState = ToggleState;

        // Raise property changed event if state changed
        if (oldState != newState)
        {
            RaisePropertyChangedEvent(AutomationProperty.ToggleStateProperty, oldState, newState);
        }
    }

    #endregion
}

/// <summary>
/// Exposes CheckBox types to UI Automation.
/// </summary>
public sealed class CheckBoxAutomationPeer : ToggleButtonAutomationPeer
{
    /// <summary>
    /// Initializes a new instance of the CheckBoxAutomationPeer class.
    /// </summary>
    /// <param name="owner">The CheckBox that is associated with this peer.</param>
    public CheckBoxAutomationPeer(CheckBox owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the CheckBox owner.
    /// </summary>
    private CheckBox CheckBoxOwner => (CheckBox)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.CheckBox;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(CheckBox);
    }
}

/// <summary>
/// Exposes RadioButton types to UI Automation.
/// </summary>
public sealed class RadioButtonAutomationPeer : ToggleButtonAutomationPeer, ISelectionItemProvider
{
    /// <summary>
    /// Initializes a new instance of the RadioButtonAutomationPeer class.
    /// </summary>
    /// <param name="owner">The RadioButton that is associated with this peer.</param>
    public RadioButtonAutomationPeer(RadioButton owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the RadioButton owner.
    /// </summary>
    private RadioButton RadioButtonOwner => (RadioButton)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.RadioButton;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(RadioButton);
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.SelectionItem)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    #region ISelectionItemProvider

    /// <summary>
    /// Gets a value that indicates whether the radio button is selected.
    /// </summary>
    public bool IsSelected => RadioButtonOwner.IsChecked == true;

    /// <summary>
    /// Gets the selection container (not implemented for RadioButton).
    /// </summary>
    public AutomationPeer SelectionContainer => null!;

    /// <summary>
    /// Selects the radio button.
    /// </summary>
    public void Select()
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException("Cannot select a disabled radio button.");
        }

        RadioButtonOwner.IsChecked = true;
    }

    /// <summary>
    /// Adds to selection (same as Select for RadioButton).
    /// </summary>
    public void AddToSelection()
    {
        Select();
    }

    /// <summary>
    /// Removes from selection (sets IsChecked to false).
    /// </summary>
    public void RemoveFromSelection()
    {
        // RadioButtons typically can't be deselected by clicking them
        // but we support it through automation
        if (!IsEnabled())
        {
            throw new InvalidOperationException("Cannot modify a disabled radio button.");
        }

        RadioButtonOwner.IsChecked = false;
    }

    #endregion
}
