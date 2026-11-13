namespace Jalium.UI.Styling;

internal static partial class CssTableTracks
{
    internal readonly record struct WidthRequest(CssLayoutLength Length, double Insets = 0, double Scale = 1, bool Native = false, CssLayoutLength PercentLimit = default)
    {
        internal bool Specified => Length.IsSet && !Length.IsAuto;
        internal bool Percentage => Specified && Length.IsPercent;
        internal bool ConstantCalculation(out double value)
        {
            value = 0;
            if (Length.Expression is null || !(this with { Insets = 0 }).Affine(out var offset, out var percent) || percent != 0 || offset == 0) return false;
            value = Resolve(0); return double.IsFinite(value);
        }
        internal double Resolve(double basis)
        {
            var value = Length.Resolve(basis, double.NaN);
            if (PercentLimit.IsPercent) value = Math.Min(value, PercentLimit.Resolve(basis, double.PositiveInfinity));
            return double.IsFinite(value) ? Math.Max(0, Math.Max(0, value) * Scale + Insets) : double.NaN;
        }
        internal bool Affine(out double offset, out double percent)
        {
            var length = Length;
            var valid = length.Expression is { } expression ? Terms(expression, out offset, out percent) : Literal(length, out offset, out percent);
            offset = offset * Scale + Insets; percent *= Scale;
            if (valid && PercentLimit.IsPercent)
            {
                var limit = new WidthRequest(PercentLimit, Insets, Scale);
                if (!limit.Affine(out var edge, out var fraction)) return false;
                if (edge <= offset && fraction <= percent) { offset = edge; percent = fraction; }
                else if (edge < offset || fraction < percent) return false;
            }
            return valid;
            bool Terms(CssMathExpression expression, out double constant, out double fraction)
            {
                if (!expression.UsesPercent) { fraction = 0; return expression.TryEvaluate(length.Context, 0, out constant); }
                if (expression.Operation == "value" && expression.Literal.Unit == CssUnit.Percent)
                { constant = 0; fraction = expression.Literal.Value / 100; return true; }
                constant = fraction = 0;
                if (expression.Operation is not ("calc" or "negate" or "+" or "-" or "*" or "/")) return false;
                if (!Terms(expression.Arguments[0], out var a, out var p)) return false;
                if (expression.Operation is "calc" or "negate") { constant = expression.Operation == "negate" ? -a : a; fraction = expression.Operation == "negate" ? -p : p; return true; }
                if (!Terms(expression.Arguments[1], out var b, out var q)) return false;
                if (expression.Operation is "+" or "-") { var sign = expression.Operation == "+" ? 1 : -1; constant = a + sign * b; fraction = p + sign * q; return true; }
                if (expression.Operation == "*" && (p == 0 || q == 0)) { constant = a * b; fraction = p * b + q * a; return true; }
                if (expression.Operation == "/" && q == 0 && b != 0) { constant = a / b; fraction = p / b; return true; }
                return false;
            }
            static bool Literal(CssLayoutLength length, out double constant, out double fraction)
            {
                fraction = length.IsPercent ? length.Value : 0; constant = length.IsPercent ? 0 : length.Resolve(0, 0); return length.IsSet && !length.IsAuto;
            }
        }
    }

    internal static WidthRequest WidthOf(DependencyObject source, double insets = 0)
    {
        var node = CssNode.Get(source); var native = source.HasLocalOrAnimatedValue(FrameworkElement.WidthProperty);
        if (!native && node.CssLayout is { } state && state.Width.IsSet)
            return new(state.Width, state.BoxSizing == CssBoxSizing.ContentBox ? insets : 0,
                PercentLimit: !source.HasLocalOrAnimatedValue(FrameworkElement.MaxWidthProperty) && state.MaxWidth.IsPercent ? state.MaxWidth : default);
        var width = (double)source.GetValue(FrameworkElement.WidthProperty)!;
        return double.IsFinite(width) ? new(CssLayoutLength.Px(Math.Max(0, width)),
            Native: native || node.CssRuntimeState?.Applied?.ContainsKey(FrameworkElement.WidthProperty) != true) : default;
    }

