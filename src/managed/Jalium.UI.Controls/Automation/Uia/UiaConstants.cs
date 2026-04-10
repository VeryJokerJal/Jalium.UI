using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>
/// Windows UI Automation property, pattern, control type, and event ID constants.
/// Values defined by the Microsoft UI Automation specification.
/// </summary>
internal static class UiaConstants
{
    // ========================================================================
    // UIA Property IDs
    // ========================================================================

    internal const int UIA_RuntimeIdPropertyId = 30000;
    internal const int UIA_BoundingRectanglePropertyId = 30001;
    internal const int UIA_ProcessIdPropertyId = 30002;
    internal const int UIA_ControlTypePropertyId = 30003;
    internal const int UIA_LocalizedControlTypePropertyId = 30004;
    internal const int UIA_NamePropertyId = 30005;
    internal const int UIA_AcceleratorKeyPropertyId = 30006;
    internal const int UIA_AccessKeyPropertyId = 30007;
    internal const int UIA_HasKeyboardFocusPropertyId = 30008;
    internal const int UIA_IsKeyboardFocusablePropertyId = 30009;
    internal const int UIA_IsEnabledPropertyId = 30010;
    internal const int UIA_AutomationIdPropertyId = 30011;
    internal const int UIA_ClassNamePropertyId = 30012;
    internal const int UIA_HelpTextPropertyId = 30013;
    internal const int UIA_IsContentElementPropertyId = 30017;
    internal const int UIA_IsControlElementPropertyId = 30016;
    internal const int UIA_IsOffscreenPropertyId = 30022;
    internal const int UIA_FrameworkIdPropertyId = 30024;
    internal const int UIA_ItemStatusPropertyId = 30026;
    internal const int UIA_ItemTypePropertyId = 30021;
    internal const int UIA_IsPasswordPropertyId = 30019;
    internal const int UIA_NativeWindowHandlePropertyId = 30020;
    internal const int UIA_ProviderDescriptionPropertyId = 30107;

    // Pattern availability properties
    internal const int UIA_IsInvokePatternAvailablePropertyId = 30031;
    internal const int UIA_IsSelectionPatternAvailablePropertyId = 30036;
    internal const int UIA_IsValuePatternAvailablePropertyId = 30043;
    internal const int UIA_IsRangeValuePatternAvailablePropertyId = 30032;
    internal const int UIA_IsScrollPatternAvailablePropertyId = 30035;
    internal const int UIA_IsExpandCollapsePatternAvailablePropertyId = 30027;
    internal const int UIA_IsTogglePatternAvailablePropertyId = 30041;
    internal const int UIA_IsSelectionItemPatternAvailablePropertyId = 30037;
    internal const int UIA_IsScrollItemPatternAvailablePropertyId = 30038;

    // ========================================================================
    // UIA Control Type IDs
    // ========================================================================

    internal const int UIA_ButtonControlTypeId = 50000;
    internal const int UIA_CalendarControlTypeId = 50001;
    internal const int UIA_CheckBoxControlTypeId = 50002;
    internal const int UIA_ComboBoxControlTypeId = 50003;
    internal const int UIA_EditControlTypeId = 50004;
    internal const int UIA_HyperlinkControlTypeId = 50005;
    internal const int UIA_ImageControlTypeId = 50006;
    internal const int UIA_ListItemControlTypeId = 50007;
    internal const int UIA_ListControlTypeId = 50008;
    internal const int UIA_MenuControlTypeId = 50009;
    internal const int UIA_MenuBarControlTypeId = 50010;
    internal const int UIA_MenuItemControlTypeId = 50011;
    internal const int UIA_ProgressBarControlTypeId = 50012;
    internal const int UIA_RadioButtonControlTypeId = 50013;
    internal const int UIA_ScrollBarControlTypeId = 50014;
    internal const int UIA_SliderControlTypeId = 50015;
    internal const int UIA_SpinnerControlTypeId = 50016;
    internal const int UIA_StatusBarControlTypeId = 50017;
    internal const int UIA_TabControlTypeId = 50018;
    internal const int UIA_TabItemControlTypeId = 50019;
    internal const int UIA_TextControlTypeId = 50020;
    internal const int UIA_ToolBarControlTypeId = 50021;
    internal const int UIA_ToolTipControlTypeId = 50025;
    internal const int UIA_TreeControlTypeId = 50023;
    internal const int UIA_TreeItemControlTypeId = 50024;
    internal const int UIA_CustomControlTypeId = 50025;
    internal const int UIA_GroupControlTypeId = 50026;
    internal const int UIA_ThumbControlTypeId = 50027;
    internal const int UIA_DataGridControlTypeId = 50028;
    internal const int UIA_DataItemControlTypeId = 50029;
    internal const int UIA_DocumentControlTypeId = 50030;
    internal const int UIA_SplitButtonControlTypeId = 50031;
    internal const int UIA_WindowControlTypeId = 50032;
    internal const int UIA_PaneControlTypeId = 50033;
    internal const int UIA_HeaderControlTypeId = 50034;
    internal const int UIA_HeaderItemControlTypeId = 50035;
    internal const int UIA_TableControlTypeId = 50036;
    internal const int UIA_TitleBarControlTypeId = 50037;
    internal const int UIA_SeparatorControlTypeId = 50038;

