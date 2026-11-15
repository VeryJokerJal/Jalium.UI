using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal sealed partial class CssFlowLayout
{
    internal IEnumerable<CssClampContent> ClampContents
    {
        get
        {
            if (_snapshot is not { } snapshot) yield break;
            var paragraphs = new HashSet<TextBlock.CssTextFlowLayout>();
            var lines = new HashSet<InlineLine>();
            var shared = snapshot.Items.Where(static item => item.InlineParagraph is not null)
                .Select(static item => item.InlineParagraph!.Layout).Distinct().ToArray();
            foreach (var item in snapshot.Items)
            {
                if (item.InlineParagraph is { } paragraph)
                {
                    if (paragraphs.Add(paragraph.Layout)) yield return new(Inline: paragraph.Layout);
                    continue;
                }
                if (item.Float != CssFloatSide.None)
                {
                    if (!shared.Any(layout => layout.ContainsClampFloat(item.Box.Element))) yield return new(Box: item.Box.Element, Floating: true);
                    continue;
                }
                if (item.Inline)
                {
                    var line = snapshot.InlineLines.FirstOrDefault(line => line.Items.Contains(item));
                    if (line is not null && lines.Add(line)) yield return new(Line: new(new(line.Items[0].Box.Element, CssClampParagraphKind.Atomic), owner, 0,
                        line.Top + snapshot.Insets.Top, line.Height, 0));
                    continue;
                }
                if (item.Box.Element is ICssAnonymousTextBox { Owner: TextBlock text }) yield return new(Text: text);
                else yield return new(Box: item.Box.Element);
            }
        }
    }
}
