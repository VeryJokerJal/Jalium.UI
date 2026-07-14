using System.Globalization;
using System.Text;
using Jalium.UI.Automation;
using RawProvider = Jalium.UI.Automation.Provider;

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

internal enum AtSpiLegacyTextPosition
{
    At,
    Before,
    After,
}

internal readonly record struct AtSpiTextChange(
    int Offset,
    string DeletedText,
    int DeletedCount,
    string InsertedText,
    int InsertedCount);

/// <summary>
/// Maps AT-SPI Unicode-scalar offsets to the UTF-16 offsets used by Jalium's
/// managed text providers. Invalid, unpaired surrogate code units are kept as
/// one addressable character so malformed application text remains navigable.
/// </summary>
internal sealed class AtSpiTextMap
{
    private readonly int[] _utf16Offsets;

    internal AtSpiTextMap(string? text)
    {
        Text = text ?? string.Empty;
        var offsets = new List<int>(Text.Length + 1) { 0 };
        int utf16Offset = 0;
        while (utf16Offset < Text.Length)
        {
            utf16Offset += char.IsHighSurrogate(Text[utf16Offset]) &&
                           utf16Offset + 1 < Text.Length &&
                           char.IsLowSurrogate(Text[utf16Offset + 1])
                ? 2
                : 1;
            offsets.Add(utf16Offset);
        }

        _utf16Offsets = offsets.ToArray();
    }

    internal string Text { get; }
    internal int CharacterCount => _utf16Offsets.Length - 1;

    internal int ToUtf16Offset(int scalarOffset) =>
        _utf16Offsets[Math.Clamp(scalarOffset, 0, CharacterCount)];

    internal int ToScalarOffset(int utf16Offset, bool roundUp = false)
    {
        utf16Offset = Math.Clamp(utf16Offset, 0, Text.Length);
        int index = Array.BinarySearch(_utf16Offsets, utf16Offset);
        if (index >= 0)
            return index;

        int insertionIndex = ~index;
        return roundUp ? insertionIndex : insertionIndex - 1;
    }

    internal int GetCodePoint(int scalarOffset)
    {
        if (scalarOffset < 0 || scalarOffset >= CharacterCount)
            return -1;

        int utf16Offset = _utf16Offsets[scalarOffset];
        char first = Text[utf16Offset];
        return char.IsHighSurrogate(first) &&
               utf16Offset + 1 < Text.Length &&
               char.IsLowSurrogate(Text[utf16Offset + 1])
            ? char.ConvertToUtf32(first, Text[utf16Offset + 1])
            : first;
    }

    internal string Slice(int start, int end)
    {
        start = Math.Clamp(start, 0, CharacterCount);
        end = end < 0 ? CharacterCount : Math.Clamp(end, start, CharacterCount);
        int utf16Start = ToUtf16Offset(start);
        int utf16End = ToUtf16Offset(end);
        return Text[utf16Start..utf16End];
    }
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

        if (peer.GetPattern(PatternInterface.Value) is RawProvider.IValueProvider value)
            states |= value.IsReadOnly ? State(AtSpiState.ReadOnly) : State(AtSpiState.Editable);

        if (peer.GetPattern(PatternInterface.Text) is RawProvider.ITextProvider text)
        {
            string content = text.DocumentRange.GetText(-1);
            states |= State(AtSpiState.SelectableText);
            states |= content.Contains('\n', StringComparison.Ordinal)
                ? State(AtSpiState.MultiLine)
                : State(AtSpiState.SingleLine);
            if (text.DocumentRange.GetAttributeValue(40015) is true)
                states |= State(AtSpiState.ReadOnly);
            else
                states |= State(AtSpiState.Editable);
        }

        if (peer.GetPattern(PatternInterface.ExpandCollapse) is RawProvider.IExpandCollapseProvider expand)
        {
            states |= State(AtSpiState.Expandable);
            states |= expand.ExpandCollapseState switch
            {
                ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded => State(AtSpiState.Expanded),
                ExpandCollapseState.Collapsed => State(AtSpiState.Collapsed),
                _ => 0,
            };
        }

