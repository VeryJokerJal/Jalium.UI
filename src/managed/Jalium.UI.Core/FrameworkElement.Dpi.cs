namespace Jalium.UI;

public partial class FrameworkElement
{
    /// <summary>
    /// Called when the DPI scale used to render this element changes.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
    }

    /// <summary>
    /// Notifies this element and its visual descendants of a host DPI transition.
    /// </summary>
    internal void NotifyDpiChangedRecursive(DpiScale oldDpi, DpiScale newDpi)
    {
        OnDpiChanged(oldDpi, newDpi);

        List<FrameworkElement>? children = null;
        for (var index = 0; index < VisualChildrenCount; index++)
        {
            if (GetVisualChild(index) is FrameworkElement child)
            {
                children ??= new List<FrameworkElement>();
                children.Add(child);
            }
        }

        if (children == null)
        {
            return;
        }

        foreach (var child in children)
        {
            child.NotifyDpiChangedRecursive(oldDpi, newDpi);
        }
    }
}
