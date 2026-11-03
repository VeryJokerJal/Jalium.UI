namespace Jalium.UI;

public partial class Application
{
    /// <summary>Configures a styled window before its native handle and presentation are initialized.</summary>
    protected virtual void OnWindowShowing(Window window)
    {
    }

    internal void NotifyWindowShowing(Window window) => OnWindowShowing(window);
}
