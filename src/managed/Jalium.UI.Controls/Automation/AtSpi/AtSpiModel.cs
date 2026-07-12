using System.Globalization;
using System.Text;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.AtSpi;

internal enum AtSpiRole : uint
{
    Invalid = 0,
    Calendar = 5,
    CheckBox = 7,
    ColumnHeader = 10,
    ComboBox = 11,
    Frame = 23,
    Image = 27,
    Label = 29,
    List = 31,
    ListItem = 32,
    Menu = 33,
    MenuBar = 34,
    MenuItem = 35,
    PageTab = 37,
    PageTabList = 38,
    Panel = 39,
    PasswordText = 40,
    ProgressBar = 42,
    Button = 43,
    RadioButton = 44,
    ScrollBar = 48,
    Separator = 50,
    Slider = 51,
    SpinButton = 52,
    StatusBar = 54,
    Table = 55,
    TableCell = 56,
    Text = 61,
    ToolBar = 63,
    ToolTip = 64,
    Tree = 65,
    Header = 71,
    Application = 75,
    Entry = 79,
    DocumentFrame = 82,
    Link = 88,
    TreeItem = 91,
    Grouping = 99,
    TitleBar = 104,
}

internal enum AtSpiState : int
{
    Active = 1,
    Checked = 4,
    Collapsed = 5,
    Defunct = 6,
    Editable = 7,
    Enabled = 8,
    Expandable = 9,
    Expanded = 10,
    Focusable = 11,
    Focused = 12,
    MultiLine = 17,
    Resizable = 21,
    Selectable = 22,
    Selected = 23,
    Sensitive = 24,
    Showing = 25,
    SingleLine = 26,
    Visible = 30,
    Indeterminate = 32,
    SelectableText = 38,
    Checkable = 41,
    ReadOnly = 43,
}

internal static class AtSpiModel
{
    internal static AtSpiRole MapRole(AutomationControlType controlType, string? className = null)
    {
        if (controlType == AutomationControlType.Edit &&
            string.Equals(className, "PasswordBox", StringComparison.Ordinal))
        {
            return AtSpiRole.PasswordText;
        }

        return controlType switch
        {
            AutomationControlType.Button or AutomationControlType.SplitButton => AtSpiRole.Button,
            AutomationControlType.Calendar => AtSpiRole.Calendar,
            AutomationControlType.CheckBox => AtSpiRole.CheckBox,
            AutomationControlType.ComboBox => AtSpiRole.ComboBox,
            AutomationControlType.Edit => AtSpiRole.Entry,
            AutomationControlType.Hyperlink => AtSpiRole.Link,
            AutomationControlType.Image => AtSpiRole.Image,
            AutomationControlType.ListItem => AtSpiRole.ListItem,
            AutomationControlType.List => AtSpiRole.List,
            AutomationControlType.Menu => AtSpiRole.Menu,
            AutomationControlType.MenuBar => AtSpiRole.MenuBar,
            AutomationControlType.MenuItem => AtSpiRole.MenuItem,
            AutomationControlType.ProgressBar => AtSpiRole.ProgressBar,
            AutomationControlType.RadioButton => AtSpiRole.RadioButton,
            AutomationControlType.ScrollBar => AtSpiRole.ScrollBar,
            AutomationControlType.Slider or AutomationControlType.Thumb => AtSpiRole.Slider,
            AutomationControlType.Spinner => AtSpiRole.SpinButton,
            AutomationControlType.StatusBar => AtSpiRole.StatusBar,
            AutomationControlType.Tab => AtSpiRole.PageTabList,
            AutomationControlType.TabItem => AtSpiRole.PageTab,
            AutomationControlType.Text => AtSpiRole.Label,
            AutomationControlType.ToolBar => AtSpiRole.ToolBar,
            AutomationControlType.ToolTip => AtSpiRole.ToolTip,
            AutomationControlType.Tree => AtSpiRole.Tree,
            AutomationControlType.TreeItem => AtSpiRole.TreeItem,
            AutomationControlType.Group => AtSpiRole.Grouping,
            AutomationControlType.DataGrid or AutomationControlType.Table => AtSpiRole.Table,
            AutomationControlType.DataItem => AtSpiRole.TableCell,
            AutomationControlType.Document => AtSpiRole.DocumentFrame,
            AutomationControlType.Window => AtSpiRole.Frame,
            AutomationControlType.Header => AtSpiRole.Header,
            AutomationControlType.HeaderItem => AtSpiRole.ColumnHeader,
            AutomationControlType.TitleBar => AtSpiRole.TitleBar,
            AutomationControlType.Separator => AtSpiRole.Separator,
            _ => AtSpiRole.Panel,
        };
    }

