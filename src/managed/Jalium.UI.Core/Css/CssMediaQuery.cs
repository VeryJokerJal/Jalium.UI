using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Interop;

namespace Jalium.UI.Styling;

/// <summary>Media-query lists retain unknown feature values under negation and resolve against the host viewport.</summary>
internal sealed class CssMediaQuery(CssBooleanQuery[] queries)
{
    // The current software and GPU presentation surfaces expose sRGB colors in
    // eight bits per component. Indexed and monochrome presentation are absent.
    private const int OutputColorBitsPerComponent = 8;
    internal const int PointingCoarse = 1;
    internal const int PointingFine = 2;
    internal const int PointingHover = 4;
    internal const int PointingPrimaryFine = 8;
    private static readonly object s_pointingGate = new();
    private static bool s_hasPointingSnapshot;
    private static int? s_pointingSnapshot;

    private static int? ReadPointingCapabilities()
    {
        try
        {
            return NativeMethods.InputGetPointingCapabilities(out var capabilities) == JaliumResult.Ok
                ? capabilities : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    private static int? GetPointingCapabilities()
    {
        lock (s_pointingGate)
            if (s_hasPointingSnapshot) return s_pointingSnapshot;
        var capabilities = ReadPointingCapabilities();
        lock (s_pointingGate)
        {
            if (!s_hasPointingSnapshot)
            {
                s_pointingSnapshot = capabilities;
                s_hasPointingSnapshot = true;
            }
            return s_pointingSnapshot;
        }
    }

    internal static void RefreshPointingDevices()
    {
        var capabilities = ReadPointingCapabilities();
        bool changed;
        lock (s_pointingGate)
        {
            changed = !s_hasPointingSnapshot || s_pointingSnapshot != capabilities;
            s_pointingSnapshot = capabilities;
            s_hasPointingSnapshot = true;
        }
        if (changed) CssMediaPreferenceDependency.PointingDevicesChanged();
    }

    internal static CssMediaQuery Parse(string text)
    {
        var reader = new CssTokenReader(CssParser.StripComments(text));
        if (reader.AtEnd) return new([new CssBooleanQuery.Constant(CssQueryResult.True)]);
        var queries = new List<CssBooleanQuery>();
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var part)) {queries.Add(new CssBooleanQuery.Constant(CssQueryResult.False)); break;}
            queries.Add(ParseOne(part.ToString()) ?? new CssBooleanQuery.Constant(CssQueryResult.False));
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) break;
            if (reader.AtEnd) queries.Add(new CssBooleanQuery.Constant(CssQueryResult.False));
        } while (!reader.AtEnd);
        return new(queries.ToArray());
    }

    private static CssBooleanQuery? ParseOne(string text)
    {
        var reader = new CssTokenReader(text);
        var negate = false;
        if (reader.TryReadIdent(out var type))
        {
            if (type.Equals("not",StringComparison.OrdinalIgnoreCase) || type.Equals("only",StringComparison.OrdinalIgnoreCase))
            {
                negate = type.Equals("not",StringComparison.OrdinalIgnoreCase);
                if (!reader.TryReadIdent(out type)) return negate ? CssBooleanQuery.Parse(text) : null;
            }
            var media = type.ToString().ToLowerInvariant();
            if (media is "and" or "or" or "not" or "only" or "layer") return null;
            CssBooleanQuery result = new CssBooleanQuery.Constant(media is "all" or "screen" ? CssQueryResult.True : CssQueryResult.False);
            if (!reader.AtEnd)
            {
                if (!reader.TryReadIdent(out var and) || !and.Equals("and",StringComparison.OrdinalIgnoreCase)) return null;
                var condition = CssBooleanQuery.Parse(reader.Remaining.ToString());
                if (condition is null || condition is CssBooleanQuery.Operation { Name:"or" }) return null;
                result = new CssBooleanQuery.Operation("and",result,condition);
            }
            return negate ? new CssBooleanQuery.Operation("not",result) : result;
        }
        return CssBooleanQuery.Parse(text);
    }

    internal bool Evaluate(CssNode element)
    {
        CssLengthContext? context = null;
        double? resolution = null;
        CssQueryResult Feature(string raw)
        {
            context ??= CssEngine.BuildLengthContext(element);
            resolution ??= DeviceResolution(element);
            return EvaluateFeature(raw,context.Value,resolution.Value,element);
        }
        return queries.Any(query => query.Evaluate(Feature) == CssQueryResult.True);
    }

    private static double DeviceResolution(CssNode element)
    {
        while (element.FrameworkParent is { } parent) element = parent;
        if (element.Target is Visual root)
        {
            var scale = VisualTreeHelper.GetDpi(root).DpiScaleY;
            if (double.IsFinite(scale) && scale > 0) return scale;
        }
        return 1;
    }

    private static Window? HostWindow(CssNode element)
    {
        for (var current=element; current is not null; current=current.FrameworkParent)
        {
            if (current.Target is Window window) return window;
            if (current.Target is Visual visual && Window.GetWindow(visual) is { } host)
                return host;
        }
        return null;
    }

    private static CssQueryResult EvaluateFeature(string text, CssLengthContext context, double resolution,
        CssNode element)
    {
        var parts = new List<string>(); var operations = new List<string>();
        var start = 0; var depth = 0;
        for (var i=0;i<text.Length;i++)
        {
            var c = text[i];
            if (c is '\'' or '"') { i=CssTokenReader.SkipString(text,i)-1; continue; }
            if (c=='\\') { if (!CssSyntax.ReadEscape(text,ref i,out _)) return CssQueryResult.Unknown; i--; continue; }
            if (c=='(') depth++;
            else if (c==')') depth--;
            else if (depth==0 && c is ':' or '<' or '>' or '=')
            {
                parts.Add(text[start..i].Trim()); var op=c.ToString();
                if (c is '<' or '>' && i+1<text.Length && text[i+1]=='=') {op+='='; i++;}
                operations.Add(op); start=i+1;
            }
        }
        parts.Add(text[start..].Trim());
        static string? Identifier(string text)
        {var reader=new CssTokenReader(text); return reader.TryReadIdent(out var name) && reader.AtEnd ? name.ToString().ToLowerInvariant() : null;}
        var name=Identifier(parts[0]); var reversed=false;
        if (name is null && parts.Count>1) {name=Identifier(parts[1]); reversed=true;}
        if (name is null) return CssQueryResult.Unknown;
        var colonSyntax=operations.Count==1 && operations[0]==":" && !reversed;
        if (colonSyntax)
        {
            operations[0]="=";
            if (name.StartsWith("min-",StringComparison.Ordinal)) {name=name[4..]; operations[0]=">=";}
            else if (name.StartsWith("max-",StringComparison.Ordinal)) {name=name[4..]; operations[0]="<=";}
        }
        if (name=="prefers-reduced-motion")
        {
            string? preference = null;
            if (operations.Count>0)
            {
                if (!colonSyntax || operations[0]!="=" || parts.Count!=2) return CssQueryResult.Unknown;
                preference=Identifier(parts[1]);
                if (preference is not ("reduce" or "no-preference")) return CssQueryResult.Unknown;
            }
            var state=CssEngine.EnsureState(element);
            (state.MediaPreferenceDependency ??= new(element)).Observe();
            var reduce=!SystemParameters.ClientAreaAnimation || !SystemParameters.UIEffects;
            return EvaluateReducedMotion(reduce,preference);
        }
        if (name=="prefers-color-scheme")
        {
            string? preference = null;
            if (operations.Count>0)
            {
                if (!colonSyntax || operations[0]!="=" || parts.Count!=2) return CssQueryResult.Unknown;
                preference=Identifier(parts[1]);
                if (preference is not ("light" or "dark")) return CssQueryResult.Unknown;
            }
            var state=CssEngine.EnsureState(element);
            (state.MediaPreferenceDependency ??= new(element)).Observe();
            return EvaluateColorScheme(SystemParameters.PrefersDarkColorScheme,preference);
        }
        if (name=="prefers-contrast")
        {
            string? preference = null;
            if (operations.Count>0)
            {
                if (!colonSyntax || operations[0]!="=" || parts.Count!=2) return CssQueryResult.Unknown;
                preference=Identifier(parts[1]);
                if (preference is not ("no-preference" or "less" or "more" or "custom"))
                    return CssQueryResult.Unknown;
            }
            var state=CssEngine.EnsureState(element);
            (state.MediaPreferenceDependency ??= new(element)).Observe();
            return EvaluateContrast(SystemParameters.HighContrast,preference);
        }
        if (name=="display-mode")
        {
            string? mode = null;
            if (operations.Count>0)
            {
                if (!colonSyntax || operations[0]!="=" || parts.Count!=2) return CssQueryResult.Unknown;
                mode=Identifier(parts[1]);
                if (mode is not ("fullscreen" or "standalone" or "minimal-ui" or "browser" or
                    "picture-in-picture")) return CssQueryResult.Unknown;
            }
            var fullscreen=HostWindow(element)?.WindowState==WindowState.FullScreen;
            return EvaluateDisplayMode(fullscreen,mode);
        }
        if (name is "pointer" or "hover" or "any-pointer" or "any-hover")
        {
            string? keyword = null;
            if (operations.Count>0)
            {
                if (!colonSyntax || operations[0]!="=" || parts.Count!=2)
                    return CssQueryResult.Unknown;
                keyword=Identifier(parts[1]);
                if (keyword is not ("none" or "coarse" or "fine" or "hover") ||
                    (name is "pointer" or "any-pointer") != (keyword is "none" or "coarse" or "fine"))
                    return CssQueryResult.Unknown;
            }
            var state=CssEngine.EnsureState(element);
            (state.MediaPreferenceDependency ??= new(element)).Observe();
            var capabilities=GetPointingCapabilities();
            return capabilities is { } value
                ? EvaluatePointingFeature(name,keyword,value)
                : CssQueryResult.Unknown;
        }
        if (name=="grid")
        {
            if (operations.Count==0) return CssQueryResult.False;
            if (!colonSyntax || operations[0]!="=" || parts.Count!=2) return CssQueryResult.Unknown;
            var reader=new CssTokenReader(parts[1]);
            if (!reader.TryReadInteger(out var value) || !reader.AtEnd || value is not (0 or 1))
                return CssQueryResult.Unknown;
            return value==0 ? CssQueryResult.True : CssQueryResult.False;
        }
        if (name is "color-gamut" or "video-color-gamut" or "scripting" or "dynamic-range" or
            "video-dynamic-range" or "environment-blending")
        {
            // Video is composited into the same eight-bit graphics surface;
            // there is no separate video display plane with different capabilities.
            var capability=name switch
            {
                "video-color-gamut" => "color-gamut",
                "video-dynamic-range" => "dynamic-range",
                _ => name,
            };
            if (operations.Count==0) return capability=="scripting" ? CssQueryResult.False : CssQueryResult.True;
            if (!colonSyntax || operations[0]!="=" || parts.Count!=2) return CssQueryResult.Unknown;
            var keyword=Identifier(parts[1]);
            return capability switch
            {
                "color-gamut" when keyword=="srgb" => CssQueryResult.True,
                "color-gamut" when keyword is "p3" or "rec2020" => CssQueryResult.False,
                "scripting" when keyword=="none" => CssQueryResult.True,
                "scripting" when keyword is "initial-only" or "enabled" => CssQueryResult.False,
                "dynamic-range" when keyword=="standard" => CssQueryResult.True,
                // The current eight-bit output cannot meet the HDR color-depth requirement.
                "dynamic-range" when keyword=="high" => CssQueryResult.False,
                "environment-blending" when keyword=="opaque" => CssQueryResult.True,
                "environment-blending" when keyword is "additive" or "subtractive" => CssQueryResult.False,
                _ => CssQueryResult.Unknown,
            };
        }
        if (name is not ("width" or "height" or "aspect-ratio" or "orientation" or "resolution" or
            "color" or "monochrome" or "color-index")) return CssQueryResult.Unknown;
        if (name=="orientation")
        {
            if (operations.Count==0) return CssQueryResult.True;
            if (!colonSyntax || operations[0]!="=") return CssQueryResult.Unknown;
            var orientation=Identifier(parts[1]);
            return orientation is not ("portrait" or "landscape") ? CssQueryResult.Unknown :
                (orientation=="landscape" ? context.ViewportWidth>context.ViewportHeight : context.ViewportHeight>=context.ViewportWidth) ? CssQueryResult.True : CssQueryResult.False;
        }
        var actual=name=="width" ? context.ViewportWidth : name=="height" ? context.ViewportHeight
            : name=="resolution" ? resolution
            : name=="color" ? OutputColorBitsPerComponent
            : name is "monochrome" or "color-index" ? 0
            : context.ViewportHeight==0 ? context.ViewportWidth==0 ? 1 : double.PositiveInfinity : context.ViewportWidth/context.ViewportHeight;
        if (operations.Count==0) return actual==0 ? CssQueryResult.False : CssQueryResult.True;
        if (operations.Count>2 || operations.Contains(":") || operations.Count==2 && (!reversed || operations[0][0]!=operations[1][0] || operations[0]=="=")) return CssQueryResult.Unknown;
        var first=parts[reversed ? 0 : 1];
        if (!Target(first,name,context,out var target)) return CssQueryResult.Unknown;
        var matches=Compare(actual,reversed ? Reverse(operations[0]) : operations[0],target);
        if (operations.Count==2)
        {
            if (!Target(parts[2],name,context,out target)) return CssQueryResult.Unknown;
            matches &= Compare(actual,operations[1],target);
        }
        return matches ? CssQueryResult.True : CssQueryResult.False;
    }

    private static bool Target(string text,string name,CssLengthContext context,out double value)
    {
        value=0;
        var reader=new CssTokenReader(text);
        if (name is "color" or "monochrome" or "color-index")
        {
            if (!reader.TryReadInteger(out var depth) || !reader.AtEnd) return false;
            value=depth;
            return true;
        }
        if (name=="resolution")
        {
            if (text.Equals("infinite",StringComparison.OrdinalIgnoreCase))
            {
                value=double.PositiveInfinity;
                return true;
            }
            if (!reader.TryReadNumber(out var number,out var unit) || !reader.AtEnd || !double.IsFinite(number)) return false;
            value=unit switch
            {
                CssUnit.Dppx => number,
                CssUnit.Dpi => number/96,
                CssUnit.Dpcm => number*2.54/96,
                _ => double.NaN,
            };
            return double.IsFinite(value);
        }
        if (name=="aspect-ratio")
        {
            if (!reader.TryReadNumber(out var numerator,out var unit) || unit!=CssUnit.None || numerator<0) return false;
            var denominator=1d;
            if (!reader.AtEnd && (!reader.TryReadSlash() || !reader.TryReadNumber(out denominator,out unit) || unit!=CssUnit.None || denominator<0)) return false;
            value=denominator==0 ? numerator==0 ? 1 : double.PositiveInfinity : numerator/denominator;
            return reader.AtEnd;
        }
        var lengths=new CssLengthContext(CssLengthContext.DefaultFontSize,CssLengthContext.DefaultFontSize,CssLengthContext.DefaultFontSize,
            context.ViewportWidth,context.ViewportHeight, fonts: CssFontContext.Initial with
            { Dependency = context.Fonts?.Dependency, MetricsEpoch = context.Fonts?.MetricsEpoch ?? 0 }, viewports: context.Viewports);
        return reader.TryReadLength(out var length) && reader.AtEnd && !length.UsesPercent &&
            (length.Unit!=CssUnit.None || length.Value==0) && length.TryResolve(lengths,CssPercentBasis.NotSupported,out value);
    }
    internal static CssQueryResult EvaluateReducedMotion(bool reduce, string? value) => value switch
    {
        null => reduce ? CssQueryResult.True : CssQueryResult.False,
        "reduce" => reduce ? CssQueryResult.True : CssQueryResult.False,
        "no-preference" => reduce ? CssQueryResult.False : CssQueryResult.True,
        _ => CssQueryResult.Unknown,
    };
    internal static CssQueryResult EvaluateColorScheme(bool dark, string? value) => value switch
    {
        null => CssQueryResult.True,
        "dark" => dark ? CssQueryResult.True : CssQueryResult.False,
        "light" => dark ? CssQueryResult.False : CssQueryResult.True,
        _ => CssQueryResult.Unknown,
    };
    internal static CssQueryResult EvaluateContrast(bool prefersMore, string? value) => value switch
    {
        null or "more" => prefersMore ? CssQueryResult.True : CssQueryResult.False,
        "no-preference" => prefersMore ? CssQueryResult.False : CssQueryResult.True,
        "less" or "custom" => CssQueryResult.False,
        _ => CssQueryResult.Unknown,
    };
    internal static CssQueryResult EvaluateDisplayMode(bool fullscreen, string? value) => value switch
    {
        null => CssQueryResult.True,
        "fullscreen" => fullscreen ? CssQueryResult.True : CssQueryResult.False,
        "standalone" => fullscreen ? CssQueryResult.False : CssQueryResult.True,
        "minimal-ui" or "browser" or "picture-in-picture" => CssQueryResult.False,
        _ => CssQueryResult.Unknown,
    };
    internal static CssQueryResult EvaluatePointingFeature(string feature, string? value, int capabilities)
    {
        if (feature is not ("pointer" or "hover" or "any-pointer" or "any-hover") ||
            value is not null &&
            ((feature is "pointer" or "any-pointer" && value is not ("none" or "coarse" or "fine")) ||
             (feature is "hover" or "any-hover" && value is not ("none" or "hover"))))
            return CssQueryResult.Unknown;
        var coarse = (capabilities & PointingCoarse) != 0;
        var fine = (capabilities & PointingFine) != 0;
        var hover = (capabilities & PointingHover) != 0;
        var primaryFine = fine && (!coarse || (capabilities & PointingPrimaryFine) != 0);
        var matches = feature switch
        {
            "pointer" => value switch
            {
                null => coarse || fine,
                "none" => !coarse && !fine,
                "coarse" => coarse && !primaryFine,
                "fine" => primaryFine,
                _ => false,
            },
            "any-pointer" => value switch
            {
                null => coarse || fine,
                "none" => !coarse && !fine,
                "coarse" => coarse,
                "fine" => fine,
                _ => false,
            },
            "hover" => value switch
            {
                null or "hover" => primaryFine && hover,
                "none" => !primaryFine || !hover,
                _ => false,
            },
            "any-hover" => value switch
            {
                null or "hover" => hover,
                "none" => !hover,
                _ => false,
            },
            _ => false,
        };
        return matches ? CssQueryResult.True : CssQueryResult.False;
    }
    private static string Reverse(string op) => op switch { ">"=>"<", ">="=>"<=", "<"=>">", "<="=>">=", _=>op };
    private static bool Compare(double actual,string op,double target) => op switch
    { ">"=>actual>target, ">="=>actual>=target, "<"=>actual<target, "<="=>actual<=target, "="=>actual==target || Math.Abs(actual-target)<1e-9, _=>false };
}
