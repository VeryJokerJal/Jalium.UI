using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class TextDecorationParityTests
{
    [Fact]
    public void TypesAndSurface_UseTheWpfRootNamespaceAndFreezableContracts()
    {
        Assert.Equal("Jalium.UI", typeof(TextDecoration).Namespace);
        Assert.Equal("Jalium.UI", typeof(TextDecorationCollection).Namespace);
        Assert.Equal(typeof(Animatable), typeof(TextDecoration).BaseType);
        Assert.Equal(typeof(Animatable), typeof(TextDecorationCollection).BaseType);

        foreach (var name in new[]
                 {
                     nameof(TextDecoration.LocationProperty),
                     nameof(TextDecoration.PenProperty),
                     nameof(TextDecoration.PenOffsetProperty),
                     nameof(TextDecoration.PenOffsetUnitProperty),
                     nameof(TextDecoration.PenThicknessUnitProperty),
                 })
        {
            var field = typeof(TextDecoration).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.NotNull(field);
            Assert.True(field!.IsInitOnly);
            Assert.Equal(typeof(DependencyProperty), field.FieldType);
        }

        Assert.NotNull(typeof(TextDecoration).GetConstructor(
            [typeof(TextDecorationLocation), typeof(Pen), typeof(double), typeof(TextDecorationUnit), typeof(TextDecorationUnit)]));
        Assert.Equal(typeof(TextDecorationCollection.Enumerator),
            typeof(TextDecorationCollection).GetMethod(nameof(TextDecorationCollection.GetEnumerator), Type.EmptyTypes)!.ReturnType);
    }

    [Fact]
    public void CloneFreezeAndTryRemove_PreserveIndependentValueSemantics()
    {
        var underline = new TextDecoration(
            TextDecorationLocation.Underline,
            new Pen(Brushes.Red, 2.0),
            1.5,
            TextDecorationUnit.Pixel,
            TextDecorationUnit.FontRenderingEmSize);
        var strike = new TextDecoration { Location = TextDecorationLocation.Strikethrough };
        var collection = new TextDecorationCollection(2) { underline, strike };

        var clone = collection.Clone();
        Assert.NotSame(collection, clone);
        Assert.NotSame(collection[0], clone[0]);
        Assert.NotSame(collection[0].Pen, clone[0].Pen);
        Assert.Equal(2.0, clone[0].Pen!.Thickness);

        Assert.True(collection.TryRemove([underline], out var withoutUnderline));
        Assert.Single(withoutUnderline);
        Assert.Equal(TextDecorationLocation.Strikethrough, withoutUnderline[0].Location);
        Assert.Equal(2, collection.Count);

        collection.Freeze();
        Assert.True(collection.IsFrozen);
        Assert.True(collection[0].IsFrozen);
        Assert.Throws<InvalidOperationException>(() => collection.Add(new TextDecoration()));
        Assert.Throws<InvalidOperationException>(() => collection[0].PenOffset = 4.0);
    }

    [Fact]
    public async Task PredefinedCollections_AreFrozenStableAndFreeThreaded()
    {
        TextDecorationCollection[] expected =
        [
            TextDecorations.Underline,
            TextDecorations.Strikethrough,
            TextDecorations.OverLine,
            TextDecorations.Baseline,
        ];

        Assert.All(expected, static collection =>
        {
            Assert.True(collection.IsFrozen);
            Assert.Single(collection);
        });

        TextDecorationCollection[] actual = await Task.Run(static () =>
            new TextDecorationCollection[]
        {
            TextDecorations.Underline,
            TextDecorations.Strikethrough,
            TextDecorations.OverLine,
            TextDecorations.Baseline,
        });

        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index], actual[index]);
            Assert.Equal(expected[index][0].Location, actual[index][0].Location);
        }
    }
}
