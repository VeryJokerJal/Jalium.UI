using Jalium.UI.Media.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;

namespace Jalium.UI
{
    public partial class FrameworkElement
    {
        internal byte CssSizingAnimationMask;
        internal Styling.CssLayoutLength CssPresentedSize(Styling.CssLayoutSlotField field)
            => Styling.CssSizingPresentation.Presented(this, (int)field,
                Styling.CssSizingPresentation.FromLayout(CssLayout, field));
    }
}

namespace Jalium.UI.Styling
{
    /// <summary>Typed sizing animation values supplement the native double DP channel.</summary>
    internal static class CssSizingPresentation
    {
        internal static readonly DependencyProperty AllowKeywordsProperty = DependencyProperty.RegisterAttached(
            "CssInterpolateSizeKeywords", typeof(bool), typeof(CssSizingPresentation),
            new PropertyMetadata(false, null, null, inherits: true));
        private static readonly DependencyProperty[] s_native =
        [FrameworkElement.WidthProperty, FrameworkElement.HeightProperty, FrameworkElement.MinWidthProperty,
         FrameworkElement.MinHeightProperty, FrameworkElement.MaxWidthProperty, FrameworkElement.MaxHeightProperty,
         FlexPanel.BasisProperty];
        internal static readonly DependencyProperty[] Properties = s_native.Select((native, index) =>
            DependencyProperty.RegisterAttached("CssSizing" + native.Name, typeof(CssLayoutLength),
                typeof(CssSizingPresentation), new PropertyMetadata(index is 4 or 5 ? default(CssLayoutLength) : CssLayoutLength.Auto,
                    static (target, e) => SourceChanged(target, e.Property)))).ToArray();

        internal static void Register() => CssPropertyRegistry.Register(new()
        {
            Name = "interpolate-size", Kind = CssPropertyKind.Longhand, StorageProperty = AllowKeywordsProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                reader.TryReadIdent(out var value) && reader.AtEnd &&
                (value.Equals("numeric-only", StringComparison.OrdinalIgnoreCase) || value.Equals("allow-keywords", StringComparison.OrdinalIgnoreCase))
                    ? new CssImmediateValue(AllowKeywordsProperty, value.Equals("allow-keywords", StringComparison.OrdinalIgnoreCase)) : null,
        });

        internal static bool AllowKeywords(DependencyObject element) => (bool)element.GetValue(AllowKeywordsProperty)!;
        internal static DependencyProperty? Native(DependencyProperty property)
        {
            if (property.OwnerType != typeof(CssSizingPresentation)) return null;
            var index = Array.IndexOf(Properties, property);
            return index < 0 ? null : s_native[index];
        }
        internal static bool IsPresentation(DependencyProperty property) => Native(property) is not null;
        internal static void SourceChanged(DependencyObject target, DependencyProperty property)
        {
            if (property.OwnerType != typeof(CssSizingPresentation)) return;
            var index = Array.IndexOf(Properties, property);
            if (index < 0) return;
            if (target is not FrameworkElement element)
            {
                if (target is FlowDocument document) document.NotifyPaginationStyleChanged();
                else if (target is TextElement text) text.NotifyTextContentChanged();
                CssNode.Existing(target)?.CssRuntimeState?.QueryContainer?.Notify(CssContainerDependency.Style);
                return;
            }
            var previous = element.CssSizingAnimationMask;
            var active = element.HasCssAnimatedValue(property) || element.GetValueSourceInternal(property).IsAnimated;
            if (active)
            {
                element.CssSizingAnimationMask |= (byte)(1 << index);
                element.StopAutomaticTransition(s_native[index], clearAnimatedValue: true);
            }
            else element.CssSizingAnimationMask &= (byte)~(1 << index);
            element.InvalidateMeasure();
            if (active || previous != element.CssSizingAnimationMask)
                CssNode.Existing(element)?.CssRuntimeState?.QueryContainer?.Notify(CssContainerDependency.Style);
        }
        internal static CssLayoutLength Presented(FrameworkElement element, int index, CssLayoutLength declared)
            => (element.CssSizingAnimationMask & (1 << index)) != 0
                ? (CssLayoutLength)element.GetValue(Properties[index])! : declared;
        internal static CssLayoutLength Presented(DependencyObject element, int index, CssLayoutLength declared)
            => element is FrameworkElement framework ? Presented(framework, index, declared)
                : element.HasCssAnimatedValue(Properties[index]) || element.GetValueSourceInternal(Properties[index]).IsAnimated
                    ? (CssLayoutLength)element.GetValue(Properties[index])! : declared;
        internal static object? FlexBasis(UIElement element)
            => element is FrameworkElement framework && (framework.CssSizingAnimationMask & 64) != 0
                ? framework.GetValue(Properties[6]) : element.GetValue(FlexPanel.CssBasisProperty);
        internal static bool IsFlexBasisAnimated(FrameworkElement? element) => element is not null && (element.CssSizingAnimationMask & 64) != 0;
        internal static CssLayoutLength FromLayout(CssLayoutState? state, CssLayoutSlotField field) => field switch
        {
            CssLayoutSlotField.Width => state?.Width ?? default,
            CssLayoutSlotField.Height => state?.Height ?? default,
            CssLayoutSlotField.MinWidth => state?.MinWidth ?? default,
            CssLayoutSlotField.MinHeight => state?.MinHeight ?? default,
            CssLayoutSlotField.MaxWidth => state?.MaxWidth ?? default,
            _ => state?.MaxHeight ?? default,
        };

