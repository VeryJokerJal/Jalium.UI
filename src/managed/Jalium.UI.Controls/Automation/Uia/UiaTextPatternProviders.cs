using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

// Source-generated COM wrappers ([GeneratedComClass]) that adapt the framework-neutral
// Jalium.UI.Automation.ITextProvider to the native UIA Text pattern COM interfaces
// (IUiaTextProvider / IUiaTextRangeProvider). This is what lets an external UIA client — a screen
// reader (Narrator), dictation, or a translation / look-up ("划词") tool — detect the currently
// selected text: it QIs the provider for TextPattern, calls GetSelection() to obtain a range, and
// GetText() to read it.
//
// A range is modelled as a half-open character interval [_start, _end) into ITextProvider.Text.
// When UIA passes one of our own ranges back to us (Compare / CompareEndpoints / MoveEndpointByRange),
// ComWrappers unwraps the CCW to the SAME managed UiaTextRangeProvider instance we handed out, so a
// plain `is UiaTextRangeProvider` cast recovers its endpoints — identical to how WinForms/WPF do it.

/// <summary>UIA TextUnit (granularity for range navigation). Values match the native enum.</summary>
internal enum TextUnit
{
    Character = 0,
    Format = 1,
    Word = 2,
    Line = 3,
    Paragraph = 4,
    Page = 5,
    Document = 6,
}

/// <summary>UIA range endpoint discriminator. Values match the native enum.</summary>
internal enum TextPatternRangeEndpoint
{
    Start = 0,
    End = 1,
}

/// <summary>Adapts <see cref="ITextProvider"/> to the native UIA <c>ITextProvider</c>.</summary>
[GeneratedComClass]
internal sealed partial class UiaTextProviderWrapper : IUiaTextProvider
{
    private readonly ITextProvider _inner;
    private readonly AutomationPeerProvider _host;

    internal UiaTextProviderWrapper(ITextProvider inner, AutomationPeerProvider host)
    {
        _inner = inner;
        _host = host;
    }

    public IUiaTextRangeProvider[]? GetSelection()
    {
        int len = _inner.Text.Length;
        int start = Math.Clamp(_inner.SelectionStart, 0, len);
        int end = Math.Clamp(start + Math.Max(0, _inner.SelectionLength), start, len);
        // A control with only a caret (no selected text) reports a single degenerate range at the
        // caret — required by the Text pattern contract, so clients see the insertion point.
        return [new UiaTextRangeProvider(_inner, _host, start, end)];
    }

    public IUiaTextRangeProvider[]? GetVisibleRanges()
        => [new UiaTextRangeProvider(_inner, _host, 0, _inner.Text.Length)];

    public IUiaTextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement)
        => new UiaTextRangeProvider(_inner, _host, 0, _inner.Text.Length);

    public IUiaTextRangeProvider? RangeFromPoint(UiaPoint point)
        => new UiaTextRangeProvider(_inner, _host, 0, 0);

    public IUiaTextRangeProvider? get_DocumentRange()
        => new UiaTextRangeProvider(_inner, _host, 0, _inner.Text.Length);

    public int get_SupportedTextSelection() => (int)_inner.SupportedTextSelection;
}

/// <summary>Adapts a character-offset range over <see cref="ITextProvider"/> to native UIA <c>ITextRangeProvider</c>.</summary>
[GeneratedComClass]
internal sealed partial class UiaTextRangeProvider : IUiaTextRangeProvider
{
    private readonly ITextProvider _source;
    private readonly AutomationPeerProvider _host;
    private int _start;
    private int _end;

    internal UiaTextRangeProvider(ITextProvider source, AutomationPeerProvider host, int start, int end)
    {
        _source = source;
        _host = host;
        int len = source.Text.Length;
        _start = Math.Clamp(Math.Min(start, end), 0, len);
        _end = Math.Clamp(Math.Max(start, end), _start, len);
    }

    private string DocText => _source.Text;

    // The document can change under a range UIA is holding; re-clamp before every read.
    private void ClampToDocument()
    {
        int len = DocText.Length;
        if (_start < 0) _start = 0;
        if (_start > len) _start = len;
        if (_end < _start) _end = _start;
        if (_end > len) _end = len;
    }

    public IUiaTextRangeProvider? Clone() => new UiaTextRangeProvider(_source, _host, _start, _end);

    public int Compare(IUiaTextRangeProvider range)
        => range is UiaTextRangeProvider o && ReferenceEquals(o._source, _source)
           && o._start == _start && o._end == _end ? 1 : 0;

