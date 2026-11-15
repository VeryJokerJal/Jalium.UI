namespace Jalium.UI.Styling;

internal static partial class CssTableTracks
{
    internal readonly record struct HeightRequest(CssLayoutLength Length, double Insets = 0)
    {
        internal bool Specified => Length.IsSet && !Length.IsAuto;
        internal bool Percentage => Specified && Length.IsPercent;
        internal double Resolve(double basis)
        {
            var value = Length.Resolve(basis, double.NaN);
            return double.IsFinite(value) ? Math.Max(0, value) + Insets : 0;
        }
    }

    internal static HeightRequest HeightOf(DependencyObject source, double insets = 0)
    {
        if (!source.HasLocalOrAnimatedValue(FrameworkElement.HeightProperty) && CssNode.Get(source).CssLayout is { } state && state.Height.IsSet)
            return new(state.Height, state.BoxSizing == CssBoxSizing.ContentBox ? insets : 0);
        var height = (double)source.GetValue(FrameworkElement.HeightProperty)!;
        return double.IsFinite(height) ? new(CssLayoutLength.Px(Math.Max(0, height))) : default;
    }

    internal static Axis Rows(int count, double gap, IReadOnlyList<double> minimums, IReadOnlyList<Cell> cells,
        bool[]? collapsed = null, double minimumHeight = double.NaN, IReadOnlyList<HeightRequest>? definitions = null,
        IReadOnlyList<RowGroup>? groups = null, double percentageResolutionHeight = double.NaN)
    {
        if (groups is { Count: > 0 }) return GroupRows(count, gap, minimums, cells, collapsed, minimumHeight, definitions, groups);
        var sizes = new double[count]; var specified = new bool[count];
        var percentages = new List<HeightRequest>[count];
        var active = Enumerable.Range(0, count).Where(i => collapsed?[i] != true).ToArray();
        foreach (var row in active)
        {
            var request = row < (definitions?.Count ?? 0) ? definitions![row] : default;
            sizes[row] = row < minimums.Count && double.IsFinite(minimums[row]) ? Math.Max(0, minimums[row]) : 0;
            sizes[row] = Math.Max(sizes[row], request.Resolve(0));
            specified[row] = request.Specified || definitions is null && row < minimums.Count && double.IsFinite(minimums[row]);
            percentages[row] = [];
            if (request.Percentage) percentages[row].Add(request);
        }
        foreach (var cell in cells.Where(cell => cell.RowSpan == 1 && collapsed?[cell.Row] != true))
        {
            var row = cell.Row; var request = cell.SpecifiedHeight;
            sizes[row] = Math.Max(sizes[row], Math.Max(cell.Height, request.Resolve(0)) + cell.OffsetY);
            specified[row] |= request.Specified;
            if (request.Percentage) percentages[row].Add(request);
        }
        var origins = cells.Where(cell => cell.RowSpan > 1).Select(cell => cell.Row).ToHashSet();
        foreach (var cell in cells.Where(cell => cell.RowSpan > 1).OrderBy(cell => cell.RowSpan))
        {
            if (collapsed?[cell.Row] != true && cell.HasBaseline)
                sizes[cell.Row] = Math.Max(sizes[cell.Row], cell.Baseline + cell.OffsetY);
            var rows = active.Where(row => row >= cell.Row && row < cell.Row + cell.RowSpan).ToArray();
            var desired = Math.Max(cell.Height, cell.SpecifiedHeight.Resolve(0)) + cell.OffsetY - gap * Math.Max(0, rows.Length - 1);
            var extra = Math.Max(0, desired - rows.Sum(row => sizes[row]));
            Distribute(rows, extra, span: true);
        }
        var spacing = gap * (active.Length + 1);
        var target = double.IsFinite(minimumHeight) ? Math.Max(sizes.Sum(), minimumHeight - spacing) : sizes.Sum();
        var remaining = Math.Max(0, target - sizes.Sum());
        if (remaining > 0)
        {
            // Single-row cell percentages join the row's maximum request.
            // Consume the 100% budget in source-row order, before distributing
            // a deficit; natural content and fixed minima never shrink.
            var deficits = new double[count]; var budget = 1d;
            var tableHeight = double.IsFinite(percentageResolutionHeight) ? Math.Max(0, percentageResolutionHeight) : target + spacing;
            foreach (var row in active)
            {
                var requests = percentages[row];
                if (requests.Count == 0) continue;
                var fraction = tableHeight > 0 ? requests.Max(request => Math.Max(0, request.Resolve(tableHeight) - request.Resolve(0)) / tableHeight) : 0;
                var share = Math.Min(budget, fraction); budget = Math.Max(0, budget - share);
                var wanted = requests.Max(request => request.Resolve(0) + Math.Min(share,
                    tableHeight > 0 ? Math.Max(0, request.Resolve(tableHeight) - request.Resolve(0)) / tableHeight : 0) * tableHeight);
                deficits[row] = Math.Max(0, wanted - sizes[row]);
            }
            var total = deficits.Sum(); var used = Math.Min(remaining, total);
            if (total > 0) foreach (var row in active) sizes[row] += used * deficits[row] / total;
            remaining -= used; Distribute(active, remaining, span: false);
        }
        return Build(sizes, gap, collapsed);

        void Distribute(int[] rows, double extra, bool span)
        {
            if (extra <= 0 || rows.Length == 0) return;
            var starts = span ? rows.Where(row => row != rows[0] && origins.Contains(row)).ToArray() : [];
            if (starts.Length > 0) { Add(starts, extra, false); return; }
            var flexible = rows.Where(row => !specified[row] && sizes[row] > 0).ToArray();
            if (flexible.Length > 0) { Add(flexible, extra, true); return; }
            var empty = rows.Where(row => sizes[row] == 0).ToArray();
            if (empty.Length == rows.Length && span) { sizes[empty[^1]] += extra; return; }
            if (!span && empty.Length > 0)
            {
                var free = empty.Where(row => !specified[row]).ToArray();
                Add(free.Length > 0 ? free : empty, extra, false); return;
            }
            var nonempty = rows.Where(row => sizes[row] > 0).ToArray();
            Add(nonempty.Length > 0 ? nonempty : rows, extra, nonempty.Length > 0);
        }
        void Add(int[] rows, double extra, bool proportional)
        {
            var weight = proportional ? rows.Sum(row => sizes[row]) : rows.Length;
            var left = extra;
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index]; var delta = index == rows.Length - 1 ? left : extra * (proportional ? sizes[row] : 1) / weight;
                sizes[row] += delta; left -= delta;
            }
        }
    }
}
