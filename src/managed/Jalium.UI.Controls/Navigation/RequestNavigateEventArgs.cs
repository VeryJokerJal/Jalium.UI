namespace Jalium.UI.Controls.Navigation;

/// <summary>
/// Provides data for the Hyperlink.RequestNavigate event.
/// </summary>
public sealed class RequestNavigateEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestNavigateEventArgs"/> class.
    /// </summary>
    /// <param name="uri">The URI to navigate to.</param>
    /// <param name="target">The name of the target window or frame.</param>
    public RequestNavigateEventArgs(Uri uri, string? target)
    {
        Uri = uri;
        Target = target;
    }

    /// <summary>
    /// Gets the URI to navigate to.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// Gets the name of the target window or frame.
    /// </summary>
    public string? Target { get; }
}
