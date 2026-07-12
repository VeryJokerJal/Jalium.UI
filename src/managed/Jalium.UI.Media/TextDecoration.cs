using System.Collections;
using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Specifies the location of the text decoration with respect to the text.
/// </summary>
public enum TextDecorationLocation
{
    /// <summary>
    /// The text decoration is underline.
    /// </summary>
    Underline,

    /// <summary>
    /// The text decoration is overline.
    /// </summary>
    OverLine,

    /// <summary>
    /// The text decoration is strikethrough.
    /// </summary>
    Strikethrough,

    /// <summary>
    /// The text decoration is baseline.
    /// </summary>
    Baseline
}

/// <summary>
/// Represents a text decoration, such as an underline.
/// </summary>
public sealed class TextDecoration : Freezable
{
    public static readonly DependencyProperty LocationProperty =
        DependencyProperty.Register(nameof(Location), typeof(TextDecorationLocation), typeof(TextDecoration),
            new PropertyMetadata(TextDecorationLocation.Underline));

    public static readonly DependencyProperty PenProperty =
        DependencyProperty.Register(nameof(Pen), typeof(Pen), typeof(TextDecoration), new PropertyMetadata(null));

    public static readonly DependencyProperty PenOffsetProperty =
        DependencyProperty.Register(nameof(PenOffset), typeof(double), typeof(TextDecoration), new PropertyMetadata(0.0));

    public static readonly DependencyProperty PenOffsetUnitProperty =
        DependencyProperty.Register(nameof(PenOffsetUnit), typeof(TextDecorationUnit), typeof(TextDecoration),
            new PropertyMetadata(TextDecorationUnit.FontRecommended));

    public static readonly DependencyProperty PenThicknessUnitProperty =
        DependencyProperty.Register(nameof(PenThicknessUnit), typeof(TextDecorationUnit), typeof(TextDecoration),
            new PropertyMetadata(TextDecorationUnit.FontRecommended));

    /// <summary>
    /// Gets or sets the location of the text decoration.
    /// </summary>
    public TextDecorationLocation Location
    {
        get => (TextDecorationLocation)(GetValue(LocationProperty) ?? TextDecorationLocation.Underline);
        set => SetValue(LocationProperty, value);
    }

    /// <summary>Gets or sets the pen used to render the decoration.</summary>
    public Pen? Pen
    {
        get => (Pen?)GetValue(PenProperty);
        set => SetValue(PenProperty, value);
    }

    /// <summary>Gets or sets the decoration offset.</summary>
    public double PenOffset
    {
        get => (double)(GetValue(PenOffsetProperty) ?? 0.0);
        set => SetValue(PenOffsetProperty, value);
    }

    /// <summary>Gets or sets the unit used for <see cref="PenOffset"/>.</summary>
    public TextDecorationUnit PenOffsetUnit
    {
        get => (TextDecorationUnit)(GetValue(PenOffsetUnitProperty) ?? TextDecorationUnit.FontRecommended);
        set => SetValue(PenOffsetUnitProperty, value);
    }

