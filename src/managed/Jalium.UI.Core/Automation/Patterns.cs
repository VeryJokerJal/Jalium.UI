namespace Jalium.UI.Automation;

/// <summary>
/// Exposes a method to support UI Automation access to controls that initiate or perform
/// a single, unambiguous action and do not maintain state when activated.
/// </summary>
public interface IInvokeProvider
{
    /// <summary>
    /// Sends a request to activate a control and initiate its single, unambiguous action.
    /// </summary>
    void Invoke();
}

/// <summary>
/// Exposes methods and properties to support UI Automation access to controls that can
/// cycle through a set of states and maintain a state once set.
/// </summary>
public interface IToggleProvider
{
    /// <summary>
    /// Gets the toggle state of the control.
    /// </summary>
    ToggleState ToggleState { get; }

    /// <summary>
    /// Cycles through the toggle states of a control.
    /// </summary>
    void Toggle();
}

/// <summary>
/// Exposes methods and properties to support UI Automation access to controls that have
/// an intrinsic value that does not span a range, and that can be represented as a string.
/// </summary>
public interface IValueProvider
{
    /// <summary>
    /// Gets the value of the control.
    /// </summary>
    string Value { get; }

    /// <summary>
    /// Gets a value that specifies whether the value of a control is read-only.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Sets the value of a control.
    /// </summary>
    /// <param name="value">The value to set.</param>
    void SetValue(string value);
}

/// <summary>
/// Exposes methods and properties to support UI Automation access to controls that can
/// be set to a value within a range.
/// </summary>
public interface IRangeValueProvider
{
    /// <summary>
    /// Gets the current value of the control.
    /// </summary>
    double Value { get; }

    /// <summary>
    /// Gets the minimum range value that is supported by the control.
    /// </summary>
    double Minimum { get; }

    /// <summary>
    /// Gets the maximum range value that is supported by the control.
    /// </summary>
    double Maximum { get; }

    /// <summary>
    /// Gets the value that is added to or subtracted from the current value when a small
    /// change is made, such as with an arrow key.
    /// </summary>
    double SmallChange { get; }

    /// <summary>
    /// Gets the value that is added to or subtracted from the current value when a large
    /// change is made, such as with the PAGE DOWN key.
    /// </summary>
    double LargeChange { get; }

    /// <summary>
    /// Gets a value that specifies whether the value of a control is read-only.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Sets the value of the control.
    /// </summary>
    /// <param name="value">The value to set.</param>
    void SetValue(double value);
}

/// <summary>
/// Exposes methods and properties to support UI Automation access to controls that
/// visually expand to display content and collapse to hide content.
/// </summary>
public interface IExpandCollapseProvider
{
    /// <summary>
    /// Gets the expand/collapse state of the control.
    /// </summary>
    ExpandCollapseState ExpandCollapseState { get; }

    /// <summary>
    /// Displays all child nodes, controls, or content of the control.
    /// </summary>
    void Expand();

    /// <summary>
    /// Hides all descendant nodes, controls, or content of the control.
    /// </summary>
    void Collapse();
}

/// <summary>
/// Exposes methods and properties to support UI Automation access to controls that act
/// as containers for a collection of individual, selectable child items.
/// </summary>
public interface ISelectionProvider
{
    /// <summary>
    /// Gets the UI Automation providers for the selected items.
    /// </summary>
    /// <returns>The selected items.</returns>
    AutomationPeer[] GetSelection();

    /// <summary>
    /// Gets a value that specifies whether the control requires at least one item to be selected.
    /// </summary>
    bool IsSelectionRequired { get; }

    /// <summary>
    /// Gets a value that specifies whether the control supports selection of multiple items.
    /// </summary>
    bool CanSelectMultiple { get; }
}

/// <summary>
/// Exposes methods and properties to support UI Automation access to individual, selectable
/// child controls of containers that implement ISelectionProvider.
/// </summary>
public interface ISelectionItemProvider
{
    /// <summary>
    /// Gets a value that indicates whether an item is selected.
    /// </summary>
    bool IsSelected { get; }

    /// <summary>
    /// Gets the UI Automation provider that implements ISelectionProvider and acts as
    /// the container for the calling object.
    /// </summary>
    AutomationPeer SelectionContainer { get; }

    /// <summary>
    /// Clears any existing selection and then selects the current element.
    /// </summary>
    void Select();

    /// <summary>
    /// Adds the current element to the collection of selected items.
    /// </summary>
    void AddToSelection();

    /// <summary>
    /// Removes the current element from the collection of selected items.
    /// </summary>
    void RemoveFromSelection();
}

