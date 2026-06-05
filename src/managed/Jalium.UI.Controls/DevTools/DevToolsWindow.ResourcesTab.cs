using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private StackPanel? _resourcesChainPanel;
    private TextBox? _resourcesSearchBox;
    private DevToolsUi.DevToolsButton? _resourcesWinnersOnlyButton;
    private DispatcherTimer? _resourcesSearchTimer;
    private string _resourcesSearchText = string.Empty;
    private bool _resourcesWinnersOnly;

    private UIElement BuildResourcesTab()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ──
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(DevToolsUi.Muted("Filter:"));

        _resourcesSearchBox = DevToolsUi.TextInput(220, "key substring");
        _resourcesSearchBox.Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0);
        _resourcesSearchBox.TextChanged += (_, _) => ScheduleResourcesRefresh();
        toolbar.Children.Add(_resourcesSearchBox);

        _resourcesWinnersOnlyButton = DevToolsUi.Toggle("Winners only", () =>
        {
            _resourcesWinnersOnly = !_resourcesWinnersOnly;
            if (_resourcesWinnersOnlyButton != null)
                _resourcesWinnersOnlyButton.IsActive = _resourcesWinnersOnly;
            RefreshResourcesChain();
        }, _resourcesWinnersOnly, icon: "⚑");
        toolbar.Children.Add(_resourcesWinnersOnlyButton);

        toolbar.Children.Add(DevToolsUi.Button("Refresh", () => RefreshResourcesChain(), icon: "↻"));
        toolbar.Children.Add(DevToolsUi.Muted("  · winner = nearest scope where the key resolves first."));

        var toolbarBar = DevToolsUi.Toolbar(toolbar);
        Grid.SetRow(toolbarBar, 0);
        root.Children.Add(toolbarBar);

        // ── Chain viewport ──
        // The chain content panel is the tracked, refreshable container. We re-parent
        // it (unchanged instance) into an instrument Panel that supplies the framed
        // "RESOURCE MERGE CHAIN" eyebrow header + hairline divider.
        _resourcesChainPanel = new StackPanel();
        var chainPanel = DevToolsUi.Panel("Resource Merge Chain", _resourcesChainPanel);
        chainPanel.Margin = new Thickness(DevToolsTheme.GutterBase);
        var scroll = new ScrollViewer
        {
            Content = chainPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var card = new Border
        {
            Child = scroll,
            ClipToBounds = true,
        };
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        return new Border
        {
            Background = DevToolsTheme.Surface,
            Child = root,
            ClipToBounds = true,
        };
    }

    partial void OnResourcesTabActivated()
    {
        RefreshResourcesChain();
    }

    private void ScheduleResourcesRefresh()
    {
        if (_resourcesSearchTimer == null)
        {
            _resourcesSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _resourcesSearchTimer.Tick += (_, _) =>
            {
                _resourcesSearchTimer!.Stop();
                _resourcesSearchText = _resourcesSearchBox?.Text ?? string.Empty;
                RefreshResourcesChain();
            };
        }
        _resourcesSearchTimer.Stop();
        _resourcesSearchTimer.Start();
    }

    private void RefreshResourcesChain()
    {
        if (_resourcesChainPanel == null) return;
        _resourcesChainPanel.Children.Clear();

        // Section title is now carried by the enclosing instrument Panel header.

        // Collect ordered chain: selected element → ancestors → Application.
        var chain = new List<(string ScopeName, ResourceDictionary? Dict)>();
        if (_selectedVisual is FrameworkElement fe)
        {
            FrameworkElement? cur = fe;
            while (cur != null)
            {
                chain.Add(($"{cur.GetType().Name}" + (string.IsNullOrEmpty(cur.Name) ? "" : $" [#{cur.Name}]"), cur.Resources));
                cur = cur.VisualParent as FrameworkElement;
            }
        }
        else
        {
            chain.Add(("Target window", _targetWindow.Resources));
        }
        try
        {
            var app = Application.Current;
            if (app != null)
                chain.Add(("Application", app.Resources));
        }
        catch { /* Application may not exist in all hosting setups */ }

        if (chain.Count == 0)
        {
            var empty = DevToolsUi.Muted("Select an element to view its resource resolution path.");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.TextAlignment = TextAlignment.Center;
            empty.Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterBase);
            _resourcesChainPanel.Children.Add(empty);
            return;
        }

        // Determine winners — the first scope (nearest the element) where each key
        // appears. Later scopes show the key as shadowed so the user can see which
        // declaration actually wins the lookup.
        var winnerScope = new Dictionary<object, int>();
        for (int i = 0; i < chain.Count; i++)
        {
            var dict = chain[i].Dict;
            if (dict == null) continue;
            foreach (var key in dict.Keys)
            {
                if (key == null) continue;
                if (!winnerScope.ContainsKey(key))
                    winnerScope[key] = i;
            }
        }

        // Summary strip: how many keys are there across the chain after filtering.
        int totalKeys = 0, winnerKeys = 0;
        foreach (var (_, dict) in chain)
        {
            if (dict == null) continue;
            totalKeys += dict.Count;
        }
        winnerKeys = winnerScope.Count;

        _resourcesChainPanel.Children.Add(MakeSummaryRow(chain.Count, totalKeys, winnerKeys));

        var subtitle = DevToolsUi.Muted("From the selected element walking up the tree to the Application.", DevToolsTheme.FontXS);
        subtitle.Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterBase);
        _resourcesChainPanel.Children.Add(subtitle);

        for (int depth = 0; depth < chain.Count; depth++)
        {
            var (scope, dict) = chain[depth];
            _resourcesChainPanel.Children.Add(MakeScopeCard(depth, scope, dict, winnerScope));
        }
    }

    private Border MakeSummaryRow(int scopes, int totalKeys, int winnerKeys)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterBase),
        };
        row.Children.Add(DevToolsUi.Pill($"{scopes} scopes", DevToolsTheme.Info));
        row.Children.Add(DevToolsUi.Pill($"{totalKeys} keys total", DevToolsTheme.TextMuted));
        row.Children.Add(DevToolsUi.Pill($"{winnerKeys} distinct", DevToolsTheme.Success));
        if (!string.IsNullOrEmpty(_resourcesSearchText))
            row.Children.Add(DevToolsUi.Pill($"filter: {_resourcesSearchText}", DevToolsTheme.Warning));
        return new Border
        {
            Child = row,
            Padding = new Thickness(0, 0, 0, DevToolsTheme.GutterXS),
        };
    }

    private Border MakeScopeCard(int depth, string scope, ResourceDictionary? dict, Dictionary<object, int> winnerScope)
    {
        var body = new StackPanel();

        // ── Header row ──
        var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm) };
        scopeRow.Children.Add(new TextBlock
        {
            Text = $"[{depth}]",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        scopeRow.Children.Add(new TextBlock
        {
            Text = scope,
            FontSize = DevToolsTheme.FontBase,
            FontFamily = DevToolsTheme.UiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = DevToolsTheme.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        });
        int keyCount = dict?.Count ?? 0;
        scopeRow.Children.Add(DevToolsUi.Pill($"{keyCount} key{(keyCount == 1 ? "" : "s")}", DevToolsTheme.Info));
        if (dict?.MergedDictionaries is { Count: > 0 } merged)
            scopeRow.Children.Add(DevToolsUi.Pill($"{merged.Count} merged", DevToolsTheme.TextMuted));
        body.Children.Add(scopeRow);

        // ── Direct keys ──
        if (dict == null || dict.Count == 0)
        {
            body.Children.Add(DevToolsUi.Muted("(empty)", DevToolsTheme.FontSm));
        }
        else
        {
            int shown = 0;
            foreach (var kvp in dict)
            {
                if (!MatchesResourceFilter(kvp.Key)) continue;
                bool isWinner = kvp.Key != null
                    && winnerScope.TryGetValue(kvp.Key, out var winnerDepth)
                    && winnerDepth == depth;
                if (_resourcesWinnersOnly && !isWinner) continue;
                body.Children.Add(MakeResourceRow(kvp.Key, kvp.Value, isWinner));
                shown++;
            }
            if (shown == 0 && (!string.IsNullOrEmpty(_resourcesSearchText) || _resourcesWinnersOnly))
            {
                body.Children.Add(DevToolsUi.Muted("(no keys match the current filter)", DevToolsTheme.FontXS));
            }
        }

        // ── Merged dictionaries ──
        if (dict?.MergedDictionaries is { Count: > 0 } mergedDicts)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"↳ MergedDictionaries ({mergedDicts.Count})",
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = DevToolsTheme.TextMuted,
                Margin = new Thickness(0, DevToolsTheme.GutterBase, 0, DevToolsTheme.GutterXS),
            });
            int mi = 0;
            foreach (var m in mergedDicts)
            {
                var chip = new Border
                {
                    Background = DevToolsTheme.Chrome,
                    BorderBrush = DevToolsTheme.BorderSubtle,
                    BorderThickness = DevToolsTheme.ThicknessHairline,
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
                    Margin = new Thickness(DevToolsTheme.GutterLg, 0, 0, DevToolsTheme.GutterXS),
                };
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock
                {
                    Text = $"[{mi}]",
                    FontSize = DevToolsTheme.FontXS,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, DevToolsTheme.GutterBase, 0),
                });
                content.Children.Add(DevToolsUi.Pill($"{m.Count} entries", DevToolsTheme.TextMuted));
                string? src = m.Source?.ToString();
                content.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(src) ? "(inline)" : src,
                    FontSize = DevToolsTheme.FontXS,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(DevToolsTheme.GutterSm, 0, 0, 0),
                });
                chip.Child = content;
                body.Children.Add(chip);
                mi++;
            }
        }

        return new Border
        {
            Background = DevToolsTheme.Chrome,
            BorderBrush = DevToolsTheme.BorderSubtle,
            BorderThickness = DevToolsTheme.ThicknessHairline,
            CornerRadius = DevToolsTheme.RadiusBase,
            Margin = new Thickness(0, 0, 0, DevToolsTheme.GutterSm),
            Padding = new Thickness(DevToolsTheme.GutterLg, DevToolsTheme.GutterBase, DevToolsTheme.GutterLg, DevToolsTheme.GutterBase),
            Child = body,
        };
    }

    private Border MakeResourceRow(object? key, object? value, bool isWinner)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });                        // type chip
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });    // key
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });                        // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });      // value preview

        var chip = MakeValueTypeChip(value);
        Grid.SetColumn(chip, 0);
        grid.Children.Add(chip);

        var keyText = new TextBlock
        {
            Text = key?.ToString() ?? "<null>",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = isWinner ? DevToolsTheme.TokenProperty : DevToolsTheme.TextMuted,
            FontWeight = isWinner ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(DevToolsTheme.GutterSm, 0, DevToolsTheme.GutterSm, 0),
        };
        Grid.SetColumn(keyText, 1);
        grid.Children.Add(keyText);

        var arrow = new TextBlock
        {
            Text = "→",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        var valueView = MakeValuePreview(value);
        Grid.SetColumn(valueView, 3);
        grid.Children.Add(valueView);

        var accentBar = isWinner
            ? new SolidColorBrush(Color.FromArgb(0x80, DevToolsTheme.SuccessColor.R, DevToolsTheme.SuccessColor.G, DevToolsTheme.SuccessColor.B))
            : new SolidColorBrush(Color.FromArgb(0x30, DevToolsTheme.TextMutedColor.R, DevToolsTheme.TextMutedColor.G, DevToolsTheme.TextMutedColor.B));

        return new Border
        {
            Background = isWinner ? DevToolsTheme.RowAlt : null,
            BorderBrush = accentBar,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(DevToolsTheme.GutterBase, DevToolsTheme.GutterXS, DevToolsTheme.GutterBase, DevToolsTheme.GutterXS),
            Margin = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    private static UIElement MakeValueTypeChip(object? value) => value switch
    {
        null                    => SimpleChip("∅", DevToolsTheme.TextMuted),
        SolidColorBrush scb     => ColorChip(scb.Color),
        LinearGradientBrush     => SimpleChip("↘", DevToolsTheme.Accent),
        RadialGradientBrush     => SimpleChip("◎", DevToolsTheme.Accent),
        Brush                   => SimpleChip("▦", DevToolsTheme.Accent),
        Jalium.UI.Style         => SimpleChip("S", DevToolsTheme.Warning),
        ControlTemplate         => SimpleChip("T", DevToolsTheme.Warning),
        DataTemplate            => SimpleChip("D", DevToolsTheme.Warning),
        Thickness               => SimpleChip("⊞", DevToolsTheme.Info),
        double                  => SimpleChip("#", DevToolsTheme.TokenNumber),
        int                     => SimpleChip("#", DevToolsTheme.TokenNumber),
        float                   => SimpleChip("#", DevToolsTheme.TokenNumber),
        long                    => SimpleChip("#", DevToolsTheme.TokenNumber),
        bool                    => SimpleChip("✓", DevToolsTheme.TokenBool),
        string                  => SimpleChip("\u201C", DevToolsTheme.TokenString),
        Enum                    => SimpleChip("E", DevToolsTheme.Accent),
        _                       => SimpleChip("?", DevToolsTheme.TextMuted),
    };

    private static Border SimpleChip(string glyph, Brush fg)
    {
        return new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = DevToolsTheme.Control,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.UiFont,
                Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
    }

    private static Border ColorChip(Color color)
    {
        return new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color),
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    private static UIElement MakeValuePreview(object? value)
    {
        switch (value)
        {
            case null:
                return new TextBlock
                {
                    Text = "<null>",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                };

            case SolidColorBrush scb:
                return ColorValue(scb.Color);

            case Style style:
                return StyleValue(style);

            case ControlTemplate ct:
                return new TextBlock
                {
                    Text = $"ControlTemplate · TargetType={ct.TargetType?.Name ?? "?"}",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TokenType,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

            case DataTemplate dt:
                return new TextBlock
                {
                    Text = $"DataTemplate · DataType={dt.DataType?.ToString() ?? "?"}",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = DevToolsTheme.TokenType,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

            default:
                Brush fg = value switch
                {
                    string => DevToolsTheme.TokenString,
                    Enum   => DevToolsTheme.TokenEnum,
                    bool   => DevToolsTheme.TokenBool,
                    double or int or float or long => DevToolsTheme.TokenNumber,
                    _ => DevToolsTheme.TextPrimary,
                };
                return new TextBlock
                {
                    Text = value is string s ? $"\"{s}\"" : value.ToString() ?? "",
                    FontSize = DevToolsTheme.FontSm,
                    FontFamily = DevToolsTheme.MonoFont,
                    Foreground = fg,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
        }
    }

    private static StackPanel ColorValue(Color color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(color),
            BorderBrush = DevToolsTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, DevToolsTheme.GutterSm, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenString,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private static StackPanel StyleValue(Style style)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = "Style",
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TokenType,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (style.TargetType != null)
        {
            row.Children.Add(new TextBlock
            {
                Text = $"  TargetType={style.TargetType.Name}",
                FontSize = DevToolsTheme.FontXS,
                FontFamily = DevToolsTheme.MonoFont,
                Foreground = DevToolsTheme.Accent,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        int setterCount = style.Setters?.Count ?? 0;
        row.Children.Add(DevToolsUi.Pill($"{setterCount} setter{(setterCount == 1 ? "" : "s")}", DevToolsTheme.TextMuted));
        int triggerCount = style.Triggers?.Count ?? 0;
        if (triggerCount > 0)
            row.Children.Add(DevToolsUi.Pill($"{triggerCount} trigger{(triggerCount == 1 ? "" : "s")}", DevToolsTheme.Warning));
        if (style.BasedOn != null)
            row.Children.Add(DevToolsUi.Pill($"BasedOn={style.BasedOn.TargetType?.Name ?? "?"}", DevToolsTheme.Info));
        return row;
    }

    private bool MatchesResourceFilter(object? key)
    {
        if (string.IsNullOrEmpty(_resourcesSearchText)) return true;
        var s = key?.ToString();
        if (s == null) return false;
        return s.IndexOf(_resourcesSearchText, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
