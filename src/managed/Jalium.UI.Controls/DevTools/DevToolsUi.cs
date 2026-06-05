using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>
/// Factory helpers that produce consistently-styled UI parts for the DevTools
/// surfaces. Tabs should use these instead of hand-rolling Borders so the
/// "Instrument" visual language (graphite surfaces, signal-amber accents,
/// Bahnschrift uppercase labels, Cascadia Code data, hairline rules) stays
/// uniform across every page.
/// </summary>
internal static class DevToolsUi
{
    // Dark ink for text painted on top of the bright amber accent fill — amber is
    // too luminous for light text to read against.
    private static readonly SolidColorBrush OnAccent = new(DevToolsTheme.ChromeColor);

    // --- Typography --------------------------------------------------------

    public static TextBlock Text(string content, double size = DevToolsTheme.FontBase, Brush? color = null, FontWeight? weight = null, bool mono = false)
    {
        return new TextBlock
        {
            Text = content,
            FontSize = size,
            FontFamily = mono ? DevToolsTheme.MonoFont : DevToolsTheme.UiFont,
            Foreground = color ?? DevToolsTheme.TextPrimary,
            FontWeight = weight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static TextBlock Muted(string content, double size = DevToolsTheme.FontSm)
        => Text(content, size, DevToolsTheme.TextSecondary);

    public static TextBlock Mono(string content, double size = DevToolsTheme.FontSm, Brush? color = null)
        => Text(content, size, color ?? DevToolsTheme.TextPrimary, mono: true);

    /// <summary>
    /// Letter-spaces a short, display-only label by inserting thin spaces between
    /// characters — the framework has no letter-spacing property, so this fakes the
    /// "S P A C E D   C A P S" instrument look. Only use on static labels (headings,
    /// pills, eyebrows), never on user-editable / measured-width text.
    /// </summary>
    public static string Tracked(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 2) return s ?? string.Empty;
        return string.Join(' ', s.ToCharArray());
    }

    /// <summary>
    /// A section title: uppercase, tracked, Bahnschrift, painted in the signal amber.
    /// </summary>
    public static TextBlock SectionHeading(string content)
    {
        return new TextBlock
        {
            Text = Tracked(content.ToUpperInvariant()),
            FontSize = DevToolsTheme.FontLg,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.Accent,
            Margin = new Thickness(0, DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterSm),
        };
    }

    /// <summary>A panel heading: uppercase Bahnschrift in primary text.</summary>
    public static TextBlock PanelHeading(string content)
    {
        return new TextBlock
        {
            Text = content.ToUpperInvariant(),
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm),
        };
    }