    private sealed class ColumnConstraint
    {
        internal double Min, Max, Weight, Fraction, PercentScale = 1;
        internal bool Fixed;
        internal readonly List<WidthRequest> Requests = [];
        internal bool Percentage => Requests.Count > 0;
        internal double Requested(double basis) => Math.Max(Min, Requests.Count == 0 ? 0 : Requests.Max(request =>
        {
            var value = request.Resolve(basis);
            if (!double.IsFinite(value)) return 0;
            if (PercentScale < 1 && request.Affine(out var offset, out var percent) && percent > 0)
                value = Math.Max(0, offset + percent * PercentScale * basis);
            return value;
        }));
    }

    private static ColumnConstraint[] ColumnConstraints(int count, double gap, IReadOnlyList<Column> definitions, IReadOnlyList<Cell> cells)
    {
        var columns = Enumerable.Range(0, count).Select(_ => new ColumnConstraint()).ToArray();
        for (var index = 0; index < Math.Min(count, definitions.Count); index++)
        {
            var definition = definitions[index]; var column = columns[index]; column.Weight = definition.Weight;
            if (definition.Min > 0) { column.Min = Math.Max(column.Min, definition.Min); column.Max = Math.Max(column.Min, column.Max); }
            var constant = definition.Request.ConstantCalculation(out var calculated);
            if (definition.Request.Percentage && !constant)
            {
                var request = definition.Request with { Insets = 0 };
                if (!request.Affine(out var offset, out var fraction) || offset != 0 || fraction != 0) column.Requests.Add(request);
            }
            else if (constant || double.IsFinite(definition.Width))
            {
                var specified = Math.Clamp(Math.Max(0, constant ? calculated : definition.Width), definition.Min, definition.Max);
                column.Max = Math.Max(column.Min, specified);
                column.Min = Math.Max(column.Min, definition.Request.Native || !definition.Request.Specified ? column.Max : 0);
                column.Fixed = true;
            }
        }
        foreach (var cell in cells.Where(cell => cell.ColumnSpan == 1))
        {
            var column = columns[cell.Column]; var hasPreferred = Preferred(cell, out var preferred);
            column.Min = Math.Max(column.Min, Math.Max(cell.Min, cell.SpecifiedWidth.Native ? preferred : 0));
            column.Max = Math.Max(column.Max, Math.Max(column.Min, hasPreferred ? preferred : cell.Max));
            column.Fixed |= hasPreferred;
            // Automatic tables resolve a cell percentage against the column
            // border box. Fixed tables separately retain content-box insets.
            if (cell.SpecifiedWidth.Percentage && !hasPreferred) column.Requests.Add(cell.SpecifiedWidth with { Insets = 0 });
        }
        for (var index = 0; index < Math.Min(count, definitions.Count); index++)
            if (double.IsFinite(definitions[index].Max) && !definitions[index].Request.Percentage)
                columns[index].Max = Math.Max(columns[index].Min, Math.Min(columns[index].Max, definitions[index].Max));
        foreach (var column in columns) column.Fraction = column.Requests.Select(request => request.Affine(out _, out var p) ? Math.Max(0, p) : 0).DefaultIfEmpty().Max();
        foreach (var cell in cells.Where(cell => cell.ColumnSpan > 1).OrderBy(cell => cell.ColumnSpan).ThenBy(cell => cell.Column))
        {
            var span = columns.Skip(cell.Column).Take(cell.ColumnSpan).ToArray();
            var hasPreferred = Preferred(cell, out var preferred);
            // Percentage weights use the columns before this spanning cell
            // contributes its content; growing first would change the weights.
            if (cell.SpecifiedWidth.Percentage && !hasPreferred && (cell.SpecifiedWidth with { Insets = 0 }).Affine(out var offset, out var fraction))
            {
                var extra = Math.Max(0, fraction - span.Sum(column => column.Fraction));
                var free = span.Where(column => column.Fraction == 0).ToArray();
                if (extra > 0)
                {
                    var targets = free.Length > 0 ? free : span;
                    var weight = targets.Sum(column => column.Max);
                    foreach (var column in targets)
                    {
                        var share = weight > 0 ? column.Max / weight : 1d / targets.Length;
                        var nextFraction = (free.Length > 0 ? 0 : column.Fraction) + extra * share;
                        column.Requests.Add(new(CssLayoutLength.Percent(nextFraction), (offset - gap * (cell.ColumnSpan - 1)) * share));
                        column.Fraction = nextFraction;
                    }
                }
            }
            else if (cell.SpecifiedWidth.Percentage && !hasPreferred)
            {
                var targets = span.Where(column => !column.Percentage).ToArray();
                if (targets.Length == 0) targets = span;
                var weight = targets.Sum(column => column.Max);
                var raw = cell.SpecifiedWidth with { Insets = 0 };
                foreach (var column in targets)
                {
                    var share = weight > 0 ? column.Max / weight : 1d / targets.Length;
                    column.Requests.Add(raw with { Scale = raw.Scale * share, Insets = (raw.Insets - gap * (cell.ColumnSpan - 1)) * share });
                }
            }
            var spacing = gap * (span.Length - 1);
            var min = DistributeColumns(span, Math.Max(cell.Min, cell.SpecifiedWidth.Native ? preferred : 0) - spacing, true);
            for (var index = 0; index < span.Length; index++)
            { span[index].Min = Math.Max(span[index].Min, min[index]); span[index].Max = Math.Max(span[index].Min, span[index].Max); }
            var max = DistributeColumns(span, (hasPreferred ? preferred : cell.Max) - spacing, hasPreferred || cell.SpecifiedWidth.Native);
            for (var index = 0; index < span.Length; index++) span[index].Max = Math.Max(span[index].Max, max[index]);
        }
        var budget = 1d;
        foreach (var column in columns)
        {
            var allowed = Math.Min(budget, column.Fraction); budget = Math.Max(0, budget - allowed);
            if (column.Fraction > 0) column.PercentScale = allowed / column.Fraction;
            column.Fraction = allowed; column.Max = Math.Max(column.Min, column.Max);
        }
        return columns;

        static bool Preferred(Cell cell, out double width)
        {
            if (cell.SpecifiedWidth.ConstantCalculation(out width)) return true;
            width = !cell.SpecifiedWidth.Percentage && double.IsFinite(cell.Preferred) ? cell.Preferred : 0;
            return !cell.SpecifiedWidth.Percentage && double.IsFinite(cell.Preferred);
        }
    }

