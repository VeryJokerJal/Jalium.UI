using System.Collections.ObjectModel;

namespace Jalium.UI.Styling;

/// <summary>An ordered collection of CSS style sheets; later sheets win ties in the cascade.</summary>
public sealed class CssStyleSheetCollection : Collection<CssStyleSheet>
{
    internal event Action? Changed;

    protected override void InsertItem(int index, CssStyleSheet item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
        Changed?.Invoke();
    }

    protected override void SetItem(int index, CssStyleSheet item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
        Changed?.Invoke();
    }

    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        Changed?.Invoke();
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        Changed?.Invoke();
    }
}
