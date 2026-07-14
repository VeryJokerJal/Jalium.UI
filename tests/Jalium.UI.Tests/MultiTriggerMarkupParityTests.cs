using Binding = Jalium.UI.Data.Binding;
using IAddChild = Jalium.UI.Markup.IAddChild;

namespace Jalium.UI.Tests;

public sealed class MultiTriggerMarkupParityTests
{
    private static readonly DependencyProperty FlagProperty = DependencyProperty.Register(
        "MultiTriggerMarkupFlag",
        typeof(bool),
        typeof(MultiTriggerMarkupParityTests),
        new PropertyMetadata(false));

    [Fact]
    public void MultiTriggersImplementWpfMarkupChildSemantics()
    {
        foreach (TriggerBase trigger in new TriggerBase[] { new MultiTrigger(), new MultiDataTrigger() })
        {
            var addChild = Assert.IsAssignableFrom<IAddChild>(trigger);
            var setter = new Setter(FlagProperty, true);

            addChild.AddText(null!);
            addChild.AddText(" \r\n\t");
            addChild.AddChild(setter);

            Assert.Same(setter, Assert.Single(trigger.Setters));
            Assert.Throws<ArgumentNullException>(() => addChild.AddChild(null!));
            Assert.Throws<ArgumentException>(() => addChild.AddChild(new EventSetter()));
            Assert.Throws<ArgumentException>(() => addChild.AddChild(new object()));
            Assert.Throws<ArgumentException>(() => addChild.AddText("content"));
        }
    }

    [Fact]
    public void SealingMultiTriggersSealsAndValidatesTheirConditionKind()
    {
        var propertyCondition = new Condition(FlagProperty, true);
        var multiTrigger = new MultiTrigger();
        multiTrigger.Conditions.Add(propertyCondition);
        multiTrigger.Setters.Add(new Setter(FlagProperty, false));

        var bindingCondition = new Condition(new Binding("Flag"), true);
        var multiDataTrigger = new MultiDataTrigger();
        multiDataTrigger.Conditions.Add(bindingCondition);
        multiDataTrigger.Setters.Add(new Setter(FlagProperty, false));

        Seal(multiTrigger);
        Seal(multiDataTrigger);

        Assert.True(multiTrigger.Conditions.IsSealed);
        Assert.True(multiDataTrigger.Conditions.IsSealed);
        Assert.True(multiTrigger.Setters.IsSealed);
        Assert.True(multiDataTrigger.Setters.IsSealed);
        Assert.Throws<InvalidOperationException>(() => propertyCondition.Value = false);
        Assert.Throws<InvalidOperationException>(() => bindingCondition.Value = false);
        Assert.Throws<InvalidOperationException>(() => multiTrigger.Conditions.Clear());
        Assert.Throws<InvalidOperationException>(() => multiDataTrigger.Conditions.Clear());
    }

    [Fact]
    public void SealingRejectsTheWrongConditionShape()
    {
        var multiTrigger = new MultiTrigger();
        multiTrigger.Conditions.Add(new Condition(new Binding("Flag"), true));

        var multiDataTrigger = new MultiDataTrigger();
        multiDataTrigger.Conditions.Add(new Condition(FlagProperty, true));

        Assert.Throws<InvalidOperationException>(() => Seal(multiTrigger));
        Assert.Throws<InvalidOperationException>(() => Seal(multiDataTrigger));
    }

    private static void Seal(TriggerBase trigger)
    {
        var style = new Style();
        style.Triggers.Add(trigger);
        style.Seal();
    }
}
