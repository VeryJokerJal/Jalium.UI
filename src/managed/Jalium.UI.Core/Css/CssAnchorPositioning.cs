using System.Collections.Concurrent;

namespace Jalium.UI.Styling;

/// <summary>CSS Anchor Positioning Level 1 <c>anchor-scope</c> mode.</summary>
public enum CssAnchorScopeKind : byte
{
    None = 0,
    All = 1,
    Named = 2,
}

/// <summary>Represents the parsed value of <c>anchor-scope</c>.</summary>
public readonly record struct CssAnchorScope(CssAnchorScopeKind Kind, IReadOnlyList<string> Names)
{
    public static readonly CssAnchorScope None = new(CssAnchorScopeKind.None, []);
    public static readonly CssAnchorScope All = new(CssAnchorScopeKind.All, []);

    public bool Scopes(string dashedIdent) => Kind switch
    {
        CssAnchorScopeKind.All => true,
        CssAnchorScopeKind.Named => Names is { Count: > 0 } && Names.Any(n => string.Equals(n, dashedIdent, StringComparison.Ordinal)),
        _ => false,
    };
}

/// <summary>Single axis span in a 3x3 <c>position-area</c> grid around an anchor box.</summary>
public enum CssPositionAreaTrack : byte
{
    None = 0,
    Start = 1,      // top / left / block-start / inline-start
    Center = 2,     // center
    End = 3,        // bottom / right / block-end / inline-end
    SpanStart = 4,  // span-top / span-left / span-block-start / span-inline-start (Start + Center)
    SpanEnd = 5,    // span-bottom / span-right / span-block-end / span-inline-end (Center + End)
    SpanAll = 6,    // span-all (Start + Center + End)
}

/// <summary>Represents the parsed 3x3 grid region of <c>position-area</c> (<c>inset-area</c>).</summary>
public readonly record struct CssPositionArea(CssPositionAreaTrack Row, CssPositionAreaTrack Column)
{
    public static readonly CssPositionArea None = new(CssPositionAreaTrack.None, CssPositionAreaTrack.None);

    public bool IsNone => Row == CssPositionAreaTrack.None && Column == CssPositionAreaTrack.None;

    public CssPositionArea FlipBlock() => new(FlipTrack(Row), Column);

    public CssPositionArea FlipInline() => new(Row, FlipTrack(Column));

    public CssPositionArea FlipStart() => new(Column, Row);

    private static CssPositionAreaTrack FlipTrack(CssPositionAreaTrack track) => track switch
    {
        CssPositionAreaTrack.Start => CssPositionAreaTrack.End,
        CssPositionAreaTrack.End => CssPositionAreaTrack.Start,
        CssPositionAreaTrack.SpanStart => CssPositionAreaTrack.SpanEnd,
        CssPositionAreaTrack.SpanEnd => CssPositionAreaTrack.SpanStart,
        _ => track,
    };
}

/// <summary>CSS Anchor Positioning Level 1 <c>position-try-order</c> values.</summary>
public enum CssPositionTryOrder : byte
{
    Normal = 0,
    MostWidth = 1,
    MostHeight = 2,
    MostBlockSize = 3,
    MostInlineSize = 4,
}

/// <summary>CSS Anchor Positioning Level 1 <c>position-visibility</c> flags.</summary>
[Flags]
public enum CssPositionVisibility : byte
{
    Always = 0,
    AnchorsValid = 1 << 0,
    AnchorsVisible = 1 << 1,
    NoOverflow = 1 << 2,
}

/// <summary>Built-in tactic keyword in <c>position-try-fallbacks</c>.</summary>
public enum CssPositionTryTactic : byte
{
    None = 0,
    FlipBlock = 1,
    FlipInline = 2,
    FlipStart = 3,
}

/// <summary>Single fallback entry in <c>position-try-fallbacks</c>.</summary>
public readonly record struct CssPositionTryFallback(
    string? RuleName,
    CssPositionTryTactic Tactic,
    CssPositionArea Area);

/// <summary>Anchor side keyword inside <c>anchor(&lt;anchor-name&gt;? &lt;anchor-side&gt;, &lt;fallback&gt;?)</c>.</summary>
public enum CssAnchorSide : byte
{
    Top = 0,
    Right = 1,
    Bottom = 2,
    Left = 3,
    Center = 4,
    Start = 5,
    End = 6,
    SelfStart = 7,
    SelfEnd = 8,
    Inside = 9,
    Outside = 10,
    Percentage = 11,
}

/// <summary>Anchor dimension keyword inside <c>anchor-size(&lt;anchor-name&gt;? &lt;anchor-size&gt;?, &lt;fallback&gt;?)</c>.</summary>
public enum CssAnchorSizeDimension : byte
{
    Width = 0,
    Height = 1,
    Block = 2,
    Inline = 3,
    SelfBlock = 4,
    SelfInline = 5,
}

/// <summary>Parsed <c>anchor(...)</c> inset expression for a physical edge (0=left, 1=top, 2=right, 3=bottom).</summary>
public readonly record struct CssAnchorInsetExpression(
    int Edge,
    string? AnchorName,
    CssAnchorSide Side,
    double Percentage = 0.0,
    double? FallbackPx = null,
    double OffsetPx = 0.0);

/// <summary>Parsed <c>anchor-size(...)</c> sizing expression.</summary>
public readonly record struct CssAnchorSizeExpression(
    string? AnchorName,
    CssAnchorSizeDimension Dimension,
    double? FallbackPx = null,
    double Scale = 1.0,
    double OffsetPx = 0.0);

/// <summary>Represents a parsed <c>@position-try --name { ... }</c> rule.</summary>
public sealed record CssPositionTryRule(
    string Name,
    CssPositionArea? PositionArea = null,
    string? PositionAnchor = null,
    double? Left = null,
    double? Top = null,
    double? Right = null,
    double? Bottom = null,
    double? Width = null,
    double? Height = null,
    CssAnchorInsetExpression? AnchorLeft = null,
    CssAnchorInsetExpression? AnchorTop = null,
    CssAnchorInsetExpression? AnchorRight = null,
    CssAnchorInsetExpression? AnchorBottom = null);

/// <summary>Result of evaluating anchor positioning for a positioned element.</summary>
public readonly record struct CssAnchorPlacementResult(
    Rect Slot,
    bool IsVisible,
    int SelectedCandidateIndex,
    UIElement? ResolvedAnchor);

/// <summary>
/// CSS Anchor Positioning Level 1 (<c>anchor-name</c>, <c>anchor-scope</c>, <c>position-anchor</c>,
/// <c>position-area</c>, <c>position-try-fallbacks</c>, <c>position-try-order</c>, <c>position-try</c>,
/// <c>position-visibility</c>, <c>@position-try</c>, <c>anchor()</c>, and <c>anchor-size()</c>).
/// </summary>
public static class CssAnchorPositioning
{
    private static readonly ConcurrentDictionary<string, CssPositionTryRule> s_positionTryRules = new(StringComparer.Ordinal);

    public static readonly DependencyProperty AnchorNamesProperty = DependencyProperty.RegisterAttached(
        "AnchorNames",
        typeof(IReadOnlyList<string>),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(Array.Empty<string>(), OnAnchorPropertyChanged));

    public static readonly DependencyProperty AnchorScopeProperty = DependencyProperty.RegisterAttached(
        "AnchorScope",
        typeof(CssAnchorScope),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(CssAnchorScope.None, OnAnchorPropertyChanged));

    public static readonly DependencyProperty PositionAnchorProperty = DependencyProperty.RegisterAttached(
        "PositionAnchor",
        typeof(string),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(null, OnAnchorPropertyChanged));

    public static readonly DependencyProperty PositionAreaProperty = DependencyProperty.RegisterAttached(
        "PositionArea",
        typeof(CssPositionArea),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(CssPositionArea.None, OnAnchorPropertyChanged));

