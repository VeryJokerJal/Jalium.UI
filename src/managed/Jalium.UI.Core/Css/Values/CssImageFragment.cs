using System.Globalization;

namespace Jalium.UI.Styling;

/// <summary>The spatial media-fragment coordinates are in the oriented image's natural coordinate system.</summary>
internal sealed record CssImageFragment(double X, double Y, double Width, double Height, bool Percent)
{
    internal static bool IsMultiResolution(ReadOnlySpan<byte> bytes) => bytes.Length >= 6 && bytes[0] == 0 && bytes[1] == 0 &&
        bytes[2] is 1 or 2 && bytes[3] == 0 && (bytes[4] | bytes[5] << 8) > 1; // ICO / CUR directory.

    internal Rect Resolve(Size size)
    {
        var x = Percent ? Math.Floor(X * size.Width / 100) : X;
        var y = Percent ? Math.Floor(Y * size.Height / 100) : Y;
        var width = Percent ? Math.Ceiling(Width * size.Width / 100) : Width;
        var height = Percent ? Math.Ceiling(Height * size.Height / 100) : Height;
        // Media Fragments 6.3.3: ignore a rectangle whose origin is outside the image.
        if (x >= size.Width || y >= size.Height) return new(size);
        return new(x, y, Math.Min(width, size.Width - x), Math.Min(height, size.Height - y));
    }

    internal static bool Read(string reference, out string url, out CssImageFragment? fragment)
    {
        fragment = null;
        var hash = reference.IndexOf('#');
        if (hash < 0) { url = reference; return true; }
        url = reference[..hash];
        var syntax = reference[(hash + 1)..];
        if (syntax.Length == 0) return true;
        foreach (var component in syntax.Split('&'))
        {
            var equals = component.IndexOf('=');
            if (equals < 0 || !Unescape(component[..equals], out var name) || name != "xywh" ||
                !Unescape(component[(equals + 1)..], out var value)) continue;
            var percent = value.StartsWith("percent:", StringComparison.Ordinal);
            if (percent) value = value[8..];
            else if (value.StartsWith("pixel:", StringComparison.Ordinal)) value = value[6..];
            var parts = value.Split(',');
            if (parts.Length != 4) continue;
            var numbers = new double[4]; var valid = true;
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                if (part.Length == 0 || part.Any(c => c is < '0' or > '9') ||
                    !double.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]) ||
                    !double.IsFinite(numbers[index])) { valid = false; break; }
            }
            if (!valid || numbers[2] == 0 || numbers[3] == 0 || percent &&
                (numbers[0] + numbers[2] > 100 || numbers[1] + numbers[3] > 100)) continue;
            fragment = new(numbers[0], numbers[1], numbers[2], numbers[3], percent);
        }
        // Unlike url(), image() must not silently display an unknown fragment as the whole image.
        return fragment is not null;
    }

    private static bool Unescape(string value, out string result)
    {
        result = "";
        for (var i = 0; i < value.Length; i++)
            if (value[i] == '%' && (i + 2 >= value.Length || !Uri.IsHexDigit(value[++i]) || !Uri.IsHexDigit(value[++i]))) return false;
        result = Uri.UnescapeDataString(value); return true;
    }
}
