using Jalium.UI.Diagnostics;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private TextBlock? _perfBackendText;
    private TextBlock? _perfEngineText;
    private TextBlock? _perfAdapterText;
    private TextBlock? _perfFpsText;
    private TextBlock? _perfFpsHero;
    private TextBlock? _perfFpsSub;
    private StackPanel? _perfGpuPanel;
    private ScrollViewer? _perfGpuScroll;
    private StackPanel? _perfApiPanel;
    private ScrollViewer? _perfApiScroll;
    private Border? _perfGraphHost;
    private DispatcherTimer? _perfRefreshTimer;
    private Image? _perfGraphImage;
    private DevToolsUi.DevToolsButton? _perfEngineAuto;
    private DevToolsUi.DevToolsButton? _perfEngineVello;
    private DevToolsUi.DevToolsButton? _perfEngineImpeller;

    private UIElement BuildPerfTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ──
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };

        _perfBackendText = DevToolsUi.Text("Backend: ?", DevToolsTheme.FontSm, DevToolsTheme.TextPrimary);
        _perfBackendText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfBackendText);

        _perfEngineText = DevToolsUi.Text("Engine: ?", DevToolsTheme.FontSm, DevToolsTheme.TextPrimary);
        _perfEngineText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfEngineText);

        // Adapter: the GPU DXGI actually picked. When iGPU on a hybrid laptop
        // looks slow, the usual culprit isn't iGPU performance — it's that
        // DXGI fell back to WARP (Microsoft Basic Render Driver, CPU
        // software GPU) because the discrete GPU was disabled in Device
        // Manager and the iGPU wasn't visible to DXGI from the active
        // session. This line surfaces the truth: if AdapterType reads
        // "Software", that explains the 30 FPS + input-lag pattern entirely.
        _perfAdapterText = DevToolsUi.Text("Adapter: ?", DevToolsTheme.FontSm, DevToolsTheme.TextPrimary);
        _perfAdapterText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfAdapterText);

        _perfFpsText = DevToolsUi.Text("FPS —", DevToolsTheme.FontSm, DevToolsTheme.Accent, weight: FontWeights.SemiBold);
        _perfFpsText.FontFamily = DevToolsTheme.MonoFont;
        _perfFpsText.Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0);
        toolbar.Children.Add(_perfFpsText);

        toolbar.Children.Add(DevToolsUi.VerticalDivider());
        toolbar.Children.Add(DevToolsUi.Muted("Engine:"));
        _perfEngineAuto     = DevToolsUi.Toggle("Auto",     () => SwitchEngine(RenderingEngine.Auto),     false);
        _perfEngineVello    = DevToolsUi.Toggle("Vello",    () => SwitchEngine(RenderingEngine.Vello),    false);
        _perfEngineImpeller = DevToolsUi.Toggle("Impeller", () => SwitchEngine(RenderingEngine.Impeller), false);
        toolbar.Children.Add(_perfEngineAuto);
        toolbar.Children.Add(_perfEngineVello);
        toolbar.Children.Add(_perfEngineImpeller);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Frame graph ──
        _perfGraphImage = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        // Hero readout floated over the "scope screen": a large amber FPS figure
        // with a small unit + average sub-line, like an instrument's primary gauge.
        _perfFpsHero = new TextBlock
        {
            Text = "—",
            FontFamily = DevToolsTheme.DisplayFontLight,
            FontSize = DevToolsTheme.FontHero,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var heroMeta = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(DevToolsTheme.GutterSm + 2, 0, 0, 0),
        };
        heroMeta.Children.Add(DevToolsUi.Eyebrow("FPS"));
        _perfFpsSub = DevToolsUi.Mono("— ms", DevToolsTheme.FontXS, DevToolsTheme.TextMuted);
        heroMeta.Children.Add(_perfFpsSub);
        var heroRow = new StackPanel { Orientation = Orientation.Horizontal };
        heroRow.Children.Add(_perfFpsHero);
        heroRow.Children.Add(heroMeta);
        var heroBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, DevToolsTheme.ChromeColor.R, DevToolsTheme.ChromeColor.G, DevToolsTheme.ChromeColor.B)),
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterSm, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, 0, 0),
            Child = heroRow,
        };

        var scope = new Grid();
        scope.Children.Add(_perfGraphImage);
        scope.Children.Add(heroBadge);
        var scopeFramed = DevToolsUi.CornerTicks(scope);
        scopeFramed.Margin = new Thickness(DevToolsTheme.GutterXS + 1);

        _perfGraphHost = new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            Child = scopeFramed,
            ClipToBounds = true,
        };
        Grid.SetRow(_perfGraphHost, 1);
        root.Children.Add(_perfGraphHost);

        // ── Legend ──
        var legendRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
        };
        legendRow.Children.Add(MakeLegendChip(PerfColorLayout, "Layout"));
        legendRow.Children.Add(MakeLegendChip(PerfColorRender, "Render"));
        legendRow.Children.Add(MakeLegendChip(PerfColorPresent, "Present"));
        legendRow.Children.Add(DevToolsUi.Muted("  · red dashed = 16 ms budget", DevToolsTheme.FontXS));
        Grid.SetRow(legendRow, 2);
        root.Children.Add(legendRow);

        // ── Bottom panel: GPU snapshot (left, fixed-ish) +
        //     per-API call counters / timing (right, fills remainder) ──
        var bottomGrid = new Grid();
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 220 });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

        _perfGpuPanel = new StackPanel();
        _perfGpuPanel.Children.Add(DevToolsUi.Muted("(no snapshot published)"));
        _perfGpuScroll = new ScrollViewer { Content = _perfGpuPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var gpuCard = DevToolsUi.Panel("GPU Snapshot", _perfGpuScroll, DevToolsTheme.Success);
        gpuCard.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, DevToolsTheme.GutterBase);
        Grid.SetColumn(gpuCard, 0);
        bottomGrid.Children.Add(gpuCard);

        // Draggable divider so the GPU meters can be widened past the default 280px.
        var perfSplitter = new GridSplitter
        {
            Width = 6,
            Background = DevToolsTheme.BorderSubtle,
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(perfSplitter, 1);
        bottomGrid.Children.Add(perfSplitter);

        _perfApiPanel = new StackPanel();
        _perfApiPanel.Children.Add(DevToolsUi.Muted("(waiting for first frame)"));
        _perfApiScroll = new ScrollViewer { Content = _perfApiPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var apiCard = DevToolsUi.Panel("Draw API", _perfApiScroll, DevToolsTheme.Accent);
        apiCard.Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase);
        Grid.SetColumn(apiCard, 2);
        bottomGrid.Children.Add(apiCard);

        Grid.SetRow(bottomGrid, 3);
        root.Children.Add(bottomGrid);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    private static StackPanel MakeLegendChip(Color color, string label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0),
        };
        row.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    partial void OnPerfTabActivated()
    {
        // Turn on per-frame draw-API counters while the Perf tab is visible.
        // Window.RenderFrame's end-of-frame hook publishes them, RefreshPerfStats
        // renders the snapshot. The flag is the only thing gating the recording
        // overhead — outside DevTools we pay nothing.
        RenderDiagnostics.ApiStatsEnabled = true;
        RefreshPerfStats();
        if (_perfRefreshTimer == null)
        {
            _perfRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _perfRefreshTimer.Tick += (_, _) => RefreshPerfStats();
            _perfRefreshTimer.Start();
        }
    }

    private void SwitchEngine(RenderingEngine engine)
    {
        try
        {
            _targetWindow.SetRenderingEngineOverride(engine);
            RefreshPerfStats();
        }
        catch (Exception ex)
        {
            if (_perfEngineText != null)
            {
                _perfEngineText.Text = $"Engine: switch failed — {ex.Message}";
                _perfEngineText.Foreground = DevToolsTheme.Error;
            }
        }
    }

    private void RefreshPerfStats()
    {
        if (_perfBackendText == null) return;
        _perfBackendText.Text = $"Backend  {_targetWindow.CurrentRenderBackend}";
        if (_perfEngineText != null)
        {
            _perfEngineText.Text = $"Engine  {_targetWindow.CurrentRenderingEngine}";
            _perfEngineText.Foreground = DevToolsTheme.TextPrimary;
        }
        if (_perfAdapterText != null)
        {
            // Cheap to call once per perf refresh — context caches the adapter
            // description struct, no DXGI re-enumeration. If the host context
            // is gone or the backend lacks adapter info, leave the placeholder.
            var ctx = Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
            var adapter = ctx?.GetAdapterInfo();
            if (adapter is { } info)
            {
                string typeTag = info.AdapterType switch
                {
                    Jalium.UI.Interop.GpuAdapterType.Discrete => "Discrete",
                    Jalium.UI.Interop.GpuAdapterType.Integrated => "iGPU",
                    Jalium.UI.Interop.GpuAdapterType.Software => "Software/WARP",
                    _ => "?",
                };
                _perfAdapterText.Text = $"Adapter  {typeTag}: {info.Name}";
                // Red-flag the WARP fallback: this is the "iGPU feels slow"
                // pattern that turns out to be a CPU software renderer.
                // Discrete GPU disabled in Device Manager → DXGI walks to
                // Microsoft Basic Render Driver → 30 FPS + input lag.
                _perfAdapterText.Foreground = info.AdapterType == Jalium.UI.Interop.GpuAdapterType.Software
                    ? DevToolsTheme.Error
                    : DevToolsTheme.TextPrimary;
            }
            else
            {
                _perfAdapterText.Text = "Adapter  —";
                _perfAdapterText.Foreground = DevToolsTheme.TextMuted;
            }
        }

        var currentEngine = _targetWindow.CurrentRenderingEngine;
        if (_perfEngineAuto != null)     _perfEngineAuto.IsActive     = currentEngine == RenderingEngine.Auto;
        if (_perfEngineVello != null)    _perfEngineVello.IsActive    = currentEngine == RenderingEngine.Vello;
        if (_perfEngineImpeller != null) _perfEngineImpeller.IsActive = currentEngine == RenderingEngine.Impeller;

        var history = _targetWindow.FrameHistory;
        var buffer = new FrameHistory.Sample[FrameHistory.Capacity];
        int count = history.CopyTo(buffer);
        if (count > 0 && _perfFpsText != null)
        {
            double totalMs = 0;
            double worst = 0;
            for (int i = 0; i < count; i++)
            {
                totalMs += buffer[i].TotalMs;
                if (buffer[i].TotalMs > worst) worst = buffer[i].TotalMs;
            }
            double avg = totalMs / count;
            double fps = avg > 0 ? 1000.0 / avg : 0;
            _perfFpsText.Text = $"FPS {fps:F1}   avg {avg:F1} ms   worst {worst:F1} ms";
            _perfFpsText.Foreground = worst > 16
                ? DevToolsTheme.Warning
                : fps >= 55 ? DevToolsTheme.Success : DevToolsTheme.Accent;

            if (_perfFpsHero != null)
            {
                _perfFpsHero.Text = fps > 0 ? fps.ToString("F0") : "—";
                _perfFpsHero.Foreground = worst > 16
                    ? DevToolsTheme.Warning
                    : fps >= 55 ? DevToolsTheme.Success : DevToolsTheme.Accent;
            }
            if (_perfFpsSub != null) _perfFpsSub.Text = $"avg {avg:F1} ms";
        }
        else if (_perfFpsText != null)
        {
            _perfFpsText.Text = "FPS — (enable F3 HUD to collect samples)";
            _perfFpsText.Foreground = DevToolsTheme.TextMuted;
            if (_perfFpsHero != null) { _perfFpsHero.Text = "—"; _perfFpsHero.Foreground = DevToolsTheme.TextDisabled; }
            if (_perfFpsSub != null) _perfFpsSub.Text = "no samples";
        }

        if (_perfGraphImage != null)
            _perfGraphImage.Source = RenderPerfGraph(buffer.AsSpan(0, count));

        // GPU snapshot + Draw-API panels are rebuilt as real visualisations
        // (stacked bars + hit-rate meters + a data-bar table), not text blobs.
        RebuildGpuPanel(buffer, count);
        RebuildApiPanel();
    }

    // ── GPU snapshot panel (visualised) ──────────────────────────────────

    private static void GpuSection(StackPanel p, string title)
    {
        var e = DevToolsUi.Eyebrow(title);
        e.Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterSm);
        p.Children.Add(e);
    }

    // Colour a hit-rate meter by health: green good, amber so-so, red poor,
    // greyed when there is no traffic yet.
    private static void AddHitMeter(StackPanel p, string label, long hits, long total)
    {
        double frac = total > 0 ? (double)hits / total : 0;
        Brush fill = total == 0 ? DevToolsTheme.TextDisabled
            : frac >= 0.8 ? DevToolsTheme.Success
            : frac >= 0.4 ? DevToolsTheme.Warning
            : DevToolsTheme.Error;
        string val = total == 0 ? "—" : $"{hits}/{total} · {frac * 100:F0}%";
        p.Children.Add(DevToolsUi.MeterBar(label, frac, val, fill));
    }

    private static Brush BreakdownColor(string name) => name switch
    {
        "Path" => DevToolsTheme.Accent,
        "Text" => DevToolsTheme.Info,
        "SdfRect" => DevToolsTheme.Success,
        "Bitmap" => DevToolsTheme.TokenEnum,
        "Backdrop" => DevToolsTheme.TokenKeyword,
        "LiquidGlass" => DevToolsTheme.TokenType,
        _ => DevToolsTheme.TextMuted,
    };

    private void RebuildGpuPanel(FrameHistory.Sample[] buffer, int count)
    {
        if (_perfGpuPanel == null) return;
        var snap = RenderDiagnostics.LatestGpuSnapshot;
        double saved = _perfGpuScroll?.VerticalOffset ?? 0;
        var p = _perfGpuPanel;
        p.Children.Clear();

        if (snap == null)
        {
            p.Children.Add(DevToolsUi.Muted("(no snapshot — backend must call RenderDiagnostics.PublishGpuSnapshot once per frame)"));
            return;
        }

        // 1. Last-frame breakdown — Layout / Render / Present as a stacked bar.
        if (count > 0)
        {
            var last = buffer[count - 1];
            double apiMs = RenderDiagnostics.LatestDrawApiStats?.TotalMs ?? 0;
            // apiMs spans the whole frame's draw-API calls, including EndDraw
            // (which runs in the P segment after MarkRender) and the overlay's
            // own draws — so the managed-overhead gap must be measured against
            // R+P, not R alone. The old `RenderMs - apiMs` read -130 ms under a
            // slow compositor because EndDraw's Present stall lives in P.
            double gap = (last.RenderMs + last.PresentMs) - apiMs;
            GpuSection(p, "LAST FRAME");
            p.Children.Add(DevToolsUi.StackedBar(new (double Value, Brush Color)[]
            {
                (last.LayoutMs,  new SolidColorBrush(PerfColorLayout)),
                (last.RenderMs,  new SolidColorBrush(PerfColorRender)),
                (last.PresentMs, new SolidColorBrush(PerfColorPresent)),
            }));
            p.Children.Add(new Border { Height = DevToolsTheme.GutterSm });
            p.Children.Add(DevToolsUi.KeyValueRow("L / R / P", $"{last.LayoutMs:F1} / {last.RenderMs:F1} / {last.PresentMs:F1} ms"));
            p.Children.Add(DevToolsUi.KeyValueRow("Total frame", $"{last.TotalMs:F1} ms"));
            p.Children.Add(DevToolsUi.KeyValueRow("API / gap", $"{apiMs:F1} / {gap:F1} ms"));
        }

        // 2. GPU breakdown — the hero: hardware-timestamped split by category.
        var timing = RenderDiagnostics.LatestGpuTiming;
        if (timing != null && timing.Valid)
        {
            GpuSection(p, "GPU BREAKDOWN");
            p.Children.Add(DevToolsUi.KeyValueRow("Total GPU", $"{timing.TotalGpuMs:F1} ms · {timing.BatchCount} batches"));
            var cats = new (string Name, long Ns)[]
            {
                ("Path",        timing.PathNs),
                ("Text",        timing.TextNs),
                ("SdfRect",     timing.SdfRectNs),
                ("Bitmap",      timing.BitmapNs),
                ("Backdrop",    timing.BackdropNs),
                ("LiquidGlass", timing.LiquidGlassNs),
                ("Other",       timing.OtherNs),
            };
            Array.Sort(cats, (a, b) => b.Ns.CompareTo(a.Ns));
            double totMs = timing.TotalGpuMs > 0 ? timing.TotalGpuMs : 1.0;

            var segs = new List<(double Value, Brush Color)>();
            foreach (var c in cats)
                if (c.Ns > 0) segs.Add((c.Ns / 1_000_000.0, BreakdownColor(c.Name)));
            p.Children.Add(DevToolsUi.StackedBar(segs, 12));
            p.Children.Add(new Border { Height = DevToolsTheme.GutterSm });

            foreach (var c in cats)
            {
                if (c.Ns <= 0) continue;
                double ms = c.Ns / 1_000_000.0;
                double frac = ms / totMs;
                p.Children.Add(DevToolsUi.MeterBar(c.Name, frac, $"{ms:F1} ms · {frac * 100:F0}%", BreakdownColor(c.Name)));
            }
        }
        else if (timing != null)
        {
            GpuSection(p, "GPU BREAKDOWN");
            p.Children.Add(DevToolsUi.Muted("(waiting for first timestamped frame)"));
        }

        // 3. Text caches — hit-rate meters (instant red/amber/green health).
        var tc = RenderDiagnostics.LatestTextCacheStats;
        if (tc != null && (tc.DrawTextCalls > 0 || tc.LayoutHits + tc.LayoutMisses > 0))
        {
            GpuSection(p, "TEXT CACHES");
            AddHitMeter(p, "Layout", tc.LayoutHits, tc.LayoutHits + tc.LayoutMisses);
            AddHitMeter(p, "Instance", tc.InstanceHits, tc.InstanceHits + tc.InstanceMisses);
            AddHitMeter(p, "Glyph", tc.GlyphRasterHits, tc.GlyphRasterHits + tc.GlyphRasterMisses);
            p.Children.Add(DevToolsUi.KeyValueRow("DrawText / glyphs", $"{tc.DrawTextCalls} / {tc.EmittedGlyphs}"));
            if (tc.AtlasResets > 0)
                p.Children.Add(DevToolsUi.KeyValueRow("Atlas resets", tc.AtlasResets.ToString(), DevToolsTheme.Warning));
        }

        // 4. Path raster cache — hit-rate meters + CPU work.
        var pc = RenderDiagnostics.LatestPathCacheStats;
        if (pc != null)
        {
            long st = pc.StrokeHits + pc.StrokeMisses;
            long ft = pc.FillHits + pc.FillMisses;
            long gt = pc.GeometryHits + pc.GeometryMisses;
            if (st + ft + gt > 0)
            {
                GpuSection(p, "PATH RASTER CACHE");
                if (st > 0) AddHitMeter(p, "Stroke", pc.StrokeHits, st);
                if (ft > 0) AddHitMeter(p, "Fill", pc.FillHits, ft);
                if (gt > 0) AddHitMeter(p, "Geometry", pc.GeometryHits, gt);
                if (pc.FlattenNs > 0 || pc.TriangulateNs > 0)
                {
                    p.Children.Add(DevToolsUi.KeyValueRow("Flatten", $"{pc.FlattenNs / 1_000_000.0:F2} ms"));
                    p.Children.Add(DevToolsUi.KeyValueRow("Triangulate", $"{pc.TriangulateNs / 1_000_000.0:F2} ms"));
                }
            }
        }

        // 5. Retained-mode cache — replay% is the win.
        var rc = RenderDiagnostics.LatestRetainedCacheStats;
        if (rc != null && rc.Records + rc.Replays + rc.Bypasses > 0)
        {
            GpuSection(p, "RETAINED-MODE CACHE");
            AddHitMeter(p, "Replays", rc.Replays, rc.Records + rc.Replays + rc.Bypasses);
            p.Children.Add(DevToolsUi.KeyValueRow("Records / Bypass", $"{rc.Records} / {rc.Bypasses}"));
        }

        // 6. Memory — atlas fill meter + cache/texture sizes.
        GpuSection(p, "MEMORY");
        double atlasFrac = snap.GlyphAtlasSlotsTotal > 0 ? (double)snap.GlyphAtlasSlotsUsed / snap.GlyphAtlasSlotsTotal : 0;
        p.Children.Add(DevToolsUi.MeterBar("Glyph atlas", atlasFrac,
            $"{snap.GlyphAtlasSlotsUsed}/{snap.GlyphAtlasSlotsTotal} · {snap.GlyphAtlasBytes / (1024.0 * 1024.0):F1} MB",
            DevToolsTheme.Info));
        p.Children.Add(DevToolsUi.KeyValueRow("Path cache", $"{snap.PathCacheEntries} · {snap.PathCacheBytes / (1024.0 * 1024.0):F2} MB"));
        p.Children.Add(DevToolsUi.KeyValueRow("Textures", $"{snap.TextureCount} · {snap.TextureBytes / (1024.0 * 1024.0):F2} MB"));

        // 7. Frame pacing — scalar readouts.
        var pacing = RenderDiagnostics.LatestFramePacing;
        if (pacing != null)
        {
            GpuSection(p, "FRAME PACING");
            p.Children.Add(DevToolsUi.KeyValueRow("Begin attempts",
                pacing.BeginFailures == 0 ? $"{pacing.BeginAttempts} clean" : $"{pacing.BeginAttempts} +{pacing.BeginFailures} retries",
                pacing.BeginFailures == 0 ? DevToolsTheme.TextPrimary : DevToolsTheme.Warning));
            if (pacing.SwapBufferCount > 0)
            {
                p.Children.Add(DevToolsUi.KeyValueRow("Swap buffers", pacing.SwapBufferCount.ToString()));
                p.Children.Add(DevToolsUi.KeyValueRow("Waitable wait", $"{pacing.FrameWaitableWaitMs:F1} ms"));
                p.Children.Add(DevToolsUi.KeyValueRow("Fence wait", $"{pacing.FrameGpuWaitMs:F1} ms"));
            }
            if (pacing.LastFramePresentToReadyNs > 0)
                p.Children.Add(DevToolsUi.KeyValueRow("Present to ready", $"{pacing.LastFramePresentToReadyMs:F1} ms"));
        }

        // 8. Bitmap upload.
        var bmp = RenderDiagnostics.LatestBitmapUploadStats;
        if (bmp != null && (bmp.UploadCount > 0 || bmp.FastPathHits > 0))
        {
            GpuSection(p, "BITMAP UPLOAD");
            p.Children.Add(DevToolsUi.KeyValueRow("Uploads", $"{bmp.UploadCount} · {bmp.UploadBytes / (1024.0 * 1024.0):F2} MB"));
            AddHitMeter(p, "FastPath", bmp.FastPathHits, bmp.UploadCount + bmp.FastPathHits);
        }

        // 9. Layout pass.
        var lay = RenderDiagnostics.LatestLayoutPassStats;
        if (lay != null && (lay.MeasureCount > 0 || lay.ArrangeCount > 0))
        {
            GpuSection(p, "LAYOUT");
            p.Children.Add(DevToolsUi.KeyValueRow("Measure", $"{lay.MeasureCount} · {lay.MeasureMs:F1} ms"));
            p.Children.Add(DevToolsUi.KeyValueRow("Arrange", $"{lay.ArrangeCount} · {lay.ArrangeMs:F1} ms"));
            if (lay.Iterations > 1)
                p.Children.Add(DevToolsUi.KeyValueRow("Iterations", $"{lay.Iterations} (thrash)", DevToolsTheme.Warning));
        }

        if (_perfGpuScroll != null) _perfGpuScroll.ScrollToVerticalOffset(saved);
    }

    // ── Draw-API panel (visualised data-bar table) ───────────────────────

    private void RebuildApiPanel()
    {
        if (_perfApiPanel == null) return;
        var apiSnap = RenderDiagnostics.LatestDrawApiStats;
        double saved = _perfApiScroll?.VerticalOffset ?? 0;
        var p = _perfApiPanel;
        p.Children.Clear();

        if (apiSnap == null || apiSnap.Entries.Count == 0)
        {
            p.Children.Add(DevToolsUi.Muted("(waiting for next frame)"));
            return;
        }

        p.Children.Add(DevToolsUi.KeyValueRow($"frame {apiSnap.Timestamp:HH:mm:ss.fff}", $"{apiSnap.TotalMs:F2} ms total", DevToolsTheme.Accent));
        p.Children.Add(new Border { Height = DevToolsTheme.GutterSm });
        p.Children.Add(MakeApiHeaderRow());
        p.Children.Add(new Border { Height = 1, Background = DevToolsTheme.BorderSubtle, Margin = new Thickness(0, 2, 0, 3) });

        var entries = apiSnap.Entries.OrderByDescending(e => e.TotalMs).ToList();
        double maxMs = 0;
        foreach (var e in entries) if (e.TotalMs > maxMs) maxMs = e.TotalMs;
        if (maxMs <= 0) maxMs = 1;

        foreach (var e in entries)
        {
            double frac = e.TotalMs / maxMs;
            Brush msColor = frac >= 0.5 ? DevToolsTheme.Accent : frac >= 0.2 ? DevToolsTheme.Warning : DevToolsTheme.TextPrimary;
            p.Children.Add(MakeApiRow(e.Name, e.Count, e.TotalMs, e.AvgUs, frac, msColor));
        }

        if (_perfApiScroll != null) _perfApiScroll.ScrollToVerticalOffset(saved);
    }

    private static Grid ApiRowGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        return g;
    }

    private static UIElement MakeApiHeaderRow()
    {
        var g = ApiRowGrid();
        g.Children.Add(ColCaption("API", false, 0));
        g.Children.Add(ColCaption("CALLS", true, 1));
        g.Children.Add(ColCaption("MS", true, 2));
        g.Children.Add(ColCaption("AVG µS", true, 3));
        return g;
    }

    // Column caption — uppercase Bahnschrift but NOT letter-tracked, so it stays
    // inside the fixed numeric columns.
    private static TextBlock ColCaption(string text, bool right, int col)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(right ? 0 : 4, 0, right ? 6 : 0, 0),
        };
        Grid.SetColumn(t, col);
        return t;
    }

    private static UIElement MakeApiRow(string name, long calls, double ms, double avgUs, double frac, Brush msColor)
    {
        frac = Math.Clamp(double.IsNaN(frac) ? 0 : frac, 0, 1);

        // Background "data bar": proportional amber fill behind the row showing
        // relative total-ms cost, so the hottest API reads at a glance.
        var bg = new Grid();
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, frac), GridUnitType.Star) });
        bg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - frac), GridUnitType.Star) });
        var fill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x24, DevToolsTheme.AccentColor.R, DevToolsTheme.AccentColor.G, DevToolsTheme.AccentColor.B)),
            CornerRadius = DevToolsTheme.RadiusSm,
        };
        Grid.SetColumn(fill, 0);
        bg.Children.Add(fill);

        var content = ApiRowGrid();
        content.Children.Add(ApiText(name, false, DevToolsTheme.TextPrimary, 0));
        content.Children.Add(ApiText(calls.ToString(), true, DevToolsTheme.TextSecondary, 1));
        content.Children.Add(ApiText(ms.ToString("F2"), true, msColor, 2));
        content.Children.Add(ApiText(avgUs.ToString("F1"), true, DevToolsTheme.TextMuted, 3));

        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.Children.Add(bg);
        row.Children.Add(content);
        return row;
    }

    private static TextBlock ApiText(string text, bool right, Brush color, int col)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(right ? 0 : 4, 1, right ? 6 : 4, 1),
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static readonly Color PerfColorLayout  = DevToolsTheme.InfoColor;
    private static readonly Color PerfColorRender  = DevToolsTheme.AccentColor;
    private static readonly Color PerfColorPresent = DevToolsTheme.SuccessColor;
    private static readonly Color PerfColorBudget  = Color.FromArgb(0xA0, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B);

    // Reused across refreshes — ContentRevision bookkeeping (WriteableBitmap.cs)
    // triggers a native re-upload whenever we write, so we don't need to throw
    // away and re-allocate a new bitmap + native texture each tick.
    private WriteableBitmap? _perfGraphBitmap;
    private byte[]? _perfGraphPixels;

    private ImageSource? RenderPerfGraph(ReadOnlySpan<FrameHistory.Sample> samples)
    {
        // Size bitmap to the control's physical pixel dimensions — Image.Stretch.Fill
        // then becomes a 1:1 blit instead of a ~3× upscale, which is what was
        // turning our single-pixel-wide bars into a soft orange smear.
        double dipW = _perfGraphImage?.RenderSize.Width ?? 0;
        double dipH = _perfGraphImage?.RenderSize.Height ?? 0;
        // First layout pass may not have placed the image yet — fall back to the
        // host border's size.
        if (dipW <= 1 || dipH <= 1)
        {
            dipW = _perfGraphHost?.RenderSize.Width ?? 0;
            dipH = _perfGraphHost?.RenderSize.Height ?? 0;
        }
        // Scale to physical pixels so high-DPI monitors also render sharply.
        double dpiScale = GetDevicePixelScale();
        int width = Math.Max(160, (int)Math.Round(dipW * dpiScale));
        int height = Math.Max(80, (int)Math.Round(dipH * dpiScale));

        if (_perfGraphBitmap == null || _perfGraphBitmap.PixelWidth != width || _perfGraphBitmap.PixelHeight != height)
        {
            _perfGraphBitmap = new WriteableBitmap(width, height, 96, 96, Jalium.UI.Media.PixelFormats.Pbgra32, null);
            _perfGraphPixels = new byte[width * height * 4];
        }
        var bitmap = _perfGraphBitmap!;
        var pixels = _perfGraphPixels!;

        // Background uses Chrome color for consistency with toolbars/cards.
        byte bgB = DevToolsTheme.ChromeColor.B;
        byte bgG = DevToolsTheme.ChromeColor.G;
        byte bgR = DevToolsTheme.ChromeColor.R;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = bgB;
            pixels[i + 1] = bgG;
            pixels[i + 2] = bgR;
            pixels[i + 3] = 255;
        }

        // Horizontal gridlines every quarter of the max range.
        byte gridLum = DevToolsTheme.BorderSubtleColor.R;
        for (int gy = 1; gy <= 3; gy++)
        {
            int y = height * gy / 4;
            for (int x = 0; x < width; x += 2)
                WritePixel(pixels, width, x, y, Color.FromArgb(0x60, gridLum, gridLum, gridLum));
        }

        if (samples.Length > 0)
        {
            double maxMs = 16.0;
            foreach (var s in samples)
                if (s.TotalMs > maxMs) maxMs = s.TotalMs;
            maxMs = Math.Max(maxMs, 1.0);

            // Map each sample to a contiguous column range so a 300-sample buffer
            // still covers the full physical width instead of leaving most of it
            // as background.
            double colsPerSample = (double)width / Math.Max(samples.Length, 1);
            for (int i = 0; i < samples.Length; i++)
            {
                int xStart = (int)Math.Round(i * colsPerSample);
                int xEnd = (int)Math.Round((i + 1) * colsPerSample);
                if (xEnd <= xStart) xEnd = xStart + 1;
                if (xEnd > width) xEnd = width;

                var s = samples[i];
                int lY = height - 1 - (int)((s.LayoutMs / maxMs) * (height - 2));
                int rY = lY - (int)((s.RenderMs / maxMs) * (height - 2));
                int pY = rY - (int)((s.PresentMs / maxMs) * (height - 2));
                lY = Math.Clamp(lY, 1, height - 1);
                rY = Math.Clamp(rY, 1, height - 1);
                pY = Math.Clamp(pY, 1, height - 1);

                for (int x = xStart; x < xEnd; x++)
                {
                    for (int y = height - 1; y >= lY; y--) WritePixel(pixels, width, x, y, PerfColorLayout);
                    for (int y = lY - 1; y >= rY; y--) WritePixel(pixels, width, x, y, PerfColorRender);
                    for (int y = rY - 1; y >= pY; y--) WritePixel(pixels, width, x, y, PerfColorPresent);
                }
            }

            // 16 ms budget line (dashed) — scale dash gap with pixel width so it
            // remains visually dashed at all zoom levels.
            int budgetY = height - 1 - (int)((16.0 / maxMs) * (height - 2));
            budgetY = Math.Clamp(budgetY, 1, height - 1);
            int dashStride = Math.Max(4, (int)(6 * dpiScale));
            for (int x = 0; x < width; x += dashStride)
                WritePixel(pixels, width, x, budgetY, PerfColorBudget);
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return bitmap;
    }

    /// <summary>
    /// Device-to-DIP scale factor for the DevTools window. Falls back to 1.0
    /// when the window hasn't reported DPI yet.
    /// </summary>
    private double GetDevicePixelScale()
    {
        // DevToolsWindow inherits DPI state from the target window host. Use
        // the dispatcher-visible DpiScale if available, otherwise 1.0.
        try
        {
            double s = DpiScale;
            return s > 0.1 ? s : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private static void WritePixel(byte[] pixels, int width, int x, int y, Color c)
    {
        if ((uint)x >= (uint)width) return;
        int idx = (y * width + x) * 4;
        if ((uint)idx >= (uint)pixels.Length) return;
        pixels[idx + 0] = c.B;
        pixels[idx + 1] = c.G;
        pixels[idx + 2] = c.R;
        pixels[idx + 3] = c.A == 0 ? (byte)255 : c.A;
    }
}
