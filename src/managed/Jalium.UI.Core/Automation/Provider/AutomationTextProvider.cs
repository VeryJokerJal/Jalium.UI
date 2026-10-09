using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Automation.Provider;

/// <summary>
/// Adapts the framework's internal character-offset text model to the public WPF-compatible
/// Text provider contracts. Platform bridges consume these canonical contracts instead of
/// exposing a second provider API.
/// </summary>
internal sealed class AutomationTextProvider : ITextProvider
{
    private readonly AutomationPeer _peer;
    private readonly IAutomationTextProviderSource _source;

    internal AutomationTextProvider(AutomationPeer peer, IAutomationTextProviderSource source)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    internal AutomationPeer Peer => _peer;

    internal IAutomationTextProviderSource Source => _source;

    public ITextRangeProvider DocumentRange => CreateRange(0, _source.Text.Length);

    public SupportedTextSelection SupportedTextSelection => _source.SupportedTextSelection;

    public ITextRangeProvider[] GetSelection()
    {
        int length = _source.Text.Length;
        int start = Math.Clamp(_source.SelectionStart, 0, length);
        int end = Math.Clamp(start + Math.Max(0, _source.SelectionLength), start, length);
        return [CreateRange(start, end)];
    }

    public ITextRangeProvider[] GetVisibleRanges() => _source is IAutomationTextViewSource view
        ? AutomationTextNavigation.VisibleRanges(_source, view).Select(range =>
            (ITextRangeProvider)CreateRange(range.Start, range.Start + range.Length)).ToArray()
        : [DocumentRange];

    public ITextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement)
    {
        ArgumentNullException.ThrowIfNull(childElement);
        return DocumentRange;
    }

    public ITextRangeProvider? RangeFromPoint(Point screenLocation)
    {
        if (!double.IsFinite(screenLocation.X) || !double.IsFinite(screenLocation.Y))
            throw new ArgumentException("The screen location must contain finite coordinates.", nameof(screenLocation));

        if (_source is not IAutomationTextViewSource view || _peer.Owner is not UIElement element
            || !element.GetRenderMatrix().TryInvert(out _)) return null;
        Rect viewport = view.TextViewport;
        if (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0) return null;
        Point local = element.PointFromScreen(screenLocation);
        var visibility = new AutomationVisibility(element);
        if (viewport.Contains(local) && !visibility.Contains(local)) return null;
        local = new Point(Math.Clamp(local.X, viewport.Left, Math.BitDecrement(viewport.Right)),
            Math.Clamp(local.Y, viewport.Top, Math.BitDecrement(viewport.Bottom)));
        if (!visibility.Contains(local) && !visibility.TryGetPoint(viewport, local, out local)) return null;
        return view.TryGetInsertionIndex(local, out int index) ? CreateRange(index, index) : null;
    }

    internal AutomationTextRangeProvider CreateRange(int start, int end) =>
        new(this, start, end);
}

/// <summary>Canonical managed text range over an internal character-offset text source.</summary>
internal sealed class AutomationTextRangeProvider : ITextRangeProvider
{
    private const int IsReadOnlyAttributeId = 40015;

    private readonly AutomationTextProvider _provider;
    private int _start;
    private int _end;

    internal AutomationTextRangeProvider(AutomationTextProvider provider, int start, int end)
    {
        _provider = provider;
        int length = Source.Text.Length;
        _start = Math.Clamp(Math.Min(start, end), 0, length);
        _end = Math.Clamp(Math.Max(start, end), _start, length);
    }

    private IAutomationTextProviderSource Source => _provider.Source;

    private string DocumentText => Source.Text;

    public void AddToSelection()
    {
        // The current source contract supports one contiguous selection. Keep the existing
        // selection unchanged rather than fabricating a disjoint range.
    }

    public ITextRangeProvider Clone() => new AutomationTextRangeProvider(_provider, _start, _end);

    public bool Compare(ITextRangeProvider range) =>
        range is AutomationTextRangeProvider other &&
        ReferenceEquals(other._provider, _provider) &&
        other._start == _start &&
        other._end == _end;

