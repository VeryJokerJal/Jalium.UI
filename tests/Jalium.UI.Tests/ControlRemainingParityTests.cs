using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class ControlRemainingParityTests
{
    [Fact]
    public void TabNavigationPropertiesAreKeyboardNavigationOwners()
    {
        Assert.Same(KeyboardNavigation.TabIndexProperty, Control.TabIndexProperty);
        Assert.Same(KeyboardNavigation.IsTabStopProperty, Control.IsTabStopProperty);

        var control = new Control();
        Assert.Equal(int.MaxValue, control.TabIndex);
        Assert.True(control.IsTabStop);

        control.TabIndex = 7;
        control.IsTabStop = false;
        Assert.Equal(7, KeyboardNavigation.GetTabIndex(control));
        Assert.False(KeyboardNavigation.GetIsTabStop(control));
    }

    [Fact]
    public void TemplateChangesInvokeVirtualHookWithOldAndNewValues()
    {
        var control = new ProbeControl();
        var first = new ControlTemplate(typeof(ProbeControl));
        var second = new ControlTemplate(typeof(ProbeControl));

        control.Template = first;
        Assert.Null(control.LastOldTemplate);
        Assert.Same(first, control.LastNewTemplate);

        control.Template = second;
        Assert.Same(first, control.LastOldTemplate);
        Assert.Same(second, control.LastNewTemplate);
        Assert.Equal(2, control.TemplateChangedCount);
    }

    [Fact]
    public void ToStringIncludesPlainContentWhenAvailable()
    {
        var button = new Button { Content = "Save" };

        Assert.Contains("Save", button.ToString(), StringComparison.Ordinal);
    }

    private sealed class ProbeControl : Control
    {
        public int TemplateChangedCount { get; private set; }
        public ControlTemplate? LastOldTemplate { get; private set; }
        public ControlTemplate? LastNewTemplate { get; private set; }

        protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
        {
            TemplateChangedCount++;
            LastOldTemplate = oldTemplate;
            LastNewTemplate = newTemplate;
            base.OnTemplateChanged(oldTemplate, newTemplate);
        }
    }
}