    internal static (double Min, double Max) IntrinsicColumns(int count, double gap, IReadOnlyList<Column> definitions, IReadOnlyList<Cell> cells, bool percentages = true)
    {
        var columns = ColumnConstraints(count, gap, definitions, cells);
        var min = columns.Sum(column => column.Min); var max = columns.Sum(column => column.Max);
        if (percentages)
        {
            var fraction = columns.Sum(column => column.Fraction); var outside = 0d; var offsets = 0d;
            var envelopes = new List<(double Offset, double Fraction)[]>();
            foreach (var column in columns)
            {
                if (column.Fraction <= 0) { outside += column.Max; continue; }
                var terms = column.Requests.Select(request => request.Affine(out var value, out var p)
                    ? (Valid: true, Offset: value, Fraction: p > 0 ? p * column.PercentScale : p) : default).Where(term => term.Valid)
                    .Select(term => (term.Offset, term.Fraction)).Distinct().ToArray();
                envelopes.Add(terms);
                var needed = terms.Where(term => term.Fraction > 0).Select(term => Math.Max(0, column.Max - term.Offset) / term.Fraction).DefaultIfEmpty(double.PositiveInfinity).Min();
                if (terms.Any(term => term.Fraction == 0 && term.Offset >= column.Max)) needed = 0;
                max = Math.Max(max, needed);
                // Only terms with the greatest coefficient dominate at infinity.
                // A smaller coefficient's large offset must not be paired with it.
                offsets += terms.Where(term => Math.Abs(term.Fraction - column.Fraction) < 1e-12).Select(term => term.Offset).DefaultIfEmpty().Max();
            }
            if (fraction > 0)
            {
                if (fraction >= 1 - 1e-12 && outside + offsets > 0) max = double.PositiveInfinity;
                else
                {
                    if (fraction < 1 && outside + offsets > 0) max = Math.Max(max, (outside + offsets) / (1 - fraction));
                    double Required(double width) => outside + envelopes.Sum(terms => Math.Max(0, terms.Max(term => term.Offset + term.Fraction * width)));
                    if (double.IsFinite(max) && Required(max) > max)
                    {
                        var low = max; var high = Math.Max(1, low);
                        while (double.IsFinite(high) && Required(high) > high) high *= 2;
                        if (!double.IsFinite(high)) max = double.PositiveInfinity;
                        else
                        {
                            for (var iteration = 0; iteration < 64; iteration++)
                            { var middle = low + (high - low) / 2; if (Required(middle) > middle) low = middle; else high = middle; }
                            max = high;
                        }
                    }
                }
            }
            if (double.IsFinite(max) && columns.Any(column => column.Requests.Any(request => !request.Affine(out _, out _))))
            {
                foreach (var column in columns.Where(column => column.Requests.Any(request => !request.Affine(out _, out _))))
                {
                    if (column.Requested(max) >= column.Max) continue;
                    var low = max; var high = Math.Max(1, low);
                    while (high < 1e7 && column.Requested(high) < column.Max && column.Requested(high * 2) > column.Requested(high)) high *= 2;
                    if (column.Requested(high) >= column.Max)
                    {
                        for (var iteration = 0; iteration < 64; iteration++)
                        { var middle = low + (high - low) / 2; if (column.Requested(middle) < column.Max) low = middle; else high = middle; }
                        max = Math.Max(max, high);
                    }
                }
                double NonlinearRequired(double width) => columns.Sum(column => Math.Max(column.Max, column.Requested(width)));
                if (NonlinearRequired(max) > max)
                {
                    var low = max; var high = Math.Max(1, low);
                    while (high < 1e7 && NonlinearRequired(high) > high) high *= 2;
                    if (NonlinearRequired(high) > high) max = double.PositiveInfinity;
                    else
                    {
                        for (var iteration = 0; iteration < 64; iteration++)
                        { var middle = low + (high - low) / 2; if (NonlinearRequired(middle) > middle) low = middle; else high = middle; }
                        max = high;
                    }
                }
            }
        }
        var spacing = count == 0 ? 0 : gap * (count + 1); return (min + spacing, Math.Max(min, max) + spacing);
    }

