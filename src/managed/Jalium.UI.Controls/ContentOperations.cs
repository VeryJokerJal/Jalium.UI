namespace Jalium.UI;

/// <summary>
/// Provides the logical-parent operations used by nonvisual content elements.
/// </summary>
public static class ContentOperations
{
    public static DependencyObject GetParent(ContentElement reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.GetUIParentCore()!;
    }

    public static void SetParent(ContentElement reference, DependencyObject parent)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (ReferenceEquals(reference, parent))
        {
            throw new ArgumentException("A content element cannot be its own parent.", nameof(parent));
        }

        for (DependencyObject? current = parent; current is ContentElement content; current = content.GetUIParentCore())
        {
            if (ReferenceEquals(current, reference))
            {
                throw new ArgumentException("The requested parent would create a content cycle.", nameof(parent));
            }
        }

        reference.SetContentParent(parent);
    }
}
