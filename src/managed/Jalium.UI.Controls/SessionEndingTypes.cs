namespace Jalium.UI;

/// <summary>Specifies why the current user session is ending.</summary>
public enum ReasonSessionEnding : byte
{
    Logoff = 0,
    Shutdown = 1,
}

/// <summary>
/// Provides data for an application or window session-ending notification.
/// </summary>
public class SessionEndingCancelEventArgs : System.ComponentModel.CancelEventArgs
{
    internal SessionEndingCancelEventArgs(ReasonSessionEnding reasonSessionEnding)
    {
        ReasonSessionEnding = reasonSessionEnding;
    }

    public ReasonSessionEnding ReasonSessionEnding { get; }
}