        internal static void Collect(in CssApplyContext context, CssEngine.CssSetterCollector collector)
        {
            if (context.Element.Target is not (FrameworkElement or FlowDocument or TextElement)) return;
            for (var index = 0; index < 6; index++)
            {
                if (!collector.Values.TryGetValue(s_native[index], out var entry) || entry.Value is not double number) continue;
                var typed = FromLayout(collector.LayoutState, (CssLayoutSlotField)index);
                if (!typed.IsSet) typed = double.IsNaN(number) ? CssLayoutLength.Auto
                    : double.IsPositiveInfinity(number) ? default : CssLayoutLength.Px(number);
                collector.Values[Properties[index]] = new(entry.Layer, typed, entry.Important);
            }
            AppliedCssValue basis;
            CssLayoutLength value;
            if (collector.Values.TryGetValue(FlexPanel.CssBasisProperty, out basis))
            {
                value = basis.Value is CssLayoutLength typed ? typed : basis.Value is CssLength length
                    ? length.Expression is { } expression ? CssLayoutLength.Math(expression, context.Lengths)
                    : length.Unit == CssUnit.Percent ? CssLayoutLength.Percent(length.Value / 100)
                    : length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) ? CssLayoutLength.Px(pixels) : default : default;
            }
            else if (collector.Values.TryGetValue(FlexPanel.CssContentBasisProperty, out basis) && basis.Value is true)
                value = CssLayoutLength.Sizing(CssLayoutLength.KindContent);
            else if (collector.Values.TryGetValue(FlexPanel.BasisProperty, out basis) && basis.Value is double number)
                value = double.IsNaN(number) ? CssLayoutLength.Auto : CssLayoutLength.Px(number);
            else return;
            collector.Values[Properties[6]] = new(basis.Layer, value, basis.Important);
        }

        internal static bool SuppressNativeTransition(FrameworkElement element, DependencyProperty property)
        {
            var index = Array.IndexOf(s_native, property);
            if (index < 0) return false;
            var old = (CssLayoutLength)element.GetValue(Properties[index])!;
            var current = index == 6 ? element.GetValue(FlexPanel.CssBasisProperty) is CssLayoutLength basis ? basis : default
                : FromLayout(element.CssLayout, (CssLayoutSlotField)index);
            return old.IsSizing || current.IsSizing || AllowKeywords(element) && (old.IsAuto || current.IsAuto);
        }

        internal static bool UsesTypedTrack(IEnumerable<CssPropertyKeyframe> frames, bool allowKeywords)
            => frames.Any(frame => frame.Value is CssLayoutLength value &&
                (value.Calculation is not null || value.IsSizing || value.IsAuto || !value.IsSet));

        internal static CssSizingAnimation? Animation(DependencyProperty property, object? from, object? to, bool allowKeywords)
            => IsPresentation(property) && from is CssLayoutLength before && to is CssLayoutLength after &&
                (before.Calculation is not null || after.Calculation is not null || allowKeywords &&
                    (before.IsSizing || before.IsAuto || after.IsSizing || after.IsAuto)) &&
                TryInterpolate(before, after, .5, out _) ? new() { From = before, To = after } : null;