    // ========================================================================
    // UIA Pattern IDs
    // ========================================================================

    internal const int UIA_InvokePatternId = 10000;
    internal const int UIA_SelectionPatternId = 10001;
    internal const int UIA_ValuePatternId = 10002;
    internal const int UIA_RangeValuePatternId = 10003;
    internal const int UIA_ScrollPatternId = 10004;
    internal const int UIA_ExpandCollapsePatternId = 10005;
    internal const int UIA_GridPatternId = 10006;
    internal const int UIA_GridItemPatternId = 10007;
    internal const int UIA_MultipleViewPatternId = 10008;
    internal const int UIA_WindowPatternId = 10009;
    internal const int UIA_SelectionItemPatternId = 10010;
    internal const int UIA_DockPatternId = 10011;
    internal const int UIA_TablePatternId = 10012;
    internal const int UIA_TableItemPatternId = 10013;
    internal const int UIA_TextPatternId = 10014;
    internal const int UIA_TogglePatternId = 10015;
    internal const int UIA_TransformPatternId = 10016;
    internal const int UIA_ScrollItemPatternId = 10017;
    internal const int UIA_ItemContainerPatternId = 10019;
    internal const int UIA_VirtualizedItemPatternId = 10020;
    internal const int UIA_SynchronizedInputPatternId = 10021;

    // ========================================================================
    // UIA Event IDs
    // ========================================================================

    internal const int UIA_ToolTipOpenedEventId = 20000;
    internal const int UIA_ToolTipClosedEventId = 20001;
    internal const int UIA_StructureChangedEventId = 20002;
    internal const int UIA_MenuOpenedEventId = 20003;
    internal const int UIA_AutomationPropertyChangedEventId = 20004;
    internal const int UIA_AutomationFocusChangedEventId = 20005;
    internal const int UIA_AsyncContentLoadedEventId = 20006;
    internal const int UIA_MenuClosedEventId = 20007;
    internal const int UIA_InputReachedTargetEventId = 20020;
    internal const int UIA_InputReachedOtherElementEventId = 20021;
    internal const int UIA_InputDiscardedEventId = 20022;
    internal const int UIA_LiveRegionChangedEventId = 20024;

    // Pattern-specific events
    internal const int UIA_Invoke_InvokedEventId = 20009;
    internal const int UIA_SelectionItem_ElementAddedToSelectionEventId = 20010;
    internal const int UIA_SelectionItem_ElementRemovedFromSelectionEventId = 20011;
    internal const int UIA_SelectionItem_ElementSelectedEventId = 20012;
    internal const int UIA_Selection_InvalidatedEventId = 20013;
    internal const int UIA_Text_TextSelectionChangedEventId = 20014;
    internal const int UIA_Text_TextChangedEventId = 20015;

    // ========================================================================
    // Runtime ID prefix
    // ========================================================================

    internal const int AppendRuntimeId = 3;

    // ========================================================================
    // Mapping Functions
    // ========================================================================