    public int CompareEndpoints(
        TextPatternRangeEndpoint endpoint,
        ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not AutomationTextRangeProvider other ||
            !ReferenceEquals(other._provider, _provider))
        {
            throw new ArgumentException("The target range belongs to a different text provider.", nameof(targetRange));
        }

        int value = endpoint == TextPatternRangeEndpoint.Start ? _start : _end;
        int target = targetEndpoint == TextPatternRangeEndpoint.Start ? other._start : other._end;
        return value - target;
    }

    public void ExpandToEnclosingUnit(TextUnit unit)
    {
        ClampToDocument();
        string text = DocumentText;
        switch (unit)
        {
            case TextUnit.Character:
            case TextUnit.Format:
                if (unit == TextUnit.Format && Source is IAutomationTextStyleSource)
                {
                    if (AutomationTextStyles.At(AutomationTextStyles.GetRuns(Source), _start, text.Length) is { } format)
                    { _start = format.Start; _end = format.Start + format.Length; }
                    break;
                }
                if (_end <= _start)
                {
                    if (_start < text.Length)
                        _end = NextCharacterBoundary(text, _start);
                    else
                        _start = PreviousCharacterBoundary(text, _end);
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

            default:
                throw new ArgumentOutOfRangeException(nameof(unit));
        }
    }

    public ITextRangeProvider? FindAttribute(int attribute, object? value, bool backward)
    {
        ClampToDocument();
        if (attribute == IsReadOnlyAttributeId) return Equals(Source.IsReadOnly, value) ? Clone() : null;
        var matches = new List<AutomationTextSpan>();
        foreach (var run in AutomationTextStyles.GetRuns(Source))
        {
            int start = Math.Max(run.Start, _start), end = Math.Min(run.Start + run.Length, _end);
            if (end <= start || AutomationTextStyles.Value(run.Style, attribute) is not { } actual || !Equals(actual, value)) continue;
            if (matches.Count > 0 && matches[^1].Start + matches[^1].Length == start)
                matches[^1] = matches[^1] with { Length = end - matches[^1].Start };
            else matches.Add(new(start, end - start));
        }
        if (matches.Count == 0) return null;
        var match = backward ? matches[^1] : matches[0];
        return _provider.CreateRange(match.Start, match.Start + match.Length);
    }

    public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(text);
        ClampToDocument();
        if (text.Length == 0)
            return null;

        string haystack = DocumentText.Substring(_start, _end - _start);
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int index = backward ? haystack.LastIndexOf(text, comparison) : haystack.IndexOf(text, comparison);
        return index < 0 ? null : new AutomationTextRangeProvider(_provider, _start + index, _start + index + text.Length);
    }

    public object? GetAttributeValue(int attribute)
    {
        ClampToDocument();
        if (attribute == IsReadOnlyAttributeId) return Source.IsReadOnly;
        var runs = AutomationTextStyles.GetRuns(Source);
        if (_start == _end)
            return AutomationTextStyles.At(runs, _start, DocumentText.Length) is { } caret ? AutomationTextStyles.Value(caret.Style, attribute) : null;
        object? result = null; bool found = false;
        foreach (var run in runs)
        {
            if (run.Start >= _end || run.Start + run.Length <= _start) continue;
            object? value = AutomationTextStyles.Value(run.Style, attribute);
            if (value == null) return null;
            if (!found) { found = true; result = value; }
            else if (!Equals(result, value)) result = AutomationTextAttributeValues.Mixed;
        }
        return result;
    }

    public double[] GetBoundingRectangles()
    {
        IReadOnlyList<Rect> rectangles = GetLocalBoundingRectangles();
        if (rectangles.Count == 0)
            return [];

        var values = new List<double>(rectangles.Count * 4);
        AutomationVisibility? visibility = _provider.Peer.Owner is UIElement owner ? new(owner) : null;
        Matrix screen = Matrix.Identity;
        if (_provider.Peer.Owner is UIElement visual)
        {
            Point origin = visual.PointToScreen(default), x = visual.PointToScreen(new(1, 0)), y = visual.PointToScreen(new(0, 1));
            screen = new Matrix(x.X - origin.X, x.Y - origin.Y, y.X - origin.X, y.Y - origin.Y, origin.X, origin.Y);
        }
        for (int index = 0; index < rectangles.Count; index++)
        {
            Rect rectangle = rectangles[index];
            if (visibility != null && !visibility.TryClip(rectangle, screen, out rectangle)) continue;

            values.Add(rectangle.Left);
            values.Add(rectangle.Top);
            values.Add(rectangle.Width);
            values.Add(rectangle.Height);
        }

        return values.ToArray();
    }

    internal IReadOnlyList<Rect> GetLocalBoundingRectangles()
    {
        ClampToDocument();
        return Source.GetBoundingRectangles(_start, _end - _start);
    }

    public IRawElementProviderSimple[] GetChildren() => [];

    public IRawElementProviderSimple? GetEnclosingElement() =>
        _provider.Peer.ProviderFromPeer(_provider.Peer);

    public string GetText(int maxLength)
    {
        ClampToDocument();
        if (maxLength < -1)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        int length = _end - _start;
        if (maxLength >= 0)
            length = Math.Min(length, maxLength);
        return length == 0 ? string.Empty : DocumentText.Substring(_start, length);
    }

    public int Move(TextUnit unit, int count)
    {
        ClampToDocument();
        if (count == 0)
            return 0;

        if (unit == TextUnit.Format && Source is IAutomationTextStyleSource)
        {
            // UIA moves a caret without expanding it; a non-empty range is
            // normalized to one format unit and must still span a unit at EOF.
            if (_start == _end)
            {
                _start = MoveOffsetByUnit(DocumentText, _start, unit, count, out int caretMoved);
                _end = _start;
                return caretMoved;
            }
            var runs = AutomationTextStyles.GetRuns(Source);
            int current = -1;
            for (int index = 0; index < runs.Count; index++)
                if (_start >= runs[index].Start && _start < runs[index].Start + runs[index].Length) { current = index; break; }
            if (current < 0) return 0;
            int target = (int)Math.Clamp((long)current + count, 0, runs.Count - 1);
            if (target == current) return 0;
            _start = runs[target].Start; _end = _start + runs[target].Length;
            return target - current;
        }

        int start = MoveOffsetByUnit(DocumentText, _start, unit, count, out int actual);
        _start = start;
        _end = start;
        ExpandToEnclosingUnit(unit);
        return actual;
    }

    public void MoveEndpointByRange(
        TextPatternRangeEndpoint endpoint,
        ITextRangeProvider targetRange,
        TextPatternRangeEndpoint targetEndpoint)
    {
        if (targetRange is not AutomationTextRangeProvider other ||
            !ReferenceEquals(other._provider, _provider))
        {
            throw new ArgumentException("The target range belongs to a different text provider.", nameof(targetRange));
        }

        int target = targetEndpoint == TextPatternRangeEndpoint.Start ? other._start : other._end;
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            _start = target;
            if (_end < _start)
                _end = _start;
        }
        else
        {
            _end = target;
            if (_start > _end)
                _start = _end;
        }

        ClampToDocument();
    }

    public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count)
    {
        ClampToDocument();
        int offset = endpoint == TextPatternRangeEndpoint.Start ? _start : _end;
        int moved = MoveOffsetByUnit(DocumentText, offset, unit, count, out int actual);
        if (endpoint == TextPatternRangeEndpoint.Start)
        {
            _start = moved;
            if (_end < _start)
                _end = _start;
        }
        else
        {
            _end = moved;
            if (_start > _end)
                _start = _end;
        }

        return actual;
    }

    public void RemoveFromSelection()
    {
        // The source supports a single contiguous selection. Preserve the existing selection;
        // this matches the prior native bridge behavior for an unsupported disjoint operation.
    }

    public void ScrollIntoView(bool alignToTop)
    {
        ClampToDocument();
        Source.ScrollIntoView(_start, _end - _start);
    }

    public void Select()
    {
        ClampToDocument();
        Source.Select(_start, _end - _start);
    }

    private void ClampToDocument()
    {
        int length = DocumentText.Length;
        _start = Math.Clamp(_start, 0, length);
        _end = Math.Clamp(_end, _start, length);
    }

    private static int NextCharacterBoundary(string text, int offset)
    {
        if (offset >= text.Length)
            return text.Length;

        offset++;
        if (offset < text.Length && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1]))
            offset++;
        return offset;
    }

    private static int PreviousCharacterBoundary(string text, int offset)
    {
        if (offset <= 0)
            return 0;

        offset--;
        if (offset > 0 && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1]))
            offset--;
        return offset;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static int WordStart(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        while (offset > 0 && IsWordCharacter(text[offset - 1]))
            offset--;
        return offset;
    }

    private static int WordEnd(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        while (offset < text.Length && IsWordCharacter(text[offset]))
            offset++;
        return offset;
    }

    private static int NextWordStart(string text, int offset)
    {
        offset = WordEnd(text, offset);
        while (offset < text.Length && !IsWordCharacter(text[offset]))
            offset++;
        return offset;
    }

    private static int PreviousWordStart(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        while (offset > 0 && !IsWordCharacter(text[offset - 1]))
            offset--;
        while (offset > 0 && IsWordCharacter(text[offset - 1]))
            offset--;
        return offset;
    }

    private static int LineStart(string text, int offset)
    {
        if (offset <= 0)
            return 0;
        offset = Math.Min(offset, text.Length);
        int newline = text.LastIndexOf('\n', offset - 1);
        return newline < 0 ? 0 : newline + 1;
    }

    private static int LineEnd(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int newline = text.IndexOf('\n', offset);
        return newline < 0 ? text.Length : newline + 1;
    }

    private static int PreviousLineStart(string text, int offset)
    {
        int start = LineStart(text, offset);
        return start == 0 ? 0 : LineStart(text, start - 1);
    }

    private int MoveOffsetByUnit(string text, int offset, TextUnit unit, int count, out int actual)
    {
        actual = 0;
        offset = Math.Clamp(offset, 0, text.Length);
        if (count == 0)
            return offset;

        int direction = count > 0 ? 1 : -1;
        int steps = (int)Math.Min(Math.Abs((long)count), text.Length + 1L);
        if (unit == TextUnit.Format && Source is IAutomationTextStyleSource)
        {
            var boundaries = AutomationTextStyles.GetRuns(Source).SelectMany(run => new[] { run.Start, run.Start + run.Length })
                .Append(0).Append(text.Length).Distinct().Order().ToArray();
            foreach (int boundary in direction > 0 ? boundaries.Where(value => value > offset) : boundaries.Where(value => value < offset).Reverse())
            { if (steps-- == 0) break; offset = boundary; actual += direction; }
            return offset;
        }
        switch (unit)
        {
            case TextUnit.Character:
            case TextUnit.Format:
                for (int index = 0; index < steps; index++)
                {
                    int next = direction > 0
                        ? NextCharacterBoundary(text, offset)
                        : PreviousCharacterBoundary(text, offset);
                    if (next == offset)
                        break;
                    offset = next;
                    actual += direction;
                }
                break;

            case TextUnit.Word:
                for (int index = 0; index < steps; index++)
                {
                    int next = direction > 0 ? NextWordStart(text, offset) : PreviousWordStart(text, offset);
                    if (next == offset)
                        break;
                    offset = next;
                    actual += direction;
                }
                break;

            case TextUnit.Line:
            case TextUnit.Paragraph:
                for (int index = 0; index < steps; index++)
                {
                    int next = direction > 0 ? LineEnd(text, offset) : PreviousLineStart(text, offset);
                    if (next == offset)
                        break;
                    offset = next;
                    actual += direction;
                }
                break;

            case TextUnit.Page:
            case TextUnit.Document:
                int destination = direction > 0 ? text.Length : 0;
                if (destination != offset)
                {
                    offset = destination;
                    actual = direction;
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(unit));
        }

        return offset;
    }
}
