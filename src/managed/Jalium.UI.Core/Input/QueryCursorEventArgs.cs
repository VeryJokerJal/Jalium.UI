namespace Jalium.UI.Input;

/// <summary>Provides data for cursor query routed events.</summary>
public class QueryCursorEventArgs : MouseEventArgs
{
    private Cursor? _cursor;

    /// <summary>
    /// Preserves the legacy parameterless construction path used by Jalium's
    /// effective-cursor query pipeline.
    /// </summary>
    public QueryCursorEventArgs()
        : base(UIElement.QueryCursorEvent)
    {
    }

    public QueryCursorEventArgs(MouseDevice mouse, int timestamp)
        : base(mouse, timestamp, stylusDevice: null)
    {
    }

    public QueryCursorEventArgs(MouseDevice mouse, int timestamp, StylusDevice? stylusDevice)
        : base(mouse, timestamp, stylusDevice)
    {
    }

    public Cursor? Cursor
    {
        get => _cursor;
        set => _cursor = value ?? Cursors.None;
    }

    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is QueryCursorEventHandler typedHandler)
        {
            typedHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

public delegate void QueryCursorEventHandler(object sender, QueryCursorEventArgs e);

/// <summary>Provides data when an access key invokes an element.</summary>
public class AccessKeyEventArgs : EventArgs
{
    internal AccessKeyEventArgs(string key, bool isMultiple, bool userInitiated)
    {
        Key = key ?? string.Empty;
        IsMultiple = isMultiple;
        UserInitiated = userInitiated;
    }

    public string Key { get; }

    public bool IsMultiple { get; }

    internal bool UserInitiated { get; }
}
