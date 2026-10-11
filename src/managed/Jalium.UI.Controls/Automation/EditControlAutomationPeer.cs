using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;

namespace Jalium.UI.Automation.Peers;

/// <summary>Exposes the code editor's document, selection and visible text geometry.</summary>
public class EditControlAutomationPeer : TextAutomationPeer, IValueProvider, IAutomationTextProviderSource, IAutomationTextViewSource, IAutomationTextStyleSource
{
    private readonly ITextProvider _textProvider;
    private string _lastText;
    private int _lastSelectionStart, _lastSelectionLength;

    /// <summary>Initializes the automation peer for a code editor.</summary>
    public EditControlAutomationPeer(EditControl owner) : base(owner)
    {
        _textProvider = new AutomationTextProvider(this, this);
        _lastText = owner.Document.Text;
        _lastSelectionStart = owner.SelectionStart;
        _lastSelectionLength = owner.SelectionLength;
    }

    private EditControl EditorOwner => (EditControl)Owner;

    IReadOnlyList<AutomationTextStyleSpan> IAutomationTextStyleSource.GetTextStyles() => EditorOwner.GetAutomationTextStyles();

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    /// <inheritdoc />
    protected override string GetClassNameCore() => nameof(EditControl);

    /// <inheritdoc />
    public override object? GetPattern(PatternInterface patternInterface) => GetPatternCore(patternInterface);

    /// <inheritdoc />
    protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.Value => this,
        PatternInterface.Text => _textProvider,
        _ => base.GetPatternCore(patternInterface)
    };

    /// <inheritdoc />
    public string Value => EditorOwner.Document.Text;

    /// <inheritdoc />
    public bool IsReadOnly => EditorOwner.IsReadOnly;

    /// <inheritdoc />
    public void SetValue(string value)
    {
        if (!IsEnabled()) throw new InvalidOperationException("Cannot set value on a disabled control.");
        if (IsReadOnly) throw new InvalidOperationException("Cannot set value on a read-only control.");
        EditorOwner.ReplaceAutomationText(value ?? string.Empty);
    }

    string IAutomationTextProviderSource.Text => Value;
    int IAutomationTextProviderSource.SelectionStart => EditorOwner.SelectionStart;
    int IAutomationTextProviderSource.SelectionLength => EditorOwner.SelectionLength;
    SupportedTextSelection IAutomationTextProviderSource.SupportedTextSelection => SupportedTextSelection.Single;
    void IAutomationTextProviderSource.Select(int start, int length) => EditorOwner.Select(start, length);
    IReadOnlyList<Rect> IAutomationTextProviderSource.GetBoundingRectangles(int start, int length)
        => AutomationVisibility.ClipRectangles(EditorOwner, EditorOwner.GetAutomationTextBounds(start, length));
    void IAutomationTextProviderSource.ScrollIntoView(int start, int length) => EditorOwner.ScrollToAutomationOffset(start);

    int IAutomationTextViewSource.CaretIndex => EditorOwner.CaretOffset;
    Rect IAutomationTextViewSource.TextViewport => EditorOwner.AutomationTextViewport;
    IReadOnlyList<AutomationTextLine> IAutomationTextViewSource.GetTextLines() => EditorOwner.GetAutomationTextLines();
    bool IAutomationTextViewSource.TryGetInsertionIndex(Point localPoint, out int index)
        => EditorOwner.TryGetAutomationInsertionIndex(localPoint, out index);
    bool IAutomationTextViewSource.ReplaceSelection(string text) => IsEnabled() && !IsReadOnly && EditorOwner.ReplaceAutomationSelection(text);

    internal void NotifyTextChanged()
    {
        string oldText = _lastText, newText = Value;
        _lastText = newText;
        if (oldText != newText)
        {
            RaisePropertyChangedEvent(AutomationProperty.ValueProperty, oldText, newText);
            RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
        }
        NotifySelectionChanged();
    }

    internal void NotifySelectionChanged()
    {
        int start = EditorOwner.SelectionStart, length = EditorOwner.SelectionLength;
        if (start == _lastSelectionStart && length == _lastSelectionLength) return;
        _lastSelectionStart = start; _lastSelectionLength = length;
        RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);
    }
}