    /// <summary>Gets or sets the unit used for the pen thickness.</summary>
    public TextDecorationUnit PenThicknessUnit
    {
        get => (TextDecorationUnit)(GetValue(PenThicknessUnitProperty) ?? TextDecorationUnit.FontRecommended);
        set => SetValue(PenThicknessUnitProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to draw the text decoration.
    /// </summary>
    public Brush? Brush
    {
        get => Pen?.Brush;
        set
        {
            var pen = CopyPen();
            pen.Brush = value;
            Pen = pen;
        }
    }

    /// <summary>
    /// Gets or sets the thickness of the decoration line.
    /// </summary>
    public double Thickness
    {
        get => Pen?.Thickness ?? 1.0;
        set
        {
            var pen = CopyPen();
            pen.Thickness = value;
            Pen = pen;
        }
    }

    /// <summary>
    /// Gets or sets the offset for the text decoration.
    /// </summary>
    public double Offset
    {
        get => PenOffset;
        set => PenOffset = value;
    }

    /// <summary>
    /// Gets or sets the offset unit for the text decoration.
    /// </summary>
    public TextDecorationUnit OffsetUnit
    {
        get => PenOffsetUnit;
        set => PenOffsetUnit = value;
    }

    /// <summary>
    /// Gets or sets the thickness unit for the text decoration.
    /// </summary>
    public TextDecorationUnit ThicknessUnit
    {
        get => PenThicknessUnit;
        set => PenThicknessUnit = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecoration"/> class.
    /// </summary>
    public TextDecoration()
    {
    }

    /// <summary>Initializes a WPF-compatible text decoration.</summary>
    public TextDecoration(
        TextDecorationLocation location,
        Pen? pen,
        double penOffset,
        TextDecorationUnit penOffsetUnit,
        TextDecorationUnit penThicknessUnit)
    {
        Location = location;
        Pen = pen;
        PenOffset = penOffset;
        PenOffsetUnit = penOffsetUnit;
        PenThicknessUnit = penThicknessUnit;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecoration"/> class with the specified location.
    /// </summary>
    /// <param name="location">The location of the text decoration.</param>
    /// <param name="brush">The brush used to draw the decoration.</param>
    /// <param name="thickness">The thickness of the decoration line.</param>
    /// <param name="offset">The offset of the decoration line.</param>
    /// <param name="offsetUnit">The unit for the offset.</param>
    /// <param name="thicknessUnit">The unit for the thickness.</param>
    public TextDecoration(TextDecorationLocation location, Brush? brush, double thickness,
        double offset, TextDecorationUnit offsetUnit, TextDecorationUnit thicknessUnit)
        : this(
            location,
            new Pen { Brush = brush, Thickness = thickness },
            offset,
            offsetUnit,
            thicknessUnit)
    {
    }

    public new TextDecoration Clone() => (TextDecoration)base.Clone();

    public new TextDecoration CloneCurrentValue() => (TextDecoration)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new TextDecoration();

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        if (((TextDecoration)sourceFreezable).Pen is { } sourcePen)
        {
            Pen = ClonePen(sourcePen);
        }
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        if (((TextDecoration)sourceFreezable).Pen is { } sourcePen)
        {
            Pen = ClonePen(sourcePen);
        }
    }

    private Pen CopyPen() => Pen is { } pen ? ClonePen(pen) : new Pen();

    private static Pen ClonePen(Pen source) => new()
    {
        Brush = source.Brush,
        Thickness = source.Thickness,
        StartLineCap = source.StartLineCap,
        EndLineCap = source.EndLineCap,
        DashCap = source.DashCap,
        LineJoin = source.LineJoin,
        MiterLimit = source.MiterLimit,
        DashStyle = source.DashStyle,
    };

    internal bool ValueEquals(TextDecoration? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Location != other.Location || PenOffset != other.PenOffset ||
            PenOffsetUnit != other.PenOffsetUnit || PenThicknessUnit != other.PenThicknessUnit)
        {
            return false;
        }

        if (Pen is null || other.Pen is null)
        {
            return Pen is null && other.Pen is null;
        }

        return Equals(Pen.Brush, other.Pen.Brush) && Pen.Thickness == other.Pen.Thickness &&
               Pen.StartLineCap == other.Pen.StartLineCap && Pen.EndLineCap == other.Pen.EndLineCap &&
               Pen.DashCap == other.Pen.DashCap && Pen.LineJoin == other.Pen.LineJoin &&
               Pen.MiterLimit == other.Pen.MiterLimit && Equals(Pen.DashStyle, other.Pen.DashStyle);
    }
}

/// <summary>
/// Specifies the units for a text decoration.
/// </summary>
public enum TextDecorationUnit
{
    /// <summary>
    /// The unit is a fraction of the font em size.
    /// </summary>
    FontRecommended,

    /// <summary>
    /// The unit is a fraction of the font em size.
    /// </summary>
    FontRenderingEmSize,

    /// <summary>
    /// The unit is in pixels.
    /// </summary>
    Pixel
}

/// <summary>
/// Represents a collection of TextDecoration objects.
/// </summary>
[TypeConverter(typeof(Jalium.UI.TextDecorationCollectionConverter))]
public sealed class TextDecorationCollection : FreezableCollection<TextDecoration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecorationCollection"/> class.
    /// </summary>
    public TextDecorationCollection()
    {
    }

    /// <summary>Initializes an empty collection with the specified capacity.</summary>
    public TextDecorationCollection(int capacity) : base(capacity)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecorationCollection"/> class with the specified decorations.
    /// </summary>
    /// <param name="decorations">The initial decorations.</param>
    public TextDecorationCollection(IEnumerable<TextDecoration> decorations) : base(decorations)
    {
    }

    /// <summary>Adds one decoration to the collection.</summary>
    public new void Add(TextDecoration value) => base.Add(value);

    /// <summary>Adds every decoration in <paramref name="textDecorations"/>.</summary>
#pragma warning disable CS3021
    [CLSCompliant(false)]
    public void Add(IEnumerable<TextDecoration> textDecorations)
    {
        ArgumentNullException.ThrowIfNull(textDecorations);
        foreach (var decoration in textDecorations)
        {
            Add(decoration);
        }
    }
#pragma warning restore CS3021

    /// <summary>
    /// Creates a clone with all value-equal decorations from the supplied sequence removed.
    /// </summary>
    public bool TryRemove(IEnumerable<TextDecoration> textDecorations, out TextDecorationCollection result)
    {
        ArgumentNullException.ThrowIfNull(textDecorations);
        result = Clone();
        var removed = false;
        foreach (var decoration in textDecorations)
        {
            for (var index = result.Count - 1; index >= 0; index--)
            {
                if (result[index].ValueEquals(decoration))
                {
                    result.RemoveAt(index);
                    removed = true;
                }
            }
        }

        return removed;
    }

    public new TextDecorationCollection Clone() => (TextDecorationCollection)base.Clone();

    public new TextDecorationCollection CloneCurrentValue() => (TextDecorationCollection)base.CloneCurrentValue();

    public new Enumerator GetEnumerator() => new(base.GetEnumerator());

    /// <summary>
    /// Determines whether the collection contains a decoration at the specified location.
    /// </summary>
    /// <param name="location">The location to check.</param>
    /// <returns>True if the collection contains a decoration at the location; otherwise, false.</returns>
    public bool HasDecoration(TextDecorationLocation location)
    {
        return this.Any(d => d.Location == location);
    }

    /// <summary>
    /// Removes all decorations at the specified location.
    /// </summary>
    /// <param name="location">The location of decorations to remove.</param>
    public void RemoveDecoration(TextDecorationLocation location)
    {
        for (var index = Count - 1; index >= 0; index--)
        {
            if (this[index].Location == location)
            {
                RemoveAt(index);
            }
        }
    }

    protected override Freezable CreateInstanceCore() => new TextDecorationCollection();

    protected override void CloneCore(Freezable source) => base.CloneCore(source);

    protected override void CloneCurrentValueCore(Freezable source) => base.CloneCurrentValueCore(source);

    protected override void GetAsFrozenCore(Freezable source) => base.GetAsFrozenCore(source);

    protected override void GetCurrentValueAsFrozenCore(Freezable source) =>
        base.GetCurrentValueAsFrozenCore(source);

    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);

