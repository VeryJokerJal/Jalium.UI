using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides unified icon measurement and rendering services for controls that display icons
/// (such as <see cref="MenuFlyoutItem"/>, <see cref="MenuItem"/>, <see cref="SwipeControl"/>, etc.).
/// Supports vector <see cref="Geometry"/>, <see cref="IconElement"/> (<see cref="PathIcon"/>,
/// <see cref="FontIcon"/>, <see cref="SymbolIcon"/>, <see cref="BitmapIcon"/>, <see cref="ImageIcon"/>),
/// <see cref="ImageSource"/>, <see cref="IconSource"/>, <see cref="Symbol"/>, and string formats (path markup,
/// symbol name, or font glyph).
/// </summary>
public static class IconRendering
{
    private static readonly FontFamily s_defaultFontFamily = new("Segoe MDL2 Assets");

    /// <summary>
    /// Draws the specified icon object inside the target rectangle, scaled with preserved aspect ratio
    /// and centered.
    /// </summary>
    /// <param name="dc">The drawing context to render to.</param>
    /// <param name="icon">The icon object (Geometry, IconElement, ImageSource, IconSource, Symbol, or string).</param>
    /// <param name="targetRect">The destination bounding box for the icon.</param>
    /// <param name="foreground">The foreground brush used for vector fill/stroke and text glyphs.</param>
    /// <param name="defaultFontSize">Fallback font size for text/glyph rendering.</param>
    /// <param name="defaultFontFamily">Fallback font family for text/glyph rendering.</param>
    public static void DrawIcon(
        DrawingContext dc,
        object? icon,
        Rect targetRect,
        Brush? foreground,
        double defaultFontSize = 16.0,
        FontFamily? defaultFontFamily = null)
    {
        if (icon == null || dc == null || targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            return;
        }

        foreground ??= Brushes.White;

        switch (icon)
        {
            case Geometry geometry:
                DrawGeometry(dc, geometry, targetRect, foreground);
                break;

            case PathIcon pathIcon:
                if (pathIcon.Data is { } pathData)
                {
                    var fg = pathIcon.Foreground ?? foreground;
                    DrawGeometry(dc, pathData, targetRect, fg);
                }
                break;

            case FontIcon fontIcon:
                if (!string.IsNullOrEmpty(fontIcon.Glyph))
                {
                    var family = fontIcon.FontFamily ?? defaultFontFamily ?? s_defaultFontFamily;
                    var size = fontIcon.FontSize > 0 ? fontIcon.FontSize : Math.Min(targetRect.Width, targetRect.Height);
                    var fg = fontIcon.Foreground ?? foreground;
                    DrawGlyph(dc, fontIcon.Glyph, family, size, targetRect, fg);
                }
                break;

            case SymbolIcon symbolIcon:
                {
                    var glyph = char.ConvertFromUtf32((int)symbolIcon.Symbol);
                    var family = defaultFontFamily ?? s_defaultFontFamily;
                    var size = Math.Min(targetRect.Width, targetRect.Height);
                    var fg = symbolIcon.Foreground ?? foreground;
                    DrawGlyph(dc, glyph, family, size, targetRect, fg);
                }
                break;

            case Symbol symbol:
                {
                    var glyph = char.ConvertFromUtf32((int)symbol);
                    var family = defaultFontFamily ?? s_defaultFontFamily;
                    var size = Math.Min(targetRect.Width, targetRect.Height);
                    DrawGlyph(dc, glyph, family, size, targetRect, foreground);
                }
                break;

            case BitmapIcon bitmapIcon:
                if (bitmapIcon.UriSource != null)
                {
                    try
                    {
                        var bmp = new BitmapImage(bitmapIcon.UriSource);
                        DrawImageFit(dc, bmp, targetRect);
                    }
                    catch
                    {
                        // Ignore decode failure
                    }
                }
                break;

            case ImageIcon imageIcon:
                if (imageIcon.Source != null)
                {
                    DrawImageFit(dc, imageIcon.Source, targetRect);
                }
                break;

            case ImageSource imageSource:
                DrawImageFit(dc, imageSource, targetRect);
                break;

            case IconSource iconSource:
                var element = iconSource.CreateIconElement();
                DrawIcon(dc, element, targetRect, foreground, defaultFontSize, defaultFontFamily);
                break;

            case string str when !string.IsNullOrEmpty(str):
                DrawStringIcon(dc, str, targetRect, foreground, defaultFontSize, defaultFontFamily);
                break;
        }
    }

    /// <summary>
    /// Fits a vector geometry uniformly into the target rectangle and renders it with proper fill and/or stroke.
    /// </summary>
    public static void DrawGeometry(DrawingContext dc, Geometry geometry, Rect targetRect, Brush foreground)
    {
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty || (bounds.Width <= 0 && bounds.Height <= 0))
        {
            return;
        }

