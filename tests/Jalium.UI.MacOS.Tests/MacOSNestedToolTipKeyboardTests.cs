using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed partial class MacOSAccessibilityFocusHostTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NestedToolTipContentCannotEnableNativePopupKeyboard(bool tooltipFocusable)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var tip = MountedToolTip(tooltipFocusable, out var editor);
        using var h = new Host("native", hitTest: false, popupChild: new Border { Child = tip });
        Assert.IsType<ContentPresenter>(editor.VisualParent);
        Assert.Same(h.Source, Window.GetMacOSPhysicalWindow(editor));
        Assert.False(editor.Focus());
        Assert.False(PopupKeyboardEligible(h));
        Assert.Same(h.Before, Keyboard.FocusedElement); Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativePopupKeyboardRequiresAnEligibleSiblingOutsideTheToolTip(bool hitTest)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var tip = MountedToolTip(true, out var editor);
        var sibling = new TextBox { Text = "ordinary sibling", Width = 100, Height = 30 };
        var body = new StackPanel();
        body.Children.Add(new Border { Child = tip }); body.Children.Add(sibling);
        using var h = new Host("native", hitTest, popupChild: body);
        Assert.IsType<ContentPresenter>(editor.VisualParent);
        Assert.Same(h.Source, Window.GetMacOSPhysicalWindow(editor));
        Assert.True(PopupKeyboardEligible(h)); Assert.True(sibling.Focus());
        sibling.Focusable = false; Assert.False(PopupKeyboardEligible(h));
        sibling.Focusable = true; Assert.True(PopupKeyboardEligible(h));
        sibling.IsEnabled = false; Assert.False(PopupKeyboardEligible(h));
        sibling.IsEnabled = true; Assert.True(PopupKeyboardEligible(h));
        sibling.Visibility = Visibility.Collapsed; Assert.False(PopupKeyboardEligible(h));
        sibling.Visibility = Visibility.Visible; Assert.True(PopupKeyboardEligible(h));
        Assert.Same(sibling, Keyboard.FocusedElement); Assert.Equal(0, h.ActivationCalls);
    }
}