    public static readonly DependencyProperty PositionTryFallbacksProperty = DependencyProperty.RegisterAttached(
        "PositionTryFallbacks",
        typeof(IReadOnlyList<CssPositionTryFallback>),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(Array.Empty<CssPositionTryFallback>(), OnAnchorPropertyChanged));

    public static readonly DependencyProperty PositionTryOrderProperty = DependencyProperty.RegisterAttached(
        "PositionTryOrder",
        typeof(CssPositionTryOrder),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(CssPositionTryOrder.Normal, OnAnchorPropertyChanged));

    public static readonly DependencyProperty PositionVisibilityProperty = DependencyProperty.RegisterAttached(
        "PositionVisibility",
        typeof(CssPositionVisibility),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(CssPositionVisibility.AnchorsValid, OnAnchorPropertyChanged));

    public static readonly DependencyProperty AnchorLeftProperty = DependencyProperty.RegisterAttached(
        "AnchorLeft",
        typeof(CssAnchorInsetExpression?),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(null, OnAnchorPropertyChanged));

    public static readonly DependencyProperty AnchorTopProperty = DependencyProperty.RegisterAttached(
        "AnchorTop",
        typeof(CssAnchorInsetExpression?),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(null, OnAnchorPropertyChanged));

    public static readonly DependencyProperty AnchorRightProperty = DependencyProperty.RegisterAttached(
        "AnchorRight",
        typeof(CssAnchorInsetExpression?),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(null, OnAnchorPropertyChanged));

    public static readonly DependencyProperty AnchorBottomProperty = DependencyProperty.RegisterAttached(
        "AnchorBottom",
        typeof(CssAnchorInsetExpression?),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(null, OnAnchorPropertyChanged));

    public static readonly DependencyProperty AnchorWidthProperty = DependencyProperty.RegisterAttached(
        "AnchorWidth",
        typeof(CssAnchorSizeExpression?),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(null, OnAnchorPropertyChanged));

    public static readonly DependencyProperty AnchorHeightProperty = DependencyProperty.RegisterAttached(
        "AnchorHeight",
        typeof(CssAnchorSizeExpression?),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(null, OnAnchorPropertyChanged));

    public static readonly DependencyProperty IsPositionHiddenProperty = DependencyProperty.RegisterAttached(
        "IsPositionHidden",
        typeof(bool),
        typeof(CssAnchorPositioning),
        new PropertyMetadata(false));

