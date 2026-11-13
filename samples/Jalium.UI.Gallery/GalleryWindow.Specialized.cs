using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Ink;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildSpecializedSection() => SectionFromCards(
        "Media & Specialized",
        "Drawing, codes, viewers, editors and resource-backed hosts.",
        CreateSpecializedCards());

    private static DeferredCard[] CreateSpecializedCards() =>
    [
        new("Gallery.Card.Specialized.QRCode", "QRCode", static () => new GallerySampleQrDemo(), Width: 0),
        new("Gallery.Card.Specialized.InkCanvas", "InkCanvas", static () => new GallerySampleInkDemo(), Width: 0),
        new("Gallery.Card.Specialized.DiffViewer", "DiffViewer", static () => new GallerySampleDiffDemo(), Width: 480),
        new("Gallery.Card.Specialized.JsonTreeViewer", "JsonTreeViewer", static () => new GallerySampleJsonDemo(), Width: 340),
        new("Gallery.Card.Specialized.PropertyGrid", "PropertyGrid", static () => new GallerySamplePropertyDemo(), Width: 340),
        new("Gallery.Card.Specialized.HexEditor", "HexEditor", static () => new GallerySampleHexDemo(), Width: 480),
        new(
            "Gallery.Card.Specialized.Image",
            "Image",
            static () => Placeholder(
                "Image",
                "Displays an ImageSource; supports Stretch modes and pinch-zoom.")),
        new(
            "Gallery.Card.Specialized.WebView",
            "WebView",
            static () => Placeholder(
                "WebView",
                "Embedded Chromium browser surface that navigates to a URL or HTML string.")),
        new(
            "Gallery.Card.Specialized.Terminal",
            "Terminal",
            static () => Placeholder(
                "Terminal",
                "Hosts an interactive shell / PTY session with ANSI color rendering.")),
        new(
            "Gallery.Card.Specialized.CameraView",
            "CameraView",
            static () => Placeholder(
                "CameraView",
                "Live preview from a connected camera device.")),
        new(
            "Gallery.Card.Specialized.MediaElement",
            "MediaElement",
            static () => Placeholder(
                "MediaElement",
                "Plays audio and video from a media file or stream.")),
        new(
            "Gallery.Card.Specialized.MapView",
            "MapView",
            static () => Placeholder(
                "MapView",
                "Pannable, zoomable map backed by network tile services.")),
        new(
            "Gallery.Card.Specialized.Viewport3D",
            "Viewport3D",
            static () => Placeholder(
                "Viewport3D",
                "Renders 3D scene content with cameras, lights and meshes.")),
        new(
            "Gallery.Card.Specialized.MiniMap",
            "MiniMap",
            static () => Placeholder(
                "MiniMap",
                "Bird's-eye overview of a larger scrollable surface.")),
    ];

}

[TemplatePart(Name = "PART_QrCode", Type = typeof(QRCode))]
public class GallerySampleQrDemo : GallerySampleDemo
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(GallerySampleQrDemo), new PropertyMetadata("https://jalium.dev"));
    public string Text { get => (string)GetValue(TextProperty)!; set => SetValue(TextProperty, value); }
    public GallerySampleQrDemo() { DefaultStyleKey = typeof(GallerySampleQrDemo);  }
}
[TemplatePart(Name = "PART_Ink", Type = typeof(InkCanvas))]
public class GallerySampleInkDemo : GallerySampleDemo
{
    public static readonly DependencyProperty StrokesProperty = DependencyProperty.Register(nameof(Strokes), typeof(StrokeCollection), typeof(GallerySampleInkDemo), new PropertyMetadata(null));
    public StrokeCollection Strokes { get => (StrokeCollection)GetValue(StrokesProperty)!; set => SetValue(StrokesProperty, value); }
    public GallerySampleInkDemo() { DefaultStyleKey = typeof(GallerySampleInkDemo); SetCurrentValue(StrokesProperty, new StrokeCollection()); }
}
[TemplatePart(Name = "PART_Diff", Type = typeof(DiffViewer))]
public class GallerySampleDiffDemo : GallerySampleDemo
{
    public static readonly DependencyProperty OriginalTextProperty = DependencyProperty.Register(nameof(OriginalText), typeof(string), typeof(GallerySampleDiffDemo), new PropertyMetadata("func greet(name string) {\n    fmt.Println(\"Hi \" + name)\n}"));
    public string OriginalText { get => (string)GetValue(OriginalTextProperty)!; set => SetValue(OriginalTextProperty, value); }
    public static readonly DependencyProperty ModifiedTextProperty = DependencyProperty.Register(nameof(ModifiedText), typeof(string), typeof(GallerySampleDiffDemo), new PropertyMetadata("func greet(name, title string) {\n    fmt.Printf(\"Hi %s %s\\n\", title, name)\n}"));
    public string ModifiedText { get => (string)GetValue(ModifiedTextProperty)!; set => SetValue(ModifiedTextProperty, value); }
    public GallerySampleDiffDemo() { DefaultStyleKey = typeof(GallerySampleDiffDemo);  }
}
[TemplatePart(Name = "PART_Json", Type = typeof(JsonTreeViewer))]
public class GallerySampleJsonDemo : GallerySampleDemo
{
    public static readonly DependencyProperty JsonTextProperty = DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(GallerySampleJsonDemo), new PropertyMetadata("{\n  \"name\": \"Jalium.UI\",\n  \"version\": \"26.10.5\",\n  \"gpu\": true,\n  \"engines\": [\"D3D12\", \"Vulkan\"]\n}"));
    public string JsonText { get => (string)GetValue(JsonTextProperty)!; set => SetValue(JsonTextProperty, value); }
    public GallerySampleJsonDemo() { DefaultStyleKey = typeof(GallerySampleJsonDemo);  }
}
[TemplatePart(Name = "PART_PropertyGrid", Type = typeof(PropertyGrid))]
public class GallerySamplePropertyDemo : GallerySampleDemo
{
    public static readonly DependencyProperty SelectedObjectProperty = DependencyProperty.Register(nameof(SelectedObject), typeof(object), typeof(GallerySamplePropertyDemo), new PropertyMetadata(null));
    public object SelectedObject { get => (object)GetValue(SelectedObjectProperty)!; set => SetValue(SelectedObjectProperty, value); }
    public GallerySamplePropertyDemo() { DefaultStyleKey = typeof(GallerySamplePropertyDemo); SetCurrentValue(SelectedObjectProperty, new GallerySampleProfile()); }
}

public sealed class GallerySampleProfile
{
    public string Name { get; set; } = "Jalium";
    public int Version { get; set; } = 26;
    public bool GpuAccelerated { get; set; } = true;
    public double Opacity { get; set; } = .95;
}
[TemplatePart(Name = "PART_HexEditor", Type = typeof(HexEditor))]
public class GallerySampleHexDemo : GallerySampleDemo
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(byte[]), typeof(GallerySampleHexDemo), new PropertyMetadata(null));
    public byte[] Data { get => (byte[])GetValue(DataProperty)!; set => SetValue(DataProperty, value); }
    public GallerySampleHexDemo() { DefaultStyleKey = typeof(GallerySampleHexDemo); SetCurrentValue(DataProperty, new byte[]
        {
            0x4A, 0x61, 0x6C, 0x69, 0x75, 0x6D, 0x2E, 0x55, 0x49, 0x20, 0x47, 0x50, 0x55, 0x20, 0x55, 0x49,
            0x00, 0x01, 0x02, 0x03, 0x80, 0xFE, 0xFF, 0x7F, 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
        }); }
}
