using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal enum CssBackgroundSizeMode
{
    Auto,
    Fill,
    Contain,
    Cover,
    Explicit,
}

internal readonly record struct CssBackgroundSize(
    CssBackgroundSizeMode Mode, CssLength? Width, CssLength? Height, CssLengthContext Lengths)
{
    internal CssBackgroundSize(CssBackgroundSizeMode mode)
        : this(mode, null, null, CssLengthContext.Default) { }

    internal CssBackgroundSize(CssLength? width, CssLength? height)
        : this(CssBackgroundSizeMode.Explicit, width, height, CssLengthContext.Default) { }

    internal CssBackgroundSize ForElement(in CssLengthContext lengths) => this with { Lengths = lengths };

    internal void ObserveDependencies(in CssLengthContext lengths)
    {
        Width?.ObserveContainerDependencies(lengths);
        Height?.ObserveContainerDependencies(lengths);
    }

    internal static double Resolve(CssLength length, double basis, in CssLengthContext context)
    {
        double value;
        if (length.Unit == CssUnit.Percent)
            value = length.Value / 100 * basis;
        else if (length.Expression is { } expression)
        {
            if (!expression.TryEvaluate(context, basis, out value)) return double.NaN;
            value = Math.Max(0, value); // Calculated out-of-range sizes clamp at used-value time.
        }
        else if (!length.TryResolve(context, CssPercentBasis.NotSupported, out value))
            return double.NaN;
        return value;
    }
}

internal enum CssBackgroundRepeatMode
{
    Repeat,
    NoRepeat,
    Space,
    Round,
}

internal readonly record struct CssBackgroundRepeat(CssBackgroundRepeatMode X, CssBackgroundRepeatMode Y)
{
    internal CssBackgroundRepeat(bool x, bool y)
        : this(x ? CssBackgroundRepeatMode.Repeat : CssBackgroundRepeatMode.NoRepeat,
            y ? CssBackgroundRepeatMode.Repeat : CssBackgroundRepeatMode.NoRepeat) { }

    internal static CssBackgroundRepeat Both => new(CssBackgroundRepeatMode.Repeat, CssBackgroundRepeatMode.Repeat);
}

internal readonly record struct CssBackgroundPositionAxis(CssLength Offset, bool FromFarEdge = false)
{
    internal double Resolve(double freeSpace, in CssLengthContext context)
    {
        double offset;
        if (Offset.Unit == CssUnit.Percent)
            offset = Offset.Value / 100 * freeSpace;
        else if (Offset.Expression is { } expression)
        {
            if (!expression.TryEvaluate(context, freeSpace, out offset)) return double.NaN;
        }
        else if (!Offset.TryResolve(context, CssPercentBasis.NotSupported, out offset))
            return double.NaN;

        return FromFarEdge ? freeSpace - offset : offset;
    }
}

internal readonly record struct CssBackgroundPosition(
    CssBackgroundPositionAxis X, CssBackgroundPositionAxis Y, CssLengthContext Lengths)
{
    internal static CssBackgroundPosition Initial => new(
        new(new CssLength(0, CssUnit.Percent)),
        new(new CssLength(0, CssUnit.Percent)), CssLengthContext.Default);

    internal CssBackgroundPosition ForElement(in CssLengthContext lengths) => this with { Lengths = lengths };

    internal void ObserveDependencies(in CssLengthContext lengths)
    {
        X.Offset.ObserveContainerDependencies(lengths);
        Y.Offset.ObserveContainerDependencies(lengths);
    }
}

