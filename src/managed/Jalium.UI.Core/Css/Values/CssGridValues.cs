namespace Jalium.UI.Styling;

internal enum CssGridBreadthKind { Auto, Length, MinContent, MaxContent, Flex }

internal readonly record struct CssGridBreadth(CssGridBreadthKind Kind, CssLength Length = default, double Flex = 0)
{
    public double Resolve(in CssLengthContext context, double basis)
    {
        if (Kind != CssGridBreadthKind.Length) return double.NaN;
        var value = Length.Unit == CssUnit.Percent ? Length.Value * basis / 100
            : Length.Expression is { } expression
                ? expression.TryEvaluate(context, basis, out var computed) ? computed : double.NaN
                : Length.TryResolve(context, CssPercentBasis.NotSupported, out var pixels) ? pixels : double.NaN;
        return double.IsFinite(value) ? Math.Max(0, value) : double.NaN;
    }
}

internal sealed record CssGridTrack(CssGridBreadth Min, CssGridBreadth Max, CssLength? FitLimit = null)
{
    public static readonly CssGridTrack Auto = new(new(CssGridBreadthKind.Auto), new(CssGridBreadthKind.Auto));
    public bool IsFixedSize => Min.Kind == CssGridBreadthKind.Length ||
        Max.Kind == CssGridBreadthKind.Length && Min.Kind != CssGridBreadthKind.Flex;
}

internal sealed record CssGridTrackPart(CssGridTrack? Track = null, string[]? Names = null,
    CssGridTrackList? Repeat = null, int Count = 0, bool AutoFit = false);

/// <summary>Track syntax retains percentages, expressions, named lines and responsive repetition.</summary>
internal sealed class CssGridTrackList(CssGridTrackPart[] parts, bool subgrid = false)
{
    // CSS Grid permits a UA limit on the extent of an explicit/implicit grid.
    internal const int MaxTracks = 4096;
    public static readonly CssGridTrackList Empty = new([]);
    public static readonly CssGridTrackList Auto = new([new(Track: CssGridTrack.Auto)]);
    public CssGridTrackPart[] Parts { get; } = parts;
    public bool IsSubgrid { get; } = subgrid;
    public int MinimumSubgridSpan => Math.Max(1, NameGroupCount() - 1);

    internal void ObserveContainerDependencies(in CssLengthContext context)
    {
        foreach (var part in Parts)
        {
            if (part.Track is { } track)
            {
                track.Min.Length.ObserveContainerDependencies(context);
                track.Max.Length.ObserveContainerDependencies(context);
                track.FitLimit?.ObserveContainerDependencies(context);
            }
            part.Repeat?.ObserveContainerDependencies(context);
        }
    }