    internal static Axis Columns(int count, double available, double gap, bool fixedLayout, IReadOnlyList<Column> definitions, IReadOnlyList<Cell> cells)
    {
        if (fixedLayout) return FixedColumns(count, available, gap, definitions, cells);
        if (count == 0) return new([], [], 0);
        var columns = ColumnConstraints(count, gap, definitions, cells);
        var target = double.IsFinite(available) ? available - gap * (count + 1) : columns.Sum(column => column.Max);
        return Build(DistributeColumns(columns, target, true), gap);
    }

    private static double[] DistributeColumns(ColumnConstraint[] columns, double target, bool constrained)
    {
        var count = columns.Length;
        var sizes = columns.Select(column => column.Min).ToArray();
        target = Math.Max(sizes.Sum(), target);
        var remaining = Math.Max(0, target - sizes.Sum());
        // The four guesses grow percentage, then specified, then automatic
        // columns, preserving every natural minimum throughout interpolation.
        foreach (var stage in Enumerable.Range(0, 3))
        {
            var wanted = columns.Select((column, index) => column.Percentage ? column.Requested(target) :
                stage >= 1 && column.Fixed || stage == 2 ? column.Max : column.Min).ToArray();
            var deficits = wanted.Select((value, index) => Math.Max(0, value - sizes[index])).ToArray();
            var total = deficits.Sum(); var used = Math.Min(remaining, total);
            if (total > 0) for (var index = 0; index < count; index++) sizes[index] += used * deficits[index] / total;
            remaining -= used;
        }
        if (remaining > 0)
        {
            var growing = Enumerable.Range(0, count).Where(index => !columns[index].Percentage && !columns[index].Fixed).ToArray();
            if (growing.Length == 0 && constrained) growing = Enumerable.Range(0, count).Where(index => !columns[index].Percentage).ToArray();
            if (growing.Length == 0) growing = Enumerable.Range(0, count).Where(index => columns[index].Percentage).ToArray();
            var stars = growing.Any(index => columns[index].Weight > 0);
            var weights = growing.Select(index => stars ? Math.Max(0, columns[index].Weight) : columns[index].Percentage ? columns[index].Fraction : columns[index].Max).ToArray();
            var total = weights.Sum(); var left = remaining;
            for (var index = 0; index < growing.Length; index++)
            {
                var extra = index == growing.Length - 1 ? left : remaining * (total > 0 ? weights[index] / total : 1d / growing.Length);
                sizes[growing[index]] += extra; left -= extra;
            }
        }
        return sizes;
    }
}
