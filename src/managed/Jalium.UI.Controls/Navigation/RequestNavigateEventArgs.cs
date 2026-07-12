namespace Jalium.UI.Navigation;

/// <summary>Represents the method that handles a request-navigation routed event.</summary>
public delegate void RequestNavigateEventHandler(object sender, RequestNavigateEventArgs e);

/// <summary>
/// Provides data for the Hyperlink.RequestNavigate event.
/// </summary>
public sealed class RequestNavigateEventArgs : RoutedEventArgs
{
    /// <summary>Initializes an empty request-navigation event payload.</summary>
#pragma warning disable CS0628 // WPF exposes this protected constructor on the sealed event-args type.
    protected RequestNavigateEventArgs()
    {
        Uri = new Uri("about:blank");
    }
#pragma warning restore CS0628

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

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        if (genericHandler is RequestNavigateEventHandler handler)
        {
            handler(genericTarget, this);
            return;
        }

        base.InvokeEventHandler(genericHandler, genericTarget);
    }
}
