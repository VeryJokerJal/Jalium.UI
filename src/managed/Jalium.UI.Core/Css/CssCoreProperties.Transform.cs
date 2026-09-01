namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    /// <summary>Maps CSS property names appearing in transition-property to framework DP names.</summary>
    private static readonly Dictionary<string, string> s_transitionTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["background-color"] = "Background",
        ["background"] = "Background",
        ["color"] = "Foreground",
        ["opacity"] = "Opacity",
        ["width"] = "Width",
        ["height"] = "Height",
        ["min-width"] = "MinWidth",
        ["min-height"] = "MinHeight",
        ["max-width"] = "MaxWidth",
        ["max-height"] = "MaxHeight",
        ["margin"] = "Margin",
        ["padding"] = "Padding",
        ["border-color"] = "BorderBrush",
        ["border-width"] = "BorderThickness",
        ["border-radius"] = "CornerRadius",
        ["transform"] = "RenderTransform",
        ["box-shadow"] = "Effect",
        ["filter"] = "Effect",
        ["font-size"] = "FontSize",
        ["visibility"] = "Visibility",
        ["outline-color"] = "OutlineBrush",
        ["outline-width"] = "OutlineThickness",
        ["outline-offset"] = "OutlineOffset",
        ["outline"] = "OutlineBrush",
    };

    internal static bool TryGetTransitionTargetName(string cssName, out string dpName)
    {
        if (s_transitionTargets.TryGetValue(cssName, out dpName!))
        {
            return true;
        }

        return CssKebabCase.TryToPascal(cssName, out dpName!);
    }

    private static void RegisterTransformAndTransition()
    {
        RegisterLonghand("transform", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssTransformParser.TryParseTransformList(ref reader, out var transform))
            {
                return null;
            }

            return new CssImmediateValue(UIElement.RenderTransformProperty, transform);
        });

        RegisterLonghand("transform-origin", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssTransformParser.TryParseTransformOrigin(ref reader, out var origin) || !reader.AtEnd)
            {
                return null;
            }

            return new CssImmediateValue(UIElement.RenderTransformOriginProperty, origin);
        });

        RegisterLonghand("transition-property", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var collection = ParseTransitionPropertyList(ref reader);
            return collection is null
                ? null
                : new CssImmediateValue(UIElement.TransitionPropertyProperty, collection);
        });

        RegisterLonghand("transition-duration", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!TryReadDurationMs(ref reader, out var ms))
            {
                return null;
            }

            if (!reader.AtEnd)
            {
                if (!reader.TryReadComma())
                {
                    return null;
                }

                CssDiagnostics.Report(
                    "transition-duration", CssDiagnosticReason.LossyConversion, null,
                    "the framework has a single transition duration; the first value applies to all properties");
                while (TryReadDurationMs(ref reader, out _) && reader.TryReadComma())
                {
                }
            }

            return new CssImmediateValue(
                UIElement.TransitionDurationProperty, new Duration(TimeSpan.FromMilliseconds(ms)));
        });

        RegisterLonghand("transition-timing-function", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var timing = ParseTimingFunction(ref reader);
            if (timing is null)
            {
                return null;
            }

            if (!reader.AtEnd && reader.TryReadComma())
            {
                CssDiagnostics.Report(
                    "transition-timing-function", CssDiagnosticReason.LossyConversion, null,
                    "the framework has a single timing function; the first value applies to all properties");
                while (ParseTimingFunction(ref reader) is not null && reader.TryReadComma())
                {
                }
            }

            return reader.AtEnd
                ? new CssImmediateValue(UIElement.TransitionTimingFunctionProperty, timing)
                : null;
        });

        RegisterLonghand("transition-delay", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!TryReadDurationMs(ref reader, out _))
            {
                return null;
            }

            CssDiagnostics.Report(
                "transition-delay", CssDiagnosticReason.LossyConversion, null,
                "transition delays are not supported; the delay is ignored");
            return CssNoOpValue.Instance;
        });

        RegisterShorthand("transition", ExpandTransitionShorthand);
    }

    private static bool ExpandTransitionShorthand(
        ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output)
    {
        var propertyNames = new List<string>();
        double? duration = null;
        object? timing = null;
        var reportedMultiDuration = false;
        var segmentIndex = 0;

        while (true)
        {
            var sawSegmentContent = false;
            string? segmentProperty = null;
            double? segmentDuration = null;
            var timeCount = 0;

            while (!reader.AtEnd)
            {
                var probe = reader;
                if (probe.TryPeekChar(out var c) && c == ',')
                {
                    break;
                }

                if (TryReadDurationMs(ref reader, out var ms))
                {
                    timeCount++;
                    sawSegmentContent = true;
                    if (timeCount == 1)
                    {
                        segmentDuration = ms;
                    }
                    else
                    {
                        CssDiagnostics.Report(
                            "transition", CssDiagnosticReason.LossyConversion, null,
                            "transition delays are not supported; the delay is ignored");
                    }

                    continue;
                }

                var timingCandidate = ParseTimingFunction(ref reader);
                if (timingCandidate is not null)
                {
                    timing ??= timingCandidate;
                    sawSegmentContent = true;
                    continue;
                }

                if (reader.TryReadIdent(out var ident))
                {
                    var name = ident.ToString();
                    sawSegmentContent = true;
                    if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        segmentProperty = TransitionPropertyCollection.AllKeyword;
                    }
                    else if (name.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        segmentProperty = TransitionPropertyCollection.NoneKeyword;
                    }
                    else if (TryGetTransitionTargetName(name, out var dpName))
                    {
                        segmentProperty = dpName;
                    }
                    else
                    {
                        CssDiagnostics.Report(
                            "transition", CssDiagnosticReason.LossyConversion, null,
                            $"'{name}' cannot be mapped to a dependency property; that segment is skipped");
                    }

                    continue;
                }

                return false;
            }

            if (!sawSegmentContent)
            {
                return false;
            }

            if (segmentProperty is not null)
            {
                propertyNames.Add(segmentProperty);
            }

            if (segmentDuration is not null)
            {
                if (duration is null)
                {
                    duration = segmentDuration;
                }
                else if (duration != segmentDuration && !reportedMultiDuration)
                {
                    reportedMultiDuration = true;
                    CssDiagnostics.Report(
                        "transition", CssDiagnosticReason.LossyConversion, null,
                        "the framework has a single transition duration; the first segment's duration applies to all");
                }
            }

            segmentIndex++;
            if (!reader.TryReadComma())
            {
                break;
            }
        }

        if (segmentIndex == 0)
        {
            return false;
        }

        TransitionPropertyCollection collection;
        if (propertyNames.Count == 1 &&
            propertyNames[0] == TransitionPropertyCollection.NoneKeyword)
        {
            collection = new TransitionPropertyCollection();
        }
        else if (propertyNames.Contains(TransitionPropertyCollection.AllKeyword))
        {
            collection = new TransitionPropertyCollection(new[] { TransitionPropertyCollection.AllKeyword });
        }
        else
        {
            collection = new TransitionPropertyCollection(
                propertyNames.Where(static n => n != TransitionPropertyCollection.NoneKeyword));
        }

        output.Add(new CssCompiledDeclaration(
            "transition-property", new CssImmediateValue(UIElement.TransitionPropertyProperty, collection), false));
        output.Add(new CssCompiledDeclaration(
            "transition-duration",
            new CssImmediateValue(
                UIElement.TransitionDurationProperty,
                new Duration(TimeSpan.FromMilliseconds(duration ?? 0))),
            false));
        output.Add(new CssCompiledDeclaration(
            "transition-timing-function",
            new CssImmediateValue(
                UIElement.TransitionTimingFunctionProperty,
                timing ?? TransitionTimingFunction.Recommended),
            false));
        return true;
    }

    private static TransitionPropertyCollection? ParseTransitionPropertyList(ref CssTokenReader reader)
    {
        var names = new List<string>();
        while (true)
        {
            if (!reader.TryReadIdent(out var ident))
            {
                return null;
            }

            var name = ident.ToString();
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(TransitionPropertyCollection.AllKeyword);
            }
            else if (name.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(TransitionPropertyCollection.NoneKeyword);
            }
            else if (TryGetTransitionTargetName(name, out var dpName))
            {
                names.Add(dpName);
            }
            else
            {
                CssDiagnostics.Report(
                    "transition-property", CssDiagnosticReason.LossyConversion, null,
                    $"'{name}' cannot be mapped to a dependency property; that entry is skipped");
            }

            if (!reader.TryReadComma())
            {
                break;
            }
        }

        if (!reader.AtEnd)
        {
            return null;
        }

        if (names.Contains(TransitionPropertyCollection.AllKeyword))
        {
            return new TransitionPropertyCollection(new[] { TransitionPropertyCollection.AllKeyword });
        }

        var effective = names.Where(static n => n != TransitionPropertyCollection.NoneKeyword).ToList();
        return effective.Count == 0
            ? new TransitionPropertyCollection()
            : new TransitionPropertyCollection(effective);
    }

    private static object? ParseTimingFunction(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            TransitionTimingFunction? timing = null;
            if (Eq(ident, "linear")) { timing = TransitionTimingFunction.Linear; }
            else if (Eq(ident, "ease")) { timing = TransitionTimingFunction.Recommended; }
            else if (Eq(ident, "ease-in")) { timing = TransitionTimingFunction.EaseIn; }
            else if (Eq(ident, "ease-out")) { timing = TransitionTimingFunction.EaseOut; }
            else if (Eq(ident, "ease-in-out")) { timing = TransitionTimingFunction.EaseInOut; }
            else if (Eq(ident, "step-start") || Eq(ident, "step-end"))
            {
                CssDiagnostics.Report(
                    "transition-timing-function", CssDiagnosticReason.LossyConversion, null,
                    $"'{ident.ToString()}' is approximated by the recommended curve");
                timing = TransitionTimingFunction.Recommended;
            }

            if (timing is not null)
            {
                reader = probe;
                return timing;
            }

            return null;
        }

        probe = reader;
        if (probe.TryReadFunction(out var fn, out _) &&
            (fn.Equals("cubic-bezier", StringComparison.OrdinalIgnoreCase) ||
             fn.Equals("steps", StringComparison.OrdinalIgnoreCase)))
        {
            CssDiagnostics.Report(
                "transition-timing-function", CssDiagnosticReason.LossyConversion, null,
                $"'{fn.ToString()}()' curves are approximated by the recommended curve");
            reader = probe;
            return TransitionTimingFunction.Recommended;
        }

        return null;
    }

    private static bool TryReadDurationMs(ref CssTokenReader reader, out double milliseconds)
    {
        milliseconds = 0;
        var probe = reader;
        if (probe.TryReadNumber(out var value, out var unit) &&
            CssUnitConversion.TryToMilliseconds(value, unit, out milliseconds) &&
            milliseconds >= 0)
        {
            reader = probe;
            return true;
        }

        return false;
    }
}
