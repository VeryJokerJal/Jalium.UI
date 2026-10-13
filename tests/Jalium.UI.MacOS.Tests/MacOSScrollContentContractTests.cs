using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Automation.Peers;
using System.Reflection;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSScrollContentContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ContentProviderIsSelectedOnlyWhenEnabledRegardlessOfAssignmentOrder(bool enabled, bool contentFirst)
    {
        var content = new Provider();
        var viewer = Viewer();
        if (contentFirst) viewer.Content = content;
        viewer.CanContentScroll = enabled;
        if (!contentFirst) viewer.Content = content;
        Layout(viewer);
        Assert.Same(enabled ? content : null, viewer.Provider);
        Assert.Same(enabled ? viewer : null, content.ScrollOwner);
        Assert.Equal(enabled ? new Size(320, 200) : new Size(double.PositiveInfinity, double.PositiveInfinity), content.Constraint);
        viewer.ScrollToHorizontalOffset(48);
        viewer.ScrollToVerticalOffset(72);
        Assert.Equal(48, viewer.HorizontalOffset);
        Assert.Equal(72, viewer.VerticalOffset);
        Assert.Equal(enabled ? 48 : 0, content.HorizontalOffset);
        Assert.Equal(enabled ? 72 : 0, content.VerticalOffset);
    }

    [Fact]
    public void DefaultUsesPhysicalScrollingAndDoesNotAttachAProvider()
    {
        var content = new Provider();
        var viewer = Viewer(); viewer.Content = content;
        Assert.False(viewer.CanContentScroll);
        Layout(viewer);
        Assert.Null(viewer.Provider);
        Assert.Null(content.ScrollOwner);
        Assert.Equal(900, viewer.ExtentWidth);
        Assert.Equal(800, viewer.ExtentHeight);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModeChangeDetachesTheOldOwnerAndRestartsBothAxes(bool initiallyEnabled)
    {
        var content = new Provider();
        var viewer = Viewer(); viewer.CanContentScroll = initiallyEnabled; viewer.Content = content;
        Layout(viewer);
        for (int i = 0; i < 6; i++)
        {
            viewer.ScrollToHorizontalOffset(48); viewer.ScrollToVerticalOffset(72); Layout(viewer);
            bool enabled = !viewer.CanContentScroll;
            viewer.CanContentScroll = enabled;
            Layout(viewer);
            Assert.Same(enabled ? content : null, viewer.Provider);
            Assert.Same(enabled ? viewer : null, content.ScrollOwner);
            Assert.Equal(0, viewer.HorizontalOffset);
            Assert.Equal(0, viewer.VerticalOffset);
            Assert.Equal(enabled ? new Size(320, 200) : new Size(double.PositiveInfinity, double.PositiveInfinity), content.Constraint);
        }
    }

    [Fact]
    public void BindingAndClearValueReconfigureTheProviderWithoutReplacingContent()
    {
        var content = new Provider(); var model = new Mode();
        var viewer = Viewer(); viewer.Content = content;
        viewer.SetBinding(ScrollViewer.CanContentScrollProperty, new Binding(nameof(Mode.Enabled)) { Source = model });
        Layout(viewer); Assert.Null(viewer.Provider);
        model.Enabled = true;
        Layout(viewer); Assert.Same(content, viewer.Provider);
        Assert.NotNull(viewer.GetBindingExpression(ScrollViewer.CanContentScrollProperty));
        model.Enabled = false;
        Layout(viewer); Assert.Null(viewer.Provider); Assert.Null(content.ScrollOwner);
        model.Enabled = true;
        Layout(viewer); Assert.Same(content, viewer.Provider);
        viewer.ClearValue(ScrollViewer.CanContentScrollProperty);
        Layout(viewer); Assert.False(viewer.CanContentScroll); Assert.Null(viewer.Provider); Assert.Null(content.ScrollOwner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContentReplacementAndRemovalReleaseOnlyThePreviousOwner(bool enabled)
    {
        var first = new Provider(); var second = new Provider();
        var viewer = Viewer(); viewer.CanContentScroll = enabled; viewer.Content = first;
        Layout(viewer); viewer.Content = second; Layout(viewer);
        Assert.Null(first.ScrollOwner);
        Assert.Same(enabled ? viewer : null, second.ScrollOwner);
        Assert.Same(enabled ? second : null, viewer.Provider);
        viewer.Content = new Border { Width = 600, Height = 500 }; Layout(viewer);
        Assert.Null(second.ScrollOwner); Assert.Null(viewer.Provider);
        viewer.Content = null; Layout(viewer);
        Assert.Null(viewer.Provider); Assert.Equal(0, viewer.ExtentWidth); Assert.Equal(0, viewer.ExtentHeight);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void DisabledAxesRemainConstrainedAndProviderCapabilitiesFollowVisibility(bool enabled, bool disableX, bool disableY)
    {
        var content = new Provider(); var viewer = Viewer();
        viewer.CanContentScroll = enabled; viewer.Content = content;
        viewer.HorizontalScrollBarVisibility = disableX ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = disableY ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Layout(viewer);
        if (enabled)
        {
            Assert.Equal(!disableX, content.CanHorizontallyScroll);
            Assert.Equal(!disableY, content.CanVerticallyScroll);
        }
        else
        {
            Assert.Equal(new Size(disableX ? 320 : double.PositiveInfinity, disableY ? 200 : double.PositiveInfinity), content.Constraint);
        }
        viewer.ScrollToHorizontalOffset(48); viewer.ScrollToVerticalOffset(72); Layout(viewer);
        Assert.Equal(disableX ? 0 : 48, viewer.HorizontalOffset);
        Assert.Equal(disableY ? 0 : 72, viewer.VerticalOffset);

        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Layout(viewer); viewer.ScrollToHorizontalOffset(48); viewer.ScrollToVerticalOffset(72); Layout(viewer);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Layout(viewer);
        Assert.Equal(0, viewer.HorizontalOffset); Assert.Equal(0, viewer.VerticalOffset);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Layout(viewer); viewer.ScrollToHorizontalOffset(64); viewer.ScrollToVerticalOffset(96); Layout(viewer);
        Assert.Equal(64, viewer.HorizontalOffset); Assert.Equal(96, viewer.VerticalOffset);
    }

    [Fact]
    public void DirectFixedWidthStackPanelScrollsInBothPhysicalAxesWithoutAWrapper()
    {
        var first = new Border { Width = 720, Height = 28 };
        var content = new StackPanel { Width = 720, Spacing = 8 }; content.Children.Add(first);
        for (int i = 1; i < 30; i++) content.Children.Add(new Border { Width = 720, Height = 28 });
        var viewer = Viewer(); viewer.Content = content; Layout(viewer);
        Assert.Null(content.ScrollOwner); Assert.Null(viewer.Provider);
        Point before = first.TransformToAncestor(viewer);
        viewer.ScrollToHorizontalOffset(120); viewer.ScrollToVerticalOffset(96); Layout(viewer);
        Point after = first.TransformToAncestor(viewer);
        Assert.Equal(120, viewer.HorizontalOffset); Assert.Equal(96, viewer.VerticalOffset);
        Assert.Equal(before.X - 120, after.X, precision: 4);
        Assert.Equal(before.Y - 96, after.Y, precision: 4);
        Assert.Equal(720, viewer.ExtentWidth); Assert.Equal(1072, viewer.ExtentHeight);
    }

    [Fact]
    public void AttachedPropertyOnOtherControlsDoesNotTryToSelectTheirProvider()
    {
        var target = new Border();
        ScrollViewer.SetCanContentScroll(target, true);
        Assert.True(ScrollViewer.GetCanContentScroll(target));
        ScrollViewer.SetCanContentScroll(target, false);
        Assert.False(ScrollViewer.GetCanContentScroll(target));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AutomationRangeMovesHostedContentAndCommitsEvenWhenThumbDraggingIsDeferred(bool enabled, bool horizontal)
    {
        var viewer = Viewer(); viewer.CanContentScroll = enabled; viewer.Content = new Provider();
        viewer.IsDeferredScrollingEnabled = true; Layout(viewer);
        viewer.ScrollToHorizontalOffset(horizontal ? 0 : 36);
        viewer.ScrollToVerticalOffset(horizontal ? 36 : 0); Layout(viewer);
        var bar = (ScrollBar)typeof(ScrollViewer).GetField(horizontal ? "_horizontalScrollBar" : "_verticalScrollBar",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
        var peer = new ScrollBarAutomationPeer(bar);
        var focus = Jalium.UI.Input.Keyboard.FocusedElement;
        peer.SetValue(48); Layout(viewer);
        Assert.Equal(horizontal ? 48 : 36, viewer.HorizontalOffset);
        Assert.Equal(horizontal ? 36 : 48, viewer.VerticalOffset);
        Assert.Equal(48, peer.Value);
        Assert.Equal(horizontal ? 48 : 36, viewer.ContentHorizontalOffset);
        Assert.Equal(horizontal ? 36 : 48, viewer.ContentVerticalOffset);
        Assert.Same(focus, Jalium.UI.Input.Keyboard.FocusedElement);
        bar.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => peer.SetValue(0));
        Assert.Equal(48, peer.Value);
        bar.IsEnabled = true; peer.SetValue(0); Layout(viewer);
        Assert.Equal(horizontal ? 0 : 36, viewer.HorizontalOffset);
        Assert.Equal(horizontal ? 36 : 0, viewer.VerticalOffset);
    }

    [Fact]
    public void StandaloneScrollbarAutomationRetainsValueCoercionWithoutScrollCommands()
    {
        var bar = new ScrollBar { Minimum = 5, Maximum = 25, Value = 10 };
        int commands = 0; bar.Scroll += (_, _) => commands++;
        var peer = new ScrollBarAutomationPeer(bar);
        peer.SetValue(30); Assert.Equal(25, peer.Value); Assert.Equal(0, commands);
        bar.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => peer.SetValue(15)); Assert.Equal(25, peer.Value);
    }

    private static Probe Viewer() => new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        IsOverlayScrollBarEnabled = true, IsScrollInertiaEnabled = false,
    };
    private static void Layout(ScrollViewer viewer)
    {
        for (int i = 0; i < 3; i++)
        { viewer.Measure(new Size(320, 200)); viewer.Arrange(new Rect(0, 0, 320, 200)); }
    }
    private sealed class Probe : ScrollViewer { internal IScrollInfo? Provider => ScrollInfo; }
    private sealed class Mode : INotifyPropertyChanged
    {
        private bool _enabled;
        public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed class Provider : Border, IScrollInfo
    {
        internal Size Constraint { get; private set; }
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth => 900;
        public double ExtentHeight => 800;
        public double ViewportWidth { get; private set; } = 320;
        public double ViewportHeight { get; private set; } = 200;
        public double HorizontalOffset { get; private set; }
        public double VerticalOffset { get; private set; }
        public ScrollViewer? ScrollOwner { get; set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            Constraint = availableSize;
            if (ScrollOwner != null) { ViewportWidth = availableSize.Width; ViewportHeight = availableSize.Height; }
            return new Size(900, 800);
        }
        public void SetHorizontalOffset(double offset) => HorizontalOffset = CanHorizontallyScroll ? Math.Clamp(offset, 0, ExtentWidth - ViewportWidth) : 0;
        public void SetVerticalOffset(double offset) => VerticalOffset = CanVerticallyScroll ? Math.Clamp(offset, 0, ExtentHeight - ViewportHeight) : 0;
        public void LineUp() => SetVerticalOffset(VerticalOffset - 16);
        public void LineDown() => SetVerticalOffset(VerticalOffset + 16);
        public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 16);
        public void LineRight() => SetHorizontalOffset(HorizontalOffset + 16);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
        public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
        public void MouseWheelUp() => LineUp();
        public void MouseWheelDown() => LineDown();
        public void MouseWheelLeft() => LineLeft();
        public void MouseWheelRight() => LineRight();
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }
}
