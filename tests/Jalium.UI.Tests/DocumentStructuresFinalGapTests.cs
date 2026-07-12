using System.Reflection;
using Jalium.UI.Documents.DocumentStructures;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class DocumentStructuresFinalGapTests
{
    [Fact]
    public void StructureTypesExposeTheConcreteExtensibleWpfShape()
    {
        Assert.False(typeof(BlockElement).IsAbstract);
        Assert.False(typeof(SemanticBasicElement).IsAbstract);
        Assert.NotNull(typeof(SemanticBasicElement).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null));

        Type[] containers =
        [
            typeof(SectionStructure), typeof(ParagraphStructure), typeof(FigureStructure),
            typeof(ListStructure), typeof(ListItemStructure), typeof(TableStructure),
            typeof(TableRowGroupStructure), typeof(TableRowStructure), typeof(TableCellStructure),
            typeof(StoryFragment), typeof(StoryFragments),
        ];

        foreach (Type type in containers)
        {
            Assert.False(type.IsSealed);
            Assert.True(typeof(IAddChild).IsAssignableFrom(type));
        }
    }

    [Fact]
    public void MarkupChildContractRoutesThroughTypedCollections()
    {
        var paragraph = new ParagraphStructure();
        var named = new NamedElement { NameReference = "caption" };
        ((IAddChild)paragraph).AddChild(named);
        ((IAddChild)paragraph).AddText("  ");
        Assert.Same(named, Assert.Single(paragraph));
        Assert.Throws<ArgumentException>(() => ((IAddChild)paragraph).AddChild(new StoryBreak()));
        Assert.Throws<ArgumentException>(() => ((IAddChild)paragraph).AddText("not allowed"));

        var fragment = new StoryFragment();
        ((IAddChild)fragment).AddChild(paragraph);
        var fragments = new StoryFragments();
        ((IAddChild)fragments).AddChild(fragment);
        Assert.Same(fragment, Assert.Single(fragments));
    }
}