    internal static string GetRoleName(AtSpiRole role) => role switch
    {
        AtSpiRole.Application => "application",
        AtSpiRole.Button => "push button",
        AtSpiRole.Calendar => "calendar",
        AtSpiRole.CheckBox => "check box",
        AtSpiRole.ColumnHeader => "column header",
        AtSpiRole.ComboBox => "combo box",
        AtSpiRole.DocumentFrame => "document frame",
        AtSpiRole.Entry => "entry",
        AtSpiRole.Frame => "frame",
        AtSpiRole.Grouping => "grouping",
        AtSpiRole.Header => "header",
        AtSpiRole.Image => "image",
        AtSpiRole.Label => "label",
        AtSpiRole.Link => "link",
        AtSpiRole.List => "list",
        AtSpiRole.ListItem => "list item",
        AtSpiRole.Menu => "menu",
        AtSpiRole.MenuBar => "menu bar",
        AtSpiRole.MenuItem => "menu item",
        AtSpiRole.PageTab => "page tab",
        AtSpiRole.PageTabList => "page tab list",
        AtSpiRole.Panel => "panel",
        AtSpiRole.PasswordText => "password text",
        AtSpiRole.ProgressBar => "progress bar",
        AtSpiRole.RadioButton => "radio button",
        AtSpiRole.ScrollBar => "scroll bar",
        AtSpiRole.Separator => "separator",
        AtSpiRole.Slider => "slider",
        AtSpiRole.SpinButton => "spin button",
        AtSpiRole.StatusBar => "status bar",
        AtSpiRole.Table => "table",
        AtSpiRole.TableCell => "table cell",
        AtSpiRole.Text => "text",
        AtSpiRole.TitleBar => "title bar",
        AtSpiRole.ToolBar => "tool bar",
        AtSpiRole.ToolTip => "tool tip",
        AtSpiRole.Tree => "tree",
        AtSpiRole.TreeItem => "tree item",
        _ => "unknown",
    };

    internal static ulong GetStates(AutomationPeer peer, bool defunct = false)
    {
        if (defunct)
            return State(AtSpiState.Defunct);

        ulong states = 0;
        if (peer.IsEnabled())
            states |= State(AtSpiState.Enabled) | State(AtSpiState.Sensitive);
        if (peer.IsKeyboardFocusable())
            states |= State(AtSpiState.Focusable);
        if (peer.HasKeyboardFocus())
            states |= State(AtSpiState.Focused);
        if (!peer.IsOffscreen())
            states |= State(AtSpiState.Visible) | State(AtSpiState.Showing);

        if (peer.GetPattern(PatternInterface.Value) is IValueProvider value)
            states |= value.IsReadOnly ? State(AtSpiState.ReadOnly) : State(AtSpiState.Editable);

        if (peer.GetPattern(PatternInterface.Text) is ITextProvider text)
        {
            states |= State(AtSpiState.SelectableText);
            states |= text.Text.Contains('\n', StringComparison.Ordinal)
                ? State(AtSpiState.MultiLine)
                : State(AtSpiState.SingleLine);
            if (text.IsReadOnly)
                states |= State(AtSpiState.ReadOnly);
            else
                states |= State(AtSpiState.Editable);
        }

        if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand)
        {
            states |= State(AtSpiState.Expandable);
            states |= expand.ExpandCollapseState switch
            {
                ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded => State(AtSpiState.Expanded),
                ExpandCollapseState.Collapsed => State(AtSpiState.Collapsed),
                _ => 0,
            };
        }

