using Jalium.UI.Documents;

namespace Jalium.UI.Styling;

internal enum CssFragmentBreak : byte
{
    Auto, Avoid, AvoidPage, Page, Left, Right, Recto, Verso,
    AvoidColumn, Column, AvoidRegion, Region,
}

internal static class CssFragmentationProperties
{
    internal static readonly DependencyProperty BeforeProperty = BreakProperty("CssBreakBefore");
    internal static readonly DependencyProperty AfterProperty = BreakProperty("CssBreakAfter");
    internal static readonly DependencyProperty InsideProperty = BreakProperty("CssBreakInside");
    internal static readonly DependencyProperty OrphansProperty = LineProperty("CssOrphans");
    internal static readonly DependencyProperty WidowsProperty = LineProperty("CssWidows");

    private static DependencyProperty BreakProperty(string name) => DependencyProperty.RegisterAttached(
        name, typeof(CssFragmentBreak), typeof(CssFragmentationProperties), new PropertyMetadata(CssFragmentBreak.Auto, Changed));
    private static DependencyProperty LineProperty(string name) => DependencyProperty.RegisterAttached(
        name, typeof(int), typeof(CssFragmentationProperties), new PropertyMetadata(2, Changed, null, inherits: true),
        static value => value is int count && count >= 1);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is FlowDocument document) document.NotifyPaginationStyleChanged();
        else if (target is TextElement text) text.NotifyTextContentChanged();
        else if (target is UIElement visual) { visual.InvalidateMeasure(); visual.InvalidateVisual(); }
    }

    internal static bool AvoidsPage(CssFragmentBreak value) => value is CssFragmentBreak.Avoid or CssFragmentBreak.AvoidPage;
    internal static bool ForcesPage(CssFragmentBreak value) => value is CssFragmentBreak.Page or CssFragmentBreak.Left or
        CssFragmentBreak.Right or CssFragmentBreak.Recto or CssFragmentBreak.Verso;
    internal static bool HasValue(DependencyObject source, DependencyProperty property)
        => source.GetValueSourceInternal(property).BaseValueSource != BaseValueSource.Default;
    internal static CssFragmentBreak Before(DependencyObject source)
        => !HasValue(source, BeforeProperty) && source is Block { BreakPageBefore: true }
            ? CssFragmentBreak.Page : (CssFragmentBreak)source.GetValue(BeforeProperty)!;
    internal static CssFragmentBreak After(DependencyObject source)
        => !HasValue(source, AfterProperty) && source is Paragraph { KeepWithNext: true }
            ? CssFragmentBreak.Avoid : (CssFragmentBreak)source.GetValue(AfterProperty)!;
    internal static CssFragmentBreak Inside(DependencyObject source)
        => !HasValue(source, InsideProperty) && source is Paragraph { KeepTogether: true }
            ? CssFragmentBreak.Avoid : (CssFragmentBreak)source.GetValue(InsideProperty)!;
    internal static int Orphans(Paragraph source)
        => Lines(source, OrphansProperty, source.MinOrphanLines);
    internal static int Widows(Paragraph source)
        => Lines(source, WidowsProperty, source.MinWidowLines);
    private static int Lines(Paragraph source, DependencyProperty property, int native)
        => native > 0 && source.GetValueSourceInternal(property).BaseValueSource is BaseValueSource.Default or BaseValueSource.Inherited
            ? native : (int)source.GetValue(property)!;

    internal static void Register()
    {
        RegisterBreak("break-before", BeforeProperty, false);
        RegisterBreak("break-after", AfterProperty, false);
        RegisterBreak("break-inside", InsideProperty, true);
        RegisterLines("orphans", OrphansProperty);
        RegisterLines("widows", WidowsProperty);
        Legacy("page-break-before", "break-before", false);
        Legacy("page-break-after", "break-after", false);
        Legacy("page-break-inside", "break-inside", true);
        CssPageProperties.Register();
    }

    private static void RegisterBreak(string name, DependencyProperty property, bool inside)
        => CssPropertyRegistry.Register(new()
        {
            Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = property,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                CssFragmentBreak? value = ident.ToString().ToLowerInvariant() switch
                {
                    "auto" => CssFragmentBreak.Auto, "avoid" => CssFragmentBreak.Avoid,
                    "avoid-page" => CssFragmentBreak.AvoidPage, "avoid-column" => CssFragmentBreak.AvoidColumn,
                    "avoid-region" => CssFragmentBreak.AvoidRegion,
                    "page" when !inside => CssFragmentBreak.Page, "left" when !inside => CssFragmentBreak.Left,
                    "right" when !inside => CssFragmentBreak.Right, "recto" when !inside => CssFragmentBreak.Recto,
                    "verso" when !inside => CssFragmentBreak.Verso, "column" when !inside => CssFragmentBreak.Column,
                    "region" when !inside => CssFragmentBreak.Region, _ => null,
                };
                return value is { } result ? new CssImmediateValue(property, result) : null;
            },
        });

    private static void RegisterLines(string name, DependencyProperty property)
        => CssPropertyRegistry.Register(new()
        {
            Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = property,
            Parse = (ref CssTokenReader reader, CssCompileContext _) => reader.TryReadInteger(out var count, minimum: 1) && reader.AtEnd
                ? new CssImmediateValue(property, count) : null,
        });

    private static void Legacy(string name, string modern, bool inside)
        => CssPropertyRegistry.Register(new()
        {
            Name = name, Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return false;
                var value = ident.ToString().ToLowerInvariant();
                if (value is not ("auto" or "avoid") && (inside || value is not ("always" or "left" or "right"))) return false;
                var mapped = new CssTokenReader(value == "always" ? "page" : value);
                var parsed = CssPropertyRegistry.Lookup(modern)!.Parse!(ref mapped, context);
                if (parsed is null) return false;
                output.Add(new(modern, parsed, false));
                return true;
            },
        });
}
