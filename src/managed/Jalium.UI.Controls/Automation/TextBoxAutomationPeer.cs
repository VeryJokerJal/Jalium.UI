using Jalium.UI.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls.Automation;

/// <summary>
/// Exposes TextBoxBase types to UI Automation.
/// </summary>
public class TextBoxBaseAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    /// <summary>
    /// Initializes a new instance of the TextBoxBaseAutomationPeer class.
    /// </summary>
    /// <param name="owner">The TextBoxBase that is associated with this peer.</param>
    public TextBoxBaseAutomationPeer(TextBoxBase owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the TextBoxBase owner.
    /// </summary>
    protected TextBoxBase TextBoxBaseOwner => (TextBoxBase)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Edit;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return Owner.GetType().Name;
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Value)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    #region IValueProvider

    /// <summary>
    /// Gets the value of the text box.
    /// </summary>
    public virtual string Value => string.Empty;

    /// <summary>
    /// Gets whether the text box is read-only.
    /// </summary>
    public bool IsReadOnly => TextBoxBaseOwner.IsReadOnly;

    /// <summary>
    /// Sets the value of the text box.
    /// </summary>
    public virtual void SetValue(string value)
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException("Cannot set value on a disabled control.");
        }

        if (IsReadOnly)
        {
            throw new InvalidOperationException("Cannot set value on a read-only control.");
        }
    }

    #endregion
}

/// <summary>
/// Exposes TextBox types to UI Automation.
/// </summary>
public sealed class TextBoxAutomationPeer : TextBoxBaseAutomationPeer, ITextProvider
{
    /// <summary>
    /// Initializes a new instance of the TextBoxAutomationPeer class.
    /// </summary>
    /// <param name="owner">The TextBox that is associated with this peer.</param>
    public TextBoxAutomationPeer(TextBox owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the TextBox owner.
    /// </summary>
    private TextBox TextBoxOwner => (TextBox)Owner;

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(TextBox);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        // For text boxes, the placeholder might serve as the accessible name
        var placeholder = TextBoxOwner.PlaceholderText;
        if (!string.IsNullOrEmpty(placeholder))
            return placeholder;

        return base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        // TextBox exposes the Text pattern (in addition to the Value pattern handled by the base
        // peer) so external UIA clients can detect and read the currently selected text.
        if (patternInterface == PatternInterface.Text)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ITextProvider

    string ITextProvider.Text => TextBoxOwner.Text ?? string.Empty;

    int ITextProvider.SelectionStart => TextBoxOwner.SelectionStart;

    int ITextProvider.SelectionLength => TextBoxOwner.SelectionLength;

    bool ITextProvider.IsReadOnly => TextBoxOwner.IsReadOnly;

    SupportedTextSelection ITextProvider.SupportedTextSelection => SupportedTextSelection.Single;

    void ITextProvider.Select(int start, int length) => TextBoxOwner.Select(start, length);

    // Precise per-glyph selection geometry is not surfaced yet; report none rather than a misleading
    // rectangle. GetText()/GetSelection() — the path external detectors rely on — remain exact.
    IReadOnlyList<Rect> ITextProvider.GetBoundingRectangles(int start, int length) => Array.Empty<Rect>();

    void ITextProvider.ScrollIntoView(int start, int length) => TextBoxOwner.ScrollToCaretPosition();

    #endregion

    #region IValueProvider

    /// <summary>
    /// Gets the text value.
    /// </summary>
    public override string Value => TextBoxOwner.Text;

    /// <summary>
    /// Sets the text value.
    /// </summary>
    public override void SetValue(string value)
    {
        base.SetValue(value);

        var oldValue = TextBoxOwner.Text;
        TextBoxOwner.Text = value ?? string.Empty;

        // Raise property changed event
        RaisePropertyChangedEvent(AutomationProperty.ValueProperty, oldValue, value);
    }

    #endregion
}

/// <summary>
/// Exposes PasswordBox types to UI Automation.
/// </summary>
public sealed class PasswordBoxAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
{
    /// <summary>
    /// Initializes a new instance of the PasswordBoxAutomationPeer class.
    /// </summary>
    /// <param name="owner">The PasswordBox that is associated with this peer.</param>
    public PasswordBoxAutomationPeer(PasswordBox owner) : base(owner)
    {
    }

    /// <summary>
    /// Gets the PasswordBox owner.
    /// </summary>
    private PasswordBox PasswordBoxOwner => (PasswordBox)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Edit;
    }

    /// <inheritdoc />
    protected override string GetClassNameCore()
    {
        return nameof(PasswordBox);
    }

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        // For password boxes, the placeholder might serve as the accessible name
        var placeholder = PasswordBoxOwner.PlaceholderText;
        if (!string.IsNullOrEmpty(placeholder))
            return placeholder;

        return base.GetNameCore();
    }

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        // PasswordBox supports Value pattern but does NOT expose the value for security
        if (patternInterface == PatternInterface.Value)
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    #region IValueProvider

    /// <summary>
    /// PasswordBox should not expose its value for security.
    /// </summary>
    public string Value => string.Empty;

    /// <summary>
    /// Gets whether the password box is read-only.
    /// </summary>
    public bool IsReadOnly => PasswordBoxOwner.IsReadOnly;

    /// <summary>
    /// PasswordBox allows setting values through automation for testing purposes.
    /// </summary>
    public void SetValue(string value)
    {
        if (!IsEnabled())
        {
            throw new InvalidOperationException("Cannot set value on a disabled control.");
        }

        if (IsReadOnly)
        {
            throw new InvalidOperationException("Cannot set value on a read-only control.");
        }

        PasswordBoxOwner.Password = value ?? string.Empty;
    }

    #endregion
}