        if (peer.GetPattern(PatternInterface.Toggle) is RawProvider.IToggleProvider toggle)
        {
            states |= State(AtSpiState.Checkable);
            if (toggle.ToggleState == ToggleState.On)
                states |= State(AtSpiState.Checked);
            else if (toggle.ToggleState == ToggleState.Indeterminate)
                states |= State(AtSpiState.Indeterminate);
        }

        if (peer.GetPattern(PatternInterface.SelectionItem) is RawProvider.ISelectionItemProvider selection)
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
        if (peer.GetPattern(PatternInterface.Text) is RawProvider.ITextProvider text)
            return text.DocumentRange.GetText(-1);
        if (peer.GetPattern(PatternInterface.Value) is RawProvider.IValueProvider value)
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
        var map = new AtSpiTextMap(value);
        if (offset < 0 || offset >= map.CharacterCount)
            return (string.Empty, 0, 0);

        if (granularity == 0)
            return (map.Slice(offset, offset + 1), offset, offset + 1);

        IReadOnlyList<int> boundaries = granularity switch
        {
            1 => GetWordStarts(map),
            2 => GetSentenceStarts(map),
            3 => GetLineStarts(map),
            4 => GetParagraphStarts(map),
            _ => [],
        };

        return GetRangeAtOffset(map, boundaries, offset);
    }

    internal static (string Text, int Start, int End) GetLegacyTextAtOffset(
        string? value,
        int offset,
        uint boundaryType,
        AtSpiLegacyTextPosition position)
    {
        var map = new AtSpiTextMap(value);
        if (map.CharacterCount == 0 || boundaryType > 6)
            return (string.Empty, 0, 0);

        IReadOnlyList<(int Start, int End)> ranges = boundaryType switch
        {
            0 => Enumerable.Range(0, map.CharacterCount)
                .Select(static index => (index, index + 1)).ToArray(),
            1 => MakeRanges(GetWordStarts(map)),
            2 => MakeRanges(GetWordEnds(map)),
            3 => MakeRanges(GetSentenceStarts(map)),
            4 => MakeRanges(GetSentenceEnds(map)),
            5 => MakeRanges(GetLineStarts(map)),
            6 => MakeRanges(GetLineEnds(map)),
            _ => [],
        };

        (int Start, int End)? selected = position switch
        {
            AtSpiLegacyTextPosition.At when offset >= 0 && offset < map.CharacterCount =>
                ranges.FirstOrDefault(range => range.Start <= offset && offset < range.End),
            AtSpiLegacyTextPosition.Before => ranges.LastOrDefault(
                range => range.End <= Math.Clamp(offset, 0, map.CharacterCount)),
            AtSpiLegacyTextPosition.After => ranges.FirstOrDefault(
                range => range.Start >= Math.Clamp(offset, 0, map.CharacterCount)),
            _ => null,
        };

        // ValueTuple's default value is also a valid-looking (0, 0), but all
        // real ranges are non-empty, so use it as the no-result sentinel.
        if (selected is not { } range || range.End <= range.Start)
            return (string.Empty, 0, 0);

        return (map.Slice(range.Start, range.End), range.Start, range.End);
    }

    internal static AtSpiTextChange GetTextChange(string? oldValue, string? newValue)
    {
        var oldMap = new AtSpiTextMap(oldValue);
        var newMap = new AtSpiTextMap(newValue);
        int commonPrefix = 0;
        int commonLimit = Math.Min(oldMap.CharacterCount, newMap.CharacterCount);
        while (commonPrefix < commonLimit &&
               oldMap.GetCodePoint(commonPrefix) == newMap.GetCodePoint(commonPrefix))
        {
            commonPrefix++;
        }

        int commonSuffix = 0;
        while (commonSuffix < oldMap.CharacterCount - commonPrefix &&
               commonSuffix < newMap.CharacterCount - commonPrefix &&
               oldMap.GetCodePoint(oldMap.CharacterCount - commonSuffix - 1) ==
               newMap.GetCodePoint(newMap.CharacterCount - commonSuffix - 1))
        {
            commonSuffix++;
        }

        int deletedCount = oldMap.CharacterCount - commonPrefix - commonSuffix;
        int insertedCount = newMap.CharacterCount - commonPrefix - commonSuffix;
        return new AtSpiTextChange(
            commonPrefix,
            oldMap.Slice(commonPrefix, commonPrefix + deletedCount),
            deletedCount,
            newMap.Slice(commonPrefix, commonPrefix + insertedCount),
            insertedCount);
    }

    internal static int GetCharacterCount(string? value) => new AtSpiTextMap(value).CharacterCount;

    internal static int ScalarToUtf16Offset(string? value, int scalarOffset) =>
        new AtSpiTextMap(value).ToUtf16Offset(scalarOffset);

    internal static int Utf16ToScalarOffset(string? value, int utf16Offset, bool roundUp = false) =>
        new AtSpiTextMap(value).ToScalarOffset(utf16Offset, roundUp);

    internal static int GetCharacterAtOffset(string? value, int scalarOffset) =>
        new AtSpiTextMap(value).GetCodePoint(scalarOffset);

    private static (string Text, int Start, int End) GetRangeAtOffset(
        AtSpiTextMap map,
        IReadOnlyList<int> boundaries,
        int offset)
    {
        IReadOnlyList<(int Start, int End)> ranges = MakeRanges(boundaries);
        foreach (var range in ranges)
        {
            if (range.Start <= offset && offset < range.End)
                return (map.Slice(range.Start, range.End), range.Start, range.End);
        }

        return (string.Empty, 0, 0);
    }

    private static IReadOnlyList<(int Start, int End)> MakeRanges(IReadOnlyList<int> boundaries)
    {
        var ranges = new List<(int Start, int End)>(Math.Max(0, boundaries.Count - 1));
        for (int index = 0; index + 1 < boundaries.Count; index++)
        {
            if (boundaries[index + 1] > boundaries[index])
                ranges.Add((boundaries[index], boundaries[index + 1]));
        }
        return ranges;
    }

    private static IReadOnlyList<int> GetWordStarts(AtSpiTextMap map)
    {
        var result = NewBoundaries(map);
        bool previousWasWord = false;
        for (int offset = 0; offset < map.CharacterCount; offset++)
        {
            bool currentIsWord = IsWordCharacter(map.GetCodePoint(offset));
            if (currentIsWord && !previousWasWord)
                AddBoundary(result, offset);
            previousWasWord = currentIsWord;
        }
        FinishBoundaries(result, map);
        return result;
    }

    private static IReadOnlyList<int> GetWordEnds(AtSpiTextMap map)
    {
        var result = NewBoundaries(map);
        bool previousWasWord = false;
        for (int offset = 0; offset < map.CharacterCount; offset++)
        {
            bool currentIsWord = IsWordCharacter(map.GetCodePoint(offset));
            if (previousWasWord && !currentIsWord)
                AddBoundary(result, offset);
            previousWasWord = currentIsWord;
        }
        if (previousWasWord)
            AddBoundary(result, map.CharacterCount);
        FinishBoundaries(result, map);
        return result;
    }

    private static IReadOnlyList<int> GetSentenceStarts(AtSpiTextMap map)
    {
        var result = NewBoundaries(map);
        for (int offset = 0; offset < map.CharacterCount; offset++)
        {
            if (!IsSentenceTerminator(map.GetCodePoint(offset)))
                continue;

            int next = offset + 1;
            while (next < map.CharacterCount && IsSentenceTerminator(map.GetCodePoint(next)))
                next++;
            while (next < map.CharacterCount && IsWhiteSpace(map.GetCodePoint(next)))
                next++;
            AddBoundary(result, next);
            offset = next - 1;
        }
        FinishBoundaries(result, map);
        return result;
    }

    private static IReadOnlyList<int> GetSentenceEnds(AtSpiTextMap map)
    {
        var result = NewBoundaries(map);
        for (int offset = 0; offset < map.CharacterCount; offset++)
        {
            if (!IsSentenceTerminator(map.GetCodePoint(offset)))
                continue;

            int next = offset + 1;
            while (next < map.CharacterCount && IsSentenceTerminator(map.GetCodePoint(next)))
                next++;
            AddBoundary(result, next);
            offset = next - 1;
        }
        FinishBoundaries(result, map);
        return result;
    }

    private static IReadOnlyList<int> GetLineStarts(AtSpiTextMap map)
    {
        var result = NewBoundaries(map);
        for (int offset = 0; offset < map.CharacterCount; offset++)
        {
            int next = ConsumeLineBreak(map, offset);
            if (next == offset)
                continue;
            AddBoundary(result, next);
            offset = next - 1;
        }
        FinishBoundaries(result, map);
        return result;
    }

    private static IReadOnlyList<int> GetLineEnds(AtSpiTextMap map)
    {
        var result = NewBoundaries(map);
        for (int offset = 0; offset < map.CharacterCount; offset++)
        {
            int next = ConsumeLineBreak(map, offset);
            if (next == offset)
                continue;
            AddBoundary(result, offset);
            offset = next - 1;
        }
        FinishBoundaries(result, map);
        return result;
    }

    private static IReadOnlyList<int> GetParagraphStarts(AtSpiTextMap map)
    {
        var result = NewBoundaries(map);
        for (int offset = 0; offset < map.CharacterCount; offset++)
        {
            int next = ConsumeParagraphBreak(map, offset);
            if (next == offset)
                continue;
            AddBoundary(result, next);
            offset = next - 1;
        }
        FinishBoundaries(result, map);
        return result;
    }

    private static List<int> NewBoundaries(AtSpiTextMap map) =>
        map.CharacterCount == 0 ? [] : [0];

    private static void FinishBoundaries(List<int> boundaries, AtSpiTextMap map) =>
        AddBoundary(boundaries, map.CharacterCount);

    private static void AddBoundary(List<int> boundaries, int offset)
    {
        if (boundaries.Count == 0 || boundaries[^1] != offset)
            boundaries.Add(offset);
    }

    private static int ConsumeLineBreak(AtSpiTextMap map, int offset)
    {
        if (offset < 0 || offset >= map.CharacterCount)
            return offset;

        int codePoint = map.GetCodePoint(offset);
        if (codePoint == '\r')
        {
            return offset + 1 < map.CharacterCount && map.GetCodePoint(offset + 1) == '\n'
                ? offset + 2
                : offset + 1;
        }
        return codePoint is '\n' or 0x85 or 0x2028 or 0x2029 ? offset + 1 : offset;
    }

    private static int ConsumeParagraphBreak(AtSpiTextMap map, int offset)
    {
        if (offset < 0 || offset >= map.CharacterCount)
            return offset;

        int codePoint = map.GetCodePoint(offset);
        if (codePoint == '\r')
        {
            return offset + 1 < map.CharacterCount && map.GetCodePoint(offset + 1) == '\n'
                ? offset + 2
                : offset + 1;
        }
        return codePoint is '\n' or 0x2029 ? offset + 1 : offset;
    }

    private static bool IsSentenceTerminator(int codePoint) =>
        codePoint is '.' or '!' or '?' or 0x2026 or 0x3002 or 0xFF01 or 0xFF1F;

    private static bool IsWhiteSpace(int codePoint) =>
        Rune.TryCreate(codePoint, out Rune rune) && Rune.IsWhiteSpace(rune);

    private static bool IsWordCharacter(int codePoint)
    {
        if (!Rune.TryCreate(codePoint, out Rune rune))
            return false;
        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.OtherNumber or
            UnicodeCategory.ConnectorPunctuation;
    }

    internal static string SliceText(string? value, int start, int end)
    {
        return new AtSpiTextMap(value).Slice(start, end);
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
