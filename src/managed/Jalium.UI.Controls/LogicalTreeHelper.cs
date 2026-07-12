using System.Collections;

namespace Jalium.UI;

/// <summary>
/// Provides static helper methods for querying objects in the logical tree.
/// </summary>
public static class LogicalTreeHelper
{
    /// <summary>Returns the logical parent of the specified object.</summary>
    public static DependencyObject? GetParent(DependencyObject current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current switch
        {
            FrameworkContentElement contentElement => contentElement.Parent,
            FrameworkElement frameworkElement => frameworkElement.Parent,
            ContentElement contentElement => contentElement.GetUIParentCore(),
            UIElement element => element.GetUIParentCore(),
            Visual visual => visual.VisualParent,
            _ => null,
        };
    }

    /// <summary>Returns the immediate logical children of a dependency object.</summary>
    public static IEnumerable GetChildren(DependencyObject current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current switch
        {
            FrameworkContentElement contentElement => GetChildren(contentElement),
            FrameworkElement frameworkElement => GetChildren(frameworkElement),
            _ => Array.Empty<object>(),
        };
    }

    /// <summary>Returns the immediate logical children of a framework element.</summary>
    public static IEnumerable GetChildren(FrameworkElement current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return Enumerate(current.LogicalChildren);
    }

    /// <summary>Returns the immediate logical children of a framework content element.</summary>
    public static IEnumerable GetChildren(FrameworkContentElement current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return Enumerate(current.LogicalChildren);
    }

    /// <summary>Finds a named object in the logical subtree.</summary>
    public static DependencyObject? FindLogicalNode(
        DependencyObject logicalTreeNode,
        string elementName)
    {
        ArgumentNullException.ThrowIfNull(logicalTreeNode);
        ArgumentNullException.ThrowIfNull(elementName);

        if (logicalTreeNode is FrameworkElement { Name: var frameworkName } &&
            frameworkName == elementName)
        {
            return logicalTreeNode;
        }

        if (logicalTreeNode is FrameworkContentElement { Name: var contentName } &&
            contentName == elementName)
        {
            return logicalTreeNode;
        }

        foreach (object? child in GetChildren(logicalTreeNode))
        {
            if (child is DependencyObject dependencyChild &&
                FindLogicalNode(dependencyChild, elementName) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Requests that the specified logical element be brought into view.</summary>
    public static void BringIntoView(DependencyObject current)
    {
        ArgumentNullException.ThrowIfNull(current);
        switch (current)
        {
            case FrameworkContentElement contentElement:
                contentElement.BringIntoView();
                break;
            case FrameworkElement frameworkElement:
                frameworkElement.BringIntoView();
                break;
        }
    }

    private static IEnumerable Enumerate(IEnumerator enumerator)
    {
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current;
        }
    }
}