/// <summary>
/// Exposes methods and properties to support UI Automation access to controls that provide,
/// and are able to switch between, multiple representations of the same set of information
/// or child controls.
/// </summary>
public interface IScrollProvider
{
    /// <summary>
    /// Gets the current horizontal scroll position.
    /// </summary>
    double HorizontalScrollPercent { get; }

    /// <summary>
    /// Gets the current vertical scroll position.
    /// </summary>
    double VerticalScrollPercent { get; }

    /// <summary>
    /// Gets the current horizontal view size.
    /// </summary>
    double HorizontalViewSize { get; }

    /// <summary>
    /// Gets the current vertical view size.
    /// </summary>
    double VerticalViewSize { get; }

    /// <summary>
    /// Gets a value that indicates whether the control can scroll horizontally.
    /// </summary>
    bool HorizontallyScrollable { get; }

    /// <summary>
    /// Gets a value that indicates whether the control can scroll vertically.
    /// </summary>
    bool VerticallyScrollable { get; }

    /// <summary>
    /// Scrolls the visible region of the content area horizontally and vertically.
    /// </summary>
    void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount);

    /// <summary>
    /// Sets the horizontal and vertical scroll position as a percentage of the total
    /// content area within the control.
    /// </summary>
    void SetScrollPercent(double horizontalPercent, double verticalPercent);
}

/// <summary>
/// Specifies the direction and distance to scroll.
/// </summary>
public enum ScrollAmount
{
    /// <summary>
    /// Scrolls by a large decrement.
    /// </summary>
    LargeDecrement,

    /// <summary>
    /// Scrolls by a small decrement.
    /// </summary>
    SmallDecrement,

    /// <summary>
    /// Does not scroll.
    /// </summary>
    NoAmount,

    /// <summary>
    /// Scrolls by a large increment.
    /// </summary>
    LargeIncrement,

    /// <summary>
    /// Scrolls by a small increment.
    /// </summary>
    SmallIncrement
}

/// <summary>
/// Exposes a method to scroll a container to make an item within that container visible.
/// </summary>
public interface IScrollItemProvider
{
    /// <summary>
    /// Scrolls the content area of a container object to display the item within the visible region.
    /// </summary>
    void ScrollIntoView();
}

/// <summary>
/// Specifies the kind of text selection a control supports for the UI Automation Text pattern.
/// </summary>
public enum SupportedTextSelection
{
    /// <summary>The control does not support text selection.</summary>
    None = 0,

    /// <summary>The control supports a single, contiguous text selection.</summary>
    Single = 1,

    /// <summary>The control supports multiple, disjoint text selections.</summary>
    Multiple = 2,
}

/// <summary>
/// Exposes a control's text content and its current selection to UI Automation's Text pattern,
/// so external clients (screen readers such as Narrator, dictation, and translation/OCR "look-up"
/// tools) can detect which text is currently selected via <c>TextPattern.GetSelection()</c> and
/// read it with <c>ITextRangeProvider.GetText()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by an <see cref="AutomationPeer"/>; the Windows UI Automation bridge adapts it to
/// the native <c>ITextProvider</c> / <c>ITextRangeProvider</c> COM interfaces. The model is a plain
/// character-offset one: the document is <see cref="Text"/> and every range is a half-open interval
/// <c>[start, start + length)</c> of UTF-16 code units into it — the same model UIA's own sample
/// providers use for plain edit controls.
/// </para>
/// </remarks>
public interface ITextProvider
{
    /// <summary>Gets the full text of the document.</summary>
    string Text { get; }

    /// <summary>Gets the start offset (in characters) of the current selection or caret.</summary>
    int SelectionStart { get; }

    /// <summary>Gets the length (in characters) of the current selection; zero when only a caret is present.</summary>
    int SelectionLength { get; }

    /// <summary>Gets a value indicating whether the text is read-only.</summary>
    bool IsReadOnly { get; }

    /// <summary>Gets the kind of text selection the control supports.</summary>
    SupportedTextSelection SupportedTextSelection { get; }

    /// <summary>
    /// Selects the character range <c>[start, start + length)</c>. Invoked when a UI Automation
    /// client requests a selection through the Text pattern.
    /// </summary>
    void Select(int start, int length);

    /// <summary>
    /// Returns the bounding rectangles, in the owner element's local coordinate space, that cover
    /// the character range <c>[start, start + length)</c>. Return an empty list when precise glyph
    /// geometry is unavailable; the bridge then reports no rectangles rather than a misleading one.
    /// </summary>
    IReadOnlyList<Rect> GetBoundingRectangles(int start, int length);

    /// <summary>Best-effort scroll so the character range becomes visible. May be a no-op.</summary>
    void ScrollIntoView(int start, int length);
}