    internal static int MapControlType(AutomationControlType controlType) => controlType switch
    {
        AutomationControlType.Button => UIA_ButtonControlTypeId,
        AutomationControlType.Calendar => UIA_CalendarControlTypeId,
        AutomationControlType.CheckBox => UIA_CheckBoxControlTypeId,
        AutomationControlType.ComboBox => UIA_ComboBoxControlTypeId,
        AutomationControlType.Edit => UIA_EditControlTypeId,
        AutomationControlType.Hyperlink => UIA_HyperlinkControlTypeId,
        AutomationControlType.Image => UIA_ImageControlTypeId,
        AutomationControlType.ListItem => UIA_ListItemControlTypeId,
        AutomationControlType.List => UIA_ListControlTypeId,
        AutomationControlType.Menu => UIA_MenuControlTypeId,
        AutomationControlType.MenuBar => UIA_MenuBarControlTypeId,
        AutomationControlType.MenuItem => UIA_MenuItemControlTypeId,
        AutomationControlType.ProgressBar => UIA_ProgressBarControlTypeId,
        AutomationControlType.RadioButton => UIA_RadioButtonControlTypeId,
        AutomationControlType.ScrollBar => UIA_ScrollBarControlTypeId,
        AutomationControlType.Slider => UIA_SliderControlTypeId,
        AutomationControlType.Spinner => UIA_SpinnerControlTypeId,
        AutomationControlType.StatusBar => UIA_StatusBarControlTypeId,
        AutomationControlType.Tab => UIA_TabControlTypeId,
        AutomationControlType.TabItem => UIA_TabItemControlTypeId,
        AutomationControlType.Text => UIA_TextControlTypeId,
        AutomationControlType.ToolBar => UIA_ToolBarControlTypeId,
        AutomationControlType.ToolTip => UIA_ToolTipControlTypeId,
        AutomationControlType.Tree => UIA_TreeControlTypeId,
        AutomationControlType.TreeItem => UIA_TreeItemControlTypeId,
        AutomationControlType.Custom => UIA_CustomControlTypeId,
        AutomationControlType.Group => UIA_GroupControlTypeId,
        AutomationControlType.Thumb => UIA_ThumbControlTypeId,
        AutomationControlType.DataGrid => UIA_DataGridControlTypeId,
        AutomationControlType.DataItem => UIA_DataItemControlTypeId,
        AutomationControlType.Document => UIA_DocumentControlTypeId,
        AutomationControlType.SplitButton => UIA_SplitButtonControlTypeId,
        AutomationControlType.Window => UIA_WindowControlTypeId,
        AutomationControlType.Pane => UIA_PaneControlTypeId,
        AutomationControlType.Header => UIA_HeaderControlTypeId,
        AutomationControlType.HeaderItem => UIA_HeaderItemControlTypeId,
        AutomationControlType.Table => UIA_TableControlTypeId,
        AutomationControlType.TitleBar => UIA_TitleBarControlTypeId,
        AutomationControlType.Separator => UIA_SeparatorControlTypeId,
        _ => UIA_CustomControlTypeId,
    };

    internal static int MapPatternId(PatternInterface pattern) => pattern switch
    {
        PatternInterface.Invoke => UIA_InvokePatternId,
        PatternInterface.Selection => UIA_SelectionPatternId,
        PatternInterface.Value => UIA_ValuePatternId,
        PatternInterface.RangeValue => UIA_RangeValuePatternId,
        PatternInterface.Scroll => UIA_ScrollPatternId,
        PatternInterface.ScrollItem => UIA_ScrollItemPatternId,
        PatternInterface.ExpandCollapse => UIA_ExpandCollapsePatternId,
        PatternInterface.Grid => UIA_GridPatternId,
        PatternInterface.GridItem => UIA_GridItemPatternId,
        PatternInterface.MultipleView => UIA_MultipleViewPatternId,
        PatternInterface.Window => UIA_WindowPatternId,
        PatternInterface.SelectionItem => UIA_SelectionItemPatternId,
        PatternInterface.Dock => UIA_DockPatternId,
        PatternInterface.Table => UIA_TablePatternId,
        PatternInterface.TableItem => UIA_TableItemPatternId,
        PatternInterface.Text => UIA_TextPatternId,
        PatternInterface.Toggle => UIA_TogglePatternId,
        PatternInterface.Transform => UIA_TransformPatternId,
        PatternInterface.ItemContainer => UIA_ItemContainerPatternId,
        PatternInterface.VirtualizedItem => UIA_VirtualizedItemPatternId,
        PatternInterface.SynchronizedInput => UIA_SynchronizedInputPatternId,
        _ => 0,
    };

    internal static PatternInterface? MapUiaPatternIdToPatternInterface(int uiaPatternId) => uiaPatternId switch
    {
        UIA_InvokePatternId => PatternInterface.Invoke,
        UIA_SelectionPatternId => PatternInterface.Selection,
        UIA_ValuePatternId => PatternInterface.Value,
        UIA_RangeValuePatternId => PatternInterface.RangeValue,
        UIA_ScrollPatternId => PatternInterface.Scroll,
        UIA_ScrollItemPatternId => PatternInterface.ScrollItem,
        UIA_ExpandCollapsePatternId => PatternInterface.ExpandCollapse,
        UIA_GridPatternId => PatternInterface.Grid,
        UIA_GridItemPatternId => PatternInterface.GridItem,
        UIA_MultipleViewPatternId => PatternInterface.MultipleView,
        UIA_WindowPatternId => PatternInterface.Window,
        UIA_SelectionItemPatternId => PatternInterface.SelectionItem,
        UIA_DockPatternId => PatternInterface.Dock,
        UIA_TablePatternId => PatternInterface.Table,
        UIA_TableItemPatternId => PatternInterface.TableItem,
        UIA_TextPatternId => PatternInterface.Text,
        UIA_TogglePatternId => PatternInterface.Toggle,
        UIA_TransformPatternId => PatternInterface.Transform,
        UIA_ItemContainerPatternId => PatternInterface.ItemContainer,
        UIA_VirtualizedItemPatternId => PatternInterface.VirtualizedItem,
        UIA_SynchronizedInputPatternId => PatternInterface.SynchronizedInput,
        _ => null,
    };