/// <summary>One axis of a CSS bitmap pattern, shared by software sampling and native tiles.</summary>
internal readonly record struct CssBackgroundAxisPattern(
    CssBackgroundRepeatMode Mode, double Origin, double TileSize, double Step,
    double AreaSize, double SpaceCount)
{
    internal static CssBackgroundAxisPattern Create(double areaStart, double areaSize,
        double tileSize, double positionedStart, CssBackgroundRepeatMode mode)
    {
        if (mode == CssBackgroundRepeatMode.Space)
        {
            var count = Math.Floor(areaSize / tileSize);
            if (count >= 2)
                return new(mode, areaStart, tileSize, (areaSize - tileSize) / (count - 1),
                    areaSize, count);
            mode = CssBackgroundRepeatMode.NoRepeat;
        }
        return new(mode, positionedStart, tileSize, tileSize, areaSize, 0);
    }

    internal bool TryMap(double coordinate, out double withinTile)
    {
        withinTile = double.NaN;
        if (Mode == CssBackgroundRepeatMode.NoRepeat)
            withinTile = coordinate - Origin;
        else if (Mode == CssBackgroundRepeatMode.Space)
        {
            var period = Math.Floor((coordinate - Origin) / AreaSize);
            var withinPeriod = coordinate - Origin - period * AreaSize;
            var index = Math.Floor(withinPeriod / Step);
            withinTile = withinPeriod - index * Step;
        }
        else
        {
            var index = Math.Floor((coordinate - Origin) / Step);
            withinTile = coordinate - Origin - index * Step;
        }
        return withinTile >= 0 && withinTile < TileSize;
    }

    internal List<double> TileStarts(double coverageStart, double coverageEnd, int maxTiles)
    {
        var starts = new List<double>();
        if (coverageEnd <= coverageStart || maxTiles <= 0) return starts;
        if (Mode == CssBackgroundRepeatMode.NoRepeat)
        {
            if (Origin < coverageEnd && Origin + TileSize > coverageStart) starts.Add(Origin);
            return starts;
        }

        double first;
        if (Mode == CssBackgroundRepeatMode.Space)
        {
            var period = Math.Floor((coverageStart - Origin) / AreaSize);
            var local = coverageStart - Origin - period * AreaSize;
            first = period * SpaceCount + Math.Min(SpaceCount - 1, Math.Floor(local / Step));
        }
        else first = Math.Floor((coverageStart - Origin) / Step);

        for (var index = first; starts.Count < maxTiles; index++)
        {
            double start;
            if (Mode == CssBackgroundRepeatMode.Space)
            {
                var period = Math.Floor(index / SpaceCount);
                var local = index - period * SpaceCount;
                start = Origin + period * AreaSize + local * Step;
            }
            else start = Origin + index * Step;
            if (!double.IsFinite(start) || start >= coverageEnd) break;
            if (start + TileSize > coverageStart) starts.Add(start);
            if (index + 1 == index) break; // No useful progress at extreme double magnitudes.
        }
        return starts;
    }
}

internal readonly record struct CssBackgroundTilePattern(
    Rect ImageRect, CssBackgroundAxisPattern X, CssBackgroundAxisPattern Y);

/// <summary>Geometry used only by CSS-created image brushes.</summary>
internal sealed class CssBackgroundImageLayout
{
    private readonly Thickness? _positioningInsets;
    internal CssBackgroundSizeMode Size { get; }
    internal CssBackgroundSize SizeValue { get; }
    internal CssBackgroundRepeat Repeat { get; }
    internal bool RepeatX { get; }
    internal bool RepeatY { get; }
    internal CssBackgroundPosition Position { get; }

    internal CssBackgroundImageLayout(CssBackgroundSizeMode size, CssBackgroundRepeat repeat,
        CssBackgroundPosition? position = null)
        : this(new CssBackgroundSize(size), repeat, position) { }

    internal CssBackgroundImageLayout(CssBackgroundSize size, CssBackgroundRepeat repeat,
        CssBackgroundPosition? position = null, Thickness? positioningInsets = null)
    {
        Size = size.Mode;
        SizeValue = size;
        Repeat = repeat;
        RepeatX = repeat.X != CssBackgroundRepeatMode.NoRepeat;
        RepeatY = repeat.Y != CssBackgroundRepeatMode.NoRepeat;
        Position = position ?? CssBackgroundPosition.Initial;
        _positioningInsets = positioningInsets;
    }

    internal CssBackgroundImageLayout WithPositioningInsets(Thickness insets)
        => new(SizeValue, Repeat, Position, insets);

    internal Rect ImageRect(Rect area, double intrinsicWidth, double intrinsicHeight)
        => TilePattern(area, intrinsicWidth, intrinsicHeight).ImageRect;

    internal CssBackgroundTilePattern TilePattern(Rect area, double intrinsicWidth, double intrinsicHeight)
        => TilePatternCore(area, intrinsicWidth, intrinsicHeight, hasNaturalSize: true);

