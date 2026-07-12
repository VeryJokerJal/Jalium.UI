namespace Jalium.UI.Automation.Peers;

/// <summary>Identifies a UI Automation control pattern.</summary>
public enum PatternInterface
{
    Invoke = 0,
    Selection = 1,
    Value = 2,
    RangeValue = 3,
    Scroll = 4,
    ScrollItem = 5,
    ExpandCollapse = 6,
    Grid = 7,
    GridItem = 8,
    MultipleView = 9,
    Window = 10,
    SelectionItem = 11,
    Dock = 12,
    Table = 13,
    TableItem = 14,
    Toggle = 15,
    Transform = 16,
    Text = 17,
    ItemContainer = 18,
    VirtualizedItem = 19,
    SynchronizedInput = 20,
}

/// <summary>Identifies an event raised through UI Automation.</summary>
public enum AutomationEvents
{
    ToolTipOpened = 0,
    ToolTipClosed = 1,
    MenuOpened = 2,
    MenuClosed = 3,
    AutomationFocusChanged = 4,
    InvokePatternOnInvoked = 5,
    SelectionItemPatternOnElementAddedToSelection = 6,
    SelectionItemPatternOnElementRemovedFromSelection = 7,
    SelectionItemPatternOnElementSelected = 8,
    SelectionPatternOnInvalidated = 9,
    TextPatternOnTextSelectionChanged = 10,
    TextPatternOnTextChanged = 11,
    AsyncContentLoaded = 12,
    PropertyChanged = 13,
    StructureChanged = 14,
    InputReachedTarget = 15,
    InputReachedOtherElement = 16,
    InputDiscarded = 17,
    LiveRegionChanged = 18,
    Notification = 19,
    ActiveTextPositionChanged = 20,
}
