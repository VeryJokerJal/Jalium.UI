using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;

namespace Jalium.UI.Automation.Peers;

/// <summary>Exposes the terminal's character buffer independently of shell input.</summary>
public sealed class TerminalAutomationPeer : FrameworkElementAutomationPeer,
    IValueProvider, IAutomationTextProviderSource, IAutomationTextViewSource
{
    private readonly ITextProvider _text;
    private int _selectionStart, _selectionLength;

    public TerminalAutomationPeer(Terminal owner) : base(owner)
    {
        _text = new AutomationTextProvider(this, this);
        _selectionStart = owner.AutomationSelectionStart;
        _selectionLength = owner.AutomationSelectionLength;
    }

    private Terminal TerminalOwner => (Terminal)Owner;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    protected override string GetClassNameCore() => nameof(Terminal);
    protected override string GetLocalizedControlTypeCore() => "terminal";
    protected override string GetNameCore() => !string.IsNullOrEmpty(TerminalOwner.Title)
        ? TerminalOwner.Title : string.IsNullOrEmpty(base.GetNameCore()) ? "Terminal" : base.GetNameCore();
    public override object? GetPattern(PatternInterface patternInterface) => GetPatternCore(patternInterface);
    protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.Value => this,
        PatternInterface.Text => _text,
        _ => base.GetPatternCore(patternInterface)
    };

    public string Value => TerminalOwner.AutomationText;
    public bool IsReadOnly => true;
    public void SetValue(string value) => throw new InvalidOperationException("Terminal output cannot be replaced through accessibility.");
    string IAutomationTextProviderSource.Text => Value;
    int IAutomationTextProviderSource.SelectionStart => TerminalOwner.AutomationSelectionStart;
    int IAutomationTextProviderSource.SelectionLength => TerminalOwner.AutomationSelectionLength;
    SupportedTextSelection IAutomationTextProviderSource.SupportedTextSelection => SupportedTextSelection.Single;
    void IAutomationTextProviderSource.Select(int start, int length) => TerminalOwner.SelectAutomationText(start, length);
    IReadOnlyList<Rect> IAutomationTextProviderSource.GetBoundingRectangles(int start, int length) => TerminalOwner.GetAutomationTextBounds(start, length);
    void IAutomationTextProviderSource.ScrollIntoView(int start, int length) => TerminalOwner.ScrollAutomationTextIntoView(start, length);
    int IAutomationTextViewSource.CaretIndex => TerminalOwner.AutomationCaretIndex;
    Rect IAutomationTextViewSource.TextViewport => TerminalOwner.AutomationTextViewport;
    IReadOnlyList<AutomationTextLine> IAutomationTextViewSource.GetTextLines() => TerminalOwner.GetAutomationTextLines();
    bool IAutomationTextViewSource.TryGetInsertionIndex(Point point, out int index) => TerminalOwner.TryGetAutomationInsertionIndex(point, out index);
    bool IAutomationTextViewSource.ReplaceSelection(string text) => false;

    internal void NotifyTextChanged() => RaiseAutomationEvent(AutomationEvents.TextPatternOnTextChanged);
    internal void NotifySelectionChanged()
    {
        int start = TerminalOwner.AutomationSelectionStart, length = TerminalOwner.AutomationSelectionLength;
        if (start == _selectionStart && length == _selectionLength) return;
        _selectionStart = start; _selectionLength = length;
        RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);
    }
}
