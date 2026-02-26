namespace Jalium.UI.Controls;

/// <summary>
/// Specifies how an application will shutdown.
/// </summary>
public enum ShutdownMode
{
    /// <summary>
    /// The application shuts down when the last window closes.
    /// </summary>
    OnLastWindowClose = 0,

    /// <summary>
    /// The application shuts down when the main window closes.
    /// </summary>
    OnMainWindowClose = 1,

    /// <summary>
    /// The application only shuts down when <see cref="Application.Shutdown"/> is called.
    /// </summary>
    OnExplicitShutdown = 2
}