    /// <summary>
    /// A tiny uppercase, tracked, muted meta-label (a "channel" caption).
    /// </summary>
    public static TextBlock Eyebrow(string content, Brush? color = null)
    {
        return new TextBlock
        {
            Text = Tracked(content.ToUpperInvariant()),
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = color ?? DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // --- Panels / surfaces -------------------------------------------------

    public static Border Card(UIElement child, bool alternate = false)
    {
        return new Border
        {
            Background = alternate ? DevToolsTheme.Surface : DevToolsTheme.SurfaceAlt,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Padding = new Thickness(DevToolsTheme.GutterBase),
            Child = child,
            ClipToBounds = true,
        };
    }

    /// <summary>
    /// Frames an element with four amber L-shaped corner ticks — the instrument
    /// "registration mark" motif. Cheap (4 hairline borders) and shadow-free.
    /// </summary>
    public static Grid CornerTicks(UIElement child, Brush? tick = null, double size = 9)
    {
        var brush = tick ?? DevToolsTheme.Accent;
        var grid = new Grid();
        grid.Children.Add(child);

        Border Corner(Thickness sides, HorizontalAlignment h, VerticalAlignment v) => new()
        {
            Width = size,
            Height = size,
            BorderBrush = brush,
            BorderThickness = sides,
            HorizontalAlignment = h,
            VerticalAlignment = v,
            IsHitTestVisible = false,
        };

        grid.Children.Add(Corner(new Thickness(1, 1, 0, 0), HorizontalAlignment.Left,  VerticalAlignment.Top));
        grid.Children.Add(Corner(new Thickness(0, 1, 1, 0), HorizontalAlignment.Right, VerticalAlignment.Top));
        grid.Children.Add(Corner(new Thickness(1, 0, 0, 1), HorizontalAlignment.Left,  VerticalAlignment.Bottom));
        grid.Children.Add(Corner(new Thickness(0, 0, 1, 1), HorizontalAlignment.Right, VerticalAlignment.Bottom));
        return grid;
    }

    /// <summary>
    /// A small instrument LED: a faint ring around a solid core. "On" lights the
    /// core in <paramref name="color"/>; "off" dims it. No drop shadow.
    /// </summary>
    public static Grid StatusLed(Brush color, bool on = true, double size = 8)
    {
        var c = (color as SolidColorBrush)?.Color ?? DevToolsTheme.SuccessColor;
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

        grid.Children.Add(new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(Color.FromArgb(on ? (byte)0x33 : (byte)0x16, c.R, c.G, c.B)),
        });
        double core = size * 0.5;
        grid.Children.Add(new Border
        {
            Width = core,
            Height = core,
            CornerRadius = new CornerRadius(core / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = on ? color : DevToolsTheme.TextDisabled,
        });
        return grid;
    }

    /// <summary>
    /// A framed instrument panel: a SurfaceAlt card with a tracked uppercase Eyebrow
    /// header (and optional status LED pushed to the right), a hairline divider, then
    /// the body. The cohesive building block for the data/perf surfaces.
    /// </summary>
    public static Border Panel(string title, UIElement body, Brush? led = null, bool ledOn = true)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var eyebrow = Eyebrow(title, DevToolsTheme.TextSecondary);
        Grid.SetColumn(eyebrow, 0);
        header.Children.Add(eyebrow);
        if (led != null)
        {
            var dot = StatusLed(led, ledOn);
            Grid.SetColumn(dot, 1);
            header.Children.Add(dot);
        }

        var headerStack = new StackPanel { Orientation = Orientation.Vertical };
        headerStack.Children.Add(header);
        headerStack.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterBase),
        });

        // Header in an Auto row, body in a Star row. Critically this gives the body
        // a BOUNDED height, so a ScrollViewer body actually scrolls (shows its
        // scrollbar) instead of growing unbounded and being clipped by the card.
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(headerStack, 0);
        Grid.SetRow(body, 1);
        grid.Children.Add(headerStack);
        grid.Children.Add(body);

        return new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg),
            Child = grid,
            ClipToBounds = true,
        };
    }

    /// <summary>
    /// A bar along the top of a panel hosting buttons/filters. Graphite chrome with
    /// a bottom hairline, mirroring an instrument's control strip.
    /// </summary>
    public static Border Toolbar(UIElement child)
    {
        // Chrome bar with a faint scanline wash beneath the controls.
        var layers = new Grid();
        layers.Children.Add(new Border { Background = DevToolsTheme.Scanline, IsHitTestVisible = false });
        layers.Children.Add(new Border
        {
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterSm, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            Child = child,
        });
        return new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessBottom,
            Child = layers,
        };
    }

    public static Border StatusBar(UIElement child)
    {
        var layers = new Grid();
        layers.Children.Add(new Border { Background = DevToolsTheme.Scanline, IsHitTestVisible = false });
        layers.Children.Add(new Border
        {
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
            Child = child,
        });
        return new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = layers,
        };
    }

    // --- Buttons -----------------------------------------------------------

    public sealed class DevToolsButton : Border
    {
        private readonly Border _content;
        private bool _isActive;
        private bool _isHovered;
        private bool _isPressed;
        private ButtonStyle _style;
        private readonly TextBlock? _iconText;
        private readonly TextBlock _labelText;
        private string _labelValue;

        /// <summary>
        /// Change the visual style at runtime. Useful for toggle buttons that
        /// should swap between Primary (start) and Danger (stop) palettes.
        /// </summary>
        public new ButtonStyle Style
        {
            get => _style;
            set
            {
                if (_style == value) return;
                _style = value;
                ApplyVisualState();
            }
        }

        public DevToolsButton(string label, Action? onClick, ButtonStyle style = ButtonStyle.Default, string? iconGlyph = null)
        {
            _style = style;
            _labelValue = label;

            BorderThickness = DevToolsTheme.ThicknessHairline;
            CornerRadius = DevToolsTheme.RadiusSm;
            Padding = new Thickness(0);
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0);

            _iconText = iconGlyph == null ? null : new TextBlock
            {
                Text = iconGlyph,
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            };
            _labelText = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.DisplayFont,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (_iconText != null) row.Children.Add(_iconText);
            row.Children.Add(_labelText);

            _content = new Border
            {
                Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterSm - 1, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm - 1),
                Child = row,
            };
            Child = _content;

            ApplyVisualState();

            MouseEnter += (_, _) => { _isHovered = true; ApplyVisualState(); };
            MouseLeave += (_, _) => { _isHovered = false; _isPressed = false; ApplyVisualState(); };
            MouseDown += (_, _) => { _isPressed = true; ApplyVisualState(); };
            MouseUp += (_, _) =>
            {
                if (_isPressed && _isHovered)
                    onClick?.Invoke();
                _isPressed = false;
                ApplyVisualState();
            };
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                ApplyVisualState();
            }
        }

        public string Label
        {
            get => _labelValue;
            set
            {
                _labelValue = value;
                _labelText.Text = value.ToUpperInvariant();
            }
        }

        public void SetIcon(string? glyph)
        {
            if (_iconText != null)
                _iconText.Text = glyph ?? string.Empty;
        }

        private void ApplyVisualState()
        {
            Brush bg, border, fg;
            switch (_style)
            {
                case ButtonStyle.Primary:
                    bg = _isPressed ? DevToolsTheme.AccentPressed
                        : (_isHovered ? DevToolsTheme.AccentHover : DevToolsTheme.Accent);
                    border = bg;
                    fg = OnAccent; // dark ink on bright amber
                    break;

                case ButtonStyle.Danger:
                    // Solid red fill — used for "Stop" / destructive toggles.
                    var baseRed = DevToolsTheme.ErrorColor;
                    var hoverRed = BlendTowardWhite(baseRed, 0.15);
                    var pressRed = BlendTowardWhite(baseRed, -0.15);
                    bg = new SolidColorBrush(_isPressed ? pressRed : _isHovered ? hoverRed : baseRed);
                    border = bg;
                    fg = DevToolsTheme.TextPrimary;
                    break;

                default:
                    bg = _isActive
                        ? DevToolsTheme.AccentSoft
                        : (_isPressed ? DevToolsTheme.ControlPressed : (_isHovered ? DevToolsTheme.ControlHover : DevToolsTheme.Control));
                    border = _isActive ? DevToolsTheme.Accent : DevToolsTheme.Border;
                    fg = _isActive ? DevToolsTheme.Accent : (_isHovered ? DevToolsTheme.TextPrimary : DevToolsTheme.TextSecondary);
                    break;
            }

            Background = bg;
            BorderBrush = border;
            _labelText.Foreground = fg;
            if (_iconText != null) _iconText.Foreground = fg;
        }

        private static Color BlendTowardWhite(Color c, double amount)
        {
            // amount > 0 -> lighter; < 0 -> darker.
            double t = Math.Clamp(amount, -1.0, 1.0);
            byte target = t >= 0 ? (byte)255 : (byte)0;
            double mix = Math.Abs(t);
            return Color.FromRgb(
                (byte)(c.R + (target - c.R) * mix),
                (byte)(c.G + (target - c.G) * mix),
                (byte)(c.B + (target - c.B) * mix));
        }
    }

    public enum ButtonStyle
    {
        Default,
        Primary,
        Danger,
    }

    public static DevToolsButton Button(string label, Action onClick, ButtonStyle style = ButtonStyle.Default, string? icon = null)
    {
        return new DevToolsButton(label, onClick, style, icon);
    }

    public static DevToolsButton Toggle(string label, Action onClick, bool isActive, string? icon = null)
    {
        var btn = new DevToolsButton(label, onClick, ButtonStyle.Default, icon) { IsActive = isActive };
        return btn;
    }

    // --- Tables ------------------------------------------------------------

    public static TextBlock GridCell(string text, int column, Brush? color = null, bool mono = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = mono ? DevToolsTheme.MonoFont : DevToolsTheme.UiFont,
            Foreground = color ?? DevToolsTheme.TextPrimary,
            Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(tb, column);
        return tb;
    }

    public static Border GridRow(bool alt, UIElement content)
    {
        return new Border
        {
            Background = alt ? DevToolsTheme.RowAlt : null,
            Child = content,
        };
    }

    /// <summary>
    /// A monospace key/value readout row: a dim key on the left, the value on the
    /// right in Cascadia. The atom of an instrument data grid.
    /// </summary>
    public static Grid KeyValueRow(string key, string value, Brush? valueColor = null)
    {
        var g = new Grid { Margin = new Thickness(0, DevToolsTheme.GutterXS, 0, DevToolsTheme.GutterXS) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var k = new TextBlock
        {
            Text = key,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var v = new TextBlock
        {
            Text = value,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = valueColor ?? DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(DevToolsTheme.GutterLg, 0, 0, 0),
        };
        Grid.SetColumn(k, 0);
        Grid.SetColumn(v, 1);
        g.Children.Add(k);
        g.Children.Add(v);
        return g;
    }

    // --- Charts / meters ---------------------------------------------------

    /// <summary>
    /// A proportional fill bar (fraction 0..1) over a recessed track. Width-independent
    /// (uses star columns) so it fills correctly at any measured size.
    /// </summary>
    public static Border Meter(double fraction, Brush fill, double height = 5, Brush? track = null)
    {
        fraction = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        var bar = new Grid { Height = height };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, fraction), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - fraction), GridUnitType.Star) });
        var f = new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumn(f, 0);
        bar.Children.Add(f);
        return new Border
        {
            Background = track ?? DevToolsTheme.GridLine,
            CornerRadius = new CornerRadius(height / 2),
            Child = bar,
            ClipToBounds = true,
        };
    }

    /// <summary>
    /// A labelled meter row: [label .......... value] above a proportional bar.
    /// </summary>
    public static StackPanel MeterBar(string label, double fraction, string valueText, Brush fill)
    {
        var head = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new TextBlock { Text = label, FontSize = DevToolsTheme.FontXS, FontFamily = DevToolsTheme.UiFont, Foreground = DevToolsTheme.TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var v = new TextBlock { Text = valueText, FontSize = DevToolsTheme.FontXS, FontFamily = DevToolsTheme.MonoFont, Foreground = DevToolsTheme.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0) };
        Grid.SetColumn(l, 0);
        Grid.SetColumn(v, 1);
        head.Children.Add(l);
        head.Children.Add(v);

        var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm) };
        stack.Children.Add(head);
        stack.Children.Add(Meter(fraction, fill));
        return stack;
    }

    /// <summary>
    /// A single horizontal stacked bar split into proportional, colored segments.
    /// </summary>
    public static Border StackedBar(IReadOnlyList<(double Value, Brush Color)> segments, double height = 10)
    {
        double total = 0;
        for (int i = 0; i < segments.Count; i++) total += Math.Max(0, segments[i].Value);
        if (total <= 0)
            return new Border { Height = height, Background = DevToolsTheme.GridLine, CornerRadius = new CornerRadius(height / 2) };

        var bar = new Grid { Height = height };
        for (int i = 0; i < segments.Count; i++)
        {
            double v = Math.Max(0, segments[i].Value);
            if (v <= 0) continue;
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(v, GridUnitType.Star) });
            var seg = new Border { Background = segments[i].Color };
            Grid.SetColumn(seg, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(seg);
        }
        return new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Child = bar, ClipToBounds = true };
    }

    // --- Inputs ------------------------------------------------------------

    public static TextBox TextInput(double width = double.NaN, string? placeholder = null)
    {
        var tb = new TextBox
        {
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextPrimary,
            Background = DevToolsTheme.Control,
            BorderBrush = DevToolsTheme.Border,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterSm - 1, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm - 1),
        };
        if (!double.IsNaN(width)) tb.Width = width;
        if (placeholder != null) tb.PlaceholderText = placeholder;
        return tb;
    }

    // --- Divider / spacer --------------------------------------------------

    public static Border VerticalDivider(double height = 16)
    {
        return new Border
        {
            Width = 1,
            Height = height,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static UIElement Spacer(double width = DevToolsTheme.GutterBase)
    {
        return new Border { Width = width };
    }

    // --- Segmented toggle (icon-only mode switcher) ------------------------

    /// <summary>
    /// A compact segmented control: one small square per option, with the active
    /// segment tinted amber and decorated with a bottom "LED" dot. Matches the
    /// view-mode switcher seen on instrument front panels.
    /// </summary>
    public sealed class SegmentedToggle : Border
    {
        private readonly List<Border> _segments = new();
        private readonly List<Action?> _callbacks = new();
        private int _selectedIndex = -1;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value == _selectedIndex) return;
                _selectedIndex = value;
                UpdateVisualState();
            }
        }

        /// <summary>Select an option without invoking its callback.</summary>
        public void SetSelectedSilent(int index)
        {
            _selectedIndex = index;
            UpdateVisualState();
        }

        public SegmentedToggle()
        {
            Background = DevToolsTheme.Control;
            BorderBrush = DevToolsTheme.BorderSubtle;
            BorderThickness = DevToolsTheme.ThicknessHairline;
            CornerRadius = DevToolsTheme.RadiusBase;
            Padding = new Thickness(2);
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0);
            VerticalAlignment = VerticalAlignment.Center;
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            Child = strip;
        }

        public SegmentedToggle AddSegment(string glyph, string? tooltip, Action onSelect)
        {
            if (Child is not StackPanel strip) return this;

            int index = _segments.Count;
            _callbacks.Add(onSelect);

            var iconText = new TextBlock
            {
                Text = glyph,
                FontSize = 13,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.TextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            var dot = new Border
            {
                Width = 4,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = DevToolsTheme.Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2),
                Visibility = Visibility.Collapsed,
            };
            var cell = new Grid();
            cell.Children.Add(iconText);
            cell.Children.Add(dot);

            var seg = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                CornerRadius = DevToolsTheme.RadiusSm,
                Width = 28,
                Height = 24,
                Child = cell,
                Cursor = Cursors.Hand,
            };
            if (tooltip != null)
                seg.SetValue(ToolTipService.ToolTipProperty, tooltip);

            seg.MouseEnter += (_, _) =>
            {
                if (index != _selectedIndex)
                    seg.Background = DevToolsTheme.ControlHover;
            };
            seg.MouseLeave += (_, _) =>
            {
                if (index != _selectedIndex)
                    seg.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            };
            seg.MouseDown += (_, _) =>
            {
                SelectedIndex = index;
                onSelect();
            };

            // Store the icon/dot on the segment so UpdateVisualState can tint them.
            seg.Tag = (iconText, dot);

            _segments.Add(seg);
            strip.Children.Add(seg);
            return this;
        }

        private void UpdateVisualState()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var seg = _segments[i];
                var (iconText, dot) = ((TextBlock iconText, Border dot))seg.Tag!;
                bool active = i == _selectedIndex;
                seg.Background = active ? DevToolsTheme.AccentSoft : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                iconText.Foreground = active ? DevToolsTheme.Accent : DevToolsTheme.TextSecondary;
                dot.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public static SegmentedToggle Segmented(params (string Glyph, string? Tooltip, Action OnSelect)[] segments)
    {
        var toggle = new SegmentedToggle();
        foreach (var s in segments)
            toggle.AddSegment(s.Glyph, s.Tooltip, s.OnSelect);
        return toggle;
    }

    // --- Status / data tag ------------------------------------------------

    /// <summary>
    /// A small rectangular instrument tag: uppercase tracked Bahnschrift in
    /// <paramref name="foreground"/>, over a faint tint of the same hue, ringed by a
    /// hairline of that hue. Reads like a labelled readout chip.
    /// </summary>
    public static Border Pill(string text, Brush foreground, Brush? fill = null)
    {
        var hue = (foreground as SolidColorBrush)?.Color ?? DevToolsTheme.TextSecondaryColor;
        return new Border
        {
            Background = fill ?? new SolidColorBrush(Color.FromArgb(0x24, hue.R, hue.G, hue.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, hue.R, hue.G, hue.B)),
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusSm,
            Padding = new Thickness(DevToolsTheme.GutterSm + 1, 1, DevToolsTheme.GutterSm + 1, 1),
            Child = new TextBlock
            {
                Text = Tracked(text.ToUpperInvariant()),
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.DisplayFont,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
            },
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
