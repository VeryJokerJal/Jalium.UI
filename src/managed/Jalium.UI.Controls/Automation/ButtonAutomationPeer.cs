using Jalium.UI.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Exposes Button types to UI Automation.
/// </summary>
public sealed class ButtonAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    /// <summary>
    /// Initializes a new instance of the ButtonAutomationPeer class.
    /// </summary>
    /// <param name="owner">The Button that is associated with this peer.</param>
    public ButtonAutomationPeer(Button owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the Button owner.
    /// </summary>
    private Button ButtonOwner => (Button)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Button;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(Button);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        // For buttons, try to get the content as the name
        var content = ButtonOwner.Content;

        if (content is string text)
            return text;

        if (content is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
            return fe.Name;

        return base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Invoke)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    #region IInvokeProvider

    /// <summary>
    /// Sends a request to activate the button and initiate its single, unambiguous action.
    /// </summary>
    public void Invoke()
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException("Cannot invoke a disabled button.");
        }

        // Perform the click action
        ButtonOwner.PerformClick();

        // Raise the automation event
        RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
    }

    #endregion
}

/// <summary>
/// Exposes ButtonBase types to UI Automation.
/// </summary>
public class ButtonBaseAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider
{
    /// <summary>
    /// Initializes a new instance of the ButtonBaseAutomationPeer class.
    /// </summary>
    /// <param name="owner">The ButtonBase that is associated with this peer.</param>
    public ButtonBaseAutomationPeer(ButtonBase owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the ButtonBase owner.
    /// </summary>
    protected ButtonBase ButtonBaseOwner => (ButtonBase)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Button;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return Owner.GetType().Name;
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        // For buttons, try to get the content as the name
        var content = ButtonBaseOwner.Content;

        if (content is string text)
            return text;

        if (content is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
            return fe.Name;

        return base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Invoke)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    #region IInvokeProvider

    /// <summary>
    /// Sends a request to activate the button and initiate its single, unambiguous action.
    /// </summary>
    public void Invoke()
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException("Cannot invoke a disabled button.");
        }

        // Perform the click action
        ButtonBaseOwner.PerformClick();

        // Raise the automation event
        RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
    }

    #endregion
}
