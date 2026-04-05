namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the <see cref="JsonTreeViewer.SelectedNodeChanged"/> event.
/// </summary>
public sealed class JsonTreeViewerSelectedNodeChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the previously selected node.
    /// </summary>
    public JsonTreeNode? OldNode { get; }

    /// <summary>
    /// Gets the newly selected node.
    /// </summary>
    public JsonTreeNode? NewNode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonTreeViewerSelectedNodeChangedEventArgs"/> class.
    /// </summary>
    public JsonTreeViewerSelectedNodeChangedEventArgs(RoutedEvent routedEvent, JsonTreeNode? oldNode, JsonTreeNode? newNode)
    {
        RoutedEvent = routedEvent;
        OldNode = oldNode;
        NewNode = newNode;
    }
}

/// <summary>
/// Provides data for the <see cref="JsonTreeViewer.NodeExpanded"/> and
/// <see cref="JsonTreeViewer.NodeCollapsed"/> events.
/// </summary>
public sealed class JsonTreeViewerNodeToggleEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the node that was expanded or collapsed.
    /// </summary>
    public JsonTreeNode Node { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonTreeViewerNodeToggleEventArgs"/> class.
    /// </summary>
    public JsonTreeViewerNodeToggleEventArgs(RoutedEvent routedEvent, JsonTreeNode node)
    {
        RoutedEvent = routedEvent;
        Node = node;
    }
}

/// <summary>
/// Provides data for the <see cref="JsonTreeViewer.NodeValueEdited"/> event.
/// </summary>
public sealed class JsonTreeViewerNodeValueEditedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the node whose value was edited.
    /// </summary>
    public JsonTreeNode Node { get; }

    /// <summary>
    /// Gets the old value before the edit.
    /// </summary>
    public object? OldValue { get; }

    /// <summary>
    /// Gets the new value after the edit.
    /// </summary>
    public object? NewValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonTreeViewerNodeValueEditedEventArgs"/> class.
    /// </summary>
    public JsonTreeViewerNodeValueEditedEventArgs(RoutedEvent routedEvent, JsonTreeNode node, object? oldValue, object? newValue)
    {
        RoutedEvent = routedEvent;
        Node = node;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

/// <summary>
/// Provides data for the <see cref="JsonTreeViewer.PathCopied"/> event.
/// </summary>
public sealed class JsonTreeViewerPathCopiedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the node whose path was copied.
    /// </summary>
    public JsonTreeNode Node { get; }

    /// <summary>
    /// Gets the JSONPath string that was copied to the clipboard.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonTreeViewerPathCopiedEventArgs"/> class.
    /// </summary>
    public JsonTreeViewerPathCopiedEventArgs(RoutedEvent routedEvent, JsonTreeNode node, string path)
    {
        RoutedEvent = routedEvent;
        Node = node;
        Path = path;
    }
}
