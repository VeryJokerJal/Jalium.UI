using Jalium.UI.Documents.DocumentStructures;

namespace Jalium.UI.Tests;

public class DocumentStructuresTests
{
    [Fact]
    public void SectionStructure_ShouldEnumerateChildrenInInsertionOrder()
    {
        var paragraph = new ParagraphStructure();
        var figure = new FigureStructure();
        var list = new ListStructure();
        var table = new TableStructure();

        var section = new SectionStructure();
        section.Add(paragraph);
        section.Add(figure);
        section.Add(list);
        section.Add(table);

        var children = ((IEnumerable<BlockElement>)section).ToArray();

        Assert.Equal(new BlockElement[] { paragraph, figure, list, table }, children);
    }

    [Fact]
    public void ParagraphStructure_ShouldEnumerateNamedElements()
    {
        var first = new NamedElement { NameReference = "First" };
        var second = new NamedElement { NameReference = "Second" };

        var paragraph = new ParagraphStructure();
        paragraph.Add(first);
        paragraph.Add(second);

        var children = ((IEnumerable<NamedElement>)paragraph).ToArray();

        Assert.Equal(new[] { first, second }, children);
    }

    [Fact]
    public void StoryFragments_ShouldEnumerateNestedStoryFragments()
    {
        var fragment = new StoryFragment();
        fragment.Add(new StoryBreak());

        var fragments = new StoryFragments();
        fragments.Add(fragment);

        Assert.Same(fragment, Assert.Single(fragments));
        Assert.Single(((IEnumerable<BlockElement>)fragment));
    }
}
