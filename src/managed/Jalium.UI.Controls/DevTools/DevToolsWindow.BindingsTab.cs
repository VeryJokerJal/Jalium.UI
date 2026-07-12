using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private DevToolsUi.DevToolsButton? _bindingsRecordButton;
    private Border? _bindingsStatusPill;
    private StackPanel? _bindingsEventsPanel;
    private StackPanel? _bindingsOverviewPanel;
    private DispatcherTimer? _bindingsRefreshTimer;

    private UIElement BuildBindingsTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        _bindingsRecordButton = DevToolsUi.Button("Start recording", () =>
        {
            if (BindingDiagnostics.IsRecording)
            {
                BindingDiagnostics.StopRecording();
                StopBindingsRefreshTimer();
            }
            else
            {
                BindingDiagnostics.StartRecording();
                StartBindingsRefreshTimer();
            }
            ReflectBindingsRecordingState();
        }, DevToolsUi.ButtonStyle.Primary, icon: "●");
        toolbar.Children.Add(_bindingsRecordButton);
        toolbar.Children.Add(DevToolsUi.Button("Reset", () =>
        {
            BindingDiagnostics.Reset();
            RefreshBindingsOverview();
            RefreshBindingsEvents();
        }, icon: "↺"));

        _bindingsStatusPill = DevToolsUi.Pill("IDLE", DevToolsTheme.TextSecondary);
        toolbar.Children.Add(_bindingsStatusPill);

        var hint = DevToolsUi.Muted("Counters per binding above · live event log below.");
        hint.Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0);
        toolbar.Children.Add(hint);

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Overview section: framed as an instrument Panel. The same
        //    _bindingsOverviewPanel instance is re-parented into the Panel body
        //    (via its ScrollViewer); refresh logic still clears/appends to it.
        _bindingsOverviewPanel = new StackPanel();
        var overviewScroll = new ScrollViewer { Content = _bindingsOverviewPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var overviewPanel = DevToolsUi.Panel("ACTIVE BINDINGS · SELECTED ELEMENT", overviewScroll, DevToolsTheme.Accent);
        overviewPanel.Margin = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm);
        Grid.SetRow(overviewPanel, 1);
        root.Children.Add(overviewPanel);

        // ── Event log section: framed as a Card. The count header (Eyebrow +
        //    Pill) is built inside _bindingsEventsPanel by RefreshBindingsEvents
        //    because the live count changes every refresh (Panel has no pill slot).
        _bindingsEventsPanel = new StackPanel();
        var eventsScroll = new ScrollViewer { Content = _bindingsEventsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var eventsCard = DevToolsUi.Card(eventsScroll);
        eventsCard.Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterBase);
        Grid.SetRow(eventsCard, 2);
        root.Children.Add(eventsCard);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    partial void OnBindingsTabActivated()
    {
        ReflectBindingsRecordingState();
        RefreshBindingsOverview();
        RefreshBindingsEvents();
        if (BindingDiagnostics.IsRecording) StartBindingsRefreshTimer();
    }

    private void StartBindingsRefreshTimer()
    {
        if (_bindingsRefreshTimer != null) return;
        _bindingsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _bindingsRefreshTimer.Tick += (_, _) =>
        {
            RefreshBindingsOverview();
            RefreshBindingsEvents();
        };
        _bindingsRefreshTimer.Start();
    }

    private void StopBindingsRefreshTimer()
    {
        _bindingsRefreshTimer?.Stop();
        _bindingsRefreshTimer = null;
    }

    private void ReflectBindingsRecordingState()
    {
        bool rec = BindingDiagnostics.IsRecording;
        if (_bindingsRecordButton != null)
        {
            _bindingsRecordButton.Label = rec ? "Stop recording" : "Start recording";
            _bindingsRecordButton.SetIcon(rec ? "■" : "●");
        }
        if (_bindingsStatusPill?.Child is TextBlock pillText)
        {
            pillText.Text = rec ? "REC" : "IDLE";
            pillText.Foreground = rec ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
            _bindingsStatusPill.Background = new SolidColorBrush(
                rec
                    ? Color.FromArgb(0x38, DevToolsTheme.ErrorColor.R, DevToolsTheme.ErrorColor.G, DevToolsTheme.ErrorColor.B)
                    : Color.FromArgb(0x22, DevToolsTheme.TextSecondaryColor.R, DevToolsTheme.TextSecondaryColor.G, DevToolsTheme.TextSecondaryColor.B));
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "EnumerateDependencyProperties walks the inheritance chain reflecting over DependencyProperty static fields, as its own RequiresUnreferencedCode message documents. DevTools is an opt-in developer/inspector control; preserving the DependencyProperty static fields of inspected types under trimming/AOT is the documented consumer responsibility for using the DevTools Bindings tab, not a defect of this site.")]
    private void RefreshBindingsOverview()
    {
        if (_bindingsOverviewPanel == null) return;
        _bindingsOverviewPanel.Children.Clear();

        // The section title + divider are now provided by the enclosing
        // DevToolsUi.Panel("ACTIVE BINDINGS · SELECTED ELEMENT", ...), so the
        // overview content starts directly with the binding cards or an
        // empty-state caption.
        if (_selectedVisual is not FrameworkElement fe)
        {
            var empty = DevToolsUi.Muted("Select a FrameworkElement in the Inspector to see its bindings.");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
            _bindingsOverviewPanel.Children.Add(empty);
            return;
        }

        int shown = 0;
        foreach (var dp in EnumerateDependencyProperties(fe.GetType()))
        {
            var expr = fe.GetBindingExpression(dp);
            if (expr == null) continue;

            _bindingsOverviewPanel.Children.Add(BuildBindingFlowCard(fe, dp, expr));
            shown++;
        }

        if (shown == 0)
        {
            var empty = DevToolsUi.Muted("(no active bindings on selected element)");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterSm, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase);
            _bindingsOverviewPanel.Children.Add(empty);
        }
    }

    // ── Binding flow card ────────────────────────────────────────────────

    private Border BuildBindingFlowCard(FrameworkElement targetFe, DependencyProperty dp, BindingExpressionBase expr)
    {
        // ── Pull everything we need from the expression ───────────
        string path;
        BindingMode mode = BindingMode.Default;
        UpdateSourceTrigger trigger = UpdateSourceTrigger.Default;
        string sourceTypeName = "<unresolved>";
        string sourceDetail = "";
        object? sourceInstance = null;
        if (expr is BindingExpression be)
        {
            path = be.ParentBinding?.Path?.Path ?? "";
            mode = be.ParentBinding?.Mode ?? BindingMode.Default;
            trigger = be.ParentBinding?.UpdateSourceTrigger ?? UpdateSourceTrigger.Default;
            sourceInstance = be.ResolvedSource;
            sourceTypeName = sourceInstance?.GetType().Name ?? "<unresolved>";
            sourceDetail = be.ParentBinding?.ElementName != null
                ? $"ElementName={be.ParentBinding.ElementName}"
                : be.ParentBinding?.RelativeSource != null
                    ? $"RelativeSource={be.ParentBinding.RelativeSource.Mode}"
                    : "";
        }
        else
        {
            path = expr.GetType().Name;
        }

        var counters = BindingDiagnostics.GetCounters(targetFe, dp);
        int upT = counters?.UpdateTargetCount ?? 0;
        int upS = counters?.UpdateSourceCount ?? 0;
        int errs = counters?.ErrorCount ?? 0;

        SolidColorBrush accent = expr.Status switch
        {
            BindingStatus.Active => DevToolsTheme.Success,
            BindingStatus.Unattached => DevToolsTheme.Warning,
            BindingStatus.Inactive => DevToolsTheme.TextMuted,
            _ => DevToolsTheme.Error,
        };

        // ── Target box ──
        var targetBox = MakeEndpointBox(
            header: $"{targetFe.GetType().Name}{(string.IsNullOrEmpty(targetFe.Name) ? "" : $"  #{targetFe.Name}")}",
            primary: dp.Name,
            primaryColor: DevToolsTheme.TokenProperty,
            footer: "TARGET",
            borderAccent: accent,
            onClick: () => RevealInInspector(targetFe));

        // ── Source box ──
        var sourceFooter = trigger == UpdateSourceTrigger.Default ? "SOURCE" : $"SOURCE · {trigger}";
        var sourceBox = MakeEndpointBox(
            header: sourceTypeName,
            primary: string.IsNullOrEmpty(path) ? "(no path)" : path,
            primaryColor: DevToolsTheme.TokenString,
            footer: string.IsNullOrEmpty(sourceDetail) ? sourceFooter : $"{sourceFooter} · {sourceDetail}",
            borderAccent: accent,
            onClick: () => { if (sourceInstance is Visual sv) RevealInInspector(sv); });

        // ── Arrow column ──
        var (arrowGlyph, arrowTitle) = FormatMode(mode);
        var arrow = MakeFlowArrow(arrowGlyph, arrowTitle, accent, upT, upS, errs);

        // ── Layout the three-column flow ──
        var flow = new Grid();
        flow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        flow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        flow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(targetBox, 0);
        Grid.SetColumn(arrow, 1);
        Grid.SetColumn(sourceBox, 2);
        flow.Children.Add(targetBox);
        flow.Children.Add(arrow);
        flow.Children.Add(sourceBox);

        // ── Status strip at the bottom ──
        var statusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, DevToolsTheme.GutterSm, 0, 0),
        };
        statusRow.Children.Add(DevToolsUi.Pill(expr.Status.ToString(), accent));
        if (counters?.LastError is { Length: > 0 } err)
        {
            statusRow.Children.Add(new TextBlock
            {
                Text = err,
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.Error,
                Margin = new Thickness(DevToolsTheme.GutterBase, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var body = new StackPanel();
        body.Children.Add(flow);
        body.Children.Add(statusRow);

        return new Border
        {
            Background = DevToolsTheme.SurfaceAlt,
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x70, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase),
            Child = body,
        };
    }

    private Border MakeEndpointBox(string header, string primary, Brush primaryColor, string footer,
        SolidColorBrush borderAccent, Action onClick)
    {
        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = header,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = primary,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = primaryColor,
            Margin = new Thickness(0, DevToolsTheme.GutterXS, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = footer,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, DevToolsTheme.GutterXS, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var accentBg = new SolidColorBrush(Color.FromArgb(
            0x1E, borderAccent.Color.R, borderAccent.Color.G, borderAccent.Color.B));
        var box = new Border
        {
            Background = accentBg,
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterSm, DevToolsTheme.GutterBase, DevToolsTheme.GutterSm),
            Child = stack,
            Cursor = Cursors.Hand,
        };
        box.MouseDown += (_, _) => onClick();
        return box;
    }

    private UIElement MakeFlowArrow(string glyph, string title, SolidColorBrush accent, int upT, int upS, int errs)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, 0),
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = DevToolsTheme.Font2XL,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, DevToolsTheme.GutterXS, 0, DevToolsTheme.GutterXS),
        });

        var countsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        countsRow.Children.Add(MakeCounter("T", upT, DevToolsTheme.TokenNumber));
        countsRow.Children.Add(MakeCounter("S", upS, DevToolsTheme.Info));
        countsRow.Children.Add(MakeCounter("E", errs, errs > 0 ? DevToolsTheme.Error : DevToolsTheme.TextMuted));
        panel.Children.Add(countsRow);

        return panel;
    }

    private static TextBlock MakeCounter(string prefix, int value, SolidColorBrush color)
    {
        return new TextBlock
        {
            Text = $"{prefix} {value}",
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = color,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
        };
    }

    private static (string glyph, string title) FormatMode(BindingMode mode) => mode switch
    {
        BindingMode.OneWay => ("→", "OneWay"),
        BindingMode.TwoWay => ("⇄", "TwoWay"),
        BindingMode.OneTime => ("▸|", "OneTime"),
        BindingMode.OneWayToSource => ("←", "OneWayToSource"),
        _ => ("⇢", "Default"),
    };

    // Shared with ElementContextMenu.cs; the definition lives there so that
    // partial files do not redeclare the method.

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools enumerates DependencyProperty static fields by walking the inheritance chain via reflection.")]
    private static IEnumerable<DependencyProperty> EnumerateDependencyProperties(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                      | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
            foreach (var f in fields)
            {
                if (typeof(DependencyProperty).IsAssignableFrom(f.FieldType))
                {
                    if (f.GetValue(null) is DependencyProperty dp)
                        yield return dp;
                }
            }
        }
    }

    private void RefreshBindingsEvents()
    {
        if (_bindingsEventsPanel == null) return;
        _bindingsEventsPanel.Children.Clear();
        var entries = BindingDiagnostics.Snapshot();

        // Count header: "EVENT LOG" eyebrow + live-count pill (Panel has no pill
        // slot, so the header is built here and the count stays visible).
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(DevToolsTheme.GutterLg, 0, DevToolsTheme.GutterLg, DevToolsTheme.GutterSm),
        };
        header.Children.Add(DevToolsUi.Eyebrow("EVENT LOG"));
        header.Children.Add(DevToolsUi.Pill(
            entries.Count.ToString(),
            entries.Count > 0 ? DevToolsTheme.Accent : DevToolsTheme.TextSecondary));
        _bindingsEventsPanel.Children.Add(header);

        if (entries.Count == 0)
        {
            var empty = DevToolsUi.Muted("No binding events yet. Start recording and interact with bound controls.");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterLg);
            _bindingsEventsPanel.Children.Add(empty);
            return;
        }

        // Column-caption header row mirroring BuildBindingEventRow's 5-column grid
        // (TIME / KIND / TARGET / arrow / SOURCE), with a hairline beneath it.
        _bindingsEventsPanel.Children.Add(BuildBindingEventColumnHeader());
        _bindingsEventsPanel.Children.Add(new Border
        {
            Height = 1,
            Background = DevToolsTheme.BorderSubtle,
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
        });

        int show = Math.Min(entries.Count, 120);
        for (int i = entries.Count - 1; i >= entries.Count - show; i--)
        {
            _bindingsEventsPanel.Children.Add(BuildBindingEventRow(entries[i]));
        }
    }

    // Column captions for the event log, mirroring the 5-column layout of
    // BuildBindingEventRow so the TIME / KIND / TARGET / SOURCE headers sit
    // directly above their data. The horizontal inset matches a data row's
    // outer margin + accent border + inner padding so columns line up.
    private static UIElement BuildBindingEventColumnHeader()
    {
        var grid = new Grid
        {
            Margin = new Thickness(
                DevToolsTheme.GutterBase + 2 + DevToolsTheme.GutterLg,
                DevToolsTheme.GutterXS,
                DevToolsTheme.GutterBase + DevToolsTheme.GutterLg,
                DevToolsTheme.GutterXS),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });       // timestamp
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });      // kind pill
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // target
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // source

        var time = DevToolsUi.Eyebrow("TIME");
        var kind = DevToolsUi.Eyebrow("KIND");
        var target = DevToolsUi.Eyebrow("TARGET");
        var source = DevToolsUi.Eyebrow("SOURCE");
        Grid.SetColumn(time, 0);
        Grid.SetColumn(kind, 1);
        Grid.SetColumn(target, 2);
        Grid.SetColumn(source, 4);
        grid.Children.Add(time);
        grid.Children.Add(kind);
        grid.Children.Add(target);
        grid.Children.Add(source);
        return grid;
    }

    private static UIElement BuildBindingEventRow(BindingDiagnostics.BindingEventEntry entry)
    {
        // Pick a color + directional glyph that matches the event kind.
        var (glyph, accent) = entry.Kind switch
        {
            BindingDiagnostics.BindingEventKind.UpdateTarget   => ("←", DevToolsTheme.Info),
            BindingDiagnostics.BindingEventKind.UpdateSource   => ("→", DevToolsTheme.Warning),
            BindingDiagnostics.BindingEventKind.Activated      => ("●", DevToolsTheme.Success),
            BindingDiagnostics.BindingEventKind.StatusChanged  => ("◐", DevToolsTheme.TextMuted),
            BindingDiagnostics.BindingEventKind.Error          => ("✕", DevToolsTheme.Error),
            _ => ("·", DevToolsTheme.TextMuted),
        };

        var targetBlock = new TextBlock
        {
            Text = $"{entry.TargetTypeName}.{entry.TargetPropertyName}",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenProperty,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var arrow = new TextBlock
        {
            Text = glyph,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0),
            Width = 16,
            TextAlignment = TextAlignment.Center,
        };
        var sourceBlock = new TextBlock
        {
            Text = entry.SourceDescription,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenString,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });       // timestamp
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });      // kind pill
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // target
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // source

        var ts = new TextBlock
        {
            Text = entry.Timestamp.ToString("HH:mm:ss.fff"),
            FontSize = DevToolsTheme.FontXS,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var kindPill = DevToolsUi.Pill(entry.Kind.ToString(), accent);
        kindPill.Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0);

        Grid.SetColumn(ts, 0);
        Grid.SetColumn(kindPill, 1);
        Grid.SetColumn(targetBlock, 2);
        Grid.SetColumn(arrow, 3);
        Grid.SetColumn(sourceBlock, 4);
        grid.Children.Add(ts);
        grid.Children.Add(kindPill);
        grid.Children.Add(targetBlock);
        grid.Children.Add(arrow);
        grid.Children.Add(sourceBlock);

        return new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x60, accent.Color.R, accent.Color.G, accent.Color.B)),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterXS, DevToolsTheme.GutterLg, DevToolsTheme.GutterXS),
            Margin = new Thickness(DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase, 2),
            Child = grid,
        };
    }
}
