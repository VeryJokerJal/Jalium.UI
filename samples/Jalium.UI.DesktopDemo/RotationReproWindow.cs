using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.DesktopDemo;

// TEMPORARY verification window for the native rotation / negative-diagonal
// (180 deg) RenderTransform fix. Each rotated element sits inside a fixed grey
// frame: if the content stays inside its frame and is correctly oriented the fix
// works; if it flies out / disappears the bug is still present. Text is NOT
// included inside rotated elements (the glyph/oriented-quad fix is a later stage).
internal static class RotationReproWindow
{
    private static BitmapImage MakeQuadrantBitmap(int size)
    {
        // TL=red, TR=green, BL=blue, BR=yellow — direction is unambiguous so any
        // rotation/flip is visually obvious, and a missing/garbled transform shows
        // immediately. RGBA8.
        var px = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            bool left = x < size / 2;
            bool top = y < size / 2;
            byte r, g, b;
            if (top && left) { r = 230; g = 40; b = 40; }       // TL red
            else if (top) { r = 40; g = 200; b = 40; }          // TR green
            else if (left) { r = 50; g = 90; b = 230; }         // BL blue
            else { r = 235; g = 210; b = 40; }                  // BR yellow
            px[i + 0] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255;
        }
        return BitmapImage.FromPixels(px, size, size);
    }

    private static UIElement Frame(UIElement inner, string caption)
    {
        var frame = new Border
        {
            Width = 120,
            Height = 120,
            Margin = new Thickness(10),
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 60)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
            BorderThickness = new Thickness(1),
            Child = inner
        };
        var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(frame);
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        return stack;
    }

    private static UIElement RotatedImage(BitmapImage bmp, double angle)
    {
        var img = new Image
        {
            Source = bmp,
            Width = 80,
            Height = 80,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform { Angle = angle }
        };
        return img;
    }

    private static UIElement RotatedBorder(double angle)
    {
        // Asymmetric corner radius (only the top-left corner is round) so a 180 deg
        // rotation is visually distinct (the round corner moves to bottom-right).
        return new Border
        {
            Width = 80,
            Height = 80,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(80, 180, 220)),
            CornerRadius = new CornerRadius(34, 0, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform { Angle = angle }
        };
    }

    private static UIElement RotatedContainerWithChildren(double angle)
    {
        // A content-clean container WITH children — this is what the retained-layer
        // composite path (CompositeRetainedLayer -> AddBitmap) realizes + composites
        // for an animated transform; the 180 deg case is the original chevron bug.
        var inner = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(new Border { Width = 50, Height = 16, Margin = new Thickness(2), Background = new SolidColorBrush(Color.FromRgb(230, 80, 80)) });
        inner.Children.Add(new Border { Width = 50, Height = 16, Margin = new Thickness(2), Background = new SolidColorBrush(Color.FromRgb(80, 200, 120)), CornerRadius = new CornerRadius(8, 0, 0, 0) });
        inner.Children.Add(new Border { Width = 50, Height = 16, Margin = new Thickness(2), Background = new SolidColorBrush(Color.FromRgb(120, 140, 240)) });
        return new Border
        {
            Width = 90,
            Height = 90,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 48)),
            Child = inner,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform { Angle = angle }
        };
    }

    public static Window Build()
    {
        var bmp = MakeQuadrantBitmap(64);

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(20)
        };

        root.Children.Add(new TextBlock
        {
            Text = "Rotation / 180deg negative-diagonal repro — content must stay inside its grey frame and be correctly oriented",
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Row 1: quadrant image (AddBitmap path). 0=RG/BY, 90=cw, 180=YB/GR, 270=ccw.
        var imgRow = new StackPanel { Orientation = Orientation.Horizontal };
        imgRow.Children.Add(Frame(RotatedImage(bmp, 0), "Image 0"));
        imgRow.Children.Add(Frame(RotatedImage(bmp, 90), "Image 90"));
        imgRow.Children.Add(Frame(RotatedImage(bmp, 180), "Image 180"));
        imgRow.Children.Add(Frame(RotatedImage(bmp, 270), "Image 270"));
        root.Children.Add(imgRow);

        // Row 2: asymmetric rounded Border (AddSdfRect path). Round corner = top-left
        // at 0, should be bottom-right at 180.
        var rectRow = new StackPanel { Orientation = Orientation.Horizontal };
        rectRow.Children.Add(Frame(RotatedBorder(0), "Border 0"));
        rectRow.Children.Add(Frame(RotatedBorder(90), "Border 90"));
        rectRow.Children.Add(Frame(RotatedBorder(180), "Border 180"));
        rectRow.Children.Add(Frame(RotatedBorder(270), "Border 270"));
        root.Children.Add(rectRow);

        // Row 3: container-with-children (retained-layer composite path).
        var contRow = new StackPanel { Orientation = Orientation.Horizontal };
        contRow.Children.Add(Frame(RotatedContainerWithChildren(0), "Cont 0"));
        contRow.Children.Add(Frame(RotatedContainerWithChildren(180), "Cont 180"));
        root.Children.Add(contRow);

        return new Window
        {
            Title = "Jalium Rotation Repro",
            Width = 720,
            Height = 560,
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 32)),
            Content = root
        };
    }
}
