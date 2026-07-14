using Jalium.UI.Automation;
using Jalium.UI.Controls.Primitives;
using IValueProvider = Jalium.UI.Automation.Provider.IValueProvider;

namespace Jalium.UI.Automation.Peers;

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
public class TextBoxAutomationPeer : TextAutomationPeer, IAutomationTextProviderSource, IValueProvider
{
    private readonly Jalium.UI.Automation.Provider.ITextProvider _textProvider;

    /// <summary>
    /// Initializes a new instance of the TextBoxAutomationPeer class.
    /// </summary>
    /// <param name="owner">The TextBox that is associated with this peer.</param>
    public TextBoxAutomationPeer(TextBox owner) : base(owner)
    {
        _textProvider = new Jalium.UI.Automation.Provider.AutomationTextProvider(this, this);
    }

    /// <inheritdoc />
    public override object? GetPattern(PatternInterface patternInterface) => GetPatternCore(patternInterface);

    /// <summary>
    /// Gets the TextBox owner.
    /// </summary>
    private TextBox TextBoxOwner => (TextBox)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

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
            return _textProvider;

        if (patternInterface == PatternInterface.Value)
            return this;

        return base.GetPatternCore(patternInterface);
    }

    #region ITextProvider

    string IAutomationTextProviderSource.Text => TextBoxOwner.Text ?? string.Empty;

    int IAutomationTextProviderSource.SelectionStart => TextBoxOwner.SelectionStart;

    int IAutomationTextProviderSource.SelectionLength => TextBoxOwner.SelectionLength;

    bool IAutomationTextProviderSource.IsReadOnly => TextBoxOwner.IsReadOnly;

    SupportedTextSelection IAutomationTextProviderSource.SupportedTextSelection => SupportedTextSelection.Single;

    void IAutomationTextProviderSource.Select(int start, int length) => TextBoxOwner.Select(start, length);

    // Use the same caret hit-testing as rendering, grouped into one rectangle per visual line.
    IReadOnlyList<Rect> IAutomationTextProviderSource.GetBoundingRectangles(int start, int length)
    {
        string text = TextBoxOwner.Text ?? string.Empty;
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);

        if (length == 0)
        {
            Rect caret = TextBoxOwner.GetRectFromCharacterIndex(start);
            return [new Rect(caret.X, caret.Y, Math.Max(1, caret.Width), Math.Max(1, caret.Height))];
        }

        var rectangles = new List<Rect>();
        Rect currentLine = Rect.Empty;
        int end = start + length;
        for (int index = start; index < end; index++)
        {
            Rect leading = TextBoxOwner.GetRectFromCharacterIndex(index, trailingEdge: false);
            Rect trailing = TextBoxOwner.GetRectFromCharacterIndex(index, trailingEdge: true);
            double left = Math.Min(leading.X, trailing.X);
            double right = Math.Max(leading.X, trailing.X);
            var character = new Rect(
                left,
                Math.Min(leading.Y, trailing.Y),
                Math.Max(1, right - left),
                Math.Max(1, Math.Max(leading.Height, trailing.Height)));

            if (currentLine.IsEmpty || Math.Abs(currentLine.Y - character.Y) <= 0.5)
            {
                currentLine = currentLine.IsEmpty ? character : Rect.Union(currentLine, character);
            }
            else
            {
                rectangles.Add(currentLine);
                currentLine = character;
            }
        }

        if (!currentLine.IsEmpty)
            rectangles.Add(currentLine);
        return rectangles;
    }

    void IAutomationTextProviderSource.ScrollIntoView(int start, int length) => TextBoxOwner.ScrollToCaretPosition();

    #endregion

    #region IValueProvider

    /// <summary>
    /// Gets the text value.
    /// </summary>
    public string Value => TextBoxOwner.Text;

    /// <inheritdoc />
    public bool IsReadOnly => TextBoxOwner.IsReadOnly;

    /// <summary>
    /// Sets the text value.
    /// </summary>
    public void SetValue(string value)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("Cannot set value on a disabled control.");
        if (IsReadOnly)
            throw new InvalidOperationException("Cannot set value on a read-only control.");

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
public class PasswordBoxAutomationPeer : TextAutomationPeer, Jalium.UI.Automation.Provider.IValueProvider
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

    protected override bool IsPasswordCore() => true;

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
