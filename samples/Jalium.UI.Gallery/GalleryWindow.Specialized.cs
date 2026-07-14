using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for media, drawing, code/data viewers, editors and
/// resource-backed hosts. Resource-dependent controls (WebView, Terminal,
/// CameraView, MediaElement, MapView, Viewport3D, MiniMap) and Image (no
/// bundled asset) are documented via <see cref="GalleryWindow.Placeholder"/>
/// so the catalog stays 100% safe to construct for a static screenshot.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildSpecializedSection() => Section(
        "Media & Specialized",
        "Drawing, codes, viewers, editors and resource-backed hosts.",
        Card("QRCode", QrCodeDemo(), width: 0),
        Card("InkCanvas", InkCanvasDemo(), width: 0),
        Card("DiffViewer", DiffViewerDemo(), width: 480),
        Card("JsonTreeViewer", JsonTreeViewerDemo(), width: 340),
        Card("PropertyGrid", PropertyGridDemo(), width: 340),
        Card("HexEditor", HexEditorDemo(), width: 480),
        Card("Image", Placeholder("Image", "Displays an ImageSource; supports Stretch modes and pinch-zoom.")),
        Card("WebView", Placeholder("WebView", "Embedded Chromium browser surface that navigates to a URL or HTML string.")),
        Card("Terminal", Placeholder("Terminal", "Hosts an interactive shell / PTY session with ANSI color rendering.")),
        Card("CameraView", Placeholder("CameraView", "Live preview from a connected camera device.")),
        Card("MediaElement", Placeholder("MediaElement", "Plays audio and video from a media file or stream.")),
        Card("MapView", Placeholder("MapView", "Pannable, zoomable map backed by network tile services.")),
        Card("Viewport3D", Placeholder("Viewport3D", "Renders 3D scene content with cameras, lights and meshes.")),
        Card("MiniMap", Placeholder("MiniMap", "Bird's-eye overview of a larger scrollable surface.")));

    // QRCode renders its foreground (default black) onto its Background. On the
    // dark card a default QR would be invisible, so we present it on a white
    // plate Border — this is a container, not a recolor of the control itself.
    private static UIElement QrCodeDemo()
    {
        var qr = new QRCode
        {
            Text = "https://jalium.dev",
            ErrorCorrectionLevel = QRCodeErrorCorrectionLevel.Q,
            Width = 150,
            Height = 150,
        };

        return new Border
        {
            Background = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = qr,
        };
    }

    private static UIElement InkCanvasDemo() => new InkCanvas
    {
        Width = 280,
        Height = 150,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static UIElement DiffViewerDemo() => new DiffViewer
    {
        OriginalText = "func greet(name string) {\n    fmt.Println(\"Hi \" + name)\n}",
        ModifiedText = "func greet(name, title string) {\n    fmt.Printf(\"Hi %s %s\\n\", title, name)\n}",
        Width = 452,
        Height = 190,
    };

    private static UIElement JsonTreeViewerDemo() => new JsonTreeViewer
    {
        JsonText = "{\n  \"name\": \"Jalium.UI\",\n  \"version\": \"26.10.5\",\n  \"gpu\": true,\n  \"engines\": [\"D3D12\", \"Vulkan\"]\n}",
        Width = 312,
        Height = 190,
    };

    private static UIElement PropertyGridDemo() => new PropertyGrid
    {
        SelectedObject = new SampleProfile(),
        Width = 312,
        Height = 210,
    };

    private static UIElement HexEditorDemo() => new HexEditor
    {
        Data = new byte[]
        {
            0x4A, 0x61, 0x6C, 0x69, 0x75, 0x6D, 0x2E, 0x55,
            0x49, 0x20, 0x47, 0x50, 0x55, 0x20, 0x55, 0x49,
            0x00, 0x01, 0x02, 0x03, 0x80, 0xFE, 0xFF, 0x7F,
            0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
        },
        Width = 452,
        Height = 190,
    };

    // A tiny POCO so PropertyGrid has a handful of readable rows to reflect over.
    private sealed class SampleProfile
    {
        public string Name { get; set; } = "Jalium";
        public int Version { get; set; } = 26;
        public bool GpuAccelerated { get; set; } = true;
        public double Opacity { get; set; } = 0.95;
    }
}
