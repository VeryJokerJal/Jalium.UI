using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed partial class MacOSAccessibilityFocusHostTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public void NonFocusableGestureContentNeedsKeyEligibilityButNotKeyboardFocus(bool hitTest)
    {
        using var h=new Host("native",hitTest);
        h.Target.Focusable=h.Other.Focusable=false;
        Assert.False(PopupKeyboardEligible(h));
        h.Target.IsManipulationEnabled=true;
        Assert.False(PopupKeyboardEligible(h)); // A bridge without the optional capability stays unchanged.
        typeof(Window).GetField("_platformWindow",Private)!.SetValue(h.Source,new MacOSNativeGestureTests.FakePlatform());
        try { Assert.Equal(hitTest,PopupKeyboardEligible(h)); }
        finally { typeof(Window).GetField("_platformWindow",Private)!.SetValue(h.Source,null); }
        Assert.Same(h.Before,Keyboard.FocusedElement);Assert.Equal(0,h.ActivationCalls);
        h.Target.IsManipulationEnabled=false;Assert.False(PopupKeyboardEligible(h));
    }

    [Fact]
    public void ATooltipGestureChildCannotMakeItsNativeHostKey()
    {
        var subject=new Border{Width=100,Height=50,IsManipulationEnabled=true};
        var template=new ControlTemplate(typeof(ToolTip));template.SetVisualTree(()=>new ContentPresenter{Content=subject});
        var tip=new ToolTip{Content=subject,Template=template};
        using var h=new Host("native",popupChild:new Border{Child=tip});
        Assert.NotNull(subject.VisualParent);Assert.True(subject.IsManipulationEnabled&&subject.IsVisible);
        typeof(Window).GetField("_platformWindow",Private)!.SetValue(h.Source,new MacOSNativeGestureTests.FakePlatform());
        try { Assert.False(PopupKeyboardEligible(h));Assert.Same(h.Before,Keyboard.FocusedElement); }
        finally { typeof(Window).GetField("_platformWindow",Private)!.SetValue(h.Source,null); }
    }
}