    public static CssGridTrackList? Parse(ref CssTokenReader reader, bool implicitTracks = false, bool allowRepeat = true, bool allowSubgrid = true)
    {
        var probe = reader;
        if (!implicitTracks && probe.TryReadIdent(out var keyword) &&
            keyword.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        { reader = probe; return Empty; }

        probe = reader;
        if (!implicitTracks && allowSubgrid && probe.TryReadIdent(out keyword) && keyword.Equals("subgrid", StringComparison.OrdinalIgnoreCase))
        {
            var names = ReadSubgridNames(ref probe, true);
            if (names is null) return null;
            reader = probe;
            return new(names, true);
        }

        var parts = new List<CssGridTrackPart>();
        var hasTrack = false;
        var hasAutoRepeat = false;
        while (!reader.AtEnd)
        {
            if (!implicitTracks && reader.TryReadDelimiter('['))
            {
                var names = new List<string>();
                while (!reader.TryReadDelimiter(']'))
                {
                    if (!reader.TryReadIdent(out var name) || IsReservedName(name)) return null;
                    names.Add(name.ToString());
                }
                parts.Add(new(Names: names.ToArray()));
                continue;
            }

            probe = reader;
            if (allowRepeat && !implicitTracks && probe.TryReadFunction(out var function, out var args) &&
                function.Equals("repeat", StringComparison.OrdinalIgnoreCase))
            {
                var countReader = args;
                var count = 0;
                var autoFit = false;
                if (countReader.TryReadIdent(out var repetition))
                {
                    autoFit = repetition.Equals("auto-fit", StringComparison.OrdinalIgnoreCase);
                    if ((!autoFit && !repetition.Equals("auto-fill", StringComparison.OrdinalIgnoreCase)) || hasAutoRepeat) return null;
                    hasAutoRepeat = true;
                }
                else
                {
                    if (!countReader.TryReadInteger(out var number, minimum: 1)) return null;
                    count = Math.Min(number, MaxTracks);
                }
                if (!countReader.TryReadComma()) return null;
                var repeated = Parse(ref countReader, allowRepeat: false, allowSubgrid: false);
                if (repeated is null || repeated.Parts.Length == 0) return null;
                parts.Add(new(Repeat: repeated, Count: count, AutoFit: autoFit));
                hasTrack = true;
                reader = probe;
                continue;
            }

            if (ReadTrack(ref reader) is not { } track) return null;
            parts.Add(new(Track: track));
            hasTrack = true;
        }
        if (!hasTrack) return null;
        var result = new CssGridTrackList(parts.ToArray());
        if (hasAutoRepeat && !result.AllTracksFixed()) return null;
        return result;
    }

    private bool AllTracksFixed() => Parts.All(part => part.Track?.IsFixedSize ?? part.Repeat?.AllTracksFixed() ?? true);

    private static CssGridTrackPart[]? ReadSubgridNames(ref CssTokenReader reader, bool allowRepeat)
    {
        var parts = new List<CssGridTrackPart>();
        var automatic = false;
        while (!reader.AtEnd)
        {
            if (reader.TryReadDelimiter('['))
            {
                var names = new List<string>();
                while (!reader.TryReadDelimiter(']'))
                {
                    if (!reader.TryReadIdent(out var name) || IsReservedName(name)) return null;
                    names.Add(name.ToString());
                }
                parts.Add(new(Names: names.ToArray()));
                continue;
            }
            if (!allowRepeat || !reader.TryReadFunction(out var function, out var arguments) ||
                !function.Equals("repeat", StringComparison.OrdinalIgnoreCase)) return null;
            var countReader = arguments;
            var count = 0;
            if (countReader.TryReadIdent(out var keyword))
            {
                if (automatic || !keyword.Equals("auto-fill", StringComparison.OrdinalIgnoreCase)) return null;
                automatic = true;
            }
            else if (countReader.TryReadInteger(out var value, minimum: 1)) count = Math.Min(value, MaxTracks + 1);
            else return null;
            if (!countReader.TryReadComma()) return null;
            var repeated = ReadSubgridNames(ref countReader, false);
            if (repeated is null || repeated.Length == 0) return null;
            parts.Add(new(Repeat: new CssGridTrackList(repeated, true), Count: count));
        }
        return parts.ToArray();
    }

    private int NameGroupCount() => (int)Math.Min(MaxTracks + 1L,
        Parts.Sum(part => part.Names is not null ? 1L : part.Repeat is { } repeat ? (long)part.Count * repeat.NameGroupCount() : 0));

    internal Dictionary<string, List<int>> ExpandSubgridNames(int span)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var line = 0;
        var unused = Math.Max(0, span + 1 - NameGroupCount());
        void AppendNames(CssGridTrackList list)
        {
            foreach (var part in list.Parts)
            {
                if (part.Names is { } names)
                {
                    if (line > span) return;
                    foreach (var name in names)
                    {
                        if (!result.TryGetValue(name, out var indices)) result[name] = indices = [];
                        if (!indices.Contains(line)) indices.Add(line);
                    }
                    line++;
                }
                else if (part.Repeat is { } repeat)
                {
                    var count = part.Count > 0 ? part.Count : unused / Math.Max(1, repeat.NameGroupCount());
                    for (var i = 0; i < count && line <= span; i++) AppendNames(repeat);
                }
            }
        }
        AppendNames(this);
        return result;
    }

