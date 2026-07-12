using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private StackPanel? _eventsGraphPanel;
    private DevToolsUi.DevToolsButton? _eventsRecordButton;
    private Border? _eventsStatusPill;
    private TextBox? _eventsFilterTextBox;
    private DispatcherTimer? _eventsRefreshTimer;
    private TextBlock? _eventsCountText;
    // Count pill in the framed card header. Mirrors the exact same entries.Count
    // value that _eventsCountText already shows — the in-panel count line remains
    // the source of truth; this is an additive header view, no new computation.
    private TextBlock? _eventsHeaderPillText;

    // Map each rendered row → the entry reference that produced it so we can
    // do incremental updates (prepend the latest samples, trim the oldest)
    // without thrashing the entire panel on every tick.
    private readonly Dictionary<RoutedEventDiagnostics.RoutedEventEntry, UIElement> _eventsRowByEntry
        = new(ReferenceEqualityComparer.Instance);

    private const int EventsMaxVisibleRows = 80;

    private UIElement BuildEventsTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ─────────────────────────────────────────────────────
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };

        _eventsRecordButton = DevToolsUi.Button("Start recording", () =>
        {
            if (RoutedEventDiagnostics.IsRecording)
            {
                RoutedEventDiagnostics.StopRecording();
                StopEventsRefreshTimer();
            }
            else
            {
                RoutedEventDiagnostics.StartRecording();
                StartEventsRefreshTimer();
            }
            ReflectEventsRecordingState();
        }, DevToolsUi.ButtonStyle.Primary, icon: "●");
        toolbar.Children.Add(_eventsRecordButton);

        toolbar.Children.Add(DevToolsUi.Button("Reset", () =>
        {
            RoutedEventDiagnostics.Reset();
            ResetEventsGraph();
        }, icon: "↺"));

        toolbar.Children.Add(DevToolsUi.VerticalDivider());

        var suppressLabel = DevToolsUi.Muted(DevToolsUi.Tracked("SUPPRESS EVENTS:"));
        suppressLabel.FontFamily = DevToolsTheme.DisplayFont;
        toolbar.Children.Add(suppressLabel);
        _eventsFilterTextBox = DevToolsUi.TextInput(240, "e.g. MouseMove, PreviewMouseMove");
        _eventsFilterTextBox.Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0);
        _eventsFilterTextBox.Text = string.Join(", ", RoutedEventDiagnostics.GetFilter());
        _eventsFilterTextBox.LostFocus += (_, _) => ApplyEventsFilter();
        toolbar.Children.Add(_eventsFilterTextBox);
        toolbar.Children.Add(DevToolsUi.Button("Apply", ApplyEventsFilter, icon: "✓"));

        toolbar.Children.Add(DevToolsUi.VerticalDivider());
        toolbar.Children.Add(MakeLegendSwatch(DevToolsTheme.InfoColor,     "Bubble"));
        toolbar.Children.Add(MakeLegendSwatch(DevToolsTheme.WarningColor,  "Tunnel"));
        toolbar.Children.Add(MakeLegendSwatch(DevToolsTheme.SuccessColor,  "Direct"));

        _eventsStatusPill = DevToolsUi.Pill("IDLE", DevToolsTheme.TextSecondary);
        toolbar.Children.Add(_eventsStatusPill);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Graph panel ──────────────────────────────────────────────────
        // The count now lives in the framed card header pill (set in refresh).
        // _eventsCountText is preserved as child[0] of the graph panel — refresh
        // and reset logic keep updating its .Text and rely on its index — but it
        // is collapsed so the count is not shown twice.
        _eventsCountText = new TextBlock
        {
            Text = "EVENTS · 0",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(DevToolsTheme.GutterSm, DevToolsTheme.GutterSm, DevToolsTheme.GutterSm, DevToolsTheme.GutterBase),
            Visibility = Visibility.Collapsed,
        };
        _eventsGraphPanel = new StackPanel { Margin = new Thickness(DevToolsTheme.GutterBase) };
        _eventsGraphPanel.Children.Add(_eventsCountText);

        var scroll = new ScrollViewer
        {
            Content = _eventsGraphPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // Self-built card header: [ EVENTS eyebrow ........ count pill ].
        // Panel() has no pill slot, so we frame with Card() and lay out the
        // header ourselves. The pill mirrors entries.Count (set in refresh).
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerEyebrow = DevToolsUi.Eyebrow("Events", DevToolsTheme.TextSecondary);
        Grid.SetColumn(headerEyebrow, 0);
        headerRow.Children.Add(headerEyebrow);

        var headerPill = DevToolsUi.Pill("0", DevToolsTheme.Accent);
        _eventsHeaderPillText = headerPill.Child as TextBlock;
        Grid.SetColumn(headerPill, 1);
        headerRow.Children.Add(headerPill);

        var headerStack = new StackPanel { Orientation = Orientation.Vertical };
        headerStack.Children.Add(headerRow);
        headerStack.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterBase),
        });

        var cardBody = new Grid();
        cardBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(headerStack, 0);
        cardBody.Children.Add(headerStack);
        Grid.SetRow(scroll, 1);
        cardBody.Children.Add(scroll);

        var card = DevToolsUi.Card(cardBody);
        card.Margin = new Thickness(DevToolsTheme.GutterBase);
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    private static StackPanel MakeLegendSwatch(Color color, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, DevToolsTheme.GutterLg, 0) };
        row.Children.Add(new Border
        {
            Width = 10, Height = 10,
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

    partial void OnEventsTabActivated()
    {
        ReflectEventsRecordingState();
        RefreshEventsGraph();
        if (RoutedEventDiagnostics.IsRecording)
            StartEventsRefreshTimer();
    }

    private void StartEventsRefreshTimer()
    {
        if (_eventsRefreshTimer != null) return;
        _eventsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _eventsRefreshTimer.Tick += (_, _) => RefreshEventsGraph();
        _eventsRefreshTimer.Start();
    }

    private void StopEventsRefreshTimer()
    {
        _eventsRefreshTimer?.Stop();
        _eventsRefreshTimer = null;
    }

    private void ReflectEventsRecordingState()
    {
        bool rec = RoutedEventDiagnostics.IsRecording;
        if (_eventsRecordButton != null)
        {
            _eventsRecordButton.Label = rec ? "Stop recording" : "Start recording";
            _eventsRecordButton.SetIcon(rec ? "■" : "●");
        }
        if (_eventsStatusPill?.Child is TextBlock pillText)
        {
            pillText.Text = rec ? "REC" : "IDLE";
            pillText.Foreground = rec ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
            _eventsStatusPill.Background = new SolidColorBrush(
                rec
                    ? Color.FromArgb(0x38, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B)
                    : Color.FromArgb(0x22, DevToolsTheme.TextSecondaryColor.R, DevToolsTheme.TextSecondaryColor.G, DevToolsTheme.TextSecondaryColor.B));
        }
    }

    private void ApplyEventsFilter()
    {
        if (_eventsFilterTextBox == null) return;
        var text = _eventsFilterTextBox.Text ?? string.Empty;
        var names = text
            .Split(new[] { ',', ';', ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .Where(n => !string.IsNullOrEmpty(n));
        RoutedEventDiagnostics.SetFilter(names);
    }

    /// <summary>
    /// Drops every rendered row and the memoized reference map. Used by the
    /// "Reset" button and when the tab first activates.
    /// </summary>
    private void ResetEventsGraph()
    {
        if (_eventsGraphPanel == null) return;
        _eventsRowByEntry.Clear();
        _eventsGraphPanel.Children.Clear();
        if (_eventsCountText != null)
            _eventsGraphPanel.Children.Add(_eventsCountText);
        if (_eventsCountText != null)
            _eventsCountText.Text = "EVENTS · 0";
        if (_eventsHeaderPillText != null)
            _eventsHeaderPillText.Text = DevToolsUi.Tracked("0");
    }

    /// <summary>
    /// Incremental refresh: only the latest unseen entries are prepended to the
    /// panel, and old rows are trimmed from the bottom once the display cap is
    /// reached. This avoids the large per-tick rebuild that was causing the
    /// tab to stutter when the target window was producing many events.
    /// </summary>
    private void RefreshEventsGraph()
    {
        if (_eventsGraphPanel == null) return;
        if (_eventsCountText == null) return;

        var entries = RoutedEventDiagnostics.Snapshot();
        _eventsCountText.Text = $"EVENTS · {entries.Count}";
        if (_eventsHeaderPillText != null)
            _eventsHeaderPillText.Text = DevToolsUi.Tracked(entries.Count.ToString());

        if (entries.Count == 0)
        {
            // Panel only contains the header when empty.
            while (_eventsGraphPanel.Children.Count > 1)
                _eventsGraphPanel.Children.RemoveAt(_eventsGraphPanel.Children.Count - 1);
            _eventsRowByEntry.Clear();
            var empty = DevToolsUi.Muted("No events recorded yet. Start recording and interact with the target window.");
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg, 0);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            _eventsGraphPanel.Children.Add(empty);
            return;
        }

        // Remove the "empty" hint if it's still there.
        if (_eventsGraphPanel.Children.Count == 2
            && _eventsGraphPanel.Children[1] is TextBlock tb
            && !_eventsRowByEntry.ContainsValue(tb))
        {
            _eventsGraphPanel.Children.RemoveAt(1);
        }

        // Walk the snapshot from newest → oldest; stop as soon as we hit an
        // entry we've already rendered (incremental). Record new entries in
        // order so they get prepended in the correct newest-first sequence.
        List<RoutedEventDiagnostics.RoutedEventEntry>? pending = null;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (_eventsRowByEntry.ContainsKey(entry)) break;
            (pending ??= new()).Add(entry);
            if (pending.Count >= EventsMaxVisibleRows) break;
        }

        if (pending != null)
        {
            // pending is in newest-first order, but we must insert right after
            // the header so the newest appears at the top.
            // pending[last] is the oldest of the new batch — insert it first at
            // index 1, then each newer one also at index 1, pushing older ones down.
            for (int k = pending.Count - 1; k >= 0; k--)
            {
                var row = BuildEventGraphRow(pending[k]);
                _eventsRowByEntry[pending[k]] = row;
                _eventsGraphPanel.Children.Insert(1, row);
            }
        }

        // Trim: drop oldest rows past the cap. The panel layout is:
        //   [0] countHeader, [1..] rows (newest first → oldest last).
        while (_eventsGraphPanel.Children.Count - 1 > EventsMaxVisibleRows)
        {
            int lastIdx = _eventsGraphPanel.Children.Count - 1;
            var last = _eventsGraphPanel.Children[lastIdx];
            _eventsGraphPanel.Children.RemoveAt(lastIdx);
            // Drop from the lookup as well so future ticks can rebuild if the
            // same entry ever shows up again (it won't, but keep the map tidy).
            foreach (var kvp in _eventsRowByEntry)
            {
                if (ReferenceEquals(kvp.Value, last))
                {
                    _eventsRowByEntry.Remove(kvp.Key);
                    break;
                }
            }
        }

        // Prune stale lookup keys whose entries have fallen off the diagnostics
        // ring buffer — keeps the dictionary bounded when events churn quickly.
        if (_eventsRowByEntry.Count > EventsMaxVisibleRows * 2)
        {
            var liveSet = new HashSet<RoutedEventDiagnostics.RoutedEventEntry>(entries, ReferenceEqualityComparer.Instance);
            var toRemove = _eventsRowByEntry.Keys.Where(k => !liveSet.Contains(k)).ToList();
            foreach (var k in toRemove)
                _eventsRowByEntry.Remove(k);
        }
    }

    private Border BuildEventGraphRow(RoutedEventDiagnostics.RoutedEventEntry entry)
    {
        var strategyColor = entry.Strategy switch
        {
            RoutingStrategy.Bubble => DevToolsTheme.Info,
            RoutingStrategy.Tunnel => DevToolsTheme.Warning,
            _ => DevToolsTheme.Success,
        };

        // Header row: timestamp + event name + strategy pill + handled flag
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm) };
        headerRow.Children.Add(new TextBlock
        {
            Text = entry.Timestamp.ToString("HH:mm:ss.fff"),
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
        });
        headerRow.Children.Add(new TextBlock
        {
            Text = entry.EventName,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        headerRow.Children.Add(DevToolsUi.Pill(entry.Strategy.ToString().ToUpperInvariant(), strategyColor));
        if (entry.Handled)
            headerRow.Children.Add(DevToolsUi.Pill("HANDLED", DevToolsTheme.TextMuted));

        // Graph row: nodes with arrows (dispatch direction).
        // Bubble direction is source→root (list order); Tunnel flips that.
        var path = entry.Path;
        var graphRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        bool tunnel = entry.Strategy == RoutingStrategy.Tunnel;

        if (path.Count == 0)
        {
            graphRow.Children.Add(DevToolsUi.Muted("(no path captured)"));
        }
        else
        {
            int count = path.Count;
            for (int step = 0; step < count; step++)
            {
                int pathIndex = tunnel ? count - 1 - step : step;
                var node = path[pathIndex];

                bool isOriginalSource = pathIndex == 0; // always the first visual in the captured chain
                graphRow.Children.Add(BuildEventNode(node, strategyColor, isOriginalSource));

                if (step < count - 1)
                    graphRow.Children.Add(BuildArrow(strategyColor));
            }
        }

        var body = new StackPanel();
        body.Children.Add(headerRow);
        body.Children.Add(new ScrollViewer
        {
            Content = graphRow,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        return new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x80, strategyColor.Color.R, strategyColor.Color.G, strategyColor.Color.B)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(0, DevToolsTheme.RadiusBase.TopRight, DevToolsTheme.RadiusBase.BottomRight, 0),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase),
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, DevToolsTheme.GutterSm),
            Child = body,
        };
    }

    private Border BuildEventNode(RoutedEventDiagnostics.PathNode node, SolidColorBrush strategyColor, bool isOriginalSource)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        if (isOriginalSource)
        {
            label.Children.Add(new TextBlock
            {
                Text = "◉ ",
                FontSize = DevToolsTheme.FontSm,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = strategyColor,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        label.Children.Add(new TextBlock
        {
            Text = node.TypeName,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(node.ElementName))
        {
            label.Children.Add(new TextBlock
            {
                Text = $"  #{node.ElementName}",
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.Accent,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var bg = new SolidColorBrush(Color.FromArgb(
            isOriginalSource ? (byte)0x38 : (byte)0x1F,
            strategyColor.Color.R, strategyColor.Color.G, strategyColor.Color.B));

        var nodeBorder = new Border
        {
            Background = bg,
            BorderBrush = isOriginalSource ? strategyColor : DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(isOriginalSource ? 2 : 1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
            Child = label,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        // Single click-through handler. Hover highlight is intentionally skipped:
        // a node graph can have dozens of nodes per row, so per-node MouseEnter /
        // MouseLeave subscriptions turn into hundreds of live handlers in seconds.
        nodeBorder.MouseDown += (_, _) =>
        {
            if (!node.VisualRef.TryGetTarget(out var visual)) return;
            RevealInInspector(visual);
        };
        return nodeBorder;
    }

    private static UIElement BuildArrow(SolidColorBrush strategyColor)
    {
        // A slim connector line with an arrow glyph. The strategy color tints it
        // so adjacent rows stay visually bound to their event's color.
        var line = new Border
        {
            Width = 18,
            Height = 2,
            Background = new SolidColorBrush(Color.FromArgb(
                0xB0, strategyColor.Color.R, strategyColor.Color.G, strategyColor.Color.B)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var arrowText = new TextBlock
        {
            Text = "▸",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = strategyColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0),
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(DevToolsTheme.GutterXS, 0, DevToolsTheme.GutterXS, 0),
        };
        panel.Children.Add(line);
        panel.Children.Add(arrowText);
        return panel;
    }
}
