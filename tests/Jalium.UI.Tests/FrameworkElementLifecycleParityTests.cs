using System.Collections;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class FrameworkElementLifecycleParityTests
{
    [Fact]
    public void ActualSizeReadOnlyProperties_TrackArrangeResults()
    {
        var element = new ProbeElement(new Size(10, 12));

        element.Measure(new Size(100, 100));
        element.Arrange(new Rect(0, 0, 32, 24));

        Assert.Equal(32, element.ActualWidth);
        Assert.Equal(24, element.ActualHeight);
        Assert.Equal(element.RenderSize.Width, element.GetValue(FrameworkElement.ActualWidthProperty));
        Assert.Equal(element.RenderSize.Height, element.GetValue(FrameworkElement.ActualHeightProperty));
        Assert.Throws<InvalidOperationException>(() => element.SetValue(FrameworkElement.ActualWidthProperty, 99.0));
    }

    [Fact]
    public void InitializationAndLogicalTree_DriveInheritanceLoadedAndResourceLookup()
    {
        var parent = new ProbeElement(default);
        var child = new ProbeElement(default);
        var initialized = 0;
        var loaded = 0;
        var unloaded = 0;
        var routedLoaded = 0;

        parent.Initialized += (_, _) => initialized++;
        child.Loaded += (_, _) => loaded++;
        child.Unloaded += (_, _) => unloaded++;
        child.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((_, _) => routedLoaded++));

        parent.BeginInit();
        parent.EndInit();
        parent.EndInit();
        Assert.True(parent.IsInitialized);
        Assert.Equal(1, initialized);

        parent.FlowDirection = FlowDirection.RightToLeft;
        parent.BindingGroup = new BindingGroup { Name = "Shared" };
        parent.Resources["LogicalValue"] = "first";
        parent.SetLoadedState(true);
        parent.AddLogical(child);

        Assert.Same(parent, child.Parent);
        Assert.Same(parent, child.ExposedUIParent);
        Assert.Contains(child, parent.ExposedLogicalChildren.Cast<object>());
        Assert.Equal(FlowDirection.RightToLeft, child.FlowDirection);
        Assert.Same(parent.BindingGroup, child.BindingGroup);
        Assert.True(child.IsLoaded);
        Assert.Equal(1, loaded);
        Assert.Equal(1, routedLoaded);
        Assert.Equal("first", child.TryFindResource("LogicalValue"));

        child.SetResourceReference(FrameworkElement.TagProperty, "LogicalValue");
        Assert.Equal("first", child.Tag);
        parent.Resources["LogicalValue"] = "second";
        Assert.Equal("second", child.Tag);

        parent.RemoveLogical(child);
        Assert.Null(child.Parent);
        Assert.False(child.IsLoaded);
        Assert.Equal(1, unloaded);
    }

    [Fact]
    public void LayoutStateProperties_InvalidateAndAffectDesiredSizeAndCursorResolution()
    {
        var parent = new ProbeElement(default)
        {
            Cursor = Cursors.Wait,
            ForceCursor = true,
        };
        var child = new ProbeElement(new Size(10, 20))
        {
            Cursor = Cursors.Cross,
            LayoutTransform = new ScaleTransform(2, 3),
        };
        parent.AddLogical(child);

        child.Measure(new Size(100, 100));

        Assert.Equal(new Size(20, 60), child.DesiredSize);
        Assert.Same(Cursors.Wait, FrameworkElement.ResolveEffectiveCursor(child));
        Assert.Equal(FlowDirection.LeftToRight, FrameworkElement.GetFlowDirection(child));
        FrameworkElement.SetFlowDirection(child, FlowDirection.RightToLeft);
        Assert.Equal(FlowDirection.RightToLeft, child.FlowDirection);
    }

    [Fact]
    public void FrameworkElementApplyTemplate_UsesControlTemplateLifecycleAndNameLookup()
    {
        var control = new ProbeControl
        {
            Template = new ControlTemplate(typeof(ProbeControl)),
        };
        control.Template.SetVisualTree(() => new FrameworkElement { Name = "PART_Root" });

        Assert.True(control.ApplyTemplate());
        Assert.Equal(1, control.ApplyCount);
        Assert.NotNull(control.FindTemplatePart("PART_Root"));
        Assert.False(control.ApplyTemplate());
        Assert.Equal(1, control.ApplyCount);
    }

    [Fact]
    public void SerializationPredicates_ReflectLocalState()
    {
        var element = new ProbeElement(default);
        Assert.False(element.ShouldSerializeResources());
        Assert.False(element.ShouldSerializeStyle());

        element.Resources["key"] = "value";
        element.Style = new Style(typeof(ProbeElement));

        Assert.True(element.ShouldSerializeResources());
        Assert.True(element.ShouldSerializeStyle());
    }

    private sealed class ProbeElement : FrameworkElement
    {
        private readonly Size _desired;

        public ProbeElement(Size desired) => _desired = desired;

        public IEnumerable ExposedLogicalChildren => new EnumeratorEnumerable(LogicalChildren);
        public DependencyObject? ExposedUIParent => GetUIParentCore();
        public void AddLogical(object child) => AddLogicalChild(child);
        public void RemoveLogical(object child) => RemoveLogicalChild(child);

        protected override Size MeasureOverride(Size availableSize) => _desired;
    }

    private sealed class ProbeControl : Control
    {
        public int ApplyCount { get; private set; }
        public DependencyObject? FindTemplatePart(string name) => GetTemplateChild(name);

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ApplyCount++;
        }
    }

    private sealed class EnumeratorEnumerable : IEnumerable
    {
        private readonly IEnumerator _enumerator;
        public EnumeratorEnumerable(IEnumerator enumerator) => _enumerator = enumerator;
        public IEnumerator GetEnumerator() => _enumerator;
    }
}