        internal static bool TryInterpolate(CssLayoutLength from, CssLayoutLength to, double progress, out CssLayoutLength value)
        {
            value = default;
            var a = Function(from); var b = Function(to);
            if (a is null || b is null || a.Depth > 64 || b.Depth > 64) return false;
            var budget = 4096;
            a = Prepare(a, ref budget); b = Prepare(b, ref budget);
            if (a is null || b is null) return false;
            var first = a.Any ? (CssLayoutLength?)null : a.Basis;
            var second = b.Any ? (CssLayoutLength?)null : b.Basis;
            if (first is { } x && second is { } y && !SameBasis(x, y)) return false;
            var basis = first ?? second;
            value = CssLayoutLength.Calculate(new(basis ?? default, null, Size, default, basis is null,
                From: a, To: b, Progress: progress));
            return true;
        }
        private static bool SameBasis(CssLayoutLength a, CssLayoutLength b)
            => a.Equals(b) || a.Kind == b.Kind && (a.Kind == CssLayoutLength.KindFitFunction
                ? SameBasis(a.FitArgument, b.FitArgument)
                : a.Expression is { RequiresElementContext: false } expression && expression.Equals(b.Expression));
        // Values 5 §11.2.1: substitute numeric/nested bases before interpolation.
        // In particular, a percentage in the basis retains auto behavior when its
        // containing size is indefinite; a percentage in the body resolves to zero.
        private static CssCalcSizeComputed? Prepare(CssCalcSizeComputed value, ref int budget)
        {
            if (--budget < 0) return null;
            if (value.From is { } from && value.To is { } to)
            {
                var a = Prepare(from, ref budget); var b = Prepare(to, ref budget);
                if (a is null || b is null) return null;
                var expression = new CssMathExpression("+", a.Expression.Type, default,
                    [Scale(a.Expression, 1 - value.Progress), Scale(b.Expression, value.Progress)]);
                return new(value.Basis, null, expression, value.Context, value.Any);
            }
            var body = Rewrite(value.Expression, value.Context, null, false, ref budget);
            if (body is null) return null;
            var basis = value.Basis; var any = value.Any;
            CssMathExpression? insertion = null;
            if (value.Nested is { } nested)
            {
                var inner = Prepare(nested, ref budget);
                if (inner is null) return null;
                basis = inner.Basis; any = inner.Any; insertion = inner.Expression;
            }
            else if (!any && !basis.IsSizing && !basis.IsAuto)
            {
                insertion = basis.Kind switch
                {
                    CssLayoutLength.KindPx => Literal(basis.Value, CssUnit.Px, CssNumericKind.Length),
                    CssLayoutLength.KindPercent => Literal(basis.Value * 100, CssUnit.Percent, CssNumericKind.Percent),
                    CssLayoutLength.KindExpression => basis.Expression,
                    _ => null,
                };
                if (insertion is null) return null;
                insertion = Rewrite(insertion, basis.Context, null, basis.IsPercent, ref budget);
                if (insertion is null) return null;
                any = !basis.IsPercent;
                basis = any ? default : CssLayoutLength.Percent(1);
            }
            if (insertion is not null)
            {
                body = Rewrite(body, value.Context, insertion, false, ref budget);
                if (body is null) return null;
            }
            return new(basis, null, body, value.Context, any);
        }

        private static CssMathExpression Literal(double number, CssUnit unit, CssNumericKind kind)
            => new("value", CssNumericType.Of(kind), new(number, unit), []);
        private static CssMathExpression Scale(CssMathExpression expression, double factor)
            => new("*", expression.Type, default, [expression, Literal(factor, CssUnit.None, CssNumericKind.Number)]);

        private static CssMathExpression? Rewrite(CssMathExpression expression, CssLengthContext context,
            CssMathExpression? insertion, bool depercentify, ref int budget, int depth = 0)
        {
            if (--budget < 0 || depth >= 128) return null;
            if (expression.Operation == "size" && insertion is not null)
                return Rewrite(insertion, context, null, false, ref budget, depth + 1);
            if (expression.Operation == "value")
            {
                if (expression.Literal.Unit == CssUnit.Percent && depercentify)
                    return Scale(Size, expression.Literal.Value / 100);
                if (expression.Kind == CssNumericKind.Length &&
                    expression.Literal.TryResolve(context, CssPercentBasis.NotSupported, out var pixels))
                    return Literal(pixels, CssUnit.Px, CssNumericKind.Length);
                return expression;
            }
            var arguments = new CssMathExpression[expression.Arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = Rewrite(expression.Arguments[i], context, insertion, depercentify, ref budget, depth + 1);
                if (argument is null) return null;
                arguments[i] = argument;
            }
            var type = depercentify ? expression.Type with
                { Length = expression.Type.Length + expression.Type.Percent, Percent = 0, PercentHint = null } : expression.Type;
            return expression with { Arguments = arguments, Type = type };
        }
        private static readonly CssMathExpression Size = new("size", CssNumericType.Of(CssNumericKind.Length), default, []);
        private static CssCalcSizeComputed? Function(CssLayoutLength value)
        {
            if (value.Calculation is { } calculation) return calculation;
            if (value.IsSizing || value.IsAuto) return new(value, null, Size, default, false);
            CssMathExpression? expression = value.Kind switch
            {
                CssLayoutLength.KindPx => new("value", CssNumericType.Of(CssNumericKind.Length), new(value.Value, CssUnit.Px), []),
                CssLayoutLength.KindPercent => new("value", CssNumericType.Of(CssNumericKind.Percent), new(value.Value * 100, CssUnit.Percent), []),
                CssLayoutLength.KindExpression => value.Expression,
                _ => null,
            };
            return expression is null ? null : new(default, null, expression, value.Context, true);
        }
    }

    internal sealed class CssSizingAnimation : AnimationTimeline
    {
        internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
            nameof(EasingFunction), typeof(IEasingFunction), typeof(CssSizingAnimation), new PropertyMetadata(null));
        internal CssLayoutLength From, To;
        internal IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
        public override Type TargetPropertyType => typeof(CssLayoutLength);
        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            var progress = EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress;
            return CssSizingPresentation.TryInterpolate(From, To, progress, out var value) ? value : progress < .5 ? From : To;
        }
        protected override Freezable CreateInstanceCore() => new CssSizingAnimation { From = From, To = To };
    }
}