        var matrix = CreateFitMatrix(bounds, targetRect);
        dc.PushTransform(new MatrixTransform(matrix));
        try
        {
            bool hasClosed = false;
            bool hasOpen = false;

            if (geometry is PathGeometry pg && pg.Figures.Count > 0)
            {
                foreach (var fig in pg.Figures)
                {
                    if (fig.IsClosed)
                    {
                        hasClosed = true;
                    }
                    else
                    {
                        hasOpen = true;
                    }
                }
            }
            else
            {
                hasClosed = true;
            }

            var pen = hasOpen ? new Pen(foreground, 1.5) : null;
            var fill = hasClosed ? foreground : null;

            dc.DrawGeometry(fill, pen, geometry);
        }
        finally
        {
            dc.Pop();
        }
    }

    /// <summary>
    /// Creates a transformation matrix that scales <paramref name="bounds"/> uniformly to fit
    /// within <paramref name="targetRect"/> and centers it.
    /// </summary>
    public static Matrix CreateFitMatrix(Rect bounds, Rect targetRect)
    {
        double scale = 1.0;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            scale = Math.Min(targetRect.Width / bounds.Width, targetRect.Height / bounds.Height);
        }
        else if (bounds.Width > 0)
        {
            scale = targetRect.Width / bounds.Width;
        }
        else if (bounds.Height > 0)
        {
            scale = targetRect.Height / bounds.Height;
        }

        var scaledWidth = bounds.Width * scale;
        var scaledHeight = bounds.Height * scale;

        var tx = targetRect.X + (targetRect.Width - scaledWidth) / 2.0 - bounds.X * scale;
        var ty = targetRect.Y + (targetRect.Height - scaledHeight) / 2.0 - bounds.Y * scale;

        return new Matrix(scale, 0, 0, scale, tx, ty);
    }

    /// <summary>
    /// Draws an image within <paramref name="targetRect"/> preserving its aspect ratio.
    /// </summary>
    public static void DrawImageFit(DrawingContext dc, ImageSource imageSource, Rect targetRect)
    {
        double width = imageSource.Width;
        double height = imageSource.Height;

        if (width <= 0 || height <= 0)
        {
            dc.DrawImage(imageSource, targetRect);
            return;
        }

        var scale = Math.Min(targetRect.Width / width, targetRect.Height / height);
        var scaledWidth = width * scale;
        var scaledHeight = height * scale;

        var rect = new Rect(
            targetRect.X + (targetRect.Width - scaledWidth) / 2.0,
            targetRect.Y + (targetRect.Height - scaledHeight) / 2.0,
            scaledWidth,
            scaledHeight);

        dc.DrawImage(imageSource, rect);
    }

    /// <summary>
    /// Draws a text glyph centered in the target rectangle.
    /// </summary>
    public static void DrawGlyph(
        DrawingContext dc,
        string glyph,
        FontFamily fontFamily,
        double fontSize,
        Rect targetRect,
        Brush foreground)
    {
        var formatted = new FormattedText(glyph, fontFamily.Source, fontSize)
        {
            Foreground = foreground
        };
        TextMeasurement.MeasureText(formatted);
        var x = targetRect.X + (targetRect.Width - formatted.Width) / 2.0;
        var y = targetRect.Y + (targetRect.Height - formatted.Height) / 2.0;
        dc.DrawText(formatted, new Point(x, y));
    }

    private static void DrawStringIcon(
        DrawingContext dc,
        string str,
        Rect targetRect,
        Brush foreground,
        double defaultFontSize,
        FontFamily? defaultFontFamily)
    {
        var trimmed = str.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        // 1. Symbol name check (e.g. "Save", "Symbol.Save")
        var symbolName = trimmed.StartsWith("Symbol.", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring("Symbol.".Length)
            : trimmed;
        if (Enum.TryParse<Symbol>(symbolName, ignoreCase: true, out var symbol))
        {
            var glyph = char.ConvertFromUtf32((int)symbol);
            var family = defaultFontFamily ?? s_defaultFontFamily;
            var size = Math.Min(targetRect.Width, targetRect.Height);
            DrawGlyph(dc, glyph, family, size, targetRect, foreground);
            return;
        }

        // 2. Vector path markup check (e.g. "M12,5 L12,19...", "F1 M...")
        if (trimmed.Length > 2 && (trimmed.StartsWith("M", StringComparison.OrdinalIgnoreCase) ||
                                   trimmed.StartsWith("F0", StringComparison.OrdinalIgnoreCase) ||
                                   trimmed.StartsWith("F1", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var geometry = Geometry.Parse(trimmed);
                if (geometry is { Bounds.IsEmpty: false } or PathGeometry { Figures.Count: > 0 })
                {
                    DrawGeometry(dc, geometry, targetRect, foreground);
                    return;
                }
            }
            catch
            {
                // Fall back to font glyph
            }
        }

        // 3. Raw glyph string (PUA codepoint or character)
        var font = defaultFontFamily ?? s_defaultFontFamily;
        var fontSize = defaultFontSize > 0 ? defaultFontSize : Math.Min(targetRect.Width, targetRect.Height);
        DrawGlyph(dc, trimmed, font, fontSize, targetRect, foreground);
    }
}
