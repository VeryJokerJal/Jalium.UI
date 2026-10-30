namespace Jalium.UI.Styling;

public static partial class Css
{
    private static readonly System.Threading.AsyncLocal<Uri?> s_designerResourceBaseUri = new();

    internal static IDisposable? PushDesignerResourceBaseUri(Uri? baseUri)
    {
        if (baseUri is null) return null;
        var scope = new DesignerResourceScope(s_designerResourceBaseUri.Value);
        s_designerResourceBaseUri.Value = baseUri;
        return scope;
    }

    private static CssStyleSheet LoadSourceStyleSheet(Uri uri)
    {
        if (s_designerResourceBaseUri.Value is { IsAbsoluteUri: true, IsFile: true } baseUri &&
            (!uri.IsAbsoluteUri || uri.IsFile) &&
            !uri.OriginalString.Contains(";component/", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = uri.IsAbsoluteUri ? uri : new Uri(baseUri, uri);
            return CssStyleSheet.LoadAsync(resolved).GetAwaiter().GetResult();
        }

        return CssStyleSheet.FromUri(uri);
    }

    private sealed class DesignerResourceScope(Uri? previous) : IDisposable
    {
        public void Dispose() => s_designerResourceBaseUri.Value = previous;
    }
}
