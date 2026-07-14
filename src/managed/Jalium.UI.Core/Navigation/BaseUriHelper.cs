using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Navigation;

/// <summary>Resolves the base URI associated with a dependency object.</summary>
public static class BaseUriHelper
{
    private static readonly Uri s_applicationBaseUri = CreateApplicationBaseUri();

    /// <summary>Gets the effective base URI for an element.</summary>
    public static Uri GetBaseUri(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element is IUriContext context && context.BaseUri is Uri baseUri)
        {
            return baseUri.IsAbsoluteUri ? baseUri : new Uri(s_applicationBaseUri, baseUri);
        }

        for (DependencyObject? current = element; current != null; current = GetParent(current))
        {
            if (current is IUriContext ancestorContext && ancestorContext.BaseUri is Uri ancestorBaseUri)
            {
                return ancestorBaseUri.IsAbsoluteUri
                    ? ancestorBaseUri
                    : new Uri(s_applicationBaseUri, ancestorBaseUri);
            }
        }

        return s_applicationBaseUri;
    }

    private static DependencyObject? GetParent(DependencyObject element) =>
        element is Visual visual ? visual.VisualParent : null;

    private static Uri CreateApplicationBaseUri()
    {
        string path = AppContext.BaseDirectory;
        if (!path.EndsWith(Path.DirectorySeparatorChar))
        {
            path += Path.DirectorySeparatorChar;
        }

        return new Uri(path, UriKind.Absolute);
    }
}
