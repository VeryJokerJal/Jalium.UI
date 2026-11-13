using System.Net.Http.Headers;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

internal static class CssImageTypeSupport
{
    internal static bool IsSupported(string value)
    {
        if (!MediaTypeHeaderValue.TryParse(value, out var parsed) || parsed.MediaType is not { } mime || mime.Contains('*')) return false;
        if (mime.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)) return true;
        try { return BitmapImage.ResolveDecoder().SupportsMimeType(mime); }
        catch (Exception error) when (CssShapeImageResource.Expected(error)) { return false; }
    }
}
