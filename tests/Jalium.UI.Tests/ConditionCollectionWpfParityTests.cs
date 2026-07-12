using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class ConditionCollectionWpfParityTests
{
    private static readonly DependencyProperty FlagProperty = DependencyProperty.Register(
        "ConditionFlag",
        typeof(bool),
        typeof(ConditionCollectionWpfParityTests),
        new PropertyMetadata(false));

    [Fact]
    public void Condition_ConstructorsCreatePropertyOrBindingConditions()
    {
        var propertyCondition = new Condition(FlagProperty, true, "PART_Source");
        var binding = new Binding("Flag");
        var bindingCondition = new Condition(binding, true);

        Assert.Same(FlagProperty, propertyCondition.Property);
        Assert.Null(propertyCondition.Binding);
        Assert.Equal(true, propertyCondition.Value);
        Assert.Equal("PART_Source", propertyCondition.SourceName);

        Assert.Null(bindingCondition.Property);
        Assert.Same(binding, bindingCondition.Binding);
        Assert.Equal(true, bindingCondition.Value);
    }

    [Fact]
    public void Condition_RejectsPropertyAndBindingCombination()
    {
        var propertyCondition = new Condition { Property = FlagProperty };
        Assert.Throws<InvalidOperationException>(() => propertyCondition.Binding = new Binding("Flag"));

        var bindingCondition = new Condition { Binding = new Binding("Flag") };
        Assert.Throws<InvalidOperationException>(() => bindingCondition.Property = FlagProperty);
    }

    [Fact]
    public void ConditionCollection_ValidatesItemsAndBecomesImmutableWhenSealed()
    {
        var condition = new Condition(FlagProperty, true);
        var collection = new ConditionCollection { condition };

        collection.Seal(dataConditions: false);

        Assert.True(collection.IsSealed);
        Assert.Throws<InvalidOperationException>(() => collection.Add(new Condition(FlagProperty, false)));
        Assert.Throws<InvalidOperationException>(() => collection.Clear());
        Assert.Throws<InvalidOperationException>(() => condition.Value = false);
    }

    [Fact]
    public void MultiTriggersExposeSharedWpfConditionCollectionType()
    {
        Assert.IsType<ConditionCollection>(new MultiTrigger().Conditions);
        Assert.IsType<ConditionCollection>(new MultiDataTrigger().Conditions);

        var legacy = new BindingCondition { Binding = new Binding("Flag"), Value = true };
        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(legacy);

        Assert.Same(legacy.Binding, trigger.Conditions[0].Binding);
        Assert.Equal(legacy.Value, trigger.Conditions[0].Value);
    }
}
