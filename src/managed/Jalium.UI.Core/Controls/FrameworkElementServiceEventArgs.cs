namespace Jalium.UI.Controls;

/// <summary>Provides data for context-menu opening and closing events.</summary>
public sealed class ContextMenuEventArgs : RoutedEventArgs
{
    public ContextMenuEventArgs(object source, bool opening)
        : this(source, opening, -1, -1)
    {
    }

    public ContextMenuEventArgs(object source, bool opening, double left, double top)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        IsOpening = opening;
        CursorLeft = left;
        CursorTop = top;
    }

    public bool IsOpening { get; }
    public double CursorLeft { get; }
    public double CursorTop { get; }
}

/// <summary>Represents a method that handles a context-menu routed event.</summary>
public delegate void ContextMenuEventHandler(object sender, ContextMenuEventArgs e);

/// <summary>Provides data for tooltip routed events.</summary>
public sealed class ToolTipEventArgs : RoutedEventArgs
{
    public ToolTipEventArgs()
    {
    }

    public ToolTipEventArgs(RoutedEvent routedEvent)
        : base(routedEvent)
    {
    }
}

/// <summary>Represents a method that handles a tooltip routed event.</summary>
public delegate void ToolTipEventHandler(object sender, ToolTipEventArgs e);
