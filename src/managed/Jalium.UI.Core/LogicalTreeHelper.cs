namespace Jalium.UI;

/// <summary>
/// Provides static helper methods for querying objects in the logical tree.
/// </summary>
public static class LogicalTreeHelper
{
    /// <summary>
    /// Returns the parent of the specified object in the logical tree.
    /// </summary>
    public static DependencyObject? GetParent(DependencyObject current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current is Visual visual)
            return visual.VisualParent;

        return null;
    }

    /// <summary>
    /// Returns the collection of immediate child objects of the specified object in the logical tree.
    /// Uses the visual tree as a fallback since Jalium.UI treats visual and logical trees as equivalent.
    /// </summary>
    public static IEnumerable<object> GetChildren(DependencyObject current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current is Visual visual)
        {
            int count = visual.VisualChildrenCount;
            for (int i = 0; i < count; i++)
            {
                var child = visual.GetVisualChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Attempts to find and return an object that has the specified name,
    /// starting from the specified object in the logical tree.
    /// </summary>
    public static DependencyObject? FindLogicalNode(DependencyObject logicalTreeNode, string elementName)
    {
        ArgumentNullException.ThrowIfNull(logicalTreeNode);
        ArgumentNullException.ThrowIfNull(elementName);

        if (logicalTreeNode is FrameworkElement fe && fe.Name == elementName)
            return logicalTreeNode;

        foreach (var child in GetChildren(logicalTreeNode))
        {
            if (child is DependencyObject childDO)
            {
                var result = FindLogicalNode(childDO, elementName);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Brings the specified element into view.
    /// </summary>
    public static void BringIntoView(DependencyObject current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current is FrameworkElement fe)
            fe.BringIntoView();
    }
}
