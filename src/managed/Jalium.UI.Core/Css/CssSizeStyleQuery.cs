namespace Jalium.UI.Styling;

/// <summary>Computed size values retain percentages instead of comparing used box dimensions.</summary>
internal static class CssSizeStyleQuery
{
    private static readonly HashSet<string> s_names = new(StringComparer.OrdinalIgnoreCase)
    {
        "width", "height", "inline-size", "block-size",
        "min-width", "min-height", "min-inline-size", "min-block-size",
        "max-width", "max-height", "max-inline-size", "max-block-size",
    };

    internal static bool IsSupported(string name) => Descriptor(name) is not null;

    internal static bool Matches(string name, string? value, CssNode container, CssLengthContext lengths)
    {
        var descriptor = Descriptor(name);
        if (descriptor is null || !Field(descriptor.Name, out var field, out var property) ||
            CssPropertyMetadata.Initial(descriptor.Name) is not { } initialText ||
            !Compute(descriptor, initialText, field, property, container, lengths, out var initial) ||
            !ReadActual(container, field, property, out var actual)) return false;

        if (value is null) return !Equal(actual, initial);
        var keyword = CssPropertyMetadata.WideKeyword(value);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "unset") return Equal(actual, initial);
        if (keyword == "inherit")
        {
            var parent = CssMatcher.CssAncestor(container);
            return parent is null ? Equal(actual, initial)
                : ReadActual(parent, field, property, out var inherited) && Equal(actual, inherited);
        }
        return Compute(descriptor, value, field, property, container, lengths, out var expected) &&
            Equal(actual, expected);
    }

    private static CssPropertyDescriptor? Descriptor(string name)
    {
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out var canonicalName);
        return s_names.Contains(canonicalName) && descriptor is
            { Kind: CssPropertyKind.Longhand, Parse: not null } ? descriptor : null;
    }

    private static bool Field(string name, out CssLayoutSlotField field, out DependencyProperty property)
    {
        switch (name)
        {
            case "width": case "inline-size":
                field = CssLayoutSlotField.Width; property = FrameworkElement.WidthProperty; return true;
            case "height": case "block-size":
                field = CssLayoutSlotField.Height; property = FrameworkElement.HeightProperty; return true;
            case "min-width": case "min-inline-size":
                field = CssLayoutSlotField.MinWidth; property = FrameworkElement.MinWidthProperty; return true;
            case "min-height": case "min-block-size":
                field = CssLayoutSlotField.MinHeight; property = FrameworkElement.MinHeightProperty; return true;
            case "max-width": case "max-inline-size":
                field = CssLayoutSlotField.MaxWidth; property = FrameworkElement.MaxWidthProperty; return true;
            case "max-height": case "max-block-size":
                field = CssLayoutSlotField.MaxHeight; property = FrameworkElement.MaxHeightProperty; return true;
            default:
                field = default; property = FrameworkElement.WidthProperty; return false;
        }
    }

    private static CssLayoutLength LayoutValue(CssLayoutState? state, CssLayoutSlotField field)
        => field switch
        {
            CssLayoutSlotField.Width => state?.Width ?? default,
            CssLayoutSlotField.Height => state?.Height ?? default,
            CssLayoutSlotField.MinWidth => state?.MinWidth ?? default,
            CssLayoutSlotField.MinHeight => state?.MinHeight ?? default,
            CssLayoutSlotField.MaxWidth => state?.MaxWidth ?? default,
            CssLayoutSlotField.MaxHeight => state?.MaxHeight ?? default,
            _ => default,
        };

    private static bool ReadActual(CssNode node, CssLayoutSlotField field,
        DependencyProperty property, out CssLayoutLength value)
    {
        value = default;
        var element = node.Target;
        var presented = CssSizingPresentation.Properties[(int)field];
        if (!element.HasLocalOrAnimatedValue(property) &&
            (element.HasCssAnimatedValue(presented) || element.GetValueSourceInternal(presented).IsAnimated))
        {
            value = CssSizingPresentation.Presented(element, (int)field, LayoutValue(node.CssLayout, field));
            if (!value.IsSet) value = CssLayoutLength.Normal;
            return true;
        }
        var layer = node.Target.GetEffectiveValueLayer(property);
        if (layer is
            (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState) &&
            LayoutValue(node.CssLayout, field) is { IsSet: true } layout)
        {
            value = layout;
            return true;
        }
        if (layer is null && field is CssLayoutSlotField.MinWidth or CssLayoutSlotField.MinHeight)
        {
            value = CssLayoutLength.Auto;
            return true;
        }
        return node.GetValue(property) is double number && Native(number, out value);
    }

    private static bool Native(double number, out CssLayoutLength value)
    {
        value = double.IsNaN(number) ? CssLayoutLength.Auto
            : double.IsPositiveInfinity(number) ? CssLayoutLength.Normal
            : CssLayoutLength.Px(number);
        return double.IsNaN(number) || number >= 0;
    }

    private static bool Compute(CssPropertyDescriptor descriptor, string text, CssLayoutSlotField field,
        DependencyProperty property, CssNode container, CssLengthContext lengths, out CssLayoutLength value)
    {
        value = default;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse!(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var slots = new CssSlotAccumulator();
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(context, sink)) return false;
        slots.Flush(context, sink);
        if (LayoutValue(sink.LayoutState, field) is { IsSet: true } layout)
        {
            value = layout;
            return true;
        }
        return sink.Values.TryGetValue(property, out var applied) &&
            applied.Value is double number && Native(number, out value);
    }

    internal static bool Equal(CssLayoutLength actual, CssLayoutLength expected)
    {
        if (actual.Calculation is not null || expected.Calculation is not null)
            return actual.Calculation is { } before && expected.Calculation is { } after &&
                CalculationEqual(before, after);
        if (actual.IsSizing || expected.IsSizing)
            return actual.Kind == expected.Kind && (actual.Kind != CssLayoutLength.KindFitFunction ||
                Equal(actual.FitArgument, expected.FitArgument));
        if (actual.Kind is CssLayoutLength.KindAuto or CssLayoutLength.KindNormal ||
            expected.Kind is CssLayoutLength.KindAuto or CssLayoutLength.KindNormal)
            return actual.Kind == expected.Kind;
        // A zero percentage still depends on a percentage basis at computed-value time.
        if (actual.IsPercent != expected.IsPercent) return false;
        if (Linear(actual, out var actualPx, out var actualPercent) &&
            Linear(expected, out var expectedPx, out var expectedPercent))
            return actualPx == expectedPx && actualPercent == expectedPercent;
        // Nonlinear math functions retain their calculation tree at computed-value time.
        return actual.Kind == CssLayoutLength.KindExpression &&
            expected.Kind == CssLayoutLength.KindExpression &&
            Equals(actual.Expression, expected.Expression);
    }

    private static bool CalculationEqual(CssCalcSizeComputed a, CssCalcSizeComputed b)
    {
        if (a.UsesPercent != b.UsesPercent) return false;
        if (a.From is not null || b.From is not null)
            return Canonical(a, out var ab, out var av) && Canonical(b, out var bb, out var bv)
                ? SameBasis(ab, bb) && av == bv : a.Equals(b);
        if (a.Any != b.Any) return false;
        if (a.Nested is not null || b.Nested is not null)
        {
            if (a.Nested is not { } first || b.Nested is not { } second || !CalculationEqual(first, second)) return false;
        }
        else if (!a.Any && !Equal(a.Basis, b.Basis)) return false;
        return ExpressionEqual(a.Expression, a.Context, b.Expression, b.Context);
    }

    private static bool ExpressionEqual(CssMathExpression a, CssLengthContext ac,
        CssMathExpression b, CssLengthContext bc)
    {
        if (a.UsesPercent != b.UsesPercent) return false;
        if (Affine(a, ac, out var first) && Affine(b, bc, out var second)) return first == second;
        if (a.Arguments.Length == 0 || b.Arguments.Length == 0)
            return a.Operation == b.Operation && a.Kind == b.Kind &&
                (a.TryEvaluate(ac, double.NaN, out var x) && b.TryEvaluate(bc, double.NaN, out var y)
                    ? x.Equals(y) : a.Equals(b));
        return a.Operation == b.Operation && a.Option == b.Option && a.Kind == b.Kind &&
            a.Arguments.Length == b.Arguments.Length && a.Arguments.Zip(b.Arguments)
                .All(pair => ExpressionEqual(pair.First, ac, pair.Second, bc));
    }

    private readonly record struct SizeTerms(double Px, double Percent, double Size, bool Number)
    {
        internal SizeTerms Scale(double factor) => new(Px * factor, Percent * factor, Size * factor, Number);
    }

    private static bool SameBasis(CssLayoutLength? a, CssLayoutLength? b)
        => a is { } x && b is { } y ? Equal(x, y) : a is null && b is null;

    private static bool Canonical(CssCalcSizeComputed value, out CssLayoutLength? basis, out SizeTerms terms)
    {
        basis = value.Any ? null : value.SizingBasis; terms = default;
        if (value.From is { } from && value.To is { } to)
        {
            if (!Canonical(from, out var a, out var first) || !Canonical(to, out var b, out var second) ||
                !Align(a, basis, ref first) || !Align(b, basis, ref second)) return false;
            first = first.Scale(1 - value.Progress); second = second.Scale(value.Progress);
            terms = new(first.Px + second.Px, first.Percent + second.Percent, first.Size + second.Size, false); return true;
        }
        if (!Affine(value.Expression, value.Context, out terms)) return false;
        if (value.Nested is not { } nested) return true;
        if (!Canonical(nested, out basis, out var inner)) return false;
        terms = new(terms.Px + terms.Size * inner.Px, terms.Percent + terms.Size * inner.Percent,
            terms.Size * inner.Size, false); return true;
    }

    private static bool Align(CssLayoutLength? basis, CssLayoutLength? target, ref SizeTerms value)
    {
        if (SameBasis(basis, target)) return true;
        if (basis is null) return value.Size == 0;
        if (basis is not { Kind: CssLayoutLength.KindPx } numeric) return false;
        value = new(value.Px + value.Size * numeric.Value, value.Percent, 0, false); return true;
    }

    private static bool Affine(CssMathExpression expression, CssLengthContext context, out SizeTerms terms)
    {
        terms = default;
        if (expression.Operation == "size") { terms = new(0, 0, 1, false); return true; }
        if (expression.Operation == "value")
        {
            if (expression.Literal.Unit == CssUnit.Percent)
            { terms = new(0, expression.Literal.Value / 100, 0, false); return true; }
            if (expression.Kind == CssNumericKind.Number)
            { terms = new(expression.Literal.Value, 0, 0, true); return true; }
            if (!expression.Literal.TryResolve(context, CssPercentBasis.NotSupported, out var px)) return false;
            terms = new(px, 0, 0, false); return true;
        }
        if (expression.Operation is "calc" or "negate" && expression.Arguments.Length == 1 &&
            Affine(expression.Arguments[0], context, out terms))
        { if (expression.Operation == "negate") terms = terms.Scale(-1); return true; }
        if (expression.Arguments.Length != 2 ||
            !Affine(expression.Arguments[0], context, out var a) || !Affine(expression.Arguments[1], context, out var b)) return false;
        switch (expression.Operation)
        {
            case "+": terms = new(a.Px + b.Px, a.Percent + b.Percent, a.Size + b.Size, a.Number && b.Number); return true;
            case "-": terms = new(a.Px - b.Px, a.Percent - b.Percent, a.Size - b.Size, a.Number && b.Number); return true;
            case "*" when a.Number: terms = b.Scale(a.Px); return true;
            case "*" when b.Number: terms = a.Scale(b.Px); return true;
            case "/" when b.Number && b.Px != 0: terms = a.Scale(1 / b.Px); return true;
            default: return false;
        }
    }

    internal static bool Linear(CssLayoutLength value, out double px, out double percent)
    {
        px = percent = 0;
        switch (value.Kind)
        {
            case CssLayoutLength.KindPx: px = value.Value; return true;
            case CssLayoutLength.KindPercent: percent = value.Value; return true;
            case CssLayoutLength.KindExpression:
                return Linear(value.Expression!, value.Context, out px, out percent, out _);
            default: return false;
        }
    }

    private static bool Linear(CssMathExpression expression, CssLengthContext context,
        out double px, out double percent, out bool number)
    {
        px = percent = 0;
        number = expression.Kind == CssNumericKind.Number;
        if (expression.Operation is "value" or "none")
        {
            if (expression.Literal.Unit == CssUnit.Percent)
            { percent = expression.Literal.Value / 100; return true; }
            if (number) { px = expression.Literal.Value; return true; }
            return expression.Literal.TryResolve(context, CssPercentBasis.NotSupported, out px);
        }
        if (expression.Operation is "calc" or "negate" && expression.Arguments.Length == 1 &&
            Linear(expression.Arguments[0], context, out px, out percent, out number))
        {
            if (expression.Operation == "negate") { px = -px; percent = -percent; }
            return true;
        }
        if (expression.Operation is not ("+" or "-" or "*" or "/") || expression.Arguments.Length != 2 ||
            !Linear(expression.Arguments[0], context, out var leftPx, out var leftPercent, out var leftNumber) ||
            !Linear(expression.Arguments[1], context, out var rightPx, out var rightPercent, out var rightNumber))
            return false;
        switch (expression.Operation)
        {
            case "+": px = leftPx + rightPx; percent = leftPercent + rightPercent; return true;
            case "-": px = leftPx - rightPx; percent = leftPercent - rightPercent; return true;
            case "*" when leftNumber && leftPercent == 0:
                px = leftPx * rightPx; percent = leftPx * rightPercent; number = rightNumber; return true;
            case "*" when rightNumber && rightPercent == 0:
                px = leftPx * rightPx; percent = leftPercent * rightPx; number = leftNumber; return true;
            case "/" when rightNumber && rightPercent == 0 && rightPx != 0:
                px = leftPx / rightPx; percent = leftPercent / rightPx; number = leftNumber; return true;
            default: return false;
        }
    }
}
