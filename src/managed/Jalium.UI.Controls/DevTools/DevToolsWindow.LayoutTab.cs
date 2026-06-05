using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private DevToolsUi.DevToolsButton? _layoutRecordButton;
    private DevToolsUi.DevToolsButton? _layoutStackTraceButton;
    private DevToolsUi.DevToolsButton? _layoutSortButton;
    private TextBlock? _layoutStatusText;
    private Border? _layoutStatusPill;
    private StackPanel? _layoutStatsPanel;
    private StackPanel? _invalidationPanel;
    private DispatcherTimer? _layoutRefreshTimer;
    private int _layoutSortMode; // 0 = Measure µs, 1 = Arrange µs, 2 = Invalidations

    // ── Row-reuse pools ─────────────────────────────────────────────────
    // Rebuilding every row each tick was causing hundreds of UIElement
    // allocations per second while recording, which pinned the dispatcher
    // and produced the "界面非常卡顿" symptom. We instead build a fixed
    // pool of row skins once, then update only their text / bar column
    // widths when data changes.
    private readonly List<LayoutStatsRowSkin> _statsRowPool = new();
    private readonly List<InvalidationRowSkin> _invRowPool = new();
    private TextBlock? _statsFooterText;
    private TextBlock? _statsEmptyText;
    private Border? _invCountPill;
    private TextBlock? _invEmptyText;

    private UIElement BuildLayoutTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ──
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };

        _layoutRecordButton = DevToolsUi.Button("Start recording", ToggleLayoutRecording, DevToolsUi.ButtonStyle.Primary, icon: "●");
        toolbar.Children.Add(_layoutRecordButton);

        toolbar.Children.Add(DevToolsUi.Button("Reset", () =>
        {
            LayoutDiagnostics.Reset();
            RefreshLayoutStats();
            RefreshInvalidations();
        }, icon: "↺"));

        _layoutStackTraceButton = DevToolsUi.Toggle("Capture stacks", () =>
        {
            LayoutDiagnostics.CaptureStackTraces = !LayoutDiagnostics.CaptureStackTraces;
            if (_layoutStackTraceButton != null)
                _layoutStackTraceButton.IsActive = LayoutDiagnostics.CaptureStackTraces;
        }, LayoutDiagnostics.CaptureStackTraces, icon: "☰");
        toolbar.Children.Add(_layoutStackTraceButton);

        toolbar.Children.Add(DevToolsUi.VerticalDivider());

        _layoutSortButton = DevToolsUi.Button("Sort: Measure µs", () =>
        {
            _layoutSortMode = (_layoutSortMode + 1) % 3;
            if (_layoutSortButton != null)
                _layoutSortButton.Label = _layoutSortMode switch
                {
                    0 => "Sort: Measure µs",
                    1 => "Sort: Arrange µs",
                    _ => "Sort: Invalidations",
                };
            RefreshLayoutStats();
        }, icon: "⇅");
        toolbar.Children.Add(_layoutSortButton);

        _layoutStatusPill = DevToolsUi.Pill("IDLE", DevToolsTheme.TextSecondary);
        toolbar.Children.Add(_layoutStatusPill);

        _layoutStatusText = DevToolsUi.Muted("Recording is off — start to capture per-element measure / arrange timings.");
        _layoutStatusText.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0);
        toolbar.Children.Add(_layoutStatusText);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Stats section ──
        _layoutStatsPanel = new StackPanel();
        BuildStatsHeader(_layoutStatsPanel);

        var statsScroll = new ScrollViewer
        {
            Content = _layoutStatsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var statsCard = DevToolsUi.Panel("TOP ELEMENTS BY LAYOUT COST", statsScroll, DevToolsTheme.Info);
        statsCard.Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm);
        Grid.SetRow(statsCard, 1);
        root.Children.Add(statsCard);

        // ── Invalidation log ──
        _invalidationPanel = new StackPanel();
        BuildInvalidationHeader(_invalidationPanel);

        var invScroll = new ScrollViewer
        {
            Content = _invalidationPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // Panel has no pill slot, so the title + live count pill are built by
        // hand here and pinned above the scroll inside a Card. The pill object
        // (_invCountPill) is the same instance RefreshInvalidations updates.
        var invHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm) };
        invHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        invHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var invEyebrow = DevToolsUi.Eyebrow("INVALIDATION TIMELINE", DevToolsTheme.TextSecondary);
        Grid.SetColumn(invEyebrow, 0);
        invHeaderRow.Children.Add(invEyebrow);
        Grid.SetColumn(_invCountPill!, 1);
        invHeaderRow.Children.Add(_invCountPill!);

        var invHeader = new StackPanel { Orientation = Orientation.Vertical };
        invHeader.Children.Add(invHeaderRow);
        invHeader.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm),
        });

        // Header in an Auto row, scroll in a Star row so the timeline list is
        // height-bounded and actually scrolls (shows its scrollbar).
        var invStack = new Grid();
        invStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        invStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(invHeader, 0);
        Grid.SetRow(invScroll, 1);
        invStack.Children.Add(invHeader);
        invStack.Children.Add(invScroll);

        var invCard = DevToolsUi.Card(invStack);
        invCard.Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase);
        Grid.SetRow(invCard, 2);
        root.Children.Add(invCard);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    private void BuildStatsHeader(StackPanel panel)
    {
        // The section title now lives in the enclosing Panel header. Here we add
        // a hairline-underlined column-caption strip naming the per-row metrics,
        // then the empty / footer placeholders the refresh path toggles.
        // Inset = a stat row's outer GutterLg margin + inner GutterLg card padding
        // so the "#" caption sits over the rank chip.
        double statInset = DevToolsTheme.GutterLg + DevToolsTheme.GutterLg;
        var captions = new Grid
        {
            Margin = new Thickness(statInset, 0, statInset, DevToolsTheme.GutterXS),
        };
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rankCap = DevToolsUi.Eyebrow("#");
        rankCap.Width = 28;
        rankCap.TextAlignment = TextAlignment.Center;
        Grid.SetColumn(rankCap, 0);
        captions.Children.Add(rankCap);

        var elemCap = DevToolsUi.Eyebrow("ELEMENT · MEASURE / ARRANGE / INVALIDATIONS");
        elemCap.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0);
        Grid.SetColumn(elemCap, 1);
        captions.Children.Add(elemCap);

        panel.Children.Add(captions);
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
        });

        _statsEmptyText = DevToolsUi.Muted("No layout activity captured yet.");
        _statsEmptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _statsEmptyText.TextAlignment = TextAlignment.Center;
        _statsEmptyText.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
        panel.Children.Add(_statsEmptyText);

        _statsFooterText = DevToolsUi.Muted("", DevToolsTheme.FontXS);
        _statsFooterText.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
        _statsFooterText.Visibility = Visibility.Collapsed;
        panel.Children.Add(_statsFooterText);
    }

    private void BuildInvalidationHeader(StackPanel panel)
    {
        // The title + live count pill are now hosted by the enclosing Card header
        // (built in BuildLayoutTab). We only create the pill instance here so the
        // refresh path keeps the same object reference; it is parented there.
        _invCountPill = DevToolsUi.Pill("0", DevToolsTheme.Info);

        // Column captions aligned to the InvalidationRowSkin grid (TIME / KIND /
        // ELEMENT / SOURCE), with a hairline beneath. Left/right margin mirrors a
        // row's outer GutterLg margin + inner GutterLg padding so the captions
        // sit over their columns.
        double rowInset = DevToolsTheme.GutterLg + DevToolsTheme.GutterLg;
        var captions = new Grid
        {
            Margin = new Thickness(rowInset, 0, rowInset, DevToolsTheme.GutterXS),
        };
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        captions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var timeCap = DevToolsUi.Eyebrow("TIME");
        Grid.SetColumn(timeCap, 0);
        captions.Children.Add(timeCap);
        var kindCap = DevToolsUi.Eyebrow("KIND");
        Grid.SetColumn(kindCap, 1);
        captions.Children.Add(kindCap);
        var elemCap = DevToolsUi.Eyebrow("ELEMENT");
        Grid.SetColumn(elemCap, 2);
        captions.Children.Add(elemCap);
        var srcCap = DevToolsUi.Eyebrow("SOURCE");
        Grid.SetColumn(srcCap, 3);
        captions.Children.Add(srcCap);

        panel.Children.Add(captions);
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
        });

        _invEmptyText = DevToolsUi.Muted("No invalidations recorded.");
        _invEmptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _invEmptyText.TextAlignment = TextAlignment.Center;
        _invEmptyText.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
        panel.Children.Add(_invEmptyText);
    }

    private void ToggleLayoutRecording()
    {
        if (LayoutDiagnostics.IsRecording)
        {
            LayoutDiagnostics.StopRecording();
            StopLayoutRefreshTimer();
        }
        else
        {
            LayoutDiagnostics.StartRecording();
            StartLayoutRefreshTimer();
        }
        ReflectLayoutRecordingState();
    }

    partial void OnLayoutTabActivated()
    {
        ReflectLayoutRecordingState();
        RefreshLayoutStats();
        RefreshInvalidations();
        if (LayoutDiagnostics.IsRecording)
            StartLayoutRefreshTimer();
    }

    private void StartLayoutRefreshTimer()
    {
        if (_layoutRefreshTimer != null) return;
        // 1s cadence is enough for layout diagnostics — the old 500ms tick was
        // flooding the dispatcher with UI allocations while recording, which
        // is exactly the condition where the target window produces the most
        // Measure/Arrange callbacks.
        _layoutRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _layoutRefreshTimer.Tick += (_, _) =>
        {
            // Any new row skins added to the pool must get IsDiagnosticsIgnored
            // from their field initializer — otherwise the pool expansion
            // inside RefreshLayoutStats leaks DevTools UI into Layout stats.
            using var __scope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();
            RefreshLayoutStats();
            RefreshInvalidations();
        };
        _layoutRefreshTimer.Start();
    }

    private void StopLayoutRefreshTimer()
    {
        _layoutRefreshTimer?.Stop();
        _layoutRefreshTimer = null;
    }

    private void ReflectLayoutRecordingState()
    {
        bool rec = LayoutDiagnostics.IsRecording;
        if (_layoutRecordButton != null)
        {
            _layoutRecordButton.Label = rec ? "Stop recording" : "Start recording";
            _layoutRecordButton.SetIcon(rec ? "■" : "●");
        }
        if (_layoutStatusText != null)
            _layoutStatusText.Text = rec
                ? "Capturing per-element measure / arrange timings."
                : "Recording is off — start to capture per-element measure / arrange timings.";
        if (_layoutStatusPill?.Child is TextBlock pillText)
        {
            pillText.Text = rec ? "REC" : "IDLE";
            pillText.Foreground = rec ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
            _layoutStatusPill.Background = new SolidColorBrush(
                rec
                    ? Color.FromArgb(0x38, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B)
                    : Color.FromArgb(0x22, DevToolsTheme.TextSecondaryColor.R, DevToolsTheme.TextSecondaryColor.G, DevToolsTheme.TextSecondaryColor.B));
        }
    }

    private const int StatsTopN = 30;
    private const int InvalidationMaxRows = 120;

    private void RefreshLayoutStats()
    {
        if (_layoutStatsPanel == null) return;

        var stats = LayoutDiagnostics.SnapshotStats();

        if (stats.Count == 0)
        {
            // Hide all pooled rows + footer; show the empty placeholder.
            if (_statsEmptyText != null) _statsEmptyText.Visibility = Visibility.Visible;
            if (_statsFooterText != null) _statsFooterText.Visibility = Visibility.Collapsed;
            foreach (var row in _statsRowPool) row.Root.Visibility = Visibility.Collapsed;
            return;
        }

        if (_statsEmptyText != null) _statsEmptyText.Visibility = Visibility.Collapsed;

        Func<LayoutDiagnostics.ElementStats, double> sortKey = _layoutSortMode switch
        {
            0 => s => s.MeasureTotalMicros,
            1 => s => s.ArrangeTotalMicros,
            _ => s => s.InvalidateMeasureCount + s.InvalidateArrangeCount + s.InvalidateVisualCount,
        };

        // Pick top-N in O(N log K) instead of sorting everything; avoids
        // LINQ's sort+ToList allocations on large sets.
        var top = SelectTopN(stats, sortKey, StatsTopN);
        double maxValue = top.Count > 0 ? Math.Max(1, sortKey(top[0])) : 1;

        // Grow the pool up to what we need, reusing existing rows.
        while (_statsRowPool.Count < top.Count)
        {
            var skin = LayoutStatsRowSkin.Build(RevealStatsElement);
            // Insert the new row before the footer so footer stays last.
            int footerIndex = _statsFooterText != null
                ? _layoutStatsPanel.Children.IndexOf(_statsFooterText)
                : -1;
            if (footerIndex < 0) _layoutStatsPanel.Children.Add(skin.Root);
            else _layoutStatsPanel.Children.Insert(footerIndex, skin.Root);
            _statsRowPool.Add(skin);
        }

        for (int i = 0; i < _statsRowPool.Count; i++)
        {
            var skin = _statsRowPool[i];
            if (i < top.Count)
            {
                skin.Root.Visibility = Visibility.Visible;
                BindStatsRow(skin, top[i], i, sortKey(top[i]), maxValue);
            }
            else
            {
                skin.Root.Visibility = Visibility.Collapsed;
            }
        }

        if (_statsFooterText != null)
        {
            if (stats.Count > top.Count)
            {
                _statsFooterText.Text = $"+{stats.Count - top.Count} more elements tracked";
                _statsFooterText.Visibility = Visibility.Visible;
            }
            else
            {
                _statsFooterText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void BindStatsRow(LayoutStatsRowSkin skin, LayoutDiagnostics.ElementStats s, int rank, double primaryValue, double maxValue)
    {
        // Stash the element ref on the skin so the Reveal button (bound
        // once) can read current selection without per-refresh closures.
        skin.Current = s;

        skin.RankText.Text = (rank + 1).ToString();
        skin.NameText.Text = string.IsNullOrEmpty(s.ElementName) ? s.TypeName : $"{s.TypeName}  #{s.ElementName}";

        UpdateMetricBar(
            skin.MeasureBar,
            count: s.MeasureCount,
            totalMicros: s.MeasureTotalMicros,
            averageMicros: s.MeasureAverageMicros,
            worstMicros: s.MeasureWorstMicros,
            accent: DevToolsTheme.Info,
            proportion: _layoutSortMode == 0 ? Normalize(primaryValue, maxValue) : Normalize(s.MeasureTotalMicros, maxValue),
            highlighted: _layoutSortMode == 0);

        UpdateMetricBar(
            skin.ArrangeBar,
            count: s.ArrangeCount,
            totalMicros: s.ArrangeTotalMicros,
            averageMicros: s.ArrangeAverageMicros,
            worstMicros: s.ArrangeWorstMicros,
            accent: DevToolsTheme.Warning,
            proportion: _layoutSortMode == 1 ? Normalize(primaryValue, maxValue) : Normalize(s.ArrangeTotalMicros, maxValue),
            highlighted: _layoutSortMode == 1);

        UpdateInvalidationBar(
            skin.InvalidationBar,
            s,
            proportion: _layoutSortMode == 2 ? Normalize(primaryValue, maxValue) : 0,
            highlighted: _layoutSortMode == 2);
    }

    private void RevealStatsElement(LayoutStatsRowSkin skin)
    {
        var s = skin.Current;
        if (s == null || !s.ElementRef.TryGetTarget(out var elem)) return;
        RevealInInspector(elem);
    }

    private static List<LayoutDiagnostics.ElementStats> SelectTopN(
        IReadOnlyList<LayoutDiagnostics.ElementStats> stats,
        Func<LayoutDiagnostics.ElementStats, double> key,
        int n)
    {
        if (stats.Count <= n)
        {
            var copy = new List<LayoutDiagnostics.ElementStats>(stats);
            copy.Sort((a, b) => key(b).CompareTo(key(a)));
            return copy;
        }

        // Maintain a min-heap of size n so we only keep top-n.
        var heap = new List<(double k, LayoutDiagnostics.ElementStats v)>(n);
        foreach (var s in stats)
        {
            double k = key(s);
            if (heap.Count < n)
            {
                heap.Add((k, s));
                SiftUp(heap, heap.Count - 1);
            }
            else if (k > heap[0].k)
            {
                heap[0] = (k, s);
                SiftDown(heap, 0);
            }
        }
        heap.Sort((a, b) => b.k.CompareTo(a.k));
        var result = new List<LayoutDiagnostics.ElementStats>(heap.Count);
        foreach (var (_, v) in heap) result.Add(v);
        return result;

        static void SiftUp(List<(double k, LayoutDiagnostics.ElementStats v)> h, int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (h[p].k <= h[i].k) break;
                (h[p], h[i]) = (h[i], h[p]);
                i = p;
            }
        }
        static void SiftDown(List<(double k, LayoutDiagnostics.ElementStats v)> h, int i)
        {
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, m = i;
                if (l < h.Count && h[l].k < h[m].k) m = l;
                if (r < h.Count && h[r].k < h[m].k) m = r;
                if (m == i) break;
                (h[m], h[i]) = (h[i], h[m]);
                i = m;
            }
        }
    }

    private static double Normalize(double value, double max) => max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);

    private static void UpdateMetricBar(
        MetricBarSkin skin,
        int count, double totalMicros, double averageMicros, double worstMicros,
        SolidColorBrush accent, double proportion, bool highlighted)
    {
        skin.Label.FontWeight = highlighted ? FontWeights.SemiBold : FontWeights.Normal;
        skin.Label.Foreground = highlighted ? accent : DevToolsTheme.TextSecondary;
        skin.Fill.Background = accent;

        double pFill = Math.Max(0, Math.Min(1, proportion));
        if (pFill > 0 && pFill < 0.02) pFill = 0.02;
        double pRest = Math.Max(0, 1 - pFill);

        // Reuse the existing ColumnDefinitions instead of clearing + adding —
        // clearing triggers a re-measure of every child in the Grid.
        skin.TrackHost.ColumnDefinitions[0].Width = new GridLength(pFill > 0 ? pFill : 0.0001, GridUnitType.Star);
        skin.TrackHost.ColumnDefinitions[1].Width = new GridLength(pRest > 0 ? pRest : 0.0001, GridUnitType.Star);
        skin.Fill.Visibility = pFill > 0 ? Visibility.Visible : Visibility.Hidden;

        skin.Numbers.Text = count == 0
            ? $"{count} ×   —"
            : $"{count} ×   tot {totalMicros:F0} µs   avg {averageMicros:F1}   worst {worstMicros:F1}";
    }

    private static void UpdateInvalidationBar(
        InvalidationBarSkin skin,
        LayoutDiagnostics.ElementStats s,
        double proportion, bool highlighted)
    {
        int total = s.InvalidateMeasureCount + s.InvalidateArrangeCount + s.InvalidateVisualCount;
        skin.Label.FontWeight = highlighted ? FontWeights.SemiBold : FontWeights.Normal;
        skin.Label.Foreground = highlighted ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;

        double fill = Math.Max(0, Math.Min(1, proportion <= 0 ? 1 : proportion));
        double mShare = total > 0 ? (double)s.InvalidateMeasureCount / total * fill : 0;
        double aShare = total > 0 ? (double)s.InvalidateArrangeCount / total * fill : 0;
        double vShare = total > 0 ? (double)s.InvalidateVisualCount / total * fill : 0;
        double trail = 1.0 - mShare - aShare - vShare;
        if (trail < 0) trail = 0;

        skin.StripHost.ColumnDefinitions[0].Width = new GridLength(Math.Max(mShare, 0.0001), GridUnitType.Star);
        skin.StripHost.ColumnDefinitions[1].Width = new GridLength(Math.Max(aShare, 0.0001), GridUnitType.Star);
        skin.StripHost.ColumnDefinitions[2].Width = new GridLength(Math.Max(vShare, 0.0001), GridUnitType.Star);
        skin.StripHost.ColumnDefinitions[3].Width = new GridLength(Math.Max(trail, 0.0001), GridUnitType.Star);

        skin.MeasureCell.Visibility = s.InvalidateMeasureCount > 0 ? Visibility.Visible : Visibility.Hidden;
        skin.ArrangeCell.Visibility = s.InvalidateArrangeCount > 0 ? Visibility.Visible : Visibility.Hidden;
        skin.VisualCell.Visibility = s.InvalidateVisualCount > 0 ? Visibility.Visible : Visibility.Hidden;

        skin.Numbers.Text = $"M {s.InvalidateMeasureCount}   A {s.InvalidateArrangeCount}   V {s.InvalidateVisualCount}";
    }

    private void RefreshInvalidations()
    {
        if (_invalidationPanel == null) return;

        var entries = LayoutDiagnostics.SnapshotInvalidations();

        if (_invCountPill?.Child is TextBlock pillText)
            pillText.Text = entries.Count.ToString();

        if (entries.Count == 0)
        {
            if (_invEmptyText != null) _invEmptyText.Visibility = Visibility.Visible;
            foreach (var row in _invRowPool) row.Root.Visibility = Visibility.Collapsed;
            return;
        }
        if (_invEmptyText != null) _invEmptyText.Visibility = Visibility.Collapsed;

        int show = Math.Min(entries.Count, InvalidationMaxRows);

        while (_invRowPool.Count < show)
        {
            var skin = InvalidationRowSkin.Build();
            _invalidationPanel.Children.Add(skin.Root);
            _invRowPool.Add(skin);
        }

        // Fill the pool with the most-recent `show` entries, newest first.
        for (int i = 0; i < _invRowPool.Count; i++)
        {
            var skin = _invRowPool[i];
            if (i < show)
            {
                var e = entries[entries.Count - 1 - i];
                skin.Root.Visibility = Visibility.Visible;
                BindInvalidationRow(skin, e);
            }
            else
            {
                skin.Root.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static void BindInvalidationRow(InvalidationRowSkin skin, LayoutDiagnostics.InvalidationEntry e)
    {
        SolidColorBrush accent = e.Kind switch
        {
            LayoutDiagnostics.InvalidationKind.Measure => DevToolsTheme.Info,
            LayoutDiagnostics.InvalidationKind.Arrange => DevToolsTheme.Warning,
            LayoutDiagnostics.InvalidationKind.Visual => DevToolsTheme.Error,
            _ => DevToolsTheme.TextMuted,
        };

        skin.Time.Text = e.Timestamp.ToString("HH:mm:ss.fff");
        if (skin.Pill.Child is TextBlock pillText)
        {
            pillText.Text = e.Kind.ToString().ToUpperInvariant();
            pillText.Foreground = accent;
        }
        skin.Pill.Background = new SolidColorBrush(Color.FromArgb(0x38, accent.Color.R, accent.Color.G, accent.Color.B));
        skin.Element.Text = string.IsNullOrEmpty(e.ElementName) ? e.TypeName : $"{e.TypeName}  #{e.ElementName}";
        skin.Stack.Text = string.IsNullOrEmpty(e.StackSummary) ? "" : $"← {e.StackSummary}";
        skin.Root.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, accent.Color.R, accent.Color.G, accent.Color.B));
    }

    // ── Skin types: immutable UI shells whose inner parts get re-bound ───

    private sealed class MetricBarSkin
    {
        public required TextBlock Label { get; init; }
        public required Grid TrackHost { get; init; }
        public required Border Fill { get; init; }
        public required TextBlock Numbers { get; init; }
        public required Border Root { get; init; }
    }

    private sealed class InvalidationBarSkin
    {
        public required TextBlock Label { get; init; }
        public required Grid StripHost { get; init; }
        public required Border MeasureCell { get; init; }
        public required Border ArrangeCell { get; init; }
        public required Border VisualCell { get; init; }
        public required TextBlock Numbers { get; init; }
        public required Border Root { get; init; }
    }

    private sealed class LayoutStatsRowSkin
    {
        public required Border Root { get; init; }
        public required TextBlock RankText { get; init; }
        public required TextBlock NameText { get; init; }
        public required MetricBarSkin MeasureBar { get; init; }
        public required MetricBarSkin ArrangeBar { get; init; }
        public required InvalidationBarSkin InvalidationBar { get; init; }
        public LayoutDiagnostics.ElementStats? Current { get; set; }

        public static LayoutStatsRowSkin Build(Action<LayoutStatsRowSkin> onReveal)
        {
            var rankText = new TextBlock
            {
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.TextMuted,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var rankChip = new Border
            {
                Background = DevToolsTheme.Chrome,
                BorderBrush = DevToolsTheme.BorderSubtle,
                BorderThickness = DevToolsTheme.ThicknessHairline,
                CornerRadius = new CornerRadius(3),
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Child = rankText,
            };
            var nameText = new TextBlock
            {
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                FontWeight = FontWeights.SemiBold,
                Foreground = DevToolsTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var measureBar = MakeMetricBarSkin("Measure");
            var arrangeBar = MakeMetricBarSkin("Arrange");
            var invalidationBar = MakeInvalidationBarSkin();

            // The reveal callback needs to see the final skin instance, which
            // we haven't constructed yet. Capture a local that gets assigned
            // below so the closure reads the real instance at click time.
            LayoutStatsRowSkin? self = null;
            var reveal = DevToolsUi.Button("Reveal", () =>
            {
                var s = self;
                if (s != null) onReveal(s);
            }, icon: "→");

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(rankChip, 0);
            Grid.SetColumn(nameText, 1);
            Grid.SetColumn(reveal, 2);
            topGrid.Children.Add(rankChip);
            topGrid.Children.Add(nameText);
            topGrid.Children.Add(reveal);

            var body = new StackPanel { Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0) };
            body.Children.Add(measureBar.Root);
            body.Children.Add(arrangeBar.Root);
            body.Children.Add(invalidationBar.Root);

            var whole = new StackPanel();
            whole.Children.Add(topGrid);
            whole.Children.Add(body);

            var card = new Border
            {
                Background = DevToolsTheme.Chrome,
                BorderBrush = DevToolsTheme.BorderSubtle,
                BorderThickness = DevToolsTheme.ThicknessHairline,
                CornerRadius = DevToolsTheme.RadiusBase,
                Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterSm, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
                Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
                Child = whole,
            };

            self = new LayoutStatsRowSkin
            {
                Root = card,
                RankText = rankText,
                NameText = nameText,
                MeasureBar = measureBar,
                ArrangeBar = arrangeBar,
                InvalidationBar = invalidationBar,
            };
            return self;
        }
    }

    private static MetricBarSkin MakeMetricBarSkin(string labelText)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = labelText,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var trackHost = new Grid
        {
            Height = 8,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        trackHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        trackHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fill = new Border { CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(fill, 0);
        trackHost.Children.Add(fill);

        var trackRest = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, DevToolsTheme.BorderStrongColor.R, DevToolsTheme.BorderStrongColor.G, DevToolsTheme.BorderStrongColor.B)),
            CornerRadius = new CornerRadius(2),
        };
        Grid.SetColumn(trackRest, 1);
        trackHost.Children.Add(trackRest);

        Grid.SetColumn(trackHost, 1);
        grid.Children.Add(trackHost);

        var numbers = new TextBlock
        {
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(numbers, 2);
        grid.Children.Add(numbers);

        var root = new Border { Padding = new Thickness(0, 2, 0, 2), Child = grid };
        return new MetricBarSkin
        {
            Label = label,
            TrackHost = trackHost,
            Fill = fill,
            Numbers = numbers,
            Root = root,
        };
    }

    private static InvalidationBarSkin MakeInvalidationBarSkin()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Inv",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var stripHost = new Grid
        {
            Height = 8,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
        stripHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var measureCell = new Border { Background = DevToolsTheme.Info, Visibility = Visibility.Hidden };
        Grid.SetColumn(measureCell, 0);
        stripHost.Children.Add(measureCell);

        var arrangeCell = new Border { Background = DevToolsTheme.Warning, Visibility = Visibility.Hidden };
        Grid.SetColumn(arrangeCell, 1);
        stripHost.Children.Add(arrangeCell);

        var visualCell = new Border { Background = DevToolsTheme.Error, Visibility = Visibility.Hidden };
        Grid.SetColumn(visualCell, 2);
        stripHost.Children.Add(visualCell);

        var trailCell = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, DevToolsTheme.BorderStrongColor.R, DevToolsTheme.BorderStrongColor.G, DevToolsTheme.BorderStrongColor.B)),
            CornerRadius = new CornerRadius(2),
        };
        Grid.SetColumn(trailCell, 3);
        stripHost.Children.Add(trailCell);

        Grid.SetColumn(stripHost, 1);
        grid.Children.Add(stripHost);

        var numbers = new TextBlock
        {
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(numbers, 2);
        grid.Children.Add(numbers);

        var root = new Border { Padding = new Thickness(0, 2, 0, 2), Child = grid };
        return new InvalidationBarSkin
        {
            Label = label,
            StripHost = stripHost,
            MeasureCell = measureCell,
            ArrangeCell = arrangeCell,
            VisualCell = visualCell,
            Numbers = numbers,
            Root = root,
        };
    }

    private sealed class InvalidationRowSkin
    {
        public required Border Root { get; init; }
        public required TextBlock Time { get; init; }
        public required Border Pill { get; init; }
        public required TextBlock Element { get; init; }
        public required TextBlock Stack { get; init; }

        public static InvalidationRowSkin Build()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var time = new TextBlock
            {
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = DevToolsUi.Pill("", DevToolsTheme.TextMuted);
            pill.Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0);
            var element = new TextBlock
            {
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var stack = new TextBlock
            {
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(time, 0);
            Grid.SetColumn(pill, 1);
            Grid.SetColumn(element, 2);
            Grid.SetColumn(stack, 3);
            grid.Children.Add(time);
            grid.Children.Add(pill);
            grid.Children.Add(element);
            grid.Children.Add(stack);

            var root = new Border
            {
                Background = DevToolsTheme.Chrome,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterXS, DevToolsTheme.GutterLg, DevToolsTheme.GutterXS),
                Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, 2),
                Child = grid,
            };

            return new InvalidationRowSkin
            {
                Root = root,
                Time = time,
                Pill = pill,
                Element = element,
                Stack = stack,
            };
        }
    }

    // Legacy helpers kept for other Tab files that still call MakePlainButton.
    // They return the themed DevToolsUi button; type is Border so existing
    // fields typed as Border? continue to accept the return value.
    private static Border MakePlainButton(string label, Action onClick)
        => DevToolsUi.Button(label, onClick);

    private static Border MakeToggleButton(string label, Action onClick)
        => DevToolsUi.Button(label, onClick);
}