    public int CompareEndpoints(int endpoint, IUiaTextRangeProvider targetRange, int targetEndpoint)
    {
        if (targetRange is not UiaTextRangeProvider o) return 0;
        int a = endpoint == (int)TextPatternRangeEndpoint.Start ? _start : _end;
        int b = targetEndpoint == (int)TextPatternRangeEndpoint.Start ? o._start : o._end;
        return a - b;
    }

    public void ExpandToEnclosingUnit(int unit)
    {
        ClampToDocument();
        string text = DocText;
        switch ((TextUnit)unit)
        {
            case TextUnit.Character:
            case TextUnit.Format:
                if (_end <= _start)
                {
                    if (_start < text.Length) _end = NextCharBoundary(text, _start);
                    else _start = PrevCharBoundary(text, _end);
                }
                break;
            case TextUnit.Word:
                _start = WordStart(text, _start);
                _end = WordEnd(text, _start);
                break;
            case TextUnit.Line:
            case TextUnit.Paragraph:
                _start = LineStart(text, _start);
                _end = LineEnd(text, _start);
                break;
            case TextUnit.Page:
            case TextUnit.Document:
                _start = 0;
                _end = text.Length;
                break;
        }
    }

    // Plain text has no attribute runs to search; report "not found" (null) safely.
    public IUiaTextRangeProvider? FindAttribute(int attributeId, UiaVariant val, int backward) => null;

    public IUiaTextRangeProvider? FindText(string text, int backward, int ignoreCase)
    {
        ClampToDocument();
        if (string.IsNullOrEmpty(text)) return null;
        string hay = DocText.Substring(_start, _end - _start);
        var cmp = ignoreCase != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int idx = backward != 0 ? hay.LastIndexOf(text, cmp) : hay.IndexOf(text, cmp);
        if (idx < 0) return null;
        int s = _start + idx;
        return new UiaTextRangeProvider(_source, _host, s, s + text.Length);
    }

    public UiaVariant GetAttributeValue(int attributeId)
    {
        if (attributeId == UiaConstants.UIA_IsReadOnlyAttributeId)
        {
            var v = default(UiaVariant);
            v.vt = UiaVariant.VT_BOOL;
            v.boolVal = (short)(_source.IsReadOnly ? -1 : 0);
            return v;
        }

        // Unhandled attribute: hand back UIA's reserved "not supported" sentinel so the client does
        // not mistake a VT_EMPTY for a real (empty) attribute value.
        try
        {
            if (UiaNativeMethods.UiaGetReservedNotSupportedValue(out nint punk) >= 0 && punk != nint.Zero)
                return UiaVariant.FromUnknown(punk);
        }
        catch
        {
            // Fall through to VT_EMPTY on any failure — never surface a hard error for an attribute query.
        }
        return default;
    }

    public double[]? GetBoundingRectangles()
    {
        ClampToDocument();
        var rects = _source.GetBoundingRectangles(_start, _end - _start);
        if (rects == null || rects.Count == 0) return [];
        var result = new double[rects.Count * 4];
        for (int i = 0; i < rects.Count; i++)
        {
            var screen = _host.LocalRectToScreen(rects[i]);
            result[i * 4 + 0] = screen.Left;
            result[i * 4 + 1] = screen.Top;
            result[i * 4 + 2] = screen.Width;
            result[i * 4 + 3] = screen.Height;
        }
        return result;
    }

    public IRawElementProviderSimple? GetEnclosingElement() => _host;

    public string GetText(int maxLength)
    {
        ClampToDocument();
        int len = _end - _start;
        if (len <= 0) return string.Empty;
        string s = DocText.Substring(_start, len);
        if (maxLength >= 0 && maxLength < s.Length) s = s.Substring(0, maxLength);
        return s;
    }

    public int Move(int unit, int count)
    {
        ClampToDocument();
        if (count == 0) return 0;
        int newStart = MoveOffsetByUnit(DocText, _start, (TextUnit)unit, count, out int actual);
        _start = newStart;
        _end = newStart;
        // Leave the range spanning one unit at the destination (collapsed for an empty document).
        ExpandToEnclosingUnit(unit);
        return actual;
    }

    public int MoveEndpointByUnit(int endpoint, int unit, int count)
    {
        ClampToDocument();
        int offset = endpoint == (int)TextPatternRangeEndpoint.Start ? _start : _end;
        int newOffset = MoveOffsetByUnit(DocText, offset, (TextUnit)unit, count, out int actual);
        if (endpoint == (int)TextPatternRangeEndpoint.Start)
        {
            _start = newOffset;
            if (_end < _start) _end = _start;
        }
        else
        {
            _end = newOffset;
            if (_start > _end) _start = _end;
        }
        return actual;
    }