    internal static CssGridTrack? ReadTrack(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadFunction(out var name, out var args))
        {
            if (name.Equals("minmax", StringComparison.OrdinalIgnoreCase))
            {
                if (!ReadBreadth(ref args, false, out var min) || !args.TryReadComma() ||
                    !ReadBreadth(ref args, true, out var max) || !args.AtEnd) return null;
                reader = probe;
                return new(min, max);
            }
            if (name.Equals("fit-content", StringComparison.OrdinalIgnoreCase))
            {
                if (!ReadNonnegativeLength(ref args, out var limit) || !args.AtEnd) return null;
                reader = probe;
                return new(new(CssGridBreadthKind.Auto), new(CssGridBreadthKind.MaxContent), limit);
            }
        }
        if (!ReadBreadth(ref reader, true, out var breadth)) return null;
        return breadth.Kind == CssGridBreadthKind.Flex
            ? new(new(CssGridBreadthKind.Auto), breadth) : new(breadth, breadth);
    }

    private static bool ReadBreadth(ref CssTokenReader reader, bool allowFlex, out CssGridBreadth breadth)
    {
        breadth = default;
        var probe = reader;
        if (ReadNonnegativeLength(ref probe, out var length))
        { reader = probe; breadth = new(CssGridBreadthKind.Length, length); return true; }
        probe = reader;
        if (allowFlex && probe.TryReadNumber(out var factor, out var unit) && unit == CssUnit.Fr &&
            double.IsFinite(factor) && (factor >= 0 || probe.NumberWasCalculated))
        { reader = probe; breadth = new(CssGridBreadthKind.Flex, Flex: Math.Max(0, factor)); return true; }
        probe = reader;
        if (!probe.TryReadIdent(out var keyword)) return false;
        CssGridBreadthKind? kind = keyword.ToString().ToLowerInvariant() switch
        {
            "auto" => CssGridBreadthKind.Auto, "min-content" => CssGridBreadthKind.MinContent,
            "max-content" => CssGridBreadthKind.MaxContent, _ => null,
        };
        if (kind is null) return false;
        reader = probe;
        breadth = new(kind.Value);
        return true;
    }

    internal static bool ReadNonnegativeLength(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (!probe.TryReadLength(out length) ||
            length.Expression is null && (!double.IsFinite(length.Value) || length.Value < 0 ||
                length.Unit == CssUnit.None && length.Value != 0)) return false;
        reader = probe;
        return true;
    }

    internal static bool IsReservedName(ReadOnlySpan<char> name) =>
        name.Equals("auto", StringComparison.OrdinalIgnoreCase) || name.Equals("span", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("default", StringComparison.OrdinalIgnoreCase) || CssPropertyMetadata.IsWideKeyword(name.ToString());

    public CssExpandedGridTracks Expand(in CssLengthContext context, double available, double gap)
    {
        if (IsSubgrid) return new([], new(StringComparer.Ordinal), []);
        var tracks = new List<CssGridTrack>();
        var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var autoFit = new HashSet<int>();
        var sizes = CountAndSize(context, available);
        var autoPart = Parts.FirstOrDefault(part => part.Repeat is not null && part.Count == 0);
        var repetitions = 1;
        if (autoPart?.Repeat is { } pattern && double.IsFinite(available))
        {
            var repeated = pattern.CountAndSize(context, available);
            // Floor the definite size at one CSS pixel when deciding repetition count.
            var repeatedSize = Math.Max(repeated.Size, repeated.Count);
            var denominator = repeatedSize + repeated.Count * gap;
            repetitions = denominator > 0 ? (int)Math.Clamp(Math.Floor((available - sizes.Size + gap - sizes.Count * gap) / denominator), 1, MaxTracks) : 1;
        }
        Append(this, repetitions, tracks, lines, autoFit, false);
        return new(tracks, lines, autoFit);
    }

    private (int Count, double Size) CountAndSize(in CssLengthContext context, double available)
    {
        var count = 0;
        var size = 0d;
        foreach (var part in Parts)
        {
            if (part.Track is { } track)
            {
                var min = track.Min.Resolve(context, available);
                var max = track.Max.Resolve(context, available);
                size += double.IsFinite(max) ? Math.Max(max, double.IsFinite(min) ? min : 0) : double.IsFinite(min) ? min : 0;
                count++;
            }
            else if (part.Repeat is { } repeat && part.Count > 0)
            {
                var nested = repeat.CountAndSize(context, available);
                count += nested.Count * part.Count;
                size += nested.Size * part.Count;
            }
        }
        return (count, size);
    }

    private static void Append(CssGridTrackList list, int automaticCount, List<CssGridTrack> tracks,
        Dictionary<string, List<int>> lines, HashSet<int> autoFit, bool collapseEmpty)
    {
        foreach (var part in list.Parts)
        {
            if (part.Names is { } names)
            {
                foreach (var name in names)
                {
                    if (!lines.TryGetValue(name, out var indices)) lines[name] = indices = [];
                    if (indices.Count == 0 || indices[^1] != tracks.Count) indices.Add(tracks.Count);
                }
            }
            else if (part.Track is { } track)
            {
                if (tracks.Count >= MaxTracks) return;
                if (collapseEmpty) autoFit.Add(tracks.Count);
                tracks.Add(track);
            }
            else if (part.Repeat is { } repeat)
            {
                var count = part.Count == 0 ? automaticCount : part.Count;
                for (var i = 0; i < count && tracks.Count < MaxTracks; i++)
                    Append(repeat, 1, tracks, lines, autoFit, collapseEmpty || part.AutoFit);
            }
        }
    }
}