    internal static int MapAutomationEvent(AutomationEvents eventId) => eventId switch
    {
        AutomationEvents.ToolTipOpened => UIA_ToolTipOpenedEventId,
        AutomationEvents.ToolTipClosed => UIA_ToolTipClosedEventId,
        AutomationEvents.MenuOpened => UIA_MenuOpenedEventId,
        AutomationEvents.MenuClosed => UIA_MenuClosedEventId,
        AutomationEvents.AutomationFocusChanged => UIA_AutomationFocusChangedEventId,
        AutomationEvents.InvokePatternOnInvoked => UIA_Invoke_InvokedEventId,
        AutomationEvents.SelectionItemPatternOnElementAddedToSelection => UIA_SelectionItem_ElementAddedToSelectionEventId,
        AutomationEvents.SelectionItemPatternOnElementRemovedFromSelection => UIA_SelectionItem_ElementRemovedFromSelectionEventId,
        AutomationEvents.SelectionItemPatternOnElementSelected => UIA_SelectionItem_ElementSelectedEventId,
        AutomationEvents.SelectionPatternOnInvalidated => UIA_Selection_InvalidatedEventId,
        AutomationEvents.TextPatternOnTextSelectionChanged => UIA_Text_TextSelectionChangedEventId,
        AutomationEvents.TextPatternOnTextChanged => UIA_Text_TextChangedEventId,
        AutomationEvents.AsyncContentLoaded => UIA_AsyncContentLoadedEventId,
        AutomationEvents.PropertyChanged => UIA_AutomationPropertyChangedEventId,
        AutomationEvents.StructureChanged => UIA_StructureChangedEventId,
        AutomationEvents.InputReachedTarget => UIA_InputReachedTargetEventId,
        AutomationEvents.InputReachedOtherElement => UIA_InputReachedOtherElementEventId,
        AutomationEvents.InputDiscarded => UIA_InputDiscardedEventId,
        AutomationEvents.LiveRegionChanged => UIA_LiveRegionChangedEventId,
        _ => 0,
    };

    internal static int MapAutomationProperty(AutomationProperty property)
    {
        if (property == AutomationProperty.NameProperty) return UIA_NamePropertyId;
        if (property == AutomationProperty.AutomationIdProperty) return UIA_AutomationIdPropertyId;
        if (property == AutomationProperty.IsEnabledProperty) return UIA_IsEnabledPropertyId;
        if (property == AutomationProperty.HasKeyboardFocusProperty) return UIA_HasKeyboardFocusPropertyId;
        if (property == AutomationProperty.BoundingRectangleProperty) return UIA_BoundingRectanglePropertyId;
        if (property == AutomationProperty.IsOffscreenProperty) return UIA_IsOffscreenPropertyId;
        if (property == AutomationProperty.ToggleStateProperty) return UIA_IsTogglePatternAvailablePropertyId;
        if (property == AutomationProperty.ValueProperty) return UIA_IsValuePatternAvailablePropertyId;
        if (property == AutomationProperty.RangeValueProperty) return UIA_IsRangeValuePatternAvailablePropertyId;
        if (property == AutomationProperty.ExpandCollapseStateProperty) return UIA_IsExpandCollapsePatternAvailablePropertyId;
        return 0;
    }
}

/// <summary>
/// Direction for UIA fragment navigation.
/// </summary>
public enum NavigateDirection
{
    Parent = 0,
    NextSibling = 1,
    PreviousSibling = 2,
    FirstChild = 3,
    LastChild = 4,
}

/// <summary>
/// Options for UIA providers.
/// </summary>
[Flags]
public enum ProviderOptions
{
    ClientSideProvider = 0x1,
    ServerSideProvider = 0x2,
    NonClientAreaProvider = 0x4,
    OverrideProvider = 0x8,
    ProviderOwnsSetFocus = 0x10,
    UseComThreading = 0x20,
}

/// <summary>
/// UIA structure change type.
/// </summary>
internal enum StructureChangeType
{
    ChildAdded = 0,
    ChildRemoved = 1,
    ChildrenInvalidated = 2,
    ChildrenBulkAdded = 3,
    ChildrenBulkRemoved = 4,
    ChildrenReordered = 5,
}
