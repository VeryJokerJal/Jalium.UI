using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed partial class MacOSAccessibilityFocusHostTests
{
    [Theory]
    [InlineData("overlay", false)] [InlineData("overlay", true)]
    [InlineData("native", false)] [InlineData("native", true)]
    public void MountedToolTipContentCannotTakeOrdinaryFocus(string kind, bool tooltipFocusable)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var tooltip = MountedToolTip(tooltipFocusable, out var editor);
        using var h = new Host(kind, hitTest: false, popupChild: tooltip);
        Assert.IsType<ContentPresenter>(editor.VisualParent);
        Assert.Same(h.Source, Window.GetMacOSPhysicalWindow(editor));
        Assert.False(editor.Focus());
        Assert.Same(h.Before, Keyboard.FocusedElement); Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData("overlay")] [InlineData("native")]
    public void MountedToolTipAXFocusIsRejectedBeforeActivation(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var tooltip = MountedToolTip(true, out var editor);
        using var h = new Host(kind, hitTest: false, popupChild: tooltip);
        Assert.False(h.Focus(editor)); h.Process();
        Assert.Same(h.Before, Keyboard.FocusedElement); Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData("overlay")] [InlineData("native")]
    public void OrdinaryPointerTransparentPopupRetainsMountedEditorFocus(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var h = new Host(kind, hitTest: false);
        Assert.True(h.Target.Focus()); Assert.Same(h.Target, Keyboard.FocusedElement);
        Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void MountedToolTipIsSkippedByTabPredictionAndMovement(bool tooltipFocusable)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var tooltip = MountedToolTip(tooltipFocusable, out var editor);
        using var h = new Host("overlay", hitTest: false, popupChild: tooltip);
        var next = new TextBox { Text = "next", Width = 100, Height = 30 };
        h.Content.Children.Add(next); Canvas.SetTop(next, 100);
        KeyboardNavigation.SetTabIndex(h.Before, 0); KeyboardNavigation.SetTabIndex(tooltip, 1);
        KeyboardNavigation.SetTabIndex(editor, 2); KeyboardNavigation.SetTabIndex(next, 3);
        Assert.Same(next, KeyboardNavigation.PredictFocus(h.Before, FocusNavigationDirection.Next));
        Assert.True(KeyboardNavigation.MoveFocus(h.Before, FocusNavigationDirection.Next));
        Assert.Same(next, Keyboard.FocusedElement);
        Assert.Same(h.Before, KeyboardNavigation.PredictFocus(next, FocusNavigationDirection.Previous));
        Assert.True(KeyboardNavigation.MoveFocus(next, FocusNavigationDirection.Previous));
        Assert.Same(h.Before, Keyboard.FocusedElement); Assert.Equal(0, h.ActivationCalls);
    }

    [Theory] [InlineData("overlay")] [InlineData("native")]
    public void MountedToolTipAXMetadataKeepsReadableContentWithoutFocusability(string kind)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var tooltip = MountedToolTip(true, out var editor);
        using var h = new Host(kind, hitTest: false, popupChild: tooltip);
        Assert.True(h.Tree.TryGetId(editor.GetAutomationPeer()!, out var id));
        var request = new MacOSAXRequest { NodeId = id, Operation = MacOSAXOperation.Info };
        Assert.True(h.Tree.Handle(ref request));
        Assert.True(request.Flags.HasFlag(MacOSAXFlags.Enabled));
        Assert.False(request.Flags.HasFlag(MacOSAXFlags.Focusable));
        Assert.False(request.Flags.HasFlag(MacOSAXFlags.Focused));
    }

    private static ToolTip MountedToolTip(bool focusable, out TextBox editor)
    {
        var content = new TextBox { Text = "提示内容中文🙂", Width = 100, Height = 30 };
        var template = new ControlTemplate(typeof(ToolTip));
        template.SetVisualTree(() => new ContentPresenter { Content = content });
        editor = content;
        return new ToolTip { Content = content, Template = template, Focusable = focusable };
    }
}