    /// <summary>Enumerates the collection while detecting concurrent modification.</summary>
    public new struct Enumerator : IEnumerator<TextDecoration>, IEnumerator
    {
        private FreezableCollection<TextDecoration>.Enumerator _inner;

        internal Enumerator(FreezableCollection<TextDecoration>.Enumerator inner)
        {
            _inner = inner;
        }

        public TextDecoration Current => _inner.Current;

        object IEnumerator.Current => Current;

        public bool MoveNext() => _inner.MoveNext();

        public void Reset() => _inner.Reset();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Provides a set of predefined text decorations.
/// </summary>
public static class TextDecorations
{
    private static TextDecorationCollection? _underline;
    private static TextDecorationCollection? _strikethrough;
    private static TextDecorationCollection? _overLine;
    private static TextDecorationCollection? _baseline;

    /// <summary>
    /// Gets a text decoration collection that specifies an underline.
    /// </summary>
    public static TextDecorationCollection Underline
    {
        get
        {
            _underline ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.Underline
                    }
                };
            return _underline;
        }
    }

    /// <summary>
    /// Gets a text decoration collection that specifies a strikethrough.
    /// </summary>
    public static TextDecorationCollection Strikethrough
    {
        get
        {
            _strikethrough ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.Strikethrough
                    }
                };
            return _strikethrough;
        }
    }

    /// <summary>
    /// Gets a text decoration collection that specifies an overline.
    /// </summary>
    public static TextDecorationCollection OverLine
    {
        get
        {
            _overLine ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.OverLine
                    }
                };
            return _overLine;
        }
    }

    /// <summary>
    /// Gets a text decoration collection that specifies a baseline decoration.
    /// </summary>
    public static TextDecorationCollection Baseline
    {
        get
        {
            _baseline ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.Baseline
                    }
                };
            return _baseline;
        }
    }
}
