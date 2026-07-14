using System.Reflection;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class EventTriggerRemainingParityTests
{
    [Fact]
    public void ActionsUseCanonicalCollectionAndMarkupChildContract()
    {
        Assert.Equal(
            typeof(TriggerActionCollection),
            typeof(EventTrigger).GetProperty(nameof(EventTrigger.Actions))!.PropertyType);
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(EventTrigger)));

        var trigger = new ProbeEventTrigger();
        var action = new ProbeAction();
        trigger.Add(action);
        trigger.AddWhitespace(" \r\n");

        Assert.Same(action, Assert.Single(trigger.Actions));
        Assert.True(trigger.ShouldSerializeActions());
        Assert.Throws<ArgumentException>(() => trigger.Add(new object()));
        Assert.Throws<ArgumentException>(() => trigger.AddWhitespace("content"));
    }

    [Fact]
    public void AddChildAndAddTextAreProtectedVirtual()
    {
        foreach (string methodName in new[] { "AddChild", "AddText" })
        {
            MethodInfo method = typeof(EventTrigger).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            Assert.True(method.IsFamily);
            Assert.True(method.IsVirtual);
            Assert.False(method.IsFinal);
        }
    }

    private sealed class ProbeEventTrigger : EventTrigger
    {
        public void Add(object value) => AddChild(value);
        public void AddWhitespace(string text) => AddText(text);
    }

    private sealed class ProbeAction : TriggerAction
    {
        internal override void Invoke(FrameworkElement? element)
        {
        }
    }
}
