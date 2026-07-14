using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

// TEMPORARY verification window for the Vulkan effect clip/shift fixes
// (JALIUM_RENDER_BACKEND=vulkan vs d3d12 A/B):
//   A  static OuterGlow headline        — glow must fade past the text bounds,
//                                         no hard rectangular cut (padded-rect fix)
//   B  TransformGroup entrance loop     — glow text + shadow button must FOLLOW
//                                         the animation, no pinned/detached
//                                         effect, no slicing (transform fix)
//   C  shadow card inside a rounded     — the card must still cast a (soft)
//      ClipToBounds container             shadow, not lose it (rounded-clip
//                                         fallback fix)
//   D  static glow next to a moving     — no seam/ghost line marching across
//      block (dirty-region traffic)       the halo on partial frames (capture
//                                         scissor / cull suspension fixes)
internal static class EffectReproWindow
{
    public static Window Build()
    {
        var window = new Window
        {
            Title = "Effect Repro (vulkan clip/shift)",
            Width = 980,
            Height = 720,
            Background = new SolidColorBrush(Color.FromRgb(16, 26, 20))
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(28)
        };

        // ── A: static OuterGlow headline ────────────────────────────────────
        root.Children.Add(Caption("A  static glow — halo must fade OUT past the text bounds"));
        var glowHeadline = new TextBlock
        {
            Text = "that ship.",
            FontSize = 54,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 170, 40)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 2, 0, 10),
            Effect = new OuterGlowEffect
            {
                GlowColor = Color.FromRgb(255, 150, 20),
                GlowSize = 18,
                Opacity = 0.9,
                Intensity = 1.4
            }
        };
        root.Children.Add(glowHeadline);

        // ── B: TransformGroup entrance loop (native-matrix path) ───────────
        root.Children.Add(Caption("B  entrance loop (scale+translate) — effects must FOLLOW the motion"));
        var animGlow = new TextBlock
        {
            Text = "gliding glow",
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 220, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 4),
            Effect = new OuterGlowEffect
            {
                GlowColor = Color.FromRgb(90, 200, 255),
                GlowSize = 14,
                Opacity = 0.85,
                Intensity = 1.2
            }
        };
        var animShadowButton = new Border
        {
            Width = 220,
            Height = 46,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(70, 74, 90)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 0, 10),
            Child = new TextBlock
            {
                Text = "shadow follows me",
                FontSize = 15,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                Direction = 270,
                ShadowDepth = 8,
                Opacity = 0.85
            }
        };
        var animScale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        var animTranslate = new TranslateTransform { X = 0, Y = 0 };
        var animGroup = new TransformGroup();
        animGroup.Children.Add(animScale);
        animGroup.Children.Add(animTranslate);
        var animScale2 = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        var animTranslate2 = new TranslateTransform { X = 0, Y = 0 };
        var animGroup2 = new TransformGroup();
        animGroup2.Children.Add(animScale2);
        animGroup2.Children.Add(animTranslate2);
        animGlow.RenderTransformOrigin = new Point(0.5, 0.5);
        animGlow.RenderTransform = animGroup;
        animShadowButton.RenderTransformOrigin = new Point(0.5, 0.5);
        animShadowButton.RenderTransform = animGroup2;
        root.Children.Add(animGlow);
        root.Children.Add(animShadowButton);

        // ── C: shadow card inside a rounded ClipToBounds container ─────────
        root.Children.Add(Caption("C  shadow card in a rounded clipped panel — shadow must NOT vanish"));
        var shadowCard = new Border
        {
            Width = 200,
            Height = 70,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(46, 60, 52)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "card with shadow",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                Direction = 270,
                ShadowDepth = 6,
                Opacity = 0.9
            }
        };
        var roundedPanel = new Border
        {
            Width = 340,
            Height = 130,
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(26, 38, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 96, 80)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 10),
            Child = shadowCard
        };
        root.Children.Add(roundedPanel);

        // ── D: static glow + dirty-region traffic ───────────────────────────
        root.Children.Add(Caption("D  static glow near moving block — no marching seam through the halo"));
        var row = new Canvas { Height = 110, Margin = new Thickness(8, 0, 0, 0) };
        var seamGlow = new TextBlock
        {
            Text = "steady halo",
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 255, 140)),
            Effect = new OuterGlowEffect
            {
                GlowColor = Color.FromRgb(140, 240, 90),
                GlowSize = 16,
                Opacity = 0.9,
                Intensity = 1.3
            }
        };
        Canvas.SetLeft(seamGlow, 0);
        Canvas.SetTop(seamGlow, 30);
        var movingBlock = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(230, 90, 90))
        };
        Canvas.SetLeft(movingBlock, 260);
        Canvas.SetTop(movingBlock, 40);
        row.Children.Add(seamGlow);
        row.Children.Add(movingBlock);
        root.Children.Add(row);

        window.Content = root;

        // Looping "entrance" (ease-out then reset) + dirty-region traffic.
        double t = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            t += 0.016;
            // 2.4 s cycle: 0..1 progress with ease-out cubic, hold, reset.
            double cycle = t % 2.4;
            double p = Math.Min(1.0, cycle / 1.2);
            double ease = 1 - Math.Pow(1 - p, 3);
            animTranslate.Y = 46 * (1 - ease);
            animTranslate2.Y = 46 * (1 - ease);
            animScale.ScaleX = animScale.ScaleY = 0.88 + 0.12 * ease;
            animScale2.ScaleX = animScale2.ScaleY = 0.88 + 0.12 * ease;
            // D: the block shuttles horizontally through/near the halo,
            // generating partial-frame damage every tick.
            double s = (Math.Sin(t * 2.2) + 1) * 0.5;
            Canvas.SetLeft(movingBlock, 240 + 240 * s);
        };
        timer.Start();

        return window;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = new SolidColorBrush(Color.FromRgb(150, 165, 155)),
        Margin = new Thickness(0, 10, 0, 4)
    };
}