    private static void OnAnchorPropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is UIElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element.InvalidateVisual();
            if (element.VisualParent is UIElement parent)
            {
                parent.InvalidateMeasure();
                parent.InvalidateArrange();
                parent.InvalidateVisual();
            }
        }
    }

    public static IReadOnlyList<string> GetAnchorNames(DependencyObject element)
        => element.GetValue(AnchorNamesProperty) as IReadOnlyList<string> ?? [];

    public static void SetAnchorNames(DependencyObject element, IReadOnlyList<string> value)
        => element.SetValue(AnchorNamesProperty, value);

    public static CssAnchorScope GetAnchorScope(DependencyObject element)
        => element.GetValue(AnchorScopeProperty) is CssAnchorScope scope ? scope : CssAnchorScope.None;

    public static void SetAnchorScope(DependencyObject element, CssAnchorScope value)
        => element.SetValue(AnchorScopeProperty, value);

    public static string? GetPositionAnchor(DependencyObject element)
        => element.GetValue(PositionAnchorProperty) as string;

    public static void SetPositionAnchor(DependencyObject element, string? value)
        => element.SetValue(PositionAnchorProperty, value);

    public static CssPositionArea GetPositionArea(DependencyObject element)
        => element.GetValue(PositionAreaProperty) is CssPositionArea area ? area : CssPositionArea.None;

    public static void SetPositionArea(DependencyObject element, CssPositionArea value)
        => element.SetValue(PositionAreaProperty, value);

    public static IReadOnlyList<CssPositionTryFallback> GetPositionTryFallbacks(DependencyObject element)
        => element.GetValue(PositionTryFallbacksProperty) as IReadOnlyList<CssPositionTryFallback> ?? [];

    public static void SetPositionTryFallbacks(DependencyObject element, IReadOnlyList<CssPositionTryFallback> value)
        => element.SetValue(PositionTryFallbacksProperty, value);

    public static CssPositionTryOrder GetPositionTryOrder(DependencyObject element)
        => element.GetValue(PositionTryOrderProperty) is CssPositionTryOrder order ? order : CssPositionTryOrder.Normal;

    public static void SetPositionTryOrder(DependencyObject element, CssPositionTryOrder value)
        => element.SetValue(PositionTryOrderProperty, value);

    public static CssPositionVisibility GetPositionVisibility(DependencyObject element)
        => element.GetValue(PositionVisibilityProperty) is CssPositionVisibility vis ? vis : CssPositionVisibility.AnchorsValid;

    public static void SetPositionVisibility(DependencyObject element, CssPositionVisibility value)
        => element.SetValue(PositionVisibilityProperty, value);

    public static bool GetIsPositionHidden(DependencyObject element)
        => element.GetValue(IsPositionHiddenProperty) is true;

    public static void RegisterPositionTryRule(CssPositionTryRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.Name))
        {
            s_positionTryRules[rule.Name] = rule;
        }
    }

    public static bool TryGetPositionTryRule(string name, out CssPositionTryRule? rule)
        => s_positionTryRules.TryGetValue(name, out rule);

    public static void ClearPositionTryRules() => s_positionTryRules.Clear();

    /// <summary>Parses any <c>@position-try --name { ... }</c> blocks in <paramref name="cssText"/> and registers them.</summary>
    public static int ParseAndRegisterPositionTryRules(string cssText)
    {
        if (string.IsNullOrWhiteSpace(cssText))
        {
            return 0;
        }

        var count = 0;
        var idx = 0;
        while (idx < cssText.Length)
        {
            var atIdx = cssText.IndexOf("@position-try", idx, StringComparison.OrdinalIgnoreCase);
            if (atIdx < 0)
            {
                break;
            }

            var nameStart = atIdx + "@position-try".Length;
            var braceOpen = cssText.IndexOf('{', nameStart);
            if (braceOpen < 0)
            {
                break;
            }

            var braceClose = cssText.IndexOf('}', braceOpen + 1);
            if (braceClose < 0)
            {
                break;
            }

            var ruleName = cssText[nameStart..braceOpen].Trim();
            var body = cssText[(braceOpen + 1)..braceClose];
            if (ruleName.StartsWith("--", StringComparison.Ordinal))
            {
                var rule = ParsePositionTryRuleBody(ruleName, body);
                RegisterPositionTryRule(rule);
                count++;
            }

            idx = braceClose + 1;
        }

        return count;
    }

    private static CssPositionTryRule ParsePositionTryRuleBody(string name, string body)
    {
        CssPositionArea? area = null;
        string? posAnchor = null;
        double? left = null, top = null, right = null, bottom = null, width = null, height = null;
        CssAnchorInsetExpression? aLeft = null, aTop = null, aRight = null, aBottom = null;

        foreach (var rawDecl in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = rawDecl.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var prop = rawDecl[..colon].Trim().ToLowerInvariant();
            var val = rawDecl[(colon + 1)..].Trim();
            var reader = new CssTokenReader(val);

            switch (prop)
            {
                case "position-area" or "inset-area":
                    if (TryParsePositionArea(ref reader, out var parsedArea) && reader.AtEnd)
                    {
                        area = parsedArea;
                    }
                    break;
                case "position-anchor":
                    if (reader.TryReadIdent(out var anchorIdent) && reader.AtEnd)
                    {
                        var identStr = anchorIdent.ToString();
                        posAnchor = identStr.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : identStr;
                    }
                    break;
                case "left":
                    if (TryParseAnchorInset(0, val, out var al)) aLeft = al;
                    else if (reader.TryReadLength(out var lLen) && lLen.IsAbsolute) left = lLen.ToPxAbsolute();
                    break;
                case "top":
                    if (TryParseAnchorInset(1, val, out var at)) aTop = at;
                    else if (reader.TryReadLength(out var tLen) && tLen.IsAbsolute) top = tLen.ToPxAbsolute();
                    break;
                case "right":
                    if (TryParseAnchorInset(2, val, out var ar)) aRight = ar;
                    else if (reader.TryReadLength(out var rLen) && rLen.IsAbsolute) right = rLen.ToPxAbsolute();
                    break;
                case "bottom":
                    if (TryParseAnchorInset(3, val, out var ab)) aBottom = ab;
                    else if (reader.TryReadLength(out var bLen) && bLen.IsAbsolute) bottom = bLen.ToPxAbsolute();
                    break;
                case "width" or "inline-size":
                    if (reader.TryReadLength(out var wLen) && wLen.IsAbsolute) width = wLen.ToPxAbsolute();
                    break;
                case "height" or "block-size":
                    if (reader.TryReadLength(out var hLen) && hLen.IsAbsolute) height = hLen.ToPxAbsolute();
                    break;
            }
        }

        return new CssPositionTryRule(name, area, posAnchor, left, top, right, bottom, width, height, aLeft, aTop, aRight, aBottom);
    }

    public static bool HasAnchorConfiguration(FrameworkElement element)
    {
        if (!string.IsNullOrEmpty(GetPositionAnchor(element)) || !GetPositionArea(element).IsNone)
        {
            return true;
        }

        return element.GetValue(AnchorLeftProperty) is not null
            || element.GetValue(AnchorTopProperty) is not null
            || element.GetValue(AnchorRightProperty) is not null
            || element.GetValue(AnchorBottomProperty) is not null
            || element.GetValue(AnchorWidthProperty) is not null
            || element.GetValue(AnchorHeightProperty) is not null;
    }

    /// <summary>
    /// Resolves the target anchor element for <paramref name="positionedElement"/> within <paramref name="container"/>,
    /// respecting <c>anchor-name</c>, <c>position-anchor</c>, and <c>anchor-scope</c>.
    /// </summary>
    public static UIElement? ResolveAnchorElement(
        FrameworkElement positionedElement,
        UIElement? container,
        string? explicitAnchorName = null)
    {
        var targetName = !string.IsNullOrEmpty(explicitAnchorName)
            ? explicitAnchorName
            : GetPositionAnchor(positionedElement);

        if (string.IsNullOrEmpty(targetName) || targetName.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = container ?? FindRootElement(positionedElement);
        if (root is null)
        {
            return null;
        }

        var scopeBoundary = FindScopeBoundary(positionedElement, root, targetName);
        var candidates = new List<UIElement>();
        CollectMatchingAnchors(scopeBoundary, scopeBoundary, targetName, positionedElement, candidates);
        return candidates.Count > 0 ? candidates[^1] : null;
    }

    private static UIElement? FindRootElement(UIElement start)
    {
        UIElement? current = start;
        while (current.VisualParent is UIElement parent)
        {
            current = parent;
        }

        return current;
    }

    private static UIElement FindScopeBoundary(UIElement element, UIElement fallbackRoot, string anchorName)
    {
        UIElement? current = element.VisualParent as UIElement;
        while (current is not null)
        {
            if (GetAnchorScope(current).Scopes(anchorName))
            {
                return current;
            }

            if (ReferenceEquals(current, fallbackRoot))
            {
                break;
            }

            current = current.VisualParent as UIElement;
        }

        return fallbackRoot;
    }

    private static void CollectMatchingAnchors(
        UIElement current,
        UIElement scopeBoundary,
        string anchorName,
        UIElement positionedElement,
        List<UIElement> results)
    {
        if (!ReferenceEquals(current, scopeBoundary) && GetAnchorScope(current).Scopes(anchorName))
        {
            // Nested anchor-scope isolates its subtree from outer queries for this anchorName
            return;
        }

        if (!ReferenceEquals(current, positionedElement))
        {
            var names = GetAnchorNames(current);
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], anchorName, StringComparison.Ordinal))
                {
                    results.Add(current);
                    break;
                }
            }
        }

        var count = current.VisualChildrenCount;
        for (var i = 0; i < count; i++)
        {
            if (current.GetVisualChild(i) is UIElement child)
            {
                CollectMatchingAnchors(child, scopeBoundary, anchorName, positionedElement, results);
            }
        }
    }

    public static Rect GetAnchorRectInContainer(UIElement anchor, UIElement? container)
    {
        var size = anchor.RenderSize;
        if (size.Width <= 0 && size.Height <= 0)
        {
            size = anchor.DesiredSize;
        }

        double offsetX = anchor.VisualBounds.X + anchor.RenderOffset.X;
        double offsetY = anchor.VisualBounds.Y + anchor.RenderOffset.Y;
        UIElement? current = anchor.VisualParent as UIElement;
        while (current is not null && !ReferenceEquals(current, container))
        {
            offsetX += current.VisualBounds.X + current.RenderOffset.X;
            offsetY += current.VisualBounds.Y + current.RenderOffset.Y;
            current = current.VisualParent as UIElement;
        }

        return new Rect(offsetX, offsetY, Math.Max(0, size.Width), Math.Max(0, size.Height));
    }

    /// <summary>Evaluates an <c>anchor()</c> expression against <paramref name="anchorRect"/> or returns its fallback.</summary>
    public static bool TryEvaluateAnchorInset(
        CssAnchorInsetExpression expr,
        Rect? anchorRect,
        Size containerSize,
        CssWritingMode writingMode,
        bool rightToLeft,
        out double resultPx)
    {
        if (anchorRect is null)
        {
            if (expr.FallbackPx is { } fb)
            {
                resultPx = fb + expr.OffsetPx;
                return true;
            }

            resultPx = 0;
            return false;
        }

        var rect = anchorRect.Value;
        var vertical = CssWritingModeProperties.IsVertical(writingMode);
        var isHorizontalEdge = expr.Edge is 0 or 2;

        // Coordinate on the relevant axis (X for Left/Right edges, Y for Top/Bottom edges)
        double coord = expr.Side switch
        {
            CssAnchorSide.Left => rect.Left,
            CssAnchorSide.Right => rect.Right,
            CssAnchorSide.Top => rect.Top,
            CssAnchorSide.Bottom => rect.Bottom,
            CssAnchorSide.Center => isHorizontalEdge ? rect.Left + rect.Width * 0.5 : rect.Top + rect.Height * 0.5,
            CssAnchorSide.Percentage => isHorizontalEdge
                ? rect.Left + rect.Width * (expr.Percentage / 100.0)
                : rect.Top + rect.Height * (expr.Percentage / 100.0),
            CssAnchorSide.Start or CssAnchorSide.SelfStart => isHorizontalEdge
                ? (rightToLeft ? rect.Right : rect.Left)
                : (vertical && CssWritingModeProperties.IsInlineBottomToTop(writingMode, rightToLeft) ? rect.Bottom : rect.Top),
            CssAnchorSide.End or CssAnchorSide.SelfEnd => isHorizontalEdge
                ? (rightToLeft ? rect.Left : rect.Right)
                : (vertical && CssWritingModeProperties.IsInlineBottomToTop(writingMode, rightToLeft) ? rect.Top : rect.Bottom),
            CssAnchorSide.Inside => expr.Edge switch
            {
                0 => rect.Left,
                1 => rect.Top,
                2 => rect.Right,
                _ => rect.Bottom,
            },
            CssAnchorSide.Outside => expr.Edge switch
            {
                0 => rect.Right,
                1 => rect.Bottom,
                2 => rect.Left,
                _ => rect.Top,
            },
            _ => isHorizontalEdge ? rect.Left : rect.Top,
        };

        // Convert physical coordinate into inset distance from the container edge:
        // For Left (0) and Top (1), inset is distance from 0 -> coord.
        // For Right (2), inset is containerWidth - coord.
        // For Bottom (3), inset is containerHeight - coord.
        resultPx = expr.Edge switch
        {
            0 => coord + expr.OffsetPx,
            1 => coord + expr.OffsetPx,
            2 => containerSize.Width - coord + expr.OffsetPx,
            3 => containerSize.Height - coord + expr.OffsetPx,
            _ => coord + expr.OffsetPx,
        };
        return true;
    }

    /// <summary>Evaluates an <c>anchor-size()</c> expression against <paramref name="anchorRect"/> or returns its fallback.</summary>
    public static bool TryEvaluateAnchorSize(
        CssAnchorSizeExpression expr,
        Rect? anchorRect,
        CssWritingMode writingMode,
        out double sizePx)
    {
        if (anchorRect is null)
        {
            if (expr.FallbackPx is { } fb)
            {
                sizePx = Math.Max(0, fb * expr.Scale + expr.OffsetPx);
                return true;
            }

            sizePx = double.NaN;
            return false;
        }

        var rect = anchorRect.Value;
        var vertical = CssWritingModeProperties.IsVertical(writingMode);
        var raw = expr.Dimension switch
        {
            CssAnchorSizeDimension.Width => rect.Width,
            CssAnchorSizeDimension.Height => rect.Height,
            CssAnchorSizeDimension.Inline or CssAnchorSizeDimension.SelfInline => vertical ? rect.Height : rect.Width,
            CssAnchorSizeDimension.Block or CssAnchorSizeDimension.SelfBlock => vertical ? rect.Width : rect.Height,
            _ => rect.Width,
        };

        sizePx = Math.Max(0, raw * expr.Scale + expr.OffsetPx);
        return true;
    }

    /// <summary>
    /// Computes the anchor-positioned slot for <paramref name="child"/> inside <paramref name="panelSize"/>,
    /// evaluating <c>position-area</c>, <c>anchor()</c>, <c>anchor-size()</c>, <c>position-try-fallbacks</c>,
    /// <c>position-try-order</c>, and <c>position-visibility</c>.
    /// </summary>
    internal static CssAnchorPlacementResult EvaluatePlacement(
        FrameworkElement child,
        UIElement? container,
        CssLayoutState layout,
        Size panelSize,
        Size childDesired,
        double effectiveWidth,
        double effectiveHeight)
    {
        var writingMode = CssWritingModeProperties.GetWritingMode(child);
        var rtl = child.FlowDirection == FlowDirection.RightToLeft;

        // Resolve anchor-size() if present
        if (child.GetValue(AnchorWidthProperty) is CssAnchorSizeExpression widthExpr)
        {
            var widthAnchor = ResolveAnchorElement(child, container, widthExpr.AnchorName);
            Rect? widthAnchorRect = widthAnchor is not null ? GetAnchorRectInContainer(widthAnchor, container) : null;
            if (TryEvaluateAnchorSize(widthExpr, widthAnchorRect, writingMode, out var resolvedW))
            {
                effectiveWidth = resolvedW;
            }
        }

        if (child.GetValue(AnchorHeightProperty) is CssAnchorSizeExpression heightExpr)
        {
            var heightAnchor = ResolveAnchorElement(child, container, heightExpr.AnchorName);
            Rect? heightAnchorRect = heightAnchor is not null ? GetAnchorRectInContainer(heightAnchor, container) : null;
            if (TryEvaluateAnchorSize(heightExpr, heightAnchorRect, writingMode, out var resolvedH))
            {
                effectiveHeight = resolvedH;
            }
        }

        var defaultAnchor = ResolveAnchorElement(child, container, null);
        Rect? defaultAnchorRect = defaultAnchor is not null ? GetAnchorRectInContainer(defaultAnchor, container) : null;

        var baseCandidate = EvaluateSingleCandidate(
            child,
            container,
            layout,
            panelSize,
            childDesired,
            effectiveWidth,
            effectiveHeight,
            writingMode,
            rtl,
            defaultAnchorRect,
            GetPositionArea(child),
            child.GetValue(AnchorLeftProperty) as CssAnchorInsetExpression?,
            child.GetValue(AnchorTopProperty) as CssAnchorInsetExpression?,
            child.GetValue(AnchorRightProperty) as CssAnchorInsetExpression?,
            child.GetValue(AnchorBottomProperty) as CssAnchorInsetExpression?,
            overrideRule: null,
            tactic: CssPositionTryTactic.None,
            out var referencedAnyAnchor,
            out var allReferencedAnchorsValid);

        var fallbacks = GetPositionTryFallbacks(child);
        var candidates = new List<(Rect Slot, Size AvailableArea, int Index)>
        {
            (baseCandidate.Slot, baseCandidate.AvailableArea, 0),
        };

        for (var i = 0; i < fallbacks.Count; i++)
        {
            var fb = fallbacks[i];
            CssPositionTryRule? rule = null;
            if (!string.IsNullOrEmpty(fb.RuleName))
            {
                TryGetPositionTryRule(fb.RuleName, out rule);
            }

            var fbAnchor = !string.IsNullOrEmpty(rule?.PositionAnchor)
                ? ResolveAnchorElement(child, container, rule!.PositionAnchor)
                : defaultAnchor;
            Rect? fbAnchorRect = fbAnchor is not null ? GetAnchorRectInContainer(fbAnchor, container) : defaultAnchorRect;

            var area = !fb.Area.IsNone
                ? fb.Area
                : rule?.PositionArea ?? GetPositionArea(child);

            var candidate = EvaluateSingleCandidate(
                child,
                container,
                layout,
                panelSize,
                childDesired,
                rule?.Width ?? effectiveWidth,
                rule?.Height ?? effectiveHeight,
                writingMode,
                rtl,
                fbAnchorRect,
                area,
                rule?.AnchorLeft ?? child.GetValue(AnchorLeftProperty) as CssAnchorInsetExpression?,
                rule?.AnchorTop ?? child.GetValue(AnchorTopProperty) as CssAnchorInsetExpression?,
                rule?.AnchorRight ?? child.GetValue(AnchorRightProperty) as CssAnchorInsetExpression?,
                rule?.AnchorBottom ?? child.GetValue(AnchorBottomProperty) as CssAnchorInsetExpression?,
                rule,
                fb.Tactic,
                out _,
                out _);

            candidates.Add((candidate.Slot, candidate.AvailableArea, i + 1));
        }

        // Order candidates according to position-try-order
        var order = GetPositionTryOrder(child);
        IEnumerable<(Rect Slot, Size AvailableArea, int Index)> ordered = order switch
        {
            CssPositionTryOrder.MostWidth => candidates.OrderByDescending(static c => c.AvailableArea.Width).ThenBy(static c => c.Index),
            CssPositionTryOrder.MostHeight => candidates.OrderByDescending(static c => c.AvailableArea.Height).ThenBy(static c => c.Index),
            CssPositionTryOrder.MostInlineSize => CssWritingModeProperties.IsVertical(writingMode)
                ? candidates.OrderByDescending(static c => c.AvailableArea.Height).ThenBy(static c => c.Index)
                : candidates.OrderByDescending(static c => c.AvailableArea.Width).ThenBy(static c => c.Index),
            CssPositionTryOrder.MostBlockSize => CssWritingModeProperties.IsVertical(writingMode)
                ? candidates.OrderByDescending(static c => c.AvailableArea.Width).ThenBy(static c => c.Index)
                : candidates.OrderByDescending(static c => c.AvailableArea.Height).ThenBy(static c => c.Index),
            _ => candidates,
        };

        // Pick the first candidate that fits inside container bounds, or fall back to the first in ordered list
        (Rect Slot, Size AvailableArea, int Index)? selected = null;
        foreach (var cand in ordered)
        {
            if (FitsWithinContainer(cand.Slot, panelSize))
            {
                selected = cand;
                break;
            }
        }

        var chosen = selected ?? ordered.First();
        var fits = FitsWithinContainer(chosen.Slot, panelSize);

        // Evaluate position-visibility
        var visibilityFlags = GetPositionVisibility(child);
        var isVisible = true;
        if (visibilityFlags != CssPositionVisibility.Always)
        {
            if (visibilityFlags.HasFlag(CssPositionVisibility.AnchorsValid) && referencedAnyAnchor && !allReferencedAnchorsValid)
            {
                isVisible = false;
            }

            if (isVisible && visibilityFlags.HasFlag(CssPositionVisibility.AnchorsVisible) && defaultAnchor is not null)
            {
                var anchorVisible = defaultAnchor.Visibility == Visibility.Visible &&
                    defaultAnchorRect is { } ar &&
                    ar.Right > 0 && ar.Bottom > 0 &&
                    ar.Left < panelSize.Width && ar.Top < panelSize.Height;
                if (!anchorVisible)
                {
                    isVisible = false;
                }
            }

            if (isVisible && visibilityFlags.HasFlag(CssPositionVisibility.NoOverflow) && !fits)
            {
                isVisible = false;
            }
        }

        child.SetValue(IsPositionHiddenProperty, !isVisible);
        return new CssAnchorPlacementResult(chosen.Slot, isVisible, chosen.Index, defaultAnchor);
    }

    private static bool FitsWithinContainer(Rect slot, Size panelSize)
    {
        const double epsilon = 0.5;
        return slot.Left >= -epsilon
            && slot.Top >= -epsilon
            && slot.Right <= panelSize.Width + epsilon
            && slot.Bottom <= panelSize.Height + epsilon;
    }

    private static (Rect Slot, Size AvailableArea) EvaluateSingleCandidate(
        FrameworkElement child,
        UIElement? container,
        CssLayoutState baseLayout,
        Size panelSize,
        Size childDesired,
        double effectiveWidth,
        double effectiveHeight,
        CssWritingMode writingMode,
        bool rtl,
        Rect? defaultAnchorRect,
        CssPositionArea area,
        CssAnchorInsetExpression? anchorLeft,
        CssAnchorInsetExpression? anchorTop,
        CssAnchorInsetExpression? anchorRight,
        CssAnchorInsetExpression? anchorBottom,
        CssPositionTryRule? overrideRule,
        CssPositionTryTactic tactic,
        out bool referencedAnyAnchor,
        out bool allReferencedAnchorsValid)
    {
        referencedAnyAnchor = !string.IsNullOrEmpty(GetPositionAnchor(child)) || !area.IsNone;
        allReferencedAnchorsValid = string.IsNullOrEmpty(GetPositionAnchor(child)) || defaultAnchorRect is not null;

        // Apply tactic to position-area and anchor inset expressions
        var effectiveArea = tactic switch
        {
            CssPositionTryTactic.FlipBlock => area.FlipBlock(),
            CssPositionTryTactic.FlipInline => area.FlipInline(),
            CssPositionTryTactic.FlipStart => area.FlipStart(),
            _ => area,
        };

        if (tactic != CssPositionTryTactic.None)
        {
            (anchorLeft, anchorTop, anchorRight, anchorBottom) = ApplyTacticToInsets(
                tactic, anchorLeft, anchorTop, anchorRight, anchorBottom);
        }

        // 1. If position-area is specified and we have a valid anchor rect, compute the 3x3 position-area cell
        if (!effectiveArea.IsNone && defaultAnchorRect is { } anchorRect)
        {
            var cell = ComputePositionAreaCell(effectiveArea, anchorRect, panelSize);
            var w = !double.IsNaN(effectiveWidth) ? effectiveWidth : Math.Min(childDesired.Width, cell.Width);
            var h = !double.IsNaN(effectiveHeight) ? effectiveHeight : Math.Min(childDesired.Height, cell.Height);
            if (w <= 0) w = childDesired.Width;
            if (h <= 0) h = childDesired.Height;

            var x = effectiveArea.Column switch
            {
                CssPositionAreaTrack.Start => cell.Right - w,
                CssPositionAreaTrack.End => cell.Left,
                CssPositionAreaTrack.Center => cell.Left + (cell.Width - w) * 0.5,
                CssPositionAreaTrack.SpanStart => cell.Right - w,
                CssPositionAreaTrack.SpanEnd => cell.Left,
                _ => cell.Left,
            };

            var y = effectiveArea.Row switch
            {
                CssPositionAreaTrack.Start => cell.Bottom - h,
                CssPositionAreaTrack.End => cell.Top,
                CssPositionAreaTrack.Center => cell.Top + (cell.Height - h) * 0.5,
                CssPositionAreaTrack.SpanStart => cell.Bottom - h,
                CssPositionAreaTrack.SpanEnd => cell.Top,
                _ => cell.Top,
            };

            return (new Rect(x, y, Math.Max(0, w), Math.Max(0, h)), new Size(cell.Width, cell.Height));
        }

        // 2. Otherwise evaluate anchor() insets + @position-try rule overrides
        var modifiedLayout = baseLayout.CloneForLayout();
        if (overrideRule?.Left is { } rLeft) modifiedLayout.InsetLeft = CssLayoutLength.Px(rLeft);
        if (overrideRule?.Top is { } rTop) modifiedLayout.InsetTop = CssLayoutLength.Px(rTop);
        if (overrideRule?.Right is { } rRight) modifiedLayout.InsetRight = CssLayoutLength.Px(rRight);
        if (overrideRule?.Bottom is { } rBottom) modifiedLayout.InsetBottom = CssLayoutLength.Px(rBottom);

        if (anchorLeft is { } al)
        {
            referencedAnyAnchor = true;
            var aRect = ResolveExpressionAnchorRect(child, container, al.AnchorName, defaultAnchorRect);
            if (aRect is null) allReferencedAnchorsValid = false;
            if (TryEvaluateAnchorInset(al, aRect, panelSize, writingMode, rtl, out var px))
            {
                modifiedLayout.InsetLeft = CssLayoutLength.Px(px);
            }
        }

        if (anchorTop is { } at)
        {
            referencedAnyAnchor = true;
            var aRect = ResolveExpressionAnchorRect(child, container, at.AnchorName, defaultAnchorRect);
            if (aRect is null) allReferencedAnchorsValid = false;
            if (TryEvaluateAnchorInset(at, aRect, panelSize, writingMode, rtl, out var px))
            {
                modifiedLayout.InsetTop = CssLayoutLength.Px(px);
            }
        }

        if (anchorRight is { } ar)
        {
            referencedAnyAnchor = true;
            var aRect = ResolveExpressionAnchorRect(child, container, ar.AnchorName, defaultAnchorRect);
            if (aRect is null) allReferencedAnchorsValid = false;
            if (TryEvaluateAnchorInset(ar, aRect, panelSize, writingMode, rtl, out var px))
            {
                modifiedLayout.InsetRight = CssLayoutLength.Px(px);
            }
        }

        if (anchorBottom is { } ab)
        {
            referencedAnyAnchor = true;
            var aRect = ResolveExpressionAnchorRect(child, container, ab.AnchorName, defaultAnchorRect);
            if (aRect is null) allReferencedAnchorsValid = false;
            if (TryEvaluateAnchorInset(ab, aRect, panelSize, writingMode, rtl, out var px))
            {
                modifiedLayout.InsetBottom = CssLayoutLength.Px(px);
            }
        }

        var slot = CssAbsoluteLayout.ComputeSlotCore(
            modifiedLayout, panelSize, childDesired, effectiveWidth, effectiveHeight, child, null, null);
        var availW = CalculateAvailableAxisSpace(modifiedLayout.InsetLeft, modifiedLayout.InsetRight, panelSize.Width);
        var availH = CalculateAvailableAxisSpace(modifiedLayout.InsetTop, modifiedLayout.InsetBottom, panelSize.Height);
        return (slot, new Size(availW, availH));
    }

    private static double CalculateAvailableAxisSpace(CssLayoutLength start, CssLayoutLength end, double containerExtent)
    {
        var s = start.IsSet && !start.IsAuto ? start.Resolve(containerExtent, 0) : 0;
        var e = end.IsSet && !end.IsAuto ? end.Resolve(containerExtent, 0) : 0;
        return Math.Max(0, containerExtent - s - e);
    }

    private static Rect? ResolveExpressionAnchorRect(
        FrameworkElement child,
        UIElement? container,
        string? explicitName,
        Rect? defaultAnchorRect)
    {
        if (string.IsNullOrEmpty(explicitName))
        {
            return defaultAnchorRect;
        }

        var target = ResolveAnchorElement(child, container, explicitName);
        return target is not null ? GetAnchorRectInContainer(target, container) : null;
    }

    private static (
        CssAnchorInsetExpression? Left,
        CssAnchorInsetExpression? Top,
        CssAnchorInsetExpression? Right,
        CssAnchorInsetExpression? Bottom) ApplyTacticToInsets(
            CssPositionTryTactic tactic,
            CssAnchorInsetExpression? left,
            CssAnchorInsetExpression? top,
            CssAnchorInsetExpression? right,
            CssAnchorInsetExpression? bottom)
    {
        if (tactic == CssPositionTryTactic.FlipBlock)
        {
            CssAnchorInsetExpression? newTop = bottom is { } b ? b with { Edge = 1, Side = FlipVerticalSide(b.Side) } : null;
            CssAnchorInsetExpression? newBottom = top is { } t ? t with { Edge = 3, Side = FlipVerticalSide(t.Side) } : null;
            return (left, newTop, right, newBottom);
        }

        if (tactic == CssPositionTryTactic.FlipInline)
        {
            CssAnchorInsetExpression? newLeft = right is { } r ? r with { Edge = 0, Side = FlipHorizontalSide(r.Side) } : null;
            CssAnchorInsetExpression? newRight = left is { } l ? l with { Edge = 2, Side = FlipHorizontalSide(l.Side) } : null;
            return (newLeft, top, newRight, bottom);
        }

        if (tactic == CssPositionTryTactic.FlipStart)
        {
            CssAnchorInsetExpression? newLeft = top is { } t ? t with { Edge = 0, Side = SwapAxisSide(t.Side) } : null;
            CssAnchorInsetExpression? newTop = left is { } l ? l with { Edge = 1, Side = SwapAxisSide(l.Side) } : null;
            CssAnchorInsetExpression? newRight = bottom is { } b ? b with { Edge = 2, Side = SwapAxisSide(b.Side) } : null;
            CssAnchorInsetExpression? newBottom = right is { } r ? r with { Edge = 3, Side = SwapAxisSide(r.Side) } : null;
            return (newLeft, newTop, newRight, newBottom);
        }

        return (left, top, right, bottom);
    }

    private static CssAnchorSide FlipVerticalSide(CssAnchorSide side) => side switch
    {
        CssAnchorSide.Top => CssAnchorSide.Bottom,
        CssAnchorSide.Bottom => CssAnchorSide.Top,
        CssAnchorSide.Start => CssAnchorSide.End,
        CssAnchorSide.End => CssAnchorSide.Start,
        CssAnchorSide.SelfStart => CssAnchorSide.SelfEnd,
        CssAnchorSide.SelfEnd => CssAnchorSide.SelfStart,
        _ => side,
    };

    private static CssAnchorSide FlipHorizontalSide(CssAnchorSide side) => side switch
    {
        CssAnchorSide.Left => CssAnchorSide.Right,
        CssAnchorSide.Right => CssAnchorSide.Left,
        CssAnchorSide.Start => CssAnchorSide.End,
        CssAnchorSide.End => CssAnchorSide.Start,
        CssAnchorSide.SelfStart => CssAnchorSide.SelfEnd,
        CssAnchorSide.SelfEnd => CssAnchorSide.SelfStart,
        _ => side,
    };

    private static CssAnchorSide SwapAxisSide(CssAnchorSide side) => side switch
    {
        CssAnchorSide.Left => CssAnchorSide.Top,
        CssAnchorSide.Top => CssAnchorSide.Left,
        CssAnchorSide.Right => CssAnchorSide.Bottom,
        CssAnchorSide.Bottom => CssAnchorSide.Right,
        _ => side,
    };

    public static Rect ComputePositionAreaCell(CssPositionArea area, Rect anchorRect, Size containerSize)
    {
        var (xStart, xEnd) = ResolveTrackRange(area.Column, anchorRect.Left, anchorRect.Right, containerSize.Width);
        var (yStart, yEnd) = ResolveTrackRange(area.Row, anchorRect.Top, anchorRect.Bottom, containerSize.Height);
        return new Rect(xStart, yStart, Math.Max(0, xEnd - xStart), Math.Max(0, yEnd - yStart));
    }

    private static (double Start, double End) ResolveTrackRange(
        CssPositionAreaTrack track,
        double anchorStart,
        double anchorEnd,
        double containerExtent)
        => track switch
        {
            CssPositionAreaTrack.Start => (0, Math.Max(0, anchorStart)),
            CssPositionAreaTrack.Center => (anchorStart, anchorEnd),
            CssPositionAreaTrack.End => (anchorEnd, Math.Max(anchorEnd, containerExtent)),
            CssPositionAreaTrack.SpanStart => (0, anchorEnd),
            CssPositionAreaTrack.SpanEnd => (anchorStart, Math.Max(anchorStart, containerExtent)),
            _ => (0, containerExtent),
        };

    internal static void Register()
    {
        RegisterAnchorName();
        RegisterAnchorScope();
        RegisterPositionAnchor();
        RegisterPositionArea("position-area");
        RegisterPositionArea("inset-area");
        RegisterPositionTryFallbacks();
        RegisterPositionTryOrder();
        RegisterPositionTryShorthand();
        RegisterPositionVisibility();
    }

    private static void RegisterAnchorName()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "anchor-name",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = AnchorNamesProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var single) && probe.AtEnd &&
                    single.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    reader = probe;
                    return new CssImmediateValue(AnchorNamesProperty, Array.Empty<string>());
                }

                var names = new List<string>();
                while (!reader.AtEnd)
                {
                    if (!reader.TryReadIdent(out var ident))
                    {
                        return null;
                    }

                    var name = ident.ToString();
                    if (!name.StartsWith("--", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    names.Add(name);
                    if (!reader.AtEnd && !reader.TryReadComma())
                    {
                        return null;
                    }
                }

                return names.Count > 0 ? new CssImmediateValue(AnchorNamesProperty, names.ToArray()) : null;
            },
        });

    private static void RegisterAnchorScope()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "anchor-scope",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = AnchorScopeProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var single) && probe.AtEnd)
                {
                    if (single.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        reader = probe;
                        return new CssImmediateValue(AnchorScopeProperty, CssAnchorScope.None);
                    }

                    if (single.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        reader = probe;
                        return new CssImmediateValue(AnchorScopeProperty, CssAnchorScope.All);
                    }
                }

                var names = new List<string>();
                while (!reader.AtEnd)
                {
                    if (!reader.TryReadIdent(out var ident))
                    {
                        return null;
                    }

                    var name = ident.ToString();
                    if (!name.StartsWith("--", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    names.Add(name);
                    if (!reader.AtEnd && !reader.TryReadComma())
                    {
                        return null;
                    }
                }

                return names.Count > 0
                    ? new CssImmediateValue(AnchorScopeProperty, new CssAnchorScope(CssAnchorScopeKind.Named, names.ToArray()))
                    : null;
            },
        });

    private static void RegisterPositionAnchor()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "position-anchor",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PositionAnchorProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                var str = ident.ToString();
                if (str.Equals("auto", StringComparison.OrdinalIgnoreCase) || str.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssImmediateValue(PositionAnchorProperty, null);
                }

                return str.StartsWith("--", StringComparison.Ordinal)
                    ? new CssImmediateValue(PositionAnchorProperty, str)
                    : null;
            },
        });

    private static void RegisterPositionArea(string name)
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = name,
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PositionAreaProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParsePositionArea(ref reader, out var area) && reader.AtEnd
                    ? new CssImmediateValue(PositionAreaProperty, area)
                    : null,
        });

    internal static bool TryParsePositionArea(ref CssTokenReader reader, out CssPositionArea area)
    {
        area = CssPositionArea.None;
        if (!reader.TryReadIdent(out var firstIdent))
        {
            return false;
        }

        var first = firstIdent.ToString().ToLowerInvariant();
        if (first == "none" && reader.AtEnd)
        {
            return true;
        }

        if (!TryClassifyAreaKeyword(first, out var firstAxis, out var firstTrack))
        {
            return false;
        }

        if (reader.AtEnd)
        {
            area = firstAxis switch
            {
                AreaKeywordAxis.Row => new CssPositionArea(firstTrack, CssPositionAreaTrack.SpanAll),
                AreaKeywordAxis.Column => new CssPositionArea(CssPositionAreaTrack.SpanAll, firstTrack),
                _ => new CssPositionArea(firstTrack, firstTrack),
            };
            return true;
        }

        if (!reader.TryReadIdent(out var secondIdent))
        {
            return false;
        }

        var second = secondIdent.ToString().ToLowerInvariant();
        if (!TryClassifyAreaKeyword(second, out var secondAxis, out var secondTrack))
        {
            return false;
        }

        if (firstAxis == AreaKeywordAxis.Column && secondAxis != AreaKeywordAxis.Column)
        {
            area = new CssPositionArea(secondTrack, firstTrack);
            return true;
        }

        area = new CssPositionArea(firstTrack, secondTrack);
        return true;
    }

    private enum AreaKeywordAxis : byte
    {
        Ambiguous = 0,
        Row = 1,
        Column = 2,
    }

    private static bool TryClassifyAreaKeyword(
        string keyword,
        out AreaKeywordAxis axis,
        out CssPositionAreaTrack track)
    {
        switch (keyword)
        {
            case "top" or "block-start" or "y-start" or "self-block-start":
                axis = AreaKeywordAxis.Row;
                track = CssPositionAreaTrack.Start;
                return true;
            case "bottom" or "block-end" or "y-end" or "self-block-end":
                axis = AreaKeywordAxis.Row;
                track = CssPositionAreaTrack.End;
                return true;
            case "span-top" or "span-block-start" or "span-y-start":
                axis = AreaKeywordAxis.Row;
                track = CssPositionAreaTrack.SpanStart;
                return true;
            case "span-bottom" or "span-block-end" or "span-y-end":
                axis = AreaKeywordAxis.Row;
                track = CssPositionAreaTrack.SpanEnd;
                return true;
            case "left" or "inline-start" or "x-start" or "self-inline-start":
                axis = AreaKeywordAxis.Column;
                track = CssPositionAreaTrack.Start;
                return true;
            case "right" or "inline-end" or "x-end" or "self-inline-end":
                axis = AreaKeywordAxis.Column;
                track = CssPositionAreaTrack.End;
                return true;
            case "span-left" or "span-inline-start" or "span-x-start":
                axis = AreaKeywordAxis.Column;
                track = CssPositionAreaTrack.SpanStart;
                return true;
            case "span-right" or "span-inline-end" or "span-x-end":
                axis = AreaKeywordAxis.Column;
                track = CssPositionAreaTrack.SpanEnd;
                return true;
            case "center":
                axis = AreaKeywordAxis.Ambiguous;
                track = CssPositionAreaTrack.Center;
                return true;
            case "span-all":
                axis = AreaKeywordAxis.Ambiguous;
                track = CssPositionAreaTrack.SpanAll;
                return true;
            case "start" or "self-start":
                axis = AreaKeywordAxis.Ambiguous;
                track = CssPositionAreaTrack.Start;
                return true;
            case "end" or "self-end":
                axis = AreaKeywordAxis.Ambiguous;
                track = CssPositionAreaTrack.End;
                return true;
            case "span-start" or "span-self-start":
                axis = AreaKeywordAxis.Ambiguous;
                track = CssPositionAreaTrack.SpanStart;
                return true;
            case "span-end" or "span-self-end":
                axis = AreaKeywordAxis.Ambiguous;
                track = CssPositionAreaTrack.SpanEnd;
                return true;
            default:
                axis = AreaKeywordAxis.Ambiguous;
                track = CssPositionAreaTrack.None;
                return false;
        }
    }

    private static void RegisterPositionTryFallbacks()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "position-try-fallbacks",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PositionTryFallbacksProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
                TryParsePositionTryFallbacks(ref reader, out var fallbacks) && reader.AtEnd
                    ? new CssImmediateValue(PositionTryFallbacksProperty, fallbacks)
                    : null,
        });

    private static bool TryParsePositionTryFallbacks(
        ref CssTokenReader reader,
        out IReadOnlyList<CssPositionTryFallback> fallbacks)
    {
        fallbacks = [];
        var probe = reader;
        if (probe.TryReadIdent(out var single) && probe.AtEnd &&
            single.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            return true;
        }

        var list = new List<CssPositionTryFallback>();
        while (!reader.AtEnd)
        {
            string? ruleName = null;
            var tactic = CssPositionTryTactic.None;
            var area = CssPositionArea.None;

            var itemProbe = reader;
            if (itemProbe.TryReadIdent(out var ident))
            {
                var token = ident.ToString();
                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    ruleName = token;
                    reader = itemProbe;
                    if (!reader.AtEnd && reader.TryReadIdent(out var maybeTactic))
                    {
                        tactic = ParseTactic(maybeTactic.ToString());
                    }
                }
                else if (ParseTactic(token) is var parsedTactic && parsedTactic != CssPositionTryTactic.None)
                {
                    tactic = parsedTactic;
                    reader = itemProbe;
                    if (!reader.AtEnd && reader.TryReadIdent(out var maybeRule) &&
                        maybeRule.ToString().StartsWith("--", StringComparison.Ordinal))
                    {
                        ruleName = maybeRule.ToString();
                    }
                }
                else if (TryParsePositionArea(ref reader, out var parsedArea))
                {
                    area = parsedArea;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            list.Add(new CssPositionTryFallback(ruleName, tactic, area));
            if (!reader.AtEnd && !reader.TryReadComma())
            {
                return false;
            }
        }

        if (list.Count == 0)
        {
            return false;
        }

        fallbacks = list.ToArray();
        return true;
    }

    private static CssPositionTryTactic ParseTactic(string token) => token.ToLowerInvariant() switch
    {
        "flip-block" => CssPositionTryTactic.FlipBlock,
        "flip-inline" => CssPositionTryTactic.FlipInline,
        "flip-start" => CssPositionTryTactic.FlipStart,
        _ => CssPositionTryTactic.None,
    };

    private static void RegisterPositionTryOrder()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "position-try-order",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PositionTryOrderProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd)
                {
                    return null;
                }

                return TryParseOrderKeyword(ident.ToString(), out var order)
                    ? new CssImmediateValue(PositionTryOrderProperty, order)
                    : null;
            },
        });

    private static bool TryParseOrderKeyword(string keyword, out CssPositionTryOrder order)
    {
        switch (keyword.ToLowerInvariant())
        {
            case "normal":
                order = CssPositionTryOrder.Normal;
                return true;
            case "most-width":
                order = CssPositionTryOrder.MostWidth;
                return true;
            case "most-height":
                order = CssPositionTryOrder.MostHeight;
                return true;
            case "most-block-size":
                order = CssPositionTryOrder.MostBlockSize;
                return true;
            case "most-inline-size":
                order = CssPositionTryOrder.MostInlineSize;
                return true;
            default:
                order = CssPositionTryOrder.Normal;
                return false;
        }
    }

    private static void RegisterPositionTryShorthand()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "position-try",
            Kind = CssPropertyKind.Shorthand,
            Expand = static (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                var order = CssPositionTryOrder.Normal;
                var probe = reader;
                if (probe.TryReadIdent(out var maybeOrder) && TryParseOrderKeyword(maybeOrder.ToString(), out var parsedOrder))
                {
                    order = parsedOrder;
                    reader = probe;
                }

                if (!TryParsePositionTryFallbacks(ref reader, out var fallbacks) || !reader.AtEnd)
                {
                    return false;
                }

                output.Add(new CssCompiledDeclaration(
                    "position-try-order",
                    new CssImmediateValue(PositionTryOrderProperty, order),
                    false));
                output.Add(new CssCompiledDeclaration(
                    "position-try-fallbacks",
                    new CssImmediateValue(PositionTryFallbacksProperty, fallbacks),
                    false));
                return true;
            },
        });

    private static void RegisterPositionVisibility()
        => CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "position-visibility",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = PositionVisibilityProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var single) && probe.AtEnd &&
                    single.Equals("always", StringComparison.OrdinalIgnoreCase))
                {
                    reader = probe;
                    return new CssImmediateValue(PositionVisibilityProperty, CssPositionVisibility.Always);
                }

                var flags = CssPositionVisibility.Always;
                while (!reader.AtEnd)
                {
                    if (!reader.TryReadIdent(out var ident))
                    {
                        return null;
                    }

                    var flag = ident.ToString().ToLowerInvariant() switch
                    {
                        "anchors-valid" => CssPositionVisibility.AnchorsValid,
                        "anchors-visible" => CssPositionVisibility.AnchorsVisible,
                        "no-overflow" => CssPositionVisibility.NoOverflow,
                        _ => CssPositionVisibility.Always,
                    };
                    if (flag == CssPositionVisibility.Always || flags.HasFlag(flag))
                    {
                        return null;
                    }

                    flags |= flag;
                }

                return flags != CssPositionVisibility.Always
                    ? new CssImmediateValue(PositionVisibilityProperty, flags)
                    : null;
            },
        });

    /// <summary>Parses <c>anchor(...)</c> or <c>calc(anchor(...) + 8px)</c> for an inset property.</summary>
    internal static bool TryParseAnchorInset(int edge, string rawText, out CssAnchorInsetExpression expression)
    {
        expression = default;
        var text = rawText.Trim();
        var anchorIdx = text.IndexOf("anchor(", StringComparison.OrdinalIgnoreCase);
        if (anchorIdx < 0)
        {
            return false;
        }

        var openParen = anchorIdx + "anchor(".Length - 1;
        var closeParen = FindMatchingCloseParen(text, openParen);
        if (closeParen < 0)
        {
            return false;
        }

        var inner = text[(openParen + 1)..closeParen].Trim();
        var offsetPx = ParseTrailingCalcOffset(text, closeParen + 1);

        string? fallbackPart = null;
        var commaIdx = inner.IndexOf(',');
        if (commaIdx >= 0)
        {
            fallbackPart = inner[(commaIdx + 1)..].Trim();
            inner = inner[..commaIdx].Trim();
        }

        double? fallbackPx = null;
        if (!string.IsNullOrEmpty(fallbackPart))
        {
            var fbReader = new CssTokenReader(fallbackPart);
            if (fbReader.TryReadLength(out var fbLen) && fbLen.IsAbsolute)
            {
                fallbackPx = fbLen.ToPxAbsolute();
            }
        }

        var tokens = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? anchorName = null;
        string? sideToken = null;
        foreach (var tok in tokens)
        {
            if (tok.StartsWith("--", StringComparison.Ordinal))
            {
                anchorName = tok;
            }
            else
            {
                sideToken = tok;
            }
        }

        if (string.IsNullOrEmpty(sideToken))
        {
            return false;
        }

        var pct = 0.0;
        CssAnchorSide side;
        if (sideToken.EndsWith('%') && double.TryParse(sideToken[..^1], out pct))
        {
            side = CssAnchorSide.Percentage;
        }
        else
        {
            side = sideToken.ToLowerInvariant() switch
            {
                "top" => CssAnchorSide.Top,
                "right" => CssAnchorSide.Right,
                "bottom" => CssAnchorSide.Bottom,
                "left" => CssAnchorSide.Left,
                "center" => CssAnchorSide.Center,
                "start" => CssAnchorSide.Start,
                "end" => CssAnchorSide.End,
                "self-start" => CssAnchorSide.SelfStart,
                "self-end" => CssAnchorSide.SelfEnd,
                "inside" => CssAnchorSide.Inside,
                "outside" => CssAnchorSide.Outside,
                _ => (CssAnchorSide)255,
            };
            if ((byte)side == 255)
            {
                return false;
            }
        }

        expression = new CssAnchorInsetExpression(edge, anchorName, side, pct, fallbackPx, offsetPx);
        return true;
    }

    /// <summary>Parses <c>anchor-size(...)</c> or <c>calc(anchor-size(...) * 1.5 + 8px)</c> for sizing properties.</summary>
    internal static bool TryParseAnchorSize(
        CssAnchorSizeDimension defaultDimension,
        string rawText,
        out CssAnchorSizeExpression expression)
    {
        expression = default;
        var text = rawText.Trim();
        var fnIdx = text.IndexOf("anchor-size(", StringComparison.OrdinalIgnoreCase);
        if (fnIdx < 0)
        {
            return false;
        }

        var openParen = fnIdx + "anchor-size(".Length - 1;
        var closeParen = FindMatchingCloseParen(text, openParen);
        if (closeParen < 0)
        {
            return false;
        }

        var inner = text[(openParen + 1)..closeParen].Trim();
        var offsetPx = ParseTrailingCalcOffset(text, closeParen + 1);

        string? fallbackPart = null;
        var commaIdx = inner.IndexOf(',');
        if (commaIdx >= 0)
        {
            fallbackPart = inner[(commaIdx + 1)..].Trim();
            inner = inner[..commaIdx].Trim();
        }

        double? fallbackPx = null;
        if (!string.IsNullOrEmpty(fallbackPart))
        {
            var fbReader = new CssTokenReader(fallbackPart);
            if (fbReader.TryReadLength(out var fbLen) && fbLen.IsAbsolute)
            {
                fallbackPx = fbLen.ToPxAbsolute();
            }
        }

        var tokens = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? anchorName = null;
        var dimension = defaultDimension;
        foreach (var tok in tokens)
        {
            if (tok.StartsWith("--", StringComparison.Ordinal))
            {
                anchorName = tok;
            }
            else
            {
                dimension = tok.ToLowerInvariant() switch
                {
                    "width" => CssAnchorSizeDimension.Width,
                    "height" => CssAnchorSizeDimension.Height,
                    "block" => CssAnchorSizeDimension.Block,
                    "inline" => CssAnchorSizeDimension.Inline,
                    "self-block" => CssAnchorSizeDimension.SelfBlock,
                    "self-inline" => CssAnchorSizeDimension.SelfInline,
                    _ => defaultDimension,
                };
            }
        }

        expression = new CssAnchorSizeExpression(anchorName, dimension, fallbackPx, 1.0, offsetPx);
        return true;
    }

    private static int FindMatchingCloseParen(string text, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static double ParseTrailingCalcOffset(string text, int afterCloseParenIndex)
    {
        if (afterCloseParenIndex >= text.Length)
        {
            return 0.0;
        }

        var tail = text[afterCloseParenIndex..].Trim().TrimEnd(')').Trim();
        if (tail.Length == 0)
        {
            return 0.0;
        }

        var sign = 1.0;
        if (tail[0] == '+')
        {
            tail = tail[1..].Trim();
        }
        else if (tail[0] == '-')
        {
            sign = -1.0;
            tail = tail[1..].Trim();
        }
        else
        {
            return 0.0;
        }

        var reader = new CssTokenReader(tail);
        return reader.TryReadLength(out var len) && len.IsAbsolute
            ? sign * len.ToPxAbsolute()
            : 0.0;
    }
}
