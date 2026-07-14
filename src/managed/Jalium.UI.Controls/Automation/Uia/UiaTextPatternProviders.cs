using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using RawProvider = Jalium.UI.Automation.Provider;
using Text = Jalium.UI.Automation.Text;

namespace Jalium.UI.Controls.Automation.Uia;

/// <summary>Adapts the canonical managed Text provider to the native UIA ABI.</summary>
[GeneratedComClass]
internal sealed partial class UiaTextProviderWrapper : IUiaTextProvider
{
    private readonly RawProvider.ITextProvider _inner;
    private readonly AutomationPeerProvider _host;

    internal UiaTextProviderWrapper(RawProvider.ITextProvider inner, AutomationPeerProvider host)
    {
        _inner = inner;
        _host = host;
    }

    public IUiaTextRangeProvider[]? GetSelection() => WrapRanges(_inner.GetSelection());

    public IUiaTextRangeProvider[]? GetVisibleRanges() => WrapRanges(_inner.GetVisibleRanges());

    public IUiaTextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement)
    {
        RawProvider.IRawElementProviderSimple child = childElement is AutomationPeerProvider provider
            ? _host.Peer.ProviderFromPeer(provider.Peer)
            : _host.Peer.ProviderFromPeer(_host.Peer);
        return Wrap(_inner.RangeFromChild(child));
    }

    public IUiaTextRangeProvider? RangeFromPoint(UiaPoint point) =>
        Wrap(_inner.RangeFromPoint(new Point(point.X, point.Y)));

    public IUiaTextRangeProvider? get_DocumentRange() => Wrap(_inner.DocumentRange);

    public int get_SupportedTextSelection() => (int)_inner.SupportedTextSelection;

    private IUiaTextRangeProvider? Wrap(RawProvider.ITextRangeProvider? range) =>
        range is null ? null : new UiaTextRangeProvider(range, _host);

    private IUiaTextRangeProvider[]? WrapRanges(RawProvider.ITextRangeProvider[]? ranges)
    {
        if (ranges is null || ranges.Length == 0)
            return null;

        var result = new IUiaTextRangeProvider[ranges.Length];
        for (int index = 0; index < ranges.Length; index++)
            result[index] = new UiaTextRangeProvider(ranges[index], _host);
        return result;
    }
}

/// <summary>Adapts a canonical managed text range to native UIA.</summary>
[GeneratedComClass]
internal sealed partial class UiaTextRangeProvider : IUiaTextRangeProvider
{
    private readonly RawProvider.ITextRangeProvider _inner;
    private readonly AutomationPeerProvider _host;

    internal UiaTextRangeProvider(RawProvider.ITextRangeProvider inner, AutomationPeerProvider host)
    {
        _inner = inner;
        _host = host;
    }

    public IUiaTextRangeProvider? Clone() => new UiaTextRangeProvider(_inner.Clone(), _host);

    public int Compare(IUiaTextRangeProvider range) =>
        range is UiaTextRangeProvider other && _inner.Compare(other._inner) ? 1 : 0;

    public int CompareEndpoints(int endpoint, IUiaTextRangeProvider targetRange, int targetEndpoint)
    {
        if (targetRange is not UiaTextRangeProvider other)
            return 0;

        return _inner.CompareEndpoints(
            (Text.TextPatternRangeEndpoint)endpoint,
            other._inner,
            (Text.TextPatternRangeEndpoint)targetEndpoint);
    }

    public void ExpandToEnclosingUnit(int unit) =>
        _inner.ExpandToEnclosingUnit((Text.TextUnit)unit);

    public IUiaTextRangeProvider? FindAttribute(int attributeId, UiaVariant val, int backward)
    {
        RawProvider.ITextRangeProvider? range = _inner.FindAttribute(attributeId, ToObject(val), backward != 0);
        return range is null ? null : new UiaTextRangeProvider(range, _host);
    }

    public IUiaTextRangeProvider? FindText(string text, int backward, int ignoreCase)
    {
        RawProvider.ITextRangeProvider? range = _inner.FindText(text, backward != 0, ignoreCase != 0);
        return range is null ? null : new UiaTextRangeProvider(range, _host);
    }

    public UiaVariant GetAttributeValue(int attributeId)
    {
        object? value = _inner.GetAttributeValue(attributeId);
        if (value is not null)
            return UiaVariant.From(value);

        try
        {
            if (UiaNativeMethods.UiaGetReservedNotSupportedValue(out nint unknown) >= 0 && unknown != nint.Zero)
                return UiaVariant.FromUnknown(unknown);
        }
        catch
        {
            // UIA is unavailable. Returning VT_EMPTY keeps the query non-fatal.
        }

        return default;
    }

    public double[]? GetBoundingRectangles()
    {
        if (_inner is RawProvider.AutomationTextRangeProvider managedRange)
        {
            IReadOnlyList<Rect> rectangles = managedRange.GetLocalBoundingRectangles();
            var result = new double[rectangles.Count * 4];
            for (int index = 0; index < rectangles.Count; index++)
            {
                UiaRect screen = _host.LocalRectToScreen(rectangles[index]);
                result[index * 4] = screen.Left;
                result[index * 4 + 1] = screen.Top;
                result[index * 4 + 2] = screen.Width;
                result[index * 4 + 3] = screen.Height;
            }
            return result;
        }

        return _inner.GetBoundingRectangles();
    }

    public IRawElementProviderSimple? GetEnclosingElement() => ToNative(_inner.GetEnclosingElement());

    public string GetText(int maxLength) => _inner.GetText(maxLength);

    public int Move(int unit, int count) => _inner.Move((Text.TextUnit)unit, count);

    public int MoveEndpointByUnit(int endpoint, int unit, int count) =>
        _inner.MoveEndpointByUnit((Text.TextPatternRangeEndpoint)endpoint, (Text.TextUnit)unit, count);

    public void MoveEndpointByRange(int endpoint, IUiaTextRangeProvider targetRange, int targetEndpoint)
    {
        if (targetRange is UiaTextRangeProvider other)
        {
            _inner.MoveEndpointByRange(
                (Text.TextPatternRangeEndpoint)endpoint,
                other._inner,
                (Text.TextPatternRangeEndpoint)targetEndpoint);
        }
    }

    public void Select() => _inner.Select();

    public void AddToSelection() => _inner.AddToSelection();

    public void RemoveFromSelection() => _inner.RemoveFromSelection();

    public void ScrollIntoView(int alignToTop) => _inner.ScrollIntoView(alignToTop != 0);

    public IRawElementProviderSimple[]? GetChildren()
    {
        RawProvider.IRawElementProviderSimple[] children = _inner.GetChildren();
        if (children.Length == 0)
            return [];

        var result = new List<IRawElementProviderSimple>(children.Length);
        foreach (RawProvider.IRawElementProviderSimple child in children)
        {
            IRawElementProviderSimple? native = ToNative(child);
            if (native is not null)
                result.Add(native);
        }
        return result.ToArray();
    }

    private IRawElementProviderSimple? ToNative(RawProvider.IRawElementProviderSimple? provider) =>
        provider is RawProvider.IAutomationPeerRawProvider peerProvider
            ? UiaAccessibilityBridge.GetOrCreateProvider(peerProvider.Peer, _host.Hwnd)
            : null;

    private static object? ToObject(UiaVariant value) => value.vt switch
    {
        UiaVariant.VT_I4 => value.lVal,
        UiaVariant.VT_BOOL => value.boolVal != 0,
        UiaVariant.VT_BSTR when value.bstrVal != nint.Zero => Marshal.PtrToStringBSTR(value.bstrVal),
        _ => null,
    };
}
