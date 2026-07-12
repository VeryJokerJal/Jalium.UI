using System.ComponentModel;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class WpfParityCoreConstructorsTests
{
    [Fact]
    public void Rect_TwoPointConstructor_BoundsBothPoints()
    {
        var rect = new Rect(new Point(12, -4), new Point(2, 6));

        Assert.Equal(2, rect.X);
        Assert.Equal(-4, rect.Y);
        Assert.Equal(10, rect.Width);
        Assert.Equal(10, rect.Height);
    }

    [Fact]
    public void Rect_PointVectorConstructor_BoundsNegativeVector()
    {
        var rect = new Rect(new Point(10, 20), new Vector(-3, -5));

        Assert.Equal(new Rect(7, 15, 3, 5), rect);
    }

    [Fact]
    public void PropertyMetadata_CallbackOnlyConstructor_PreservesCallback()
    {
        PropertyChangedCallback callback = static (_, _) => { };

        var metadata = new PropertyMetadata(callback);

        Assert.Null(metadata.DefaultValue);
        Assert.Same(callback, metadata.PropertyChangedCallback);
        Assert.Null(metadata.CoerceValueCallback);
    }

    [Fact]
    public void UIPropertyMetadata_CallbackOnlyConstructor_PreservesCallback()
    {
        PropertyChangedCallback callback = static (_, _) => { };

        var metadata = new UIPropertyMetadata(callback);

        Assert.Same(callback, metadata.PropertyChangedCallback);
        Assert.False(metadata.IsAnimationProhibited);
    }

    [Fact]
    public void FrameworkPropertyMetadata_CallbackAndCoercionConstructor_PreservesCallbacks()
    {
        PropertyChangedCallback changed = static (_, _) => { };
        CoerceValueCallback coerce = static (_, value) => value;

        var metadata = new FrameworkPropertyMetadata(changed, coerce);

        Assert.Same(changed, metadata.PropertyChangedCallback);
        Assert.Same(coerce, metadata.CoerceValueCallback);
        Assert.Equal(UpdateSourceTrigger.PropertyChanged, metadata.DefaultUpdateSourceTrigger);
    }

    [Fact]
    public void FrameworkPropertyMetadata_FullConstructor_AppliesAllOptions()
    {
        PropertyChangedCallback changed = static (_, _) => { };
        CoerceValueCallback coerce = static (_, value) => value;

        var metadata = new FrameworkPropertyMetadata(
            "default",
            FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            changed,
            coerce,
            isAnimationProhibited: true,
            UpdateSourceTrigger.LostFocus);

        Assert.Equal("default", metadata.DefaultValue);
        Assert.True(metadata.AffectsMeasure);
        Assert.True(metadata.Inherits);
        Assert.True(metadata.BindsTwoWayByDefault);
        Assert.True(metadata.IsAnimationProhibited);
        Assert.Equal(UpdateSourceTrigger.LostFocus, metadata.DefaultUpdateSourceTrigger);
    }

    [Fact]
    public void FrameworkPropertyMetadata_FullConstructor_RejectsInvalidTrigger()
    {
        Assert.Throws<ArgumentException>(() => new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.None,
            null,
            null,
            false,
            UpdateSourceTrigger.Default));

        Assert.Throws<InvalidEnumArgumentException>(() => new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.None,
            null,
            null,
            false,
            (UpdateSourceTrigger)99));
    }

    [Fact]
    public void FreezableCollection_CapacityAndEnumerableConstructors_CreateUsableCollections()
    {
        var first = new DependencyObject();
        var second = new DependencyObject();

        var capacityCollection = new FreezableCollection<DependencyObject>(8);
        capacityCollection.Add(first);

        var copiedCollection = new FreezableCollection<DependencyObject>(new[] { first, second });

        Assert.Single(capacityCollection);
        Assert.Equal(new[] { first, second }, copiedCollection);
    }

    [Fact]
    public void FreezableCollection_EnumerableConstructor_RejectsNullSource()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FreezableCollection<DependencyObject>((IEnumerable<DependencyObject>)null!));
    }
}
