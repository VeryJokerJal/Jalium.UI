namespace Jalium.UI.Styling;

/// <summary>Horizontal and vertical writing-mode mapping shared by box edges, corners, and logical axes.</summary>
public static class CssLogicalBoxEdges
{
    internal static bool IsMappedProperty(string name) => PhysicalName(name, rightToLeft: false) is not null;

    internal static bool IsCorner(string name) => PhysicalName(name, rightToLeft: false) is
        "border-top-left-radius" or "border-top-right-radius" or
        "border-bottom-right-radius" or "border-bottom-left-radius";

    internal static bool IsBorderColor(string name) => PhysicalName(name, rightToLeft: false) is
        "border-left-color" or "border-top-color" or "border-right-color" or "border-bottom-color";

    internal static bool IsBorderStyle(string name) => PhysicalName(name, rightToLeft: false) is
        "border-left-style" or "border-top-style" or "border-right-style" or "border-bottom-style";

    public static string? PhysicalName(string name, bool rightToLeft)
        => PhysicalName(name, rightToLeft, CssWritingMode.HorizontalTb);

    public static string? PhysicalName(string name, bool rightToLeft, CssWritingMode writingMode)
    {
        if (name is "left" or "top" or "right" or "bottom") return name;
        var vertical = CssWritingModeProperties.IsVertical(writingMode);
        if (name is "inline-size" or "min-inline-size" or "max-inline-size" or "overflow-inline")
        {
            return name switch
            {
                "inline-size" => vertical ? "height" : "width",
                "min-inline-size" => vertical ? "min-height" : "min-width",
                "max-inline-size" => vertical ? "max-height" : "max-width",
                "overflow-inline" => vertical ? "overflow-y" : "overflow-x",
                _ => null,
            };
        }

        if (name is "block-size" or "min-block-size" or "max-block-size" or "overflow-block")
        {
            return name switch
            {
                "block-size" => vertical ? "width" : "height",
                "min-block-size" => vertical ? "min-width" : "min-height",
                "max-block-size" => vertical ? "max-width" : "max-height",
                "overflow-block" => vertical ? "overflow-x" : "overflow-y",
                _ => null,
            };
        }

        if (name.StartsWith("border-", StringComparison.Ordinal))
        {
            var borderSide = name[7..];
            if (borderSide.EndsWith("-width", StringComparison.Ordinal))
            {
                var side = PhysicalSide(borderSide[..^6], rightToLeft, writingMode);
                return side is null ? null : $"border-{side}-width";
            }
            if (borderSide.EndsWith("-color", StringComparison.Ordinal))
            {
                var side = PhysicalSide(borderSide[..^6], rightToLeft, writingMode);
                return side is null ? null : $"border-{side}-color";
            }
            if (borderSide.EndsWith("-style", StringComparison.Ordinal))
            {
                var side = PhysicalSide(borderSide[..^6], rightToLeft, writingMode);
                return side is null ? null : $"border-{side}-style";
            }
            if (borderSide.EndsWith("-radius", StringComparison.Ordinal))
            {
                var corner = PhysicalCorner(borderSide[..^7], rightToLeft, writingMode);
                return corner is null ? null : $"border-{corner}-radius";
            }
            return null;
        }
        var prefix = name.StartsWith("margin-", StringComparison.Ordinal) ? "margin" :
            name.StartsWith("padding-", StringComparison.Ordinal) ? "padding" :
            name.StartsWith("inset-", StringComparison.Ordinal) ? "inset" : null;
        if (prefix is null) return null;
        var source = name[(prefix.Length + 1)..];
        if (prefix == "inset" && source is not ("inline-start" or "inline-end" or "block-start" or "block-end"))
            return null;
        var suffix = PhysicalSide(source, rightToLeft, writingMode);
        return suffix is null ? null : prefix == "inset" ? suffix : prefix + "-" + suffix;
    }

    public static string? PhysicalSide(string side, bool rightToLeft, CssWritingMode writingMode = CssWritingMode.HorizontalTb)
    {
        if (side is "left" or "top" or "right" or "bottom")
        {
            return side;
        }

        if (!CssWritingModeProperties.IsVertical(writingMode))
        {
            return side switch
            {
                "inline-start" => rightToLeft ? "right" : "left",
                "inline-end" => rightToLeft ? "left" : "right",
                "block-start" => "top",
                "block-end" => "bottom",
                _ => null,
            };
        }

        var blockRtl = CssWritingModeProperties.IsBlockRightToLeft(writingMode);
        var inlineBottomToTop = CssWritingModeProperties.IsInlineBottomToTop(writingMode, rightToLeft);
        return side switch
        {
            "inline-start" => inlineBottomToTop ? "bottom" : "top",
            "inline-end" => inlineBottomToTop ? "top" : "bottom",
            "block-start" => blockRtl ? "right" : "left",
            "block-end" => blockRtl ? "left" : "right",
            _ => null,
        };
    }

    public static string? PhysicalCorner(string corner, bool rightToLeft, CssWritingMode writingMode = CssWritingMode.HorizontalTb)
    {
        if (corner is "top-left" or "top-right" or "bottom-right" or "bottom-left")
        {
            return corner;
        }

        // CSS Logical Properties 1: border-<block>-<inline>-radius (start-start, start-end, end-start, end-end)
        var (blockSide, inlineSide) = corner switch
        {
            "start-start" => ("block-start", "inline-start"),
            "start-end" => ("block-start", "inline-end"),
            "end-start" => ("block-end", "inline-start"),
            "end-end" => ("block-end", "inline-end"),
            _ => ((string?)null, (string?)null),
        };
        if (blockSide is null || inlineSide is null)
        {
            return null;
        }

        var physBlock = PhysicalSide(blockSide, rightToLeft, writingMode);
        var physInline = PhysicalSide(inlineSide, rightToLeft, writingMode);
        var verticalEdge = physBlock is "top" or "bottom" ? physBlock : physInline;
        var horizontalEdge = physBlock is "left" or "right" ? physBlock : physInline;
        return verticalEdge is not null && horizontalEdge is not null
            ? $"{verticalEdge}-{horizontalEdge}"
            : null;
    }

    public static int Edge(string name, bool rightToLeft)
        => Edge(name, rightToLeft, CssWritingMode.HorizontalTb);

    public static int Edge(string name, bool rightToLeft, CssWritingMode writingMode)
        => PhysicalName(name, rightToLeft, writingMode) switch
        {
            "margin-left" or "padding-left" or "border-left-width" or "border-left-color" or "border-left-style" => 0,
            "margin-top" or "padding-top" or "border-top-width" or "border-top-color" or "border-top-style" => 1,
            "margin-right" or "padding-right" or "border-right-width" or "border-right-color" or "border-right-style" => 2,
            "margin-bottom" or "padding-bottom" or "border-bottom-width" or "border-bottom-color" or "border-bottom-style" => 3,
            "left" => 0,
            "top" => 1,
            "right" => 2,
            "bottom" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

    public static int Corner(string name, bool rightToLeft)
        => Corner(name, rightToLeft, CssWritingMode.HorizontalTb);

    public static int Corner(string name, bool rightToLeft, CssWritingMode writingMode)
        => PhysicalName(name, rightToLeft, writingMode) switch
        {
            "border-top-left-radius" => 0,
            "border-top-right-radius" => 1,
            "border-bottom-right-radius" => 2,
            "border-bottom-left-radius" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
}
