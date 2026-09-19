namespace Jalium.UI.Styling;

internal enum CssContainerType { Normal, InlineSize, Size }

/// <summary>Container declarations use the existing CSS layers, without replacing native layout or local values.</summary>
internal static class CssContainerProperties
{
    internal static readonly DependencyProperty TypeProperty = DependencyProperty.RegisterAttached(
        "CssContainerType", typeof(CssContainerType), typeof(CssContainerProperties), new PropertyMetadata(CssContainerType.Normal, Changed));
    internal static readonly DependencyProperty NameProperty = DependencyProperty.RegisterAttached(
        "CssContainerName", typeof(string), typeof(CssContainerProperties), new PropertyMetadata(string.Empty, Changed));

    internal static void Register()
    {
        CssPropertyRegistry.Register(new()
        {
            Name = "container-type", Kind = CssPropertyKind.Longhand, StorageProperty = TypeProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) => ReadType(ref reader, out var type) && reader.AtEnd
                ? new CssImmediateValue(TypeProperty, type) : null,
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "container-name", Kind = CssPropertyKind.Longhand, StorageProperty = NameProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) => ReadNames(ref reader, out var names) && reader.AtEnd
                ? new CssImmediateValue(NameProperty, names) : null,
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "container", Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                if (!ReadNames(ref reader, out var names)) return false;
                var type = CssContainerType.Normal;
                if (!reader.AtEnd && (!reader.TryReadSlash() || !ReadType(ref reader, out type)) || !reader.AtEnd) return false;
                output.Add(new("container-name", new CssImmediateValue(NameProperty, names), false));
                output.Add(new("container-type", new CssImmediateValue(TypeProperty, type), false));
                return true;
            },
        });
    }

    private static bool ReadType(ref CssTokenReader reader, out CssContainerType type)
    {
        type = CssContainerType.Normal;
        if (!reader.TryReadIdent(out var ident)) return false;
        switch (ident.ToString().ToLowerInvariant())
        {
            case "normal": return true;
            case "inline-size": type = CssContainerType.InlineSize; return true;
            case "size": type = CssContainerType.Size; return true;
            default: return false;
        }
    }

    private static bool ReadNames(ref CssTokenReader reader, out string names)
    {
        names = string.Empty;
        var values = new List<string>();
        while (reader.TryReadIdent(out var ident))
        {
            var name = ident.ToString();
            if (name.Equals("none", StringComparison.OrdinalIgnoreCase))
                return values.Count == 0 && (reader.AtEnd || reader.TryPeekChar(out var next) && next == '/');
            if (!IsName(name)) return false;
            values.Add(name);
        }
        // A separator that cannot occur unescaped in the decoded identifier avoids losing
        // escaped spaces. NUL itself is replaced with U+FFFD by CSS input preprocessing.
        names = string.Join('\0', values);
        return values.Count > 0;
    }

    internal static bool IsName(string name) => name.Length > 0 && name.ToLowerInvariant() is not
        ("none" or "and" or "or" or "not" or "default" or "initial" or "inherit" or "unset" or "revert" or "revert-layer");

    internal static bool HasName(CssNode node, string name)
        => ((string)node.GetValue(NameProperty)!).Split('\0').Contains(name, StringComparer.Ordinal);

    internal static CssContainerType Type(DependencyObject node) => (CssContainerType)node.GetValue(TypeProperty)!;
    internal static bool HasSizeContainment(FrameworkElement element) => Type(element) != CssContainerType.Normal;

    internal static Thickness Insets(FrameworkElement element, double containingWidth)
    {
        if (CssDisplayLayout.HasBoxFormatter(element) || CssDependencyPropertyLookup.Find(element.GetType(), "Padding") is not null)
            return CssBoxMetrics.ContentInsets(element, containingWidth);
        return CssDependencyPropertyLookup.Find(element.GetType(), "BorderThickness") is { } border &&
            element.GetValue(border) is Thickness thickness ? thickness : default;
    }

    internal static Size ContainedDesiredSize(FrameworkElement element, Size content, double containingWidth)
    {
        var type = Type(element);
        if (type == CssContainerType.Normal) return content;
        var insets = Insets(element, containingWidth);
        return new(insets.Left + insets.Right, type == CssContainerType.Size ? insets.Top + insets.Bottom : content.Height);
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is not (FrameworkElement or FrameworkContentElement)) return;
        var node = CssNode.Get(target);
        node.InvalidateMeasure();
        // Eligibility changes also affect descendants that previously had no container.
        CssEvaluationScheduler.InvalidateSubtree(node);
    }
}
