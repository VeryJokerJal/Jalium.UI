using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class KeyboardFocusPropertiesTests
{
    [Fact]
    public void IsKeyboardFocused_PropertyTrigger_ShouldTrackFocusState()
    {
        var element = new FocusProbeElement
        {
            Style = BuildKeyboardFocusedStyle(typeof(FocusProbeElement))
        };

        Assert.False(element.IsKeyboardFocused);
        Assert.False(element.IsFocused);
        Assert.Equal("Idle", element.Tag);
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(element, FrameworkElement.TagProperty).BaseValueSource);

        element.UpdateIsKeyboardFocused(true);

        Assert.True(element.IsKeyboardFocused);
        Assert.True(element.IsFocused);
        Assert.Equal("Focused", element.Tag);
        Assert.Equal(BaseValueSource.StyleTrigger, DependencyPropertyHelper.GetValueSource(element, FrameworkElement.TagProperty).BaseValueSource);

        element.UpdateIsKeyboardFocused(false);

        Assert.False(element.IsKeyboardFocused);
        Assert.False(element.IsFocused);
        Assert.Equal("Idle", element.Tag);
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(element, FrameworkElement.TagProperty).BaseValueSource);
    }

    [Fact]
    public void IsKeyboardFocusWithin_ShouldNotToggleForSharedAncestors()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();

        try
        {
            var root = new CountingPanel
            {
                Style = BuildKeyboardFocusWithinStyle(typeof(CountingPanel))
            };

            var first = new FocusProbeElement();
            var second = new FocusProbeElement();
            root.Children.Add(first);
            root.Children.Add(second);

            Assert.Equal("Outside", root.Tag);
            Assert.False(root.IsKeyboardFocusWithin);
            Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(root, FrameworkElement.TagProperty).BaseValueSource);

            Assert.True(first.Focus());
            Assert.True(first.IsKeyboardFocused);
            Assert.True(root.IsKeyboardFocusWithin);
            Assert.Equal("Inside", root.Tag);
            Assert.Equal(1, root.FocusWithinChangedCount);
            Assert.Equal(BaseValueSource.StyleTrigger, DependencyPropertyHelper.GetValueSource(root, FrameworkElement.TagProperty).BaseValueSource);

            Assert.True(second.Focus());
            Assert.False(first.IsKeyboardFocused);
            Assert.True(second.IsKeyboardFocused);
            Assert.True(root.IsKeyboardFocusWithin);
            Assert.Equal("Inside", root.Tag);
            Assert.Equal(1, root.FocusWithinChangedCount);
            Assert.Equal(BaseValueSource.StyleTrigger, DependencyPropertyHelper.GetValueSource(root, FrameworkElement.TagProperty).BaseValueSource);

            Keyboard.ClearFocus();

            Assert.False(root.IsKeyboardFocusWithin);
            Assert.Equal("Outside", root.Tag);
            Assert.Equal(2, root.FocusWithinChangedCount);
            Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(root, FrameworkElement.TagProperty).BaseValueSource);
        }
        finally
        {
            Keyboard.ClearFocus();
        }
    }

    private static Style BuildKeyboardFocusedStyle(Type targetType)
    {
        var style = new Style(targetType);
        style.Setters.Add(new Setter(FrameworkElement.TagProperty, "Idle"));

        var trigger = new PropertyTrigger
        {
            Property = UIElement.IsKeyboardFocusedProperty,
            Value = true
        };
        trigger.Setters.Add(new Setter(FrameworkElement.TagProperty, "Focused"));
        style.Triggers.Add(trigger);

        return style;
    }

    private static Style BuildKeyboardFocusWithinStyle(Type targetType)
    {
        var style = new Style(targetType);
        style.Setters.Add(new Setter(FrameworkElement.TagProperty, "Outside"));

        var trigger = new PropertyTrigger
        {
            Property = UIElement.IsKeyboardFocusWithinProperty,
            Value = true
        };
        trigger.Setters.Add(new Setter(FrameworkElement.TagProperty, "Inside"));
        style.Triggers.Add(trigger);

        return style;
    }

    private sealed class FocusProbeElement : FrameworkElement
    {
        public FocusProbeElement()
        {
            Focusable = true;
        }
    }

    private sealed class CountingPanel : StackPanel
    {
        public int FocusWithinChangedCount { get; private set; }

        protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
        {
            base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
            FocusWithinChangedCount++;
        }
    }
}
