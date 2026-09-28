namespace Jalium.UI.Styling;

/// <summary>Horizontal writing-mode mapping shared by box edges and corners.</summary>
internal static class CssLogicalBoxEdges
{
    internal static bool IsMappedProperty(string name) => PhysicalName(name, rightToLeft: false) is not null;

    internal static bool IsCorner(string name) => PhysicalName(name, rightToLeft: false) is
        "border-top-left-radius" or "border-top-right-radius" or
        "border-bottom-right-radius" or "border-bottom-left-radius";

    internal static bool IsBorderColor(string name) => PhysicalName(name, rightToLeft: false) is
        "border-left-color" or "border-top-color" or "border-right-color" or "border-bottom-color";

    internal static bool IsBorderStyle(string name) => PhysicalName(name, rightToLeft: false) is
        "border-left-style" or "border-top-style" or "border-right-style" or "border-bottom-style";

    internal static string? PhysicalName(string name, bool rightToLeft)
    {
        if (name is "left" or "top" or "right" or "bottom") return name;
        if (name.StartsWith("border-", StringComparison.Ordinal))
        {
            var borderSide = name[7..];
            if (borderSide.EndsWith("-width", StringComparison.Ordinal))
            {
                var side = PhysicalSide(borderSide[..^6], rightToLeft);
                return side is null ? null : $"border-{side}-width";
            }
            if (borderSide.EndsWith("-color", StringComparison.Ordinal))
            {
                var side = PhysicalSide(borderSide[..^6], rightToLeft);
                return side is null ? null : $"border-{side}-color";
            }
            if (borderSide.EndsWith("-style", StringComparison.Ordinal))
            {
                var side = PhysicalSide(borderSide[..^6], rightToLeft);
                return side is null ? null : $"border-{side}-style";
            }
            if (borderSide.EndsWith("-radius", StringComparison.Ordinal))
            {
                var corner = borderSide[..^7] switch
                {
                    "top-left" => "top-left",
                    "top-right" => "top-right",
                    "bottom-right" => "bottom-right",
                    "bottom-left" => "bottom-left",
                    "start-start" => rightToLeft ? "top-right" : "top-left",
                    "start-end" => rightToLeft ? "top-left" : "top-right",
                    "end-start" => rightToLeft ? "bottom-right" : "bottom-left",
                    "end-end" => rightToLeft ? "bottom-left" : "bottom-right",
                    _ => null,
                };
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
        var suffix = PhysicalSide(source, rightToLeft);
        return suffix is null ? null : prefix == "inset" ? suffix : prefix + "-" + suffix;
    }

    private static string? PhysicalSide(string side, bool rightToLeft) => side switch
    {
        "inline-start" => rightToLeft ? "right" : "left",
        "inline-end" => rightToLeft ? "left" : "right",
        "block-start" => "top",
        "block-end" => "bottom",
        "left" => "left",
        "top" => "top",
        "right" => "right",
        "bottom" => "bottom",
        _ => null,
    };

    internal static int Edge(string name, bool rightToLeft)
        => PhysicalName(name, rightToLeft) switch
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

    internal static int Corner(string name, bool rightToLeft)
        => PhysicalName(name, rightToLeft) switch
        {
            "border-top-left-radius" => 0,
            "border-top-right-radius" => 1,
            "border-bottom-right-radius" => 2,
            "border-bottom-left-radius" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
}
