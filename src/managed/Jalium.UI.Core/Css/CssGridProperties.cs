using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

[Flags]
internal enum CssGridAutoFlow { Row = 0, Column = 1, Dense = 2 }
internal enum CssBoxAlignment { Auto, Normal, Stretch, Start, End, Center, SpaceBetween, SpaceAround, SpaceEvenly }

/// <summary>CSS-specific layout values coexist with native Grid definitions and local attached values.</summary>
internal static class CssGridProperties
{
    internal static readonly DependencyProperty ColumnsProperty = Container("GridTemplateColumns", new CssComputedGridTracks(CssGridTrackList.Empty, CssLengthContext.Default));
    internal static readonly DependencyProperty RowsProperty = Container("GridTemplateRows", new CssComputedGridTracks(CssGridTrackList.Empty, CssLengthContext.Default));
    internal static readonly DependencyProperty AutoColumnsProperty = Container("GridAutoColumns", new CssComputedGridTracks(CssGridTrackList.Auto, CssLengthContext.Default));
    internal static readonly DependencyProperty AutoRowsProperty = Container("GridAutoRows", new CssComputedGridTracks(CssGridTrackList.Auto, CssLengthContext.Default));
    internal static readonly DependencyProperty AreasProperty = Container("GridTemplateAreas", CssGridAreas.Empty);
    internal static readonly DependencyProperty AutoFlowProperty = Container("GridAutoFlow", CssGridAutoFlow.Row);
    internal static readonly DependencyProperty RowStartProperty = Item("GridRowStart", new CssGridLine());
    internal static readonly DependencyProperty RowEndProperty = Item("GridRowEnd", new CssGridLine());
    internal static readonly DependencyProperty ColumnStartProperty = Item("GridColumnStart", new CssGridLine());
    internal static readonly DependencyProperty ColumnEndProperty = Item("GridColumnEnd", new CssGridLine());
    internal static readonly DependencyProperty JustifyItemsProperty = Container("CssJustifyItems", CssBoxAlignment.Normal);
    internal static readonly DependencyProperty JustifySelfProperty = Item("CssJustifySelf", CssBoxAlignment.Auto);
    internal static readonly DependencyProperty AlignItemsProperty = Container("CssAlignItems", CssBoxAlignment.Normal);
    internal static readonly DependencyProperty JustifyContentProperty = Container("CssJustifyContent", CssBoxAlignment.Normal);
    internal static readonly DependencyProperty AlignContentProperty = Container("CssAlignContent", CssBoxAlignment.Normal);

    private static DependencyProperty Container<T>(string name, T initial) => DependencyProperty.RegisterAttached(
        name, typeof(T), typeof(CssGridProperties), new PropertyMetadata(initial, static (target, _) =>
        { if (target is UIElement element) element.InvalidateMeasure(); }));

    private static DependencyProperty Item<T>(string name, T initial) => DependencyProperty.RegisterAttached(
        name, typeof(T), typeof(CssGridProperties), new PropertyMetadata(initial, static (target, _) =>
        {
            if (target is UIElement element)
            {
                element.InvalidateMeasure();
                if (element.VisualParent is UIElement parent) parent.InvalidateMeasure();
            }
        }));

