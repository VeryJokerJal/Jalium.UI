namespace Jalium.UI.Tests;

public sealed class ParityCoreSurfaceTests
{
    private static readonly DependencyProperty CurrentValueProperty = DependencyProperty.Register(
        "WpfParityCurrentValue",
        typeof(int),
        typeof(ParityCoreSurfaceTests),
        new PropertyMetadata(1));

    [Fact]
    public void VectorExposesLengthSquared()
    {
        Assert.Equal(25d, new Vector(3, 4).LengthSquared);
    }

    [Fact]
    public void RoutingStrategyUsesWpfNumericValues()
    {
        Assert.Equal(0, (int)RoutingStrategy.Tunnel);
        Assert.Equal(1, (int)RoutingStrategy.Bubble);
        Assert.Equal(2, (int)RoutingStrategy.Direct);
    }

    [Fact]
    public void RoutedEventHandlerInfoHasWpfValueSemantics()
    {
        RoutedEventHandler handler = static (_, _) => { };
        var first = new RoutedEventHandlerInfo(handler, handledEventsToo: true);
        var second = new RoutedEventHandlerInfo(handler, handledEventsToo: true);

        Assert.Same(handler, first.Handler);
        Assert.True(first.InvokeHandledEventsToo);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
    }

    [Fact]
    public void NameScopeImplementsDictionaryContract()
    {
        var scope = new NameScope();
        var value = new object();

        scope.Add("item", value);

        Assert.False(scope.IsReadOnly);
        Assert.Same(value, scope["item"]);
        Assert.Contains("item", scope.Keys);
        Assert.Contains(value, scope.Values);
        Assert.True(scope.TryGetValue("item", out object? found));
        Assert.Same(value, found);
        Assert.True(scope.Remove(new KeyValuePair<string, object>("item", value)));
        Assert.Empty(scope);
    }

    [Fact]
    public void DependencyObjectExposesTypeAndSealedState()
    {
        var first = new DependencyObject();
        var second = new DependencyObject();
        Assert.Same(first.DependencyObjectType, second.DependencyObjectType);
        Assert.False(first.IsSealed);

        var freezable = new TestFreezable();
        freezable.Freeze();
        Assert.True(freezable.IsSealed);
    }

    [Fact]
    public void SetCurrentValueIsReportedByValueSource()
    {
        var target = new DependencyObject();
        target.SetCurrentValue(CurrentValueProperty, 7);

        ValueSource source = DependencyPropertyHelper.GetValueSource(target, CurrentValueProperty);

        Assert.True(source.IsCurrent);
        Assert.Equal(BaseValueSource.Default, source.BaseValueSource);
    }

    [Fact]
    public void ReadOnlyKeyOverloadsAndLocalValueEnumeratorRoundTrip()
    {
        DependencyPropertyKey key = DependencyProperty.RegisterReadOnly(
            "WpfParityReadOnlyKeyValue",
            typeof(string),
            typeof(ParityCoreSurfaceTests),
            new PropertyMetadata(null));
        var target = new SerializableDependencyObject();

        target.SetValue(key, null);
        Assert.Null(target.ReadLocalValue(key.DependencyProperty));
        Assert.True(target.ShouldSerialize(key.DependencyProperty));

        LocalValueEnumerator enumerator = target.GetLocalValueEnumerator();
        Assert.Equal(1, enumerator.Count);
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Same(key.DependencyProperty, enumerator.Current.Property);
        Assert.Null(enumerator.Current.Value);

        target.ClearValue(key);
        Assert.Same(DependencyProperty.UnsetValue, target.ReadLocalValue(key.DependencyProperty));
        Assert.Equal(0, target.GetLocalValueEnumerator().Count);
    }

    [Fact]
    public void StyleResourcesParticipateInLookupAndStyleImplementsNameScope()
    {
        var style = new Style();
        style.Resources["accent"] = "green";
        ((Jalium.UI.Markup.INameScope)style).RegisterName("part", style);
        var element = new FrameworkElement { Style = style };

        Assert.Equal("green", ResourceLookup.FindResource(element, "accent"));
        Assert.Same(style, ((Jalium.UI.Markup.INameScope)style).FindName("part"));
    }

    private sealed class TestFreezable : Freezable
    {
        protected override Freezable CreateInstanceCore() => new TestFreezable();
    }

    private sealed class SerializableDependencyObject : DependencyObject
    {
        internal bool ShouldSerialize(DependencyProperty property) => ShouldSerializeProperty(property);
    }
}