    // Gradients have neither natural dimensions nor a natural aspect ratio.
    // An auto axis uses the corresponding positioning-area dimension.
    internal CssBackgroundTilePattern GradientTilePattern(Rect area)
        => TilePatternCore(area, 0, 0, hasNaturalSize: false);

    private CssBackgroundTilePattern TilePatternCore(Rect area, double intrinsicWidth,
        double intrinsicHeight, bool hasNaturalSize)
    {
        if (_positioningInsets is { } inset)
            area = new Rect(area.X + inset.Left, area.Y + inset.Top,
                Math.Max(0, area.Width - inset.Left - inset.Right),
                Math.Max(0, area.Height - inset.Top - inset.Bottom));
        if (area.Width < 0 || area.Height < 0 ||
            hasNaturalSize && (intrinsicWidth <= 0 || intrinsicHeight <= 0 ||
                !double.IsFinite(intrinsicWidth) || !double.IsFinite(intrinsicHeight)))
            return new(Rect.Empty, default, default);

        double width, height;
        switch (Size)
        {
            case CssBackgroundSizeMode.Auto:
                width = hasNaturalSize ? intrinsicWidth : area.Width;
                height = hasNaturalSize ? intrinsicHeight : area.Height;
                break;
            case CssBackgroundSizeMode.Contain:
            case CssBackgroundSizeMode.Cover:
                if (!hasNaturalSize)
                {
                    width = area.Width;
                    height = area.Height;
                    break;
                }
                var xScale = area.Width / intrinsicWidth;
                var yScale = area.Height / intrinsicHeight;
                var scale = Size == CssBackgroundSizeMode.Cover
                    ? Math.Max(xScale, yScale) : Math.Min(xScale, yScale);
                width = intrinsicWidth * scale;
                height = intrinsicHeight * scale;
                break;
            case CssBackgroundSizeMode.Explicit:
                width = SizeValue.Width is { } explicitWidth
                    ? CssBackgroundSize.Resolve(explicitWidth, area.Width, SizeValue.Lengths)
                    : double.NaN;
                height = SizeValue.Height is { } explicitHeight
                    ? CssBackgroundSize.Resolve(explicitHeight, area.Height, SizeValue.Lengths)
                    : double.NaN;
                if (SizeValue.Width is null)
                    width = hasNaturalSize ? height * intrinsicWidth / intrinsicHeight : area.Width;
                if (SizeValue.Height is null)
                    height = hasNaturalSize ? width * intrinsicHeight / intrinsicWidth : area.Height;
                break;
            default:
                width = area.Width;
                height = area.Height;
                break;
        }

        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height))
            return new(Rect.Empty, default, default);

        var roundX = Repeat.X == CssBackgroundRepeatMode.Round;
        var roundY = Repeat.Y == CssBackgroundRepeatMode.Round;
        if (roundX) width = area.Width / Math.Max(1, Math.Floor(area.Width / width + 0.5));
        if (roundY) height = area.Height / Math.Max(1, Math.Floor(area.Height / height + 0.5));
        if (hasNaturalSize && roundX && !roundY && (Size == CssBackgroundSizeMode.Auto ||
            Size == CssBackgroundSizeMode.Explicit && SizeValue.Height is null))
            height = width * intrinsicHeight / intrinsicWidth;
        if (hasNaturalSize && roundY && !roundX && (Size == CssBackgroundSizeMode.Auto ||
            Size == CssBackgroundSizeMode.Explicit && SizeValue.Width is null))
            width = height * intrinsicWidth / intrinsicHeight;

        var x = area.X + Position.X.Resolve(area.Width - width, Position.Lengths);
        var y = area.Y + Position.Y.Resolve(area.Height - height, Position.Lengths);
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(x) || !double.IsFinite(y))
            return new(Rect.Empty, default, default);

        var axisX = CssBackgroundAxisPattern.Create(area.X, area.Width, width, x, Repeat.X);
        var axisY = CssBackgroundAxisPattern.Create(area.Y, area.Height, height, y, Repeat.Y);
        return new(new Rect(axisX.Origin, axisY.Origin, width, height), axisX, axisY);
    }
}
