namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the context menu opening and closing events.
/// </summary>
public sealed class ContextMenuEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ContextMenuEventArgs"/> class.
    /// </summary>
    public ContextMenuEventArgs(object source, bool opening) : base()
    {
        Source = source;
        IsOpening = opening;
        CursorLeft = -1;
        CursorTop = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextMenuEventArgs"/> class
    /// with the specified cursor position.
    /// </summary>
    public ContextMenuEventArgs(object source, bool opening, double left, double top) : base()
    {
        Source = source;
        IsOpening = opening;
        CursorLeft = left;
        CursorTop = top;
    }

    /// <summary>Gets whether the event is raised for opening (<c>true</c>) or closing (<c>false</c>).</summary>
    public bool IsOpening { get; }

    /// <summary>Gets the horizontal position of the cursor when the context menu was requested.</summary>
    public double CursorLeft { get; }

    /// <summary>Gets the vertical position of the cursor when the context menu was requested.</summary>
    public double CursorTop { get; }
}

/// <summary>
/// Represents the method that handles context menu events.
/// </summary>
public delegate void ContextMenuEventHandler(object sender, ContextMenuEventArgs e);
