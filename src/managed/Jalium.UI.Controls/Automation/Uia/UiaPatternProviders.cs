using System.Runtime.InteropServices;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

[ComVisible(true)]
internal sealed class UiaInvokeProviderWrapper : StandardOleMarshalObject, IUiaInvokeProvider
{
    private readonly IInvokeProvider _inner;
    internal UiaInvokeProviderWrapper(IInvokeProvider inner) => _inner = inner;
    public void Invoke() => _inner.Invoke();
}

[ComVisible(true)]
internal sealed class UiaToggleProviderWrapper : StandardOleMarshalObject, IUiaToggleProvider
{
    private readonly IToggleProvider _inner;
    internal UiaToggleProviderWrapper(IToggleProvider inner) => _inner = inner;
    public void Toggle() => _inner.Toggle();
    public int ToggleState => (int)_inner.ToggleState;
}

[ComVisible(true)]
internal sealed class UiaValueProviderWrapper : StandardOleMarshalObject, IUiaValueProvider
{
    private readonly IValueProvider _inner;
    internal UiaValueProviderWrapper(IValueProvider inner) => _inner = inner;
    public void SetValue(string value) => _inner.SetValue(value);
    public string Value => _inner.Value ?? string.Empty;
    public bool IsReadOnly => _inner.IsReadOnly;
}

[ComVisible(true)]
internal sealed class UiaRangeValueProviderWrapper : StandardOleMarshalObject, IUiaRangeValueProvider
{
    private readonly IRangeValueProvider _inner;
    internal UiaRangeValueProviderWrapper(IRangeValueProvider inner) => _inner = inner;
    public void SetValue(double value) => _inner.SetValue(value);
    public double Value => _inner.Value;
    public bool IsReadOnly => _inner.IsReadOnly;
    public double Maximum => _inner.Maximum;
    public double Minimum => _inner.Minimum;
    public double LargeChange => _inner.LargeChange;
    public double SmallChange => _inner.SmallChange;
}

[ComVisible(true)]
internal sealed class UiaExpandCollapseProviderWrapper : StandardOleMarshalObject, IUiaExpandCollapseProvider
{
    private readonly IExpandCollapseProvider _inner;
    internal UiaExpandCollapseProviderWrapper(IExpandCollapseProvider inner) => _inner = inner;
    public void Expand() => _inner.Expand();
    public void Collapse() => _inner.Collapse();
    public int ExpandCollapseState => (int)_inner.ExpandCollapseState;
}

[ComVisible(true)]
internal sealed class UiaSelectionProviderWrapper : StandardOleMarshalObject, IUiaSelectionProvider
{
    private readonly ISelectionProvider _inner;
    internal UiaSelectionProviderWrapper(ISelectionProvider inner) => _inner = inner;

    public IRawElementProviderSimple[]? GetSelection()
    {
        var peers = _inner.GetSelection();
        if (peers == null || peers.Length == 0) return null;
        var result = new IRawElementProviderSimple[peers.Length];
        for (int i = 0; i < peers.Length; i++)
            result[i] = UiaAccessibilityBridge.GetOrCreateProvider(peers[i], nint.Zero);
        return result;
    }

    public bool CanSelectMultiple => _inner.CanSelectMultiple;
    public bool IsSelectionRequired => _inner.IsSelectionRequired;
}

[ComVisible(true)]
internal sealed class UiaSelectionItemProviderWrapper : StandardOleMarshalObject, IUiaSelectionItemProvider
{
    private readonly ISelectionItemProvider _inner;
    internal UiaSelectionItemProviderWrapper(ISelectionItemProvider inner) => _inner = inner;
    public void Select() => _inner.Select();
    public void AddToSelection() => _inner.AddToSelection();
    public void RemoveFromSelection() => _inner.RemoveFromSelection();
    public bool IsSelected => _inner.IsSelected;

    public IRawElementProviderSimple? SelectionContainer
    {
        get
        {
            var peer = _inner.SelectionContainer;
            return peer != null ? UiaAccessibilityBridge.GetOrCreateProvider(peer, nint.Zero) : null;
        }
    }
}

[ComVisible(true)]
internal sealed class UiaScrollProviderWrapper : StandardOleMarshalObject, IUiaScrollProvider
{
    private readonly IScrollProvider _inner;
    internal UiaScrollProviderWrapper(IScrollProvider inner) => _inner = inner;
    public void Scroll(int h, int v) => _inner.Scroll((ScrollAmount)h, (ScrollAmount)v);
    public void SetScrollPercent(double h, double v) => _inner.SetScrollPercent(h, v);
    public double HorizontalScrollPercent => _inner.HorizontalScrollPercent;
    public double VerticalScrollPercent => _inner.VerticalScrollPercent;
    public double HorizontalViewSize => _inner.HorizontalViewSize;
    public double VerticalViewSize => _inner.VerticalViewSize;
    public bool HorizontallyScrollable => _inner.HorizontallyScrollable;
    public bool VerticallyScrollable => _inner.VerticallyScrollable;
}

[ComVisible(true)]
internal sealed class UiaScrollItemProviderWrapper : StandardOleMarshalObject, IUiaScrollItemProvider
{
    private readonly IScrollItemProvider _inner;
    internal UiaScrollItemProviderWrapper(IScrollItemProvider inner) => _inner = inner;
    public void ScrollIntoView() => _inner.ScrollIntoView();
}
