using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed record TableGroups(TableRowGroup[] Ordered, TableRowGroup? Header, TableRowGroup? Footer)
    {
        internal bool IsBody(TableRowGroup group) => !ReferenceEquals(group, Header) && !ReferenceEquals(group, Footer);
    }

    private TableGroups DocumentTableGroups(Table table)
    {
        var source = SourceTableGroups(table);
        var header = source.FirstOrDefault(group => CssDisplayProperties.Computed(CssNode.Get(group)).Mode == CssDisplayMode.TableHeaderGroup);
        var footer = source.FirstOrDefault(group => CssDisplayProperties.Computed(CssNode.Get(group)).Mode == CssDisplayMode.TableFooterGroup);
        // Promotion is determined in source order, including empty groups.
        // Duplicates remain body groups even after the first footer moves past
        // them. Original collections and paragraph offsets never change.
        var ordered = source.Where(group => !ReferenceEquals(group, header) && !ReferenceEquals(group, footer)).ToList();
        if (header is not null) ordered.Insert(0, header);
        if (footer is not null) ordered.Add(footer);
        return new(ordered.ToArray(), header, footer);
    }
}
