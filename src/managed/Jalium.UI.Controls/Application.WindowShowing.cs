namespace Jalium.UI;

public partial class Application
{
    /// <summary>
    /// Configures a window after its styles have been applied and before its
    /// native handle, size-to-content layout, and presentation are initialized.
    /// Called on every show, including when a hidden window is shown again.
    /// </summary>
    protected virtual void OnWindowShowing(Window window)
    {
    }

    internal void NotifyWindowShowing(Window window) => OnWindowShowing(window);
}