        if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            states |= State(AtSpiState.Checkable);
            if (toggle.ToggleState == ToggleState.On)
                states |= State(AtSpiState.Checked);
            else if (toggle.ToggleState == ToggleState.Indeterminate)
                states |= State(AtSpiState.Indeterminate);
        }

        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection)
        {
            states |= State(AtSpiState.Selectable);
            if (selection.IsSelected)
                states |= State(AtSpiState.Selected);
        }

        if (peer.Owner is Window window)
        {
            states |= State(AtSpiState.Resizable);
            if (window.IsActive)
                states |= State(AtSpiState.Active);
        }

        return states;
    }

    internal static uint[] PackStates(ulong states) =>
        [(uint)(states & uint.MaxValue), (uint)(states >> 32)];

    internal static ulong State(AtSpiState state) => 1UL << (int)state;

    internal static string GetText(AutomationPeer peer)
    {
        if (peer.GetPattern(PatternInterface.Text) is ITextProvider text)
            return text.Text ?? string.Empty;
        if (peer.GetPattern(PatternInterface.Value) is IValueProvider value)
            return value.Value ?? string.Empty;
        return peer.GetAutomationControlType() == AutomationControlType.Text
            ? peer.GetName()
            : string.Empty;
    }

    internal static (string Text, int Start, int End) GetStringAtOffset(
        string? value,
        int offset,
        uint granularity)
    {
        value ??= string.Empty;
        if (value.Length == 0)
            return (string.Empty, 0, 0);

        offset = Math.Clamp(offset, 0, value.Length - 1);
        int start;
        int end;
        switch (granularity)
        {
            case 0: // character
                start = offset;
                if (char.IsLowSurrogate(value[start]) && start > 0 && char.IsHighSurrogate(value[start - 1]))
                    start--;
                end = start + 1;
                if (char.IsHighSurrogate(value[start]) && end < value.Length && char.IsLowSurrogate(value[end]))
                    end++;
                break;
            case 1: // word
                start = offset;
                while (start > 0 && !char.IsWhiteSpace(value[start - 1])) start--;
                end = offset;
                while (end < value.Length && !char.IsWhiteSpace(value[end])) end++;
                break;
            case 4: // line
                start = value.LastIndexOf('\n', offset);
                start = start < 0 ? 0 : start + 1;
                end = value.IndexOf('\n', offset);
                if (end < 0) end = value.Length;
                break;
            default: // sentence/paragraph and unknown granularities
                start = 0;
                end = value.Length;
                break;
        }

        return (value[start..end], start, end);
    }

    internal static string SliceText(string? value, int start, int end)
    {
        value ??= string.Empty;
        start = Math.Clamp(start, 0, value.Length);
        end = end < 0 ? value.Length : Math.Clamp(end, start, value.Length);
        return value[start..end];
    }

    internal static string Quote(string? value)
    {
        value ??= string.Empty;
        var builder = new StringBuilder(value.Length + 2).Append('\'');
        foreach (char c in value)
        {
            _ = c switch
            {
                '\\' => builder.Append("\\\\"),
                '\'' => builder.Append("\\'"),
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                '\t' => builder.Append("\\t"),
                _ when char.IsControl(c) => builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)),
                _ => builder.Append(c),
            };
        }

        return builder.Append('\'').ToString();
    }

    internal static string ObjectReference(string busName, string objectPath) =>
        $"({Quote(busName)}, objectpath {Quote(objectPath)})";
}