    internal static void Register()
    {
        Tracks("grid-template-columns", ColumnsProperty);
        Tracks("grid-template-rows", RowsProperty);
        Tracks("grid-auto-columns", AutoColumnsProperty, true);
        Tracks("grid-auto-rows", AutoRowsProperty, true);
        CssPropertyRegistry.Register(new()
        {
            Name = "grid-template-areas", Kind = CssPropertyKind.Longhand, StorageProperty = AreasProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) => CssGridAreas.Parse(ref reader) is { } areas
                ? new CssImmediateValue(AreasProperty, areas) : null,
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "grid-auto-flow", Kind = CssPropertyKind.Longhand, StorageProperty = AutoFlowProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var flow = CssGridAutoFlow.Row;
                var axis = false; var dense = false;
                while (!reader.AtEnd)
                {
                    if (!reader.TryReadIdent(out var keyword)) return null;
                    switch (keyword.ToString().ToLowerInvariant())
                    {
                        case "row" when !axis: axis = true; break;
                        case "column" when !axis: axis = true; flow |= CssGridAutoFlow.Column; break;
                        case "dense" when !dense: dense = true; flow |= CssGridAutoFlow.Dense; break;
                        default: return null;
                    }
                }
                return axis || dense ? new CssImmediateValue(AutoFlowProperty, flow) : null;
            },
        });

        foreach (var name in new[] { "grid-row-start", "grid-row-end", "grid-column-start", "grid-column-end" })
        {
            var propertyName = name;
            CssPropertyRegistry.Register(new()
            {
                Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = LineProperty(name),
                Parse = (ref CssTokenReader reader, CssCompileContext _) => CssGridLine.Read(ref reader, out var line) && reader.AtEnd
                    ? new GridLineValue(propertyName, line) : null,
            });
        }
        PlacementShorthand("grid-row", ["grid-row-start", "grid-row-end"]);
        PlacementShorthand("grid-column", ["grid-column-start", "grid-column-end"]);
        PlacementShorthand("grid-area", ["grid-row-start", "grid-column-start", "grid-row-end", "grid-column-end"]);
        Alignment("justify-items", JustifyItemsProperty, false);
        Alignment("justify-self", JustifySelfProperty, true);
        PairShorthand("place-items", "align-items", "justify-items");
        PairShorthand("place-self", "align-self", "justify-self");
        PairShorthand("place-content", "align-content", "justify-content");
        TemplateShorthand("grid-template", false);
        TemplateShorthand("grid", true);
    }

    private static void Tracks(string name, DependencyProperty property, bool implicitTracks = false) => CssPropertyRegistry.Register(new()
    {
        Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = property,
        Parse = (ref CssTokenReader reader, CssCompileContext _) => CssGridTrackList.Parse(ref reader, implicitTracks) is { } tracks
            ? new GridTracksValue(property, tracks) : null,
    });

    private static void TemplateShorthand(string name, bool resetAutomatic) => CssPropertyRegistry.Register(new()
    {
        Name = name, Kind = CssPropertyKind.Shorthand,
        Expand = (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
        {
            if (!reader.TryReadUntilTopLevelDelimiter('/', out var left)) return false;
            var hasSlash = reader.TryReadSlash();
            var right = reader.Remaining;
            var columns = CssGridTrackList.Empty; var rows = CssGridTrackList.Empty;
            var autoColumns = CssGridTrackList.Auto; var autoRows = CssGridTrackList.Auto;
            var areas = CssGridAreas.Empty; var flow = CssGridAutoFlow.Row;
            var leftReader = new CssTokenReader(left, reader.NumericContext); var rightReader = new CssTokenReader(right, reader.NumericContext);
            if (resetAutomatic && hasSlash && ReadAutoFlow(ref leftReader, out var automaticRows, out var dense))
            {
                autoRows = automaticRows!;
                if (CssGridTrackList.Parse(ref rightReader) is not { } tracks) return false;
                columns = tracks;
                if (dense) flow |= CssGridAutoFlow.Dense;
            }
            else if (resetAutomatic && hasSlash && ReadAutoFlow(ref rightReader, out var automaticColumns, out dense))
            {
                autoColumns = automaticColumns!;
                leftReader = new CssTokenReader(left, reader.NumericContext);
                if (CssGridTrackList.Parse(ref leftReader) is not { } tracks) return false;
                rows = tracks;
                flow = CssGridAutoFlow.Column | (dense ? CssGridAutoFlow.Dense : 0);
            }
            else
            {
                leftReader = new CssTokenReader(left, reader.NumericContext); rightReader = new CssTokenReader(right, reader.NumericContext);
                if (left.Contains('"') || left.Contains('\''))
                {
                    var parts = new List<CssGridTrackPart>();
                    var strings = new List<string>();
                    while (!leftReader.AtEnd)
                    {
                        if (leftReader.TryReadDelimiter('['))
                        {
                            var names = new List<string>();
                            while (!leftReader.TryReadDelimiter(']'))
                            {
                                if (!leftReader.TryReadIdent(out var lineName) || CssGridTrackList.IsReservedName(lineName)) return false;
                                names.Add(lineName.ToString());
                            }
                            parts.Add(new(Names: names.ToArray()));
                            continue;
                        }
                        var stringStart = leftReader.Position;
                        if (!leftReader.TryReadString(out var areaText)) return false;
                        strings.Add(left[stringStart..leftReader.Position].ToString());
                        var track = CssGridTrack.Auto;
                        if (leftReader.TryPeekChar(out var next) && next is not ('[' or '\'' or '"'))
                        {
                            if (CssGridTrackList.ReadTrack(ref leftReader) is not { } parsed) return false;
                            track = parsed;
                        }
                        parts.Add(new(Track: track));
                    }
                    var areaReader = new CssTokenReader(string.Join(" ", strings));
                    if (CssGridAreas.Parse(ref areaReader) is not { } parsedAreas) return false;
                    areas = parsedAreas;
                    rows = new CssGridTrackList(parts.ToArray());
                    if (hasSlash)
                    {
                        if (CssGridTrackList.Parse(ref rightReader, allowRepeat: false, allowSubgrid: false) is not { } parsedColumns) return false;
                        columns = parsedColumns;
                    }
                }
                else
                {
                    if (CssGridTrackList.Parse(ref leftReader) is not { } parsedRows || !hasSlash && (parsedRows.Parts.Length > 0 || parsedRows.IsSubgrid)) return false;
                    rows = parsedRows;
                    if (hasSlash)
                    {
                        if (CssGridTrackList.Parse(ref rightReader) is not { } parsedColumns) return false;
                        columns = parsedColumns;
                    }
                }
            }
            output.Add(new("grid-template-columns", new GridTracksValue(ColumnsProperty, columns), false));
            output.Add(new("grid-template-rows", new GridTracksValue(RowsProperty, rows), false));
            output.Add(new("grid-template-areas", new CssImmediateValue(AreasProperty, areas), false));
            if (resetAutomatic)
            {
                output.Add(new("grid-auto-columns", new GridTracksValue(AutoColumnsProperty, autoColumns), false));
                output.Add(new("grid-auto-rows", new GridTracksValue(AutoRowsProperty, autoRows), false));
                output.Add(new("grid-auto-flow", new CssImmediateValue(AutoFlowProperty, flow), false));
            }
            return true;
        },
    });

    private static bool ReadAutoFlow(ref CssTokenReader reader, out CssGridTrackList? tracks, out bool dense)
    {
        tracks = null; dense = false;
        var sawFlow = false;
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (!probe.TryReadIdent(out var keyword)) break;
            if (keyword.Equals("auto-flow", StringComparison.OrdinalIgnoreCase) && !sawFlow) sawFlow = true;
            else if (keyword.Equals("dense", StringComparison.OrdinalIgnoreCase) && !dense) dense = true;
            else break;
            reader = probe;
        }
        if (!sawFlow) return false;
        tracks = reader.AtEnd ? CssGridTrackList.Auto : CssGridTrackList.Parse(ref reader, true);
        return tracks is not null;
    }

    private sealed class GridTracksValue(DependencyProperty property, CssGridTrackList tracks) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            tracks.ObserveContainerDependencies(context.Lengths);
            sink.Set(property, new CssComputedGridTracks(tracks, context.Lengths));
            return true;
        }
    }

    private sealed class GridLineValue(string name, CssGridLine line) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            (context.Slots.GridPlacement ??= new()).Set(name, line, context.Slots.CurrentContributionIsState);
            return true;
        }
    }

    internal static DependencyProperty LineProperty(string name) => name switch
    {
        "grid-row-start" => RowStartProperty, "grid-row-end" => RowEndProperty,
        "grid-column-start" => ColumnStartProperty, "grid-column-end" => ColumnEndProperty,
        _ => throw new ArgumentException("Unknown grid line property", nameof(name)),
    };

    private static void PlacementShorthand(string name, string[] names) => CssPropertyRegistry.Register(new()
    {
        Name = name, Kind = CssPropertyKind.Shorthand,
        Expand = (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
        {
            var lines = new CssGridLine[names.Length];
            var count = 0;
            do
            {
                if (count == lines.Length || !CssGridLine.Read(ref reader, out lines[count++])) return false;
            } while (reader.TryReadSlash());
            if (!reader.AtEnd) return false;
            for (var i = count; i < lines.Length; i++)
            {
                var source = i == 3 ? 1 : 0;
                lines[i] = lines[source].IsNameOnly ? lines[source] : default;
            }
            for (var i = 0; i < names.Length; i++) output.Add(new(names[i], new GridLineValue(names[i], lines[i]), false));
            return true;
        },
    });

    internal static CssBoxAlignment? ParseAlignment(string value, bool self = false) => value.ToLowerInvariant() switch
    {
        "auto" when self => CssBoxAlignment.Auto, "normal" => CssBoxAlignment.Normal, "stretch" => CssBoxAlignment.Stretch,
        "start" or "self-start" or "flex-start" or "left" => CssBoxAlignment.Start,
        "end" or "self-end" or "flex-end" or "right" => CssBoxAlignment.End,
        "center" => CssBoxAlignment.Center, "space-between" => CssBoxAlignment.SpaceBetween,
        "space-around" => CssBoxAlignment.SpaceAround, "space-evenly" => CssBoxAlignment.SpaceEvenly, _ => null,
    };

    internal static void SetContainerAlignment(ICssSetterSink sink, string name, string value)
    {
        var property = name switch
        {
            "align-items" => AlignItemsProperty, "align-content" => AlignContentProperty,
            "justify-content" => JustifyContentProperty, _ => null,
        };
        if (property is not null && ParseAlignment(value) is { } alignment) sink.Set(property, alignment);
    }

    private static void Alignment(string name, DependencyProperty property, bool self) => CssPropertyRegistry.Register(new()
    {
        Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = property,
        Parse = (ref CssTokenReader reader, CssCompileContext _) =>
        {
            if (!reader.TryReadIdent(out var keyword) || !reader.AtEnd || ParseAlignment(keyword.ToString(), self) is not { } alignment ||
                alignment is CssBoxAlignment.SpaceBetween or CssBoxAlignment.SpaceAround or CssBoxAlignment.SpaceEvenly) return null;
            return new CssImmediateValue(property, alignment);
        },
    });

    private static void PairShorthand(string name, string first, string second) => CssPropertyRegistry.Register(new()
    {
        Name = name, Kind = CssPropertyKind.Shorthand,
        Expand = (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
        {
            if (!reader.TryReadIdent(out var a)) return false;
            var b = a;
            if (!reader.AtEnd && !reader.TryReadIdent(out b) || !reader.AtEnd) return false;
            var declarations = CssEngine.CompileDeclarations([
                new() { PropertyName = first, RawValue = a.ToString() }, new() { PropertyName = second, RawValue = b.ToString() },
            ], context);
            if (declarations.Length != 2) return false;
            output.AddRange(declarations);
            return true;
        },
    });
}

/// <summary>Combines placement longhands once, retaining the existing native Grid mapping.</summary>
internal sealed class CssGridPlacementParts
{
    private readonly Dictionary<string, (CssGridLine Line, bool State)> _lines = new(StringComparer.Ordinal);
    public void Set(string name, CssGridLine line, bool state) => _lines[name] = (line, state);

    public void Flush(ICssSetterSink sink)
    {
        foreach (var (name, part) in _lines)
        {
            sink.CurrentValueIsState = part.State;
            sink.Set(CssGridProperties.LineProperty(name), part.Line);
        }
        NativeAxis("grid-row-start", "grid-row-end", Grid.RowProperty, Grid.RowSpanProperty, sink);
        NativeAxis("grid-column-start", "grid-column-end", Grid.ColumnProperty, Grid.ColumnSpanProperty, sink);
    }

    private void NativeAxis(string startName, string endName, DependencyProperty position, DependencyProperty span, ICssSetterSink sink)
    {
        var hasStart = _lines.TryGetValue(startName, out var a);
        var hasEnd = _lines.TryGetValue(endName, out var b);
        if (!hasStart && !hasEnd || a.Line.Name is not null || b.Line.Name is not null || a.Line.Number < 0 || b.Line.Number < 0) return;
        var start = !a.Line.Span && a.Line.Number > 0 ? a.Line.Number - 1 : -1;
        var end = !b.Line.Span && b.Line.Number > 0 ? b.Line.Number - 1 : -1;
        var count = a.Line.Span ? a.Line.Number : b.Line.Span ? b.Line.Number : 1;
        if (start >= 0 && end >= 0) { if (end < start) (start, end) = (end, start); count = Math.Max(1, end - start); }
        else if (end >= 0) start = Math.Max(0, end - count);
        sink.CurrentValueIsState = a.State || b.State;
        sink.Set(position, Math.Max(0, start));
        sink.Set(span, Math.Max(1, count));
    }
}
