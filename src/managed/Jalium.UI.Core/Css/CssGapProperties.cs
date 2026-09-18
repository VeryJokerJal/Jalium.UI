using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Styling;

/// <summary>Retains gap lengths for formatting contexts and shares the existing native spacing mappings.</summary>
internal static class CssGapProperties
{
    internal static readonly DependencyProperty RowProperty = GapProperty("CssRowGap");
    internal static readonly DependencyProperty ColumnProperty = GapProperty("CssColumnGap");
    private static DependencyProperty GapProperty(string name) => DependencyProperty.RegisterAttached(name, typeof(CssLayoutLength),
        typeof(CssGapProperties), new PropertyMetadata(CssLayoutLength.Unset, static (target, _) =>
        { if (target is UIElement element) element.InvalidateMeasure(); }));

    internal static void Register()
    {
        Axis("row-gap", true); Axis("column-gap", false);
        CssPropertyRegistry.Register(new()
        {
            Name = "gap", Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                if (!Read(ref reader, out var row)) return false;
                var column = row;
                if (!reader.AtEnd && !Read(ref reader, out column) || !reader.AtEnd) return false;
                output.Add(new("row-gap", new GapValue(true, row), false));
                output.Add(new("column-gap", new GapValue(false, column), false));
                return true;
            },
        });
    }

    private static bool Read(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var keyword) && keyword.Equals("normal", StringComparison.OrdinalIgnoreCase))
        { reader = probe; length = new(0, CssUnit.Normal); return true; }
        return CssGridTrackList.ReadNonnegativeLength(ref reader, out length);
    }

    private static void Axis(string name, bool row) => CssPropertyRegistry.Register(new()
    {
        Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = row ? RowProperty : ColumnProperty,
        Parse = (ref CssTokenReader reader, CssCompileContext _) => Read(ref reader, out var length) && reader.AtEnd ? new GapValue(row, length) : null,
    });

    private sealed class GapValue(bool row, CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var computed = length.Unit == CssUnit.Normal ? CssLayoutLength.Normal
                : length.Expression is { } expression ? CssLayoutLength.Math(expression, context.Lengths)
                : length.Unit == CssUnit.Percent ? CssLayoutLength.Percent(length.Value / 100)
                : length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var value) ? CssLayoutLength.Px(Math.Max(0, value)) : default;
            (context.Slots.Gaps ??= new()).Set(row, computed, context.Slots.CurrentContributionIsState);
            return true;
        }
    }

    internal static double Resolve(Panel owner, bool row, double basis)
    {
        var native = owner is Grid ? row ? Grid.RowSpacingProperty : Grid.ColumnSpacingProperty
            : row ? FlexPanel.RowSpacingProperty : FlexPanel.ColumnSpacingProperty;
        var gap = (CssLayoutLength)owner.GetValue(row ? RowProperty : ColumnProperty)!;
        return gap.IsSet && !owner.HasLocalOrAnimatedValue(native) ? Math.Max(0, gap.Resolve(basis, 0)) : (double)owner.GetValue(native)!;
    }

    internal static bool IsNormal(Panel owner, bool row)
    {
        var native = owner is Grid ? row ? Grid.RowSpacingProperty : Grid.ColumnSpacingProperty
            : row ? FlexPanel.RowSpacingProperty : FlexPanel.ColumnSpacingProperty;
        if (owner.HasLocalOrAnimatedValue(native)) return false;
        var value = (CssLayoutLength)owner.GetValue(row ? RowProperty : ColumnProperty)!;
        return !value.IsSet || value.IsNormal;
    }
}

internal sealed class CssGapParts
{
    private CssLayoutLength? _row, _column;
    private bool _rowState, _columnState;
    public void Set(bool row, CssLayoutLength value, bool state)
    { if (row) { _row = value; _rowState = state; } else { _column = value; _columnState = state; } }

    public void Flush(in CssApplyContext context, ICssSetterSink sink)
    {
        if (_row is { } row) WriteAxis(true, row, _rowState, context, sink);
        if (_column is { } column) WriteAxis(false, column, _columnState, context, sink);
        if (context.Element.Target is StackPanel or DockPanel)
        {
            sink.CurrentValueIsState = _row.HasValue ? _rowState : _columnState;
            sink.Set(context.Element.Target is StackPanel ? StackPanel.SpacingProperty : DockPanel.SpacingProperty,
                Math.Max(0, (_row ?? _column)!.Value.Resolve(double.NaN, 0)));
        }
    }

    private static void WriteAxis(bool row, CssLayoutLength value, bool state, in CssApplyContext context, ICssSetterSink sink)
    {
        sink.CurrentValueIsState = state;
        sink.Set(row ? CssGapProperties.RowProperty : CssGapProperties.ColumnProperty, value);
        var pixels = Math.Max(0, value.Resolve(double.NaN, 0));
        if (context.Element.Target is Panel) sink.Set(row ? FlexPanel.RowSpacingProperty : FlexPanel.ColumnSpacingProperty, pixels);
        var native = context.Element.Target switch
        {
            Grid => row ? Grid.RowSpacingProperty : Grid.ColumnSpacingProperty,
            UniformGrid => row ? UniformGrid.RowSpacingProperty : UniformGrid.ColumnSpacingProperty,
            WrapPanel => row ? WrapPanel.VerticalSpacingProperty : WrapPanel.HorizontalSpacingProperty,
            _ => null,
        };
        if (native is not null) sink.Set(native, pixels);
    }
}
