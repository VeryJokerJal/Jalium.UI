using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A lightweight overlay panel that displays real-time render diagnostics.
/// Built with Jalium.UI controls (Border, StackPanel, TextBlock) so it
/// participates in normal layout and theming instead of raw DrawingContext painting.
/// Toggle visibility with F3.
/// </summary>
internal sealed class DebugHudOverlay : Border
{
    private readonly TextBlock _fpsText;
    private readonly TextBlock _pathText;
    private readonly TextBlock _worstText;
    private readonly TextBlock _timingText;
    private readonly Border _barLayout;
    private readonly Border _barRender;
    private readonly Border _barPresent;
    private readonly TextBlock _framesText;
    private readonly TextBlock _dirtyText;
    private readonly TextBlock _windowText;
    private readonly TextBlock _gcText;

    public DebugHudOverlay()
    {
        // ── Container style ──
        Background = new SolidColorBrush(Color.FromArgb(235, 16, 16, 22));
        BorderBrush = new SolidColorBrush(Color.FromArgb(80, 100, 160, 220));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(10, 8, 10, 8);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(4, 4, 0, 0);
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;

        var accentColor = Color.FromArgb(255, 0, 180, 255);
        var labelColor = Color.FromArgb(220, 170, 180, 190);
        var dimColor = Color.FromArgb(200, 140, 145, 155);
        var valueColor = Color.FromArgb(245, 240, 240, 240);

        // ── Header row ──
        _fpsText = new TextBlock
        {
            Text = "0",
            FontFamily = new FontFamily(FrameworkElement.DefaultFontFamilyName),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 255, 80)),
            Margin = new Thickness(0, 0, 4, 0),
        };
        var fpsLabel = new TextBlock
        {
            Text = "FPS",
            FontFamily = new FontFamily(FrameworkElement.DefaultFontFamilyName),
            FontSize = 10,
            Foreground = new SolidColorBrush(labelColor),
            Margin = new Thickness(0, 8, 12, 0),
        };
        _pathText = new TextBlock
        {
            Text = "Full [D3D12]",
            FontFamily = new FontFamily(FrameworkElement.DefaultFontFamilyName),
            FontSize = 10,
            Foreground = new SolidColorBrush(dimColor),
            Margin = new Thickness(0, 8, 0, 0),
        };
        _worstText = new TextBlock
        {
            Text = "worst 0.0ms",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(dimColor),
            Margin = new Thickness(16, 8, 0, 0),
        };

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
        headerRow.Children.Add(_fpsText);
        headerRow.Children.Add(fpsLabel);
        headerRow.Children.Add(_pathText);
        headerRow.Children.Add(_worstText);

        // ── Timing text ──
        _timingText = new TextBlock
        {
            Text = "Layout 0.0ms   Render 0.0ms   Present 0.0ms   Total 0.0ms",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(valueColor),
            Margin = new Thickness(0, 4, 0, 2),
        };

        // ── Timing bar ──
        _barLayout = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 80, 180, 255)),
            Height = 3,
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _barRender = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 255, 160, 40)),
            Height = 3,
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _barPresent = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 100, 220, 100)),
            Height = 3,
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // Fixed-width container prevents layout feedback loop (bar width change → parent resize → repeat)
        var barRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Width = BarFixedWidth, ClipToBounds = true };
        barRow.Children.Add(_barLayout);
        barRow.Children.Add(_barRender);
        barRow.Children.Add(_barPresent);

        // ── Pipeline info ──
        _framesText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(labelColor),
        };
        _dirtyText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(labelColor),
            Margin = new Thickness(0, 0, 0, 6),
        };

        // ── System info ──
        _windowText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(dimColor),
        };
        _gcText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(dimColor),
        };

        // ── Assemble ──
        var root = new StackPanel();
        root.Children.Add(headerRow);
        root.Children.Add(_timingText);
        root.Children.Add(barRow);
        root.Children.Add(_framesText);
        root.Children.Add(_dirtyText);
        root.Children.Add(_windowText);
        root.Children.Add(_gcText);
        Child = root;
    }

    private const double BarFixedWidth = 380;

    private static readonly SolidColorBrush FpsGreen = new(Color.FromArgb(255, 80, 255, 80));
    private static readonly SolidColorBrush FpsYellow = new(Color.FromArgb(255, 255, 220, 60));
    private static readonly SolidColorBrush FpsRed = new(Color.FromArgb(255, 255, 70, 70));
    private static readonly SolidColorBrush WarnBrush = new(Color.FromArgb(255, 255, 180, 60));
    private static readonly SolidColorBrush ValueBrush = new(Color.FromArgb(245, 240, 240, 240));
    private static readonly SolidColorBrush DimBrush = new(Color.FromArgb(200, 140, 145, 155));

    /// <summary>
    /// Updates all displayed values. Call once per frame after EndDraw.
    /// </summary>
    public void Update(
        double fps, double worstMs,
        double layoutMs, double renderMs, double presentMs, double totalMs,
        string renderPath, string backend,
        int fullFrames, int partialFrames, int skippedFrames, int beginFails,
        int dirtyElements, string dirtyRegion,
        int windowW, int windowH, float dpiScale,
        long gcBytes, int gen0, int gen1, int gen2)
    {
        // FPS header
        _fpsText.Text = $"{fps:F0}";
        _fpsText.Foreground = fps >= 55 ? FpsGreen : fps >= 30 ? FpsYellow : FpsRed;
        _pathText.Text = $"{renderPath}  [{backend}]";
        _worstText.Text = $"worst {worstMs:F1}ms";
        _worstText.Foreground = worstMs > 16 ? WarnBrush : DimBrush;

        // Timing
        _timingText.Text = $"Layout {layoutMs:F1}ms   Render {renderMs:F1}ms   Present {presentMs:F1}ms   Total {totalMs:F1}ms";
        _timingText.Foreground = totalMs > 16 ? WarnBrush : ValueBrush;

        // Bar widths (proportional, clamped so sum == BarFixedWidth exactly)
        double barTotal = Math.Max(totalMs, 0.01);
        double lW = Math.Floor((layoutMs / barTotal) * BarFixedWidth);
        double rW = Math.Floor((renderMs / barTotal) * BarFixedWidth);
        lW = Math.Clamp(lW, 0, BarFixedWidth);
        rW = Math.Clamp(rW, 0, BarFixedWidth - lW);
        double pW = BarFixedWidth - lW - rW;
        _barLayout.Width = lW;
        _barRender.Width = rW;
        _barPresent.Width = pW;

        // Pipeline
        _framesText.Text = $"Frames  Full={fullFrames}  Partial={partialFrames}  Skip={skippedFrames}  Fail={beginFails}";
        _framesText.Foreground = beginFails > 0 ? WarnBrush : _dirtyText.Foreground;
        _dirtyText.Text = $"Dirty   {dirtyElements} elements   {dirtyRegion}";

        // System
        _windowText.Text = $"Window  {windowW}x{windowH}   DPI {dpiScale * 96:F0} ({dpiScale:F2}x)";
        _gcText.Text = $"GC      {gcBytes / (1024.0 * 1024.0):F1} MB   Gen {gen0}/{gen1}/{gen2}";
    }
}