    public void MoveEndpointByRange(int endpoint, IUiaTextRangeProvider targetRange, int targetEndpoint)
    {
        if (targetRange is not UiaTextRangeProvider o) return;
        int target = targetEndpoint == (int)TextPatternRangeEndpoint.Start ? o._start : o._end;
        if (endpoint == (int)TextPatternRangeEndpoint.Start)
        {
            _start = target;
            if (_end < _start) _end = _start;
        }
        else
        {
            _end = target;
            if (_start > _end) _start = _end;
        }
        ClampToDocument();
    }

    public void Select()
    {
        ClampToDocument();
        _source.Select(_start, _end - _start);
    }

    // Single-selection control: adding/removing disjoint ranges is a no-op rather than an error.
    public void AddToSelection() { }

    public void RemoveFromSelection() { }

    public void ScrollIntoView(int alignToTop)
    {
        ClampToDocument();
        _source.ScrollIntoView(_start, _end - _start);
    }

    // A plain text range has no embedded child automation elements.
    public IRawElementProviderSimple[]? GetChildren() => [];

    // ========================================================================
    // Text-unit boundary helpers (plain-text, surrogate-pair aware)
    // ========================================================================

    private static int NextCharBoundary(string s, int i)
    {
        if (i >= s.Length) return s.Length;
        i++;
        if (i < s.Length && char.IsLowSurrogate(s[i]) && char.IsHighSurrogate(s[i - 1])) i++;
        return i;
    }

    private static int PrevCharBoundary(string s, int i)
    {
        if (i <= 0) return 0;
        i--;
        if (i > 0 && char.IsLowSurrogate(s[i]) && char.IsHighSurrogate(s[i - 1])) i--;
        return i;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int WordStart(string s, int i)
    {
        i = Math.Clamp(i, 0, s.Length);
        while (i > 0 && IsWordChar(s[i - 1])) i--;
        return i;
    }

    private static int WordEnd(string s, int i)
    {
        i = Math.Clamp(i, 0, s.Length);
        while (i < s.Length && IsWordChar(s[i])) i++;
        return i;
    }

    private static int NextWordStart(string s, int i)
    {
        i = WordEnd(s, i);
        while (i < s.Length && !IsWordChar(s[i])) i++;
        return i;
    }

    private static int PrevWordStart(string s, int i)
    {
        int j = Math.Clamp(i, 0, s.Length);
        while (j > 0 && !IsWordChar(s[j - 1])) j--;
        while (j > 0 && IsWordChar(s[j - 1])) j--;
        return j;
    }

    private static int LineStart(string s, int i)
    {
        if (i <= 0) return 0;
        if (i > s.Length) i = s.Length;
        int nl = s.LastIndexOf('\n', i - 1);
        return nl < 0 ? 0 : nl + 1;
    }

    private static int LineEnd(string s, int i)
    {
        i = Math.Clamp(i, 0, s.Length);
        int nl = s.IndexOf('\n', i);
        return nl < 0 ? s.Length : nl + 1;
    }

    private static int PrevLineStart(string s, int i)
    {
        int ls = LineStart(s, i);
        return ls == 0 ? 0 : LineStart(s, ls - 1);
    }

    private static int MoveOffsetByUnit(string s, int offset, TextUnit unit, int count, out int actual)
    {
        actual = 0;
        offset = Math.Clamp(offset, 0, s.Length);
        if (count == 0) return offset;
        int dir = count > 0 ? 1 : -1;
        int steps = Math.Abs(count);

        switch (unit)
        {
            case TextUnit.Character:
            case TextUnit.Format:
                for (int n = 0; n < steps; n++)
                {
                    int next = dir > 0 ? NextCharBoundary(s, offset) : PrevCharBoundary(s, offset);
                    if (next == offset) break;
                    offset = next;
                    actual += dir;
                }
                break;
            case TextUnit.Word:
                for (int n = 0; n < steps; n++)
                {
                    int next = dir > 0 ? NextWordStart(s, offset) : PrevWordStart(s, offset);
                    if (next == offset) break;
                    offset = next;
                    actual += dir;
                }
                break;
            case TextUnit.Line:
            case TextUnit.Paragraph:
                for (int n = 0; n < steps; n++)
                {
                    int next = dir > 0 ? LineEnd(s, offset) : PrevLineStart(s, offset);
                    if (next == offset) break;
                    offset = next;
                    actual += dir;
                }
                break;
            case TextUnit.Page:
            case TextUnit.Document:
                offset = dir > 0 ? s.Length : 0;
                actual = dir;
                break;
        }
        return offset;
    }
}
