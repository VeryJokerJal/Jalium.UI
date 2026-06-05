using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private TextBlock? _rulerStatusText;
    private TextBlock? _pickerStatusText;
    private TextBlock? _exportStatusText;
    private TextBlock? _screenshotStatusText;

    // Result panes — hidden while empty, populated when the user completes an
    // action so they can review the measurement / sample at leisure.
    private Border? _rulerResultHost;
    private Border? _pickerResultHost;

    private bool _rulerActive;
    private Point? _rulerStart;
    private bool _colorPickerActive;

    private DevToolsUi.DevToolsButton? _overdrawButton;
    private DevToolsUi.DevToolsButton? _dirtyRegionsButton;
    private DevToolsUi.DevToolsButton? _focusToggleButton;

    // Single toggle buttons — style flips between Primary (idle) and Danger (active).
    private DevToolsUi.DevToolsButton? _rulerToggleButton;
    private DevToolsUi.DevToolsButton? _pickerToggleButton;

    private UIElement BuildToolsTab()
    {
        var root = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase) };

        // Tab intro header
        root.Children.Add(new TextBlock
        {
            Text = DevToolsUi.Tracked("TOOLS"),
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterXS),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Interactive helpers for poking at the target window.",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterLg),
        });

        // ── Ruler / measure ──
        _rulerStatusText = DevToolsUi.Muted("Click two points inside the target window to measure distance.");
        _rulerResultHost = new Border { Visibility = Visibility.Collapsed };
        _rulerToggleButton = DevToolsUi.Button("Start ruler", ToggleRuler, DevToolsUi.ButtonStyle.Primary, icon: "▶");
        root.Children.Add(MakeToolCard(
            icon: "📏",
            title: DevToolsUi.Tracked("RULER · MEASURE"),
            description: "Measure pixel distance between two points on the target window. Hold Shift while picking the second point to snap to 0° / 45° / 90°.",
            accent: DevToolsTheme.Info,
            actions: new[] { _rulerToggleButton },
            resultHost: _rulerResultHost,
            status: _rulerStatusText));

        // ── Color picker ──
        _pickerStatusText = DevToolsUi.Muted("Click on the target window to sample the screen pixel at that position.");
        _pickerResultHost = new Border { Visibility = Visibility.Collapsed };
        _pickerToggleButton = DevToolsUi.Button("Pick color", ToggleColorPicker, DevToolsUi.ButtonStyle.Primary, icon: "▶");
        root.Children.Add(MakeToolCard(
            icon: "◉",
            title: DevToolsUi.Tracked("COLOR PICKER"),
            description: "Sample any on-screen pixel inside the target window; result is shown in status with the hex value.",
            accent: DevToolsTheme.TokenKeyword,
            actions: new[] { _pickerToggleButton },
            resultHost: _pickerResultHost,
            status: _pickerStatusText));

        // XAML export + Screenshot both live on the right-click context menu
        // inside the Inspector tree — no card in the Tools tab.
        _exportStatusText = null;
        _screenshotStatusText = null;

        // ── Render overlays ──
        _overdrawButton     = DevToolsUi.Toggle("Overdraw",      () => ToggleOverlayMode(RenderDiagnostics.OverlayMode.Overdraw), false, icon: "⚙");
        _dirtyRegionsButton = DevToolsUi.Toggle("Dirty regions", () => ToggleOverlayMode(RenderDiagnostics.OverlayMode.DirtyRegions), false, icon: "▤");
        var overlayHint = DevToolsUi.Muted("Requires native backends to call RenderDiagnostics.RecordDraw / RecordDirtyRegion.", DevToolsTheme.FontXS);
        root.Children.Add(MakeToolCard(
            icon: "▦",
            title: DevToolsUi.Tracked("RENDER OVERLAYS"),
            description: "Paint the target window with diagnostics — overdraw heatmap or dirty-region outlines.",
            accent: DevToolsTheme.Warning,
            actions: new UIElement[] { _overdrawButton, _dirtyRegionsButton },
            resultHost: null,
            status: overlayHint));

        // ── Focus visualization ──
        _focusToggleButton = DevToolsUi.Toggle("Focus overlay", ToggleFocusOverlay, false, icon: "◎");
        _focusStatusText = DevToolsUi.Muted("Highlights the currently focused element.");
        root.Children.Add(MakeToolCard(
            icon: "◎",
            title: DevToolsUi.Tracked("FOCUS VISUALIZATION"),
            description: "Draw a ring around whichever element currently owns keyboard focus.",
            accent: DevToolsTheme.Success,
            actions: new UIElement[] { _focusToggleButton },
            resultHost: null,
            status: _focusStatusText));

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            ClipToBounds = true,
        };
    }

    /// <summary>
    /// A polished tool card: left accent bar, circular icon chip, title + muted
    /// description, button row, an optional result-visualisation host (shown
    /// after the user acts), and a bottom status hint with an info glyph.
    /// </summary>
    private static Border MakeToolCard(string icon, string title, string description,
        SolidColorBrush accent, UIElement[] actions, Border? resultHost, TextBlock status)
    {
        // ── Icon chip ──
        var iconChip = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0x28, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = icon,
                FontSize = 18,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };

        // ── Title ──
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = DevToolsTheme.FontLg,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
        };

        // ── Description ──
        var descText = new TextBlock
        {
            Text = description,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            Margin = new Thickness(0, 2, 0, DevToolsTheme.GutterBase),
        };

        // ── Buttons ──
        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in actions) buttonsRow.Children.Add(b);

        // ── Status hint with info glyph ──
        status.Margin = new Thickness(0, 0, 0, 0);
        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, 0),
        };
        statusRow.Children.Add(new TextBlock
        {
            Text = "ⓘ",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        statusRow.Children.Add(status);

        // ── Body (title + description + buttons + result + status) ──
        var body = new StackPanel();
        body.Children.Add(titleText);
        body.Children.Add(descText);
        body.Children.Add(buttonsRow);
        if (resultHost != null)
        {
            resultHost.Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, 0);
            body.Children.Add(resultHost);
        }
        body.Children.Add(statusRow);

        // ── Two-column layout: icon chip | body ──
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(iconChip, 0);
        Grid.SetColumn(body, 1);
        grid.Children.Add(iconChip);
        grid.Children.Add(body);

        // Outer card with an accent bar on the left.
        return new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, DevToolsTheme.RadiusBase.TopRight, DevToolsTheme.RadiusBase.BottomRight, 0),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg),
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterBase),
            Child = grid,
        };
    }

    partial void OnToolsTabActivated()
    {
        // nothing persistent to refresh
    }

    private TextBlock _focusStatusText = new();

    // ── Ruler ────────────────────────────────────────────────────────────

    private void ToggleRuler()
    {
        if (_rulerActive) DeactivateRuler();
        else ActivateRuler();
    }

    private void UpdateRulerButton()
    {
        if (_rulerToggleButton == null) return;
        if (_rulerActive)
        {
            _rulerToggleButton.Label = "Stop ruler";
            _rulerToggleButton.SetIcon("■");
            _rulerToggleButton.Style = DevToolsUi.ButtonStyle.Danger;
        }
        else
        {
            _rulerToggleButton.Label = "Start ruler";
            _rulerToggleButton.SetIcon("▶");
            _rulerToggleButton.Style = DevToolsUi.ButtonStyle.Primary;
        }
    }

    private void ActivateRuler()
    {
        if (_rulerActive) return;
        _rulerActive = true;
        _rulerStart = null;
        _targetWindow.PreviewMouseDown += OnRulerTargetMouseDown;
        _targetWindow.PreviewMouseMove += OnRulerTargetMouseMove;
        _overlay?.BeginRuler();
        if (_rulerResultHost != null) _rulerResultHost.Visibility = Visibility.Collapsed;
        if (_rulerStatusText != null)
            _rulerStatusText.Text = "Ruler: click first point, move to preview, click again to commit.";
        UpdateRulerButton();
    }

    private void DeactivateRuler()
    {
        if (!_rulerActive) return;
        _rulerActive = false;
        _targetWindow.PreviewMouseDown -= OnRulerTargetMouseDown;
        _targetWindow.PreviewMouseMove -= OnRulerTargetMouseMove;
        _overlay?.EndRuler();
        if (_rulerStatusText != null)
            _rulerStatusText.Text = "Ruler stopped.";
        UpdateRulerButton();
    }

    private void OnRulerTargetMouseMove(object? sender, RoutedEventArgs e)
    {
        if (e is not Input.MouseEventArgs me) return;
        if (_overlay == null || _rulerStart == null) return;
        // Live preview while the user hunts for the second point.
        bool shift = (me.KeyboardModifiers & Input.ModifierKeys.Shift) != 0;
        var pt = shift ? SnapRulerToAxis(_rulerStart.Value, me.Position) : me.Position;
        _overlay.SetRulerPreviewEnd(pt);
    }

    private void OnRulerTargetMouseDown(object? sender, RoutedEventArgs e)
    {
        if (e is not Input.MouseButtonEventArgs me) return;
        if (me.ChangedButton != Input.MouseButton.Left) return;
        var rawPt = me.Position;
        bool shift = (me.KeyboardModifiers & Input.ModifierKeys.Shift) != 0;

        if (_rulerStart == null || _overlay?.RulerEndCommitted == true)
        {
            // First click of a new measurement — start fresh.
            _rulerStart = rawPt;
            _overlay?.SetRulerStart(rawPt);
            if (_rulerResultHost != null) _rulerResultHost.Visibility = Visibility.Collapsed;
            if (_rulerStatusText != null)
                _rulerStatusText.Text = $"First point ({rawPt.X:F0}, {rawPt.Y:F0}). Hold Shift to constrain to 0°/45°/90°. Click again to commit.";
        }
        else
        {
            // Second click — commit the measurement (axis-snapped if Shift is held)
            // and keep it visible until the user clicks again to start a new one.
            var p0 = _rulerStart.Value;
            var pt = shift ? SnapRulerToAxis(p0, rawPt) : rawPt;
            double dx = pt.X - p0.X;
            double dy = pt.Y - p0.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            _overlay?.CommitRulerEnd(pt);
            if (_rulerStatusText != null)
                _rulerStatusText.Text = shift ? "Measurement committed · ⇧ snapped to axis." : "Measurement committed. Click again for a new one.";
            UpdateRulerResult(p0, pt, dx, dy, dist, angle);
        }
        me.Handled = true;
    }

    private void UpdateRulerResult(Point p0, Point p1, double dx, double dy, double dist, double angleDeg)
    {
        if (_rulerResultHost == null) return;

        // ── Big distance number ──
        var distRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        distRow.Children.Add(new TextBlock
        {
            Text = dist.ToString("F1"),
            FontSize = 28,
            FontFamily = DevToolsTheme.MonoFont,
            FontWeight = FontWeights.Bold,
            Foreground = DevToolsTheme.Info,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        distRow.Children.Add(new TextBlock
        {
            Text = "px",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(4, 0, 0, 6),
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        // ── Metric chips: Δx · Δy · angle ──
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        chips.Children.Add(MakeMetricChip("Δx", $"{dx:F0}", DevToolsTheme.Info));
        chips.Children.Add(MakeMetricChip("Δy", $"{dy:F0}", DevToolsTheme.Info));
        chips.Children.Add(MakeMetricChip("∠", $"{angleDeg:F1}°", DevToolsTheme.Accent));

        // ── Coordinate line ──
        var coordText = new TextBlock
        {
            Text = $"({p0.X:F0}, {p0.Y:F0})  →  ({p1.X:F0}, {p1.Y:F0})",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0),
        };

        var body = new StackPanel();
        body.Children.Add(distRow);
        body.Children.Add(chips);
        body.Children.Add(coordText);

        _rulerResultHost.Background = new SolidColorBrush(Color.FromArgb(
            0x18, DevToolsTheme.InfoColor.R, DevToolsTheme.InfoColor.G, DevToolsTheme.InfoColor.B));
        _rulerResultHost.BorderBrush = new SolidColorBrush(Color.FromArgb(
            0x70, DevToolsTheme.InfoColor.R, DevToolsTheme.InfoColor.G, DevToolsTheme.InfoColor.B));
        _rulerResultHost.BorderThickness = new Thickness(1);
        _rulerResultHost.CornerRadius = DevToolsTheme.RadiusBase;
        _rulerResultHost.Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
        _rulerResultHost.Child = body;
        _rulerResultHost.Visibility = Visibility.Visible;
    }

    private static Border MakeMetricChip(string label, string value, SolidColorBrush accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.MonoFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(
                0x24, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x60, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(DevToolsTheme.GutterBase, 2, DevToolsTheme.GutterBase, 2),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            Child = row,
        };
    }

    /// <summary>
    /// Snap a raw cursor position to the nearest 0° / 45° / 90° / 135° axis
    /// anchored at <paramref name="start"/>. The length of the vector is kept so
    /// that shift-drag produces a straight line in the expected direction.
    /// </summary>
    private static Point SnapRulerToAxis(Point start, Point raw)
    {
        double dx = raw.X - start.X;
        double dy = raw.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.5) return start;

        // Bucket the angle into 45° steps.
        const double step = Math.PI / 4;
        double angle = Math.Atan2(dy, dx);
        double snapped = Math.Round(angle / step) * step;
        return new Point(
            start.X + len * Math.Cos(snapped),
            start.Y + len * Math.Sin(snapped));
    }

    // ── Color picker ─────────────────────────────────────────────────────

    private void ToggleColorPicker()
    {
        if (_colorPickerActive) DeactivateColorPicker();
        else ActivateColorPicker();
    }

    private void UpdatePickerButton()
    {
        if (_pickerToggleButton == null) return;
        if (_colorPickerActive)
        {
            _pickerToggleButton.Label = "Stop picker";
            _pickerToggleButton.SetIcon("■");
            _pickerToggleButton.Style = DevToolsUi.ButtonStyle.Danger;
        }
        else
        {
            _pickerToggleButton.Label = "Pick color";
            _pickerToggleButton.SetIcon("▶");
            _pickerToggleButton.Style = DevToolsUi.ButtonStyle.Primary;
        }
    }

    private void ActivateColorPicker()
    {
        if (_colorPickerActive) return;
        _colorPickerActive = true;
        _targetWindow.PreviewMouseDown += OnColorPickerTargetMouseDown;
        if (_pickerResultHost != null) _pickerResultHost.Visibility = Visibility.Collapsed;
        if (_pickerStatusText != null)
            _pickerStatusText.Text = "Color picker: click anywhere on the target window…";
        UpdatePickerButton();
    }

    private void DeactivateColorPicker()
    {
        if (!_colorPickerActive) return;
        _colorPickerActive = false;
        _targetWindow.PreviewMouseDown -= OnColorPickerTargetMouseDown;
        if (_pickerStatusText != null)
            _pickerStatusText.Text = "Color picker stopped.";
        UpdatePickerButton();
    }

    private void OnColorPickerTargetMouseDown(object? sender, RoutedEventArgs e)
    {
        if (e is not Input.MouseButtonEventArgs me) return;
        if (me.ChangedButton != Input.MouseButton.Left) return;
        try
        {
            var color = PickScreenPixel(me.Position);
            if (color.HasValue)
            {
                if (_pickerStatusText != null)
                    _pickerStatusText.Text = $"Sampled pixel at ({me.Position.X:F0}, {me.Position.Y:F0}). Click again to sample a different pixel.";
                UpdatePickerResult(color.Value, me.Position);
            }
            else
            {
                if (_pickerStatusText != null)
                    _pickerStatusText.Text = "Color picker: sampling failed (platform not supported).";
            }
        }
        catch (Exception ex)
        {
            if (_pickerStatusText != null)
                _pickerStatusText.Text = $"Color picker error: {ex.Message}";
        }
        finally
        {
            me.Handled = true;
        }
    }

    private void UpdatePickerResult(Color color, Point samplePoint)
    {
        if (_pickerResultHost == null) return;

        string hex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        var (h, s, l) = ColorToHsl(color);

        // ── Big swatch ──
        var swatch = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = DevToolsTheme.RadiusBase,
            Background = new SolidColorBrush(color),
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
        };

        // ── Right-side details ──
        var details = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterLg, 0, 0, 0) };

        // HEX (big)
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        hexRow.Children.Add(new TextBlock
        {
            Text = hex,
            FontSize = 20,
            FontFamily = DevToolsTheme.MonoFont,
            FontWeight = FontWeights.Bold,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        hexRow.Children.Add(new TextBlock
        {
            Text = "  HEX",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(DevToolsTheme.GutterSm, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        details.Children.Add(hexRow);

        // Channel chips: R G B A
        var channels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0) };
        channels.Children.Add(MakeChannelChip("R", color.R, DevToolsTheme.Error));
        channels.Children.Add(MakeChannelChip("G", color.G, DevToolsTheme.Success));
        channels.Children.Add(MakeChannelChip("B", color.B, DevToolsTheme.Info));
        channels.Children.Add(MakeChannelChip("A", color.A, DevToolsTheme.TextMuted));
        details.Children.Add(channels);

        // HSL line
        details.Children.Add(new TextBlock
        {
            Text = $"HSL  {h:F0}° · {s * 100:F0}% · {l * 100:F0}%",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextSecondary,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0),
        });

        // Source pixel coordinate (small)
        details.Children.Add(new TextBlock
        {
            Text = $"at ({samplePoint.X:F0}, {samplePoint.Y:F0})",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 2, 0, 0),
        });

        // Copy HEX button
        var copyBtn = DevToolsUi.Button("Copy HEX", () =>
        {
            try { Clipboard.SetText(hex); } catch { /* clipboard may not be available */ }
        }, icon: "⧉");
        copyBtn.Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0);
        details.Children.Add(copyBtn);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(swatch, 0);
        Grid.SetColumn(details, 1);
        grid.Children.Add(swatch);
        grid.Children.Add(details);

        var accent = DevToolsTheme.TokenKeyword;
        _pickerResultHost.Background = new SolidColorBrush(Color.FromArgb(
            0x18, accent.Color.R, accent.Color.G, accent.Color.B));
        _pickerResultHost.BorderBrush = new SolidColorBrush(Color.FromArgb(
            0x70, accent.Color.R, accent.Color.G, accent.Color.B));
        _pickerResultHost.BorderThickness = new Thickness(1);
        _pickerResultHost.CornerRadius = DevToolsTheme.RadiusBase;
        _pickerResultHost.Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
        _pickerResultHost.Child = grid;
        _pickerResultHost.Visibility = Visibility.Visible;
    }

    private static Border MakeChannelChip(string label, byte value, SolidColorBrush accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = value.ToString(),
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(
                0x22, accent.Color.R, accent.Color.G, accent.Color.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(DevToolsTheme.GutterBase, 2, DevToolsTheme.GutterBase, 2),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            Child = row,
        };
    }

    private static (double H, double S, double L) ColorToHsl(Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        double h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return (h, s, l);
    }

    private Color? PickScreenPixel(Point windowPoint)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var hwnd = _targetWindow.Handle;
        if (hwnd == nint.Zero) return null;
        var pt = new POINT { X = (int)windowPoint.X, Y = (int)windowPoint.Y };
        ClientToScreen(hwnd, ref pt);
        var dc = GetDC(nint.Zero);
        if (dc == nint.Zero) return null;
        try
        {
            uint rgb = GetPixel(dc, pt.X, pt.Y);
            byte r = (byte)(rgb & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)((rgb >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }
        finally
        {
            ReleaseDC(nint.Zero, dc);
        }
    }

    // ── XAML export ──────────────────────────────────────────────────────

    // Export / screenshot handlers live in DevToolsWindow.ElementContextMenu.cs —
    // they are invoked from the right-click menu on Inspector / Logical nodes.

    private static string BuildXamlFromVisual(Visual visual, bool recurse, int depth)
    {
        var sb = new StringBuilder();
        WriteXaml(visual, recurse, depth, sb);
        return sb.ToString();
    }

    private static void WriteXaml(Visual visual, bool recurse, int depth, StringBuilder sb)
    {
        string indent = new string(' ', depth * 2);
        string typeName = visual.GetType().Name;
        sb.Append(indent).Append('<').Append(typeName);

        if (visual is FrameworkElement fe)
        {
            if (!string.IsNullOrEmpty(fe.Name))
                sb.Append(" Name=\"").Append(EscapeXml(fe.Name)).Append("\"");
            AppendAttrIfSet(sb, fe, FrameworkElement.WidthProperty, "Width", v => v is double d && !double.IsNaN(d));
            AppendAttrIfSet(sb, fe, FrameworkElement.HeightProperty, "Height", v => v is double d && !double.IsNaN(d));
            AppendAttrIfSet(sb, fe, FrameworkElement.MarginProperty, "Margin", v => v is Thickness t && (t.Left != 0 || t.Top != 0 || t.Right != 0 || t.Bottom != 0));
            AppendAttrIfSet(sb, fe, FrameworkElement.HorizontalAlignmentProperty, "HorizontalAlignment", v => v is HorizontalAlignment ha && ha != HorizontalAlignment.Stretch);
            AppendAttrIfSet(sb, fe, FrameworkElement.VerticalAlignmentProperty, "VerticalAlignment", v => v is VerticalAlignment va && va != VerticalAlignment.Stretch);
        }

        var children = GetRenderableChildren(visual);
        bool hasChildren = recurse && children.Count > 0;
        if (!hasChildren)
        {
            sb.AppendLine(" />");
            return;
        }
        sb.AppendLine(">");
        foreach (var child in children)
            WriteXaml(child, recurse, depth + 1, sb);
        sb.Append(indent).Append("</").Append(typeName).AppendLine(">");
    }

    private static List<Visual> GetRenderableChildren(Visual visual)
    {
        var list = new List<Visual>();
        for (int i = 0; i < visual.VisualChildrenCount; i++)
        {
            if (visual.GetVisualChild(i) is Visual c)
                list.Add(c);
        }
        return list;
    }

    private static void AppendAttrIfSet(StringBuilder sb, DependencyObject dObj, DependencyProperty dp, string attrName, Func<object?, bool> isInteresting)
    {
        try
        {
            var source = DependencyPropertyHelper.GetValueSource(dObj, dp);
            if (source.BaseValueSource != BaseValueSource.Local) return;
            var val = dObj.GetValue(dp);
            if (val == null || !isInteresting(val)) return;
            sb.Append(' ').Append(attrName).Append("=\"").Append(EscapeXml(val.ToString() ?? "")).Append("\"");
        }
        catch
        {
            // Skip attributes whose getter throws.
        }
    }

    private static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    // ── Render overlay toggles ───────────────────────────────────────────

    private void ToggleOverlayMode(RenderDiagnostics.OverlayMode mode)
    {
        RenderDiagnostics.Mode = RenderDiagnostics.Mode == mode
            ? RenderDiagnostics.OverlayMode.None
            : mode;
        if (_overdrawButton != null)
            _overdrawButton.IsActive = RenderDiagnostics.Mode == RenderDiagnostics.OverlayMode.Overdraw;
        if (_dirtyRegionsButton != null)
            _dirtyRegionsButton.IsActive = RenderDiagnostics.Mode == RenderDiagnostics.OverlayMode.DirtyRegions;
        _targetWindow.RequestFullInvalidation();
        _targetWindow.InvalidateWindow();
    }

    // ── Focus overlay ────────────────────────────────────────────────────

    private bool _focusOverlayEnabled;
    private Threading.DispatcherTimer? _focusOverlayTimer;

    private void ToggleFocusOverlay()
    {
        _focusOverlayEnabled = !_focusOverlayEnabled;
        if (_focusToggleButton != null)
            _focusToggleButton.IsActive = _focusOverlayEnabled;
        if (_focusStatusText != null)
            _focusStatusText.Text = _focusOverlayEnabled
                ? "Focus overlay: highlighting the focused element each frame."
                : "Highlights the currently focused element.";
        if (_focusOverlayEnabled)
        {
            if (_focusOverlayTimer == null)
            {
                _focusOverlayTimer = new Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _focusOverlayTimer.Tick += (_, _) => UpdateFocusOverlay();
                _focusOverlayTimer.Start();
            }
            UpdateFocusOverlay();
        }
        else
        {
            _focusOverlayTimer?.Stop();
            _focusOverlayTimer = null;
            _overlay?.HighlightElement(null);
        }
    }

    private void UpdateFocusOverlay()
    {
        try
        {
            var focused = FocusService.FocusedElement as UIElement;
            _overlay?.HighlightElement(focused);
        }
        catch { /* ignore */ }
    }

    // ── Win32 P/Invoke (screenshot + color picker) ────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint hdc, nint hbmp, uint start, uint cLines, byte[]? bits, ref BITMAPINFO lpbi, uint usage);

    private static byte[] CaptureHwndPixels(nint hwnd, int width, int height)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

        nint windowDc = GetDC(hwnd);
        nint memDc = CreateCompatibleDC(windowDc);
        nint bitmap = CreateCompatibleBitmap(windowDc, width, height);
        nint oldBmp = SelectObject(memDc, bitmap);
        try
        {
            // PrintWindow with PW_RENDERFULLCONTENT (0x2) grabs DirectComposition / GPU content.
            PrintWindow(hwnd, memDc, 0x2);
            var bi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top-down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };
            byte[] pixels = new byte[width * height * 4];
            GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref bi, 0);
            return pixels;
        }
        finally
        {
            SelectObject(memDc, oldBmp);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(hwnd, windowDc);
        }
    }

    private static void WritePngFromBgra(string path, byte[] bgra, int width, int height)
    {
        // Jalium's PngBitmapEncoder does not yet emit bytes, so we write a minimal PNG
        // (signature + IHDR + single IDAT + IEND) here. Pixels arrive as BGRA; PNG wants RGBA.
        using var fs = File.Create(path);
        Span<byte> sig = stackalloc byte[8] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        fs.Write(sig);

        // IHDR
        Span<byte> ihdr = stackalloc byte[13];
        WriteUInt32BE(ihdr, 0, (uint)width);
        WriteUInt32BE(ihdr, 4, (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WritePngChunk(fs, "IHDR", ihdr);

        // IDAT — raw filter=0 per scanline, then zlib (adler32 + deflate)
        int rowBytes = width * 4;
        byte[] rawWithFilters = new byte[(rowBytes + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int srcOffset = y * rowBytes;
            int dstOffset = y * (rowBytes + 1);
            rawWithFilters[dstOffset] = 0;
            for (int x = 0; x < width; x++)
            {
                int srcPx = srcOffset + x * 4;
                int dstPx = dstOffset + 1 + x * 4;
                // BGRA → RGBA, un-premultiply not needed: source is either BGRA from GetDIBits (already straight)
                // or Pbgra32 which we treat as straight BGRA for a screenshot visualisation.
                rawWithFilters[dstPx + 0] = bgra[srcPx + 2];
                rawWithFilters[dstPx + 1] = bgra[srcPx + 1];
                rawWithFilters[dstPx + 2] = bgra[srcPx + 0];
                rawWithFilters[dstPx + 3] = bgra[srcPx + 3];
            }
        }

        using var zlibStream = new MemoryStream();
        WriteZlib(zlibStream, rawWithFilters);
        WritePngChunk(fs, "IDAT", zlibStream.ToArray());

        WritePngChunk(fs, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteZlib(Stream output, byte[] raw)
    {
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }
        uint adler = Adler32(raw);
        Span<byte> a = stackalloc byte[4];
        WriteUInt32BE(a, 0, adler);
        output.Write(a);
    }

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }

    private static void WritePngChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteUInt32BE(len, 0, (uint)data.Length);
        s.Write(len);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        s.Write(typeBytes);

        s.Write(data);

        uint crc = Crc32(typeBytes, data);
        Span<byte> crcSpan = stackalloc byte[4];
        WriteUInt32BE(crcSpan, 0, crc);
        s.Write(crcSpan);
    }

    private static readonly uint[] s_crcTable = CreateCrcTable();

    private static uint[] CreateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFF;
        for (int i = 0; i < a.Length; i++)
            c = s_crcTable[(c ^ a[i]) & 0xFF] ^ (c >> 8);
        for (int i = 0; i < b.Length; i++)
            c = s_crcTable[(c ^ b[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static void WriteUInt32BE(Span<byte> buffer, int offset, uint value)
    {
        buffer[offset + 0] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }
}