internal sealed record CssExpandedGridTracks(List<CssGridTrack> Tracks, Dictionary<string, List<int>> Lines, HashSet<int> AutoFit);
internal sealed record CssComputedGridTracks(CssGridTrackList Tracks, CssLengthContext Context);

internal readonly record struct CssGridLine(int Number = 0, string? Name = null, bool Span = false)
{
    public bool IsAuto => Number == 0 && Name is null && !Span;
    public bool IsNameOnly => Number == 0 && Name is not null && !Span;

    public static bool Read(ref CssTokenReader reader, out CssGridLine line)
    {
        line = default;
        var probe = reader;
        var number = 0;
        string? name = null;
        var span = false;
        var sawToken = false;
        while (!probe.AtEnd && (!probe.TryPeekChar(out var next) || next != '/'))
        {
            var token = probe;
            if (token.TryReadIdent(out var ident))
            {
                if (ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    if (sawToken || !token.AtEnd && (!token.TryPeekChar(out next) || next != '/')) return false;
                    reader = token;
                    return true;
                }
                if (ident.Equals("span", StringComparison.OrdinalIgnoreCase))
                { if (span) return false; span = true; }
                else
                { if (name is not null || CssGridTrackList.IsReservedName(ident)) return false; name = ident.ToString(); }
            }
            else if (token.TryReadInteger(out var value) && value != 0)
            {
                if (number != 0) return false;
                number = (int)Math.Clamp(value, -CssGridTrackList.MaxTracks, CssGridTrackList.MaxTracks);
            }
            else return false;
            sawToken = true;
            probe = token;
        }
        if (!sawToken || span && (number < 0 || number == 0 && name is null)) return false;
        line = new(span && number == 0 ? 1 : number, name, span);
        reader = probe;
        return true;
    }
}

internal readonly record struct CssGridArea(int Row, int Column, int RowSpan, int ColumnSpan);

internal sealed record CssGridAreas(int Rows, int Columns, Dictionary<string, CssGridArea> Areas)
{
    public static readonly CssGridAreas Empty = new(0, 0, new(StringComparer.Ordinal));

    public static CssGridAreas? Parse(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var name) && name.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        { reader = probe; return Empty; }
        var rows = new List<List<string?>>();
        while (!reader.AtEnd)
        {
            if (!reader.TryReadString(out var text)) return null;
            var row = new List<string?>();
            for (var i = 0; i < text.Length;)
            {
                if (CssTokenReader.IsCssWhitespace(text[i])) { i++; continue; }
                var start = i;
                if (text[i] == '.')
                { while (i < text.Length && text[i] == '.') i++; row.Add(null); }
                else
                {
                    while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '-' or '_' || text[i] >= 128)) i++;
                    if (i == start) return null;
                    row.Add(text[start..i]);
                }
                if (row.Count > CssGridTrackList.MaxTracks) return null;
            }
            if (row.Count == 0 || rows.Count > 0 && row.Count != rows[0].Count || rows.Count >= CssGridTrackList.MaxTracks) return null;
            rows.Add(row);
        }
        if (rows.Count == 0) return null;
        var cells = new Dictionary<string, List<(int Row, int Column)>>(StringComparer.Ordinal);
        for (var row = 0; row < rows.Count; row++)
            for (var column = 0; column < rows[row].Count; column++)
                if (rows[row][column] is { } cell)
                {
                    if (!cells.TryGetValue(cell, out var positions)) cells[cell] = positions = [];
                    positions.Add((row, column));
                }
        var areas = new Dictionary<string, CssGridArea>(StringComparer.Ordinal);
        foreach (var (cell, positions) in cells)
        {
            var top = positions.Min(p => p.Row); var bottom = positions.Max(p => p.Row);
            var left = positions.Min(p => p.Column); var right = positions.Max(p => p.Column);
            if (positions.Count != (bottom - top + 1) * (right - left + 1)) return null;
            areas[cell] = new(top, left, bottom - top + 1, right - left + 1);
        }
        return new(rows.Count, rows[0].Count, areas);
    }
}
