using System.Runtime.InteropServices.Marshalling;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.Automation.Uia;

// Source-generated COM wrappers ([GeneratedComClass]) that adapt the framework-neutral
// pattern providers (Jalium.UI.Automation.I*Provider) to the native UIA pattern COM
// interfaces (IUia*Provider). UIA obtains one of these from
// IRawElementProviderSimple.GetPatternProvider and QIs it for the pattern IID.
// Win32 BOOL properties are surfaced as `int` (0/1) to match UIA's 4-byte BOOL exactly.

[GeneratedComClass]
internal sealed partial class UiaInvokeProviderWrapper : IUiaInvokeProvider
{
    private readonly IInvokeProvider _inner;
    internal UiaInvokeProviderWrapper(IInvokeProvider inner) => _inner = inner;
    public void Invoke() => _inner.Invoke();
}

[GeneratedComClass]
internal sealed partial class UiaToggleProviderWrapper : IUiaToggleProvider
{
    private readonly IToggleProvider _inner;
    internal UiaToggleProviderWrapper(IToggleProvider inner) => _inner = inner;
    public void Toggle() => _inner.Toggle();
    public int get_ToggleState() => (int)_inner.ToggleState;
}

[GeneratedComClass]
internal sealed partial class UiaValueProviderWrapper : IUiaValueProvider
{
    private readonly IValueProvider _inner;
    internal UiaValueProviderWrapper(IValueProvider inner) => _inner = inner;
    public void SetValue(string value) => _inner.SetValue(value);
    public string get_Value() => _inner.Value ?? string.Empty;
    public int get_IsReadOnly() => _inner.IsReadOnly ? 1 : 0;
}

[GeneratedComClass]
internal sealed partial class UiaRangeValueProviderWrapper : IUiaRangeValueProvider
{
    private readonly IRangeValueProvider _inner;
    internal UiaRangeValueProviderWrapper(IRangeValueProvider inner) => _inner = inner;
    public void SetValue(double value) => _inner.SetValue(value);
    public double get_Value() => _inner.Value;
    public int get_IsReadOnly() => _inner.IsReadOnly ? 1 : 0;
    public double get_Maximum() => _inner.Maximum;
    public double get_Minimum() => _inner.Minimum;
    public double get_LargeChange() => _inner.LargeChange;
    public double get_SmallChange() => _inner.SmallChange;
}

[GeneratedComClass]
internal sealed partial class UiaExpandCollapseProviderWrapper : IUiaExpandCollapseProvider
{
    private readonly IExpandCollapseProvider _inner;
    internal UiaExpandCollapseProviderWrapper(IExpandCollapseProvider inner) => _inner = inner;
    public void Expand() => _inner.Expand();
    public void Collapse() => _inner.Collapse();
    public int get_ExpandCollapseState() => (int)_inner.ExpandCollapseState;
}

[GeneratedComClass]
internal sealed partial class UiaSelectionProviderWrapper : IUiaSelectionProvider
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

    public int get_CanSelectMultiple() => _inner.CanSelectMultiple ? 1 : 0;
    public int get_IsSelectionRequired() => _inner.IsSelectionRequired ? 1 : 0;
}

[GeneratedComClass]
internal sealed partial class UiaSelectionItemProviderWrapper : IUiaSelectionItemProvider
{
    private readonly ISelectionItemProvider _inner;
    internal UiaSelectionItemProviderWrapper(ISelectionItemProvider inner) => _inner = inner;
    public void Select() => _inner.Select();
    public void AddToSelection() => _inner.AddToSelection();
    public void RemoveFromSelection() => _inner.RemoveFromSelection();
    public int get_IsSelected() => _inner.IsSelected ? 1 : 0;

    public IRawElementProviderSimple? get_SelectionContainer()
    {
        var peer = _inner.SelectionContainer;
        return peer != null ? UiaAccessibilityBridge.GetOrCreateProvider(peer, nint.Zero) : null;
    }
}

[GeneratedComClass]
internal sealed partial class UiaScrollProviderWrapper : IUiaScrollProvider
{
    private readonly IScrollProvider _inner;
    internal UiaScrollProviderWrapper(IScrollProvider inner) => _inner = inner;
    public void Scroll(int h, int v) => _inner.Scroll((ScrollAmount)h, (ScrollAmount)v);
    public void SetScrollPercent(double h, double v) => _inner.SetScrollPercent(h, v);
    public double get_HorizontalScrollPercent() => _inner.HorizontalScrollPercent;
    public double get_VerticalScrollPercent() => _inner.VerticalScrollPercent;
    public double get_HorizontalViewSize() => _inner.HorizontalViewSize;
    public double get_VerticalViewSize() => _inner.VerticalViewSize;
    public int get_HorizontallyScrollable() => _inner.HorizontallyScrollable ? 1 : 0;
    public int get_VerticallyScrollable() => _inner.VerticallyScrollable ? 1 : 0;
}

[GeneratedComClass]
internal sealed partial class UiaScrollItemProviderWrapper : IUiaScrollItemProvider
{
    private readonly IScrollItemProvider _inner;
    internal UiaScrollItemProviderWrapper(IScrollItemProvider inner) => _inner = inner;
    public void ScrollIntoView() => _inner.ScrollIntoView();
}
