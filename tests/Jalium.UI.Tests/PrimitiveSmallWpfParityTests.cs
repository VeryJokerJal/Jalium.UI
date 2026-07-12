using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class PrimitiveSmallWpfParityTests
{
    [Fact]
    public void ToolBarOverflowPanel_OverridesCollectionFactoryAndSuppressesTemplateLogicalOwnership()
    {
        var method = typeof(ToolBarOverflowPanel).GetMethod(
            "CreateUIElementCollection",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new[] { typeof(FrameworkElement) },
            null);

        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.Equal(typeof(UIElementCollection), method.ReturnType);
        Assert.Equal(typeof(Panel), method.GetBaseDefinition().DeclaringType);

        var logicalOwner = new LogicalOwner();
        var standalone = new ProbeOverflowPanel();
        var standaloneChild = new Border();
        standalone.CreateCollection(logicalOwner).Add(standaloneChild);

        Assert.Same(standalone, standaloneChild.VisualParent);
        Assert.Equal(new object[] { standaloneChild }, logicalOwner.GetLogicalChildren());

        var templated = new ProbeOverflowPanel();
        templated.SetTemplatedParent(new Button());
        var templatedChild = new Border();
        templated.CreateCollection(logicalOwner).Add(templatedChild);

        Assert.Same(templated, templatedChild.VisualParent);
        Assert.Equal(new object[] { standaloneChild }, logicalOwner.GetLogicalChildren());
        Assert.Empty(templated.GetLogicalChildren());
    }

    [Fact]
    public void Thumb_ExposesProtectedSetterAndVirtualDraggingHook()
    {
        var property = typeof(Thumb).GetProperty(
            nameof(Thumb.IsDragging),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.True(property!.GetMethod!.IsPublic);
        Assert.True(property.SetMethod!.IsFamily);

        var method = typeof(Thumb).GetMethod(
            "OnDraggingChanged",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new[] { typeof(DependencyPropertyChangedEventArgs) },
            null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);
    }

    [Fact]
    public void Thumb_ProtectedSetterRunsDraggingChangeHook()
    {
        var thumb = new ProbeThumb();

        thumb.SetDragging(true);
        thumb.SetDragging(false);

        Assert.False(thumb.IsDragging);
        Assert.Equal(2, thumb.Changes.Count);
        Assert.Equal((false, true), thumb.Changes[0]);
        Assert.Equal((true, false), thumb.Changes[1]);
        Assert.Throws<InvalidOperationException>(() => thumb.SetValue(Thumb.IsDraggingProperty, true));
    }

    [Fact]
    public void DataGridRowsPresenter_OverridesVirtualizationHooks()
    {
        AssertRowsPresenterOverride(
            "OnCleanUpVirtualizedItem",
            typeof(CleanUpVirtualizedItemEventArgs));
        AssertRowsPresenterOverride(
            "OnViewportSizeChanged",
            typeof(Size),
            typeof(Size));
    }

    [Fact]
    public void DataGridRowsPresenter_PreservesRowsWithValidationErrorsDuringCleanup()
    {
        var presenter = new ProbeDataGridRowsPresenter();
        var validRow = new DataGridRow();
        var validArgs = new CleanUpVirtualizedItemEventArgs(new object(), validRow);

        presenter.CleanUp(validArgs);

        Assert.False(validArgs.Cancel);

        var invalidRow = new DataGridRow();
        invalidRow.SetValue(Validation.HasErrorProperty, true);
        var invalidArgs = new CleanUpVirtualizedItemEventArgs(new object(), invalidRow);

        presenter.CleanUp(invalidArgs);

        Assert.True(invalidArgs.Cancel);
    }

    [Fact]
    public void DataGridRowsPresenter_ViewportHookInvalidatesMeasure()
    {
        var presenter = new ProbeDataGridRowsPresenter();
        presenter.Measure(new Size(100, 100));
        Assert.True(presenter.IsMeasureValid);

        presenter.ChangeViewport(new Size(100, 100), new Size(120, 100));

        Assert.False(presenter.IsMeasureValid);
    }

    private static void AssertRowsPresenterOverride(string name, params Type[] parameterTypes)
    {
        var method = typeof(DataGridRowsPresenter).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            parameterTypes,
            null);

        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);
        Assert.Equal(typeof(VirtualizingStackPanel), method.GetBaseDefinition().DeclaringType);
    }

    private sealed class ProbeOverflowPanel : ToolBarOverflowPanel
    {
        public UIElementCollection CreateCollection(FrameworkElement logicalParent) =>
            CreateUIElementCollection(logicalParent);

        public object[] GetLogicalChildren()
        {
            var result = new List<object>();
            IEnumerator enumerator = LogicalChildren;
            while (enumerator.MoveNext())
            {
                result.Add(enumerator.Current!);
            }

            return result.ToArray();
        }
    }

    private sealed class ProbeThumb : Thumb
    {
        public List<(bool OldValue, bool NewValue)> Changes { get; } = new();

        public void SetDragging(bool value) => IsDragging = value;

        protected override void OnDraggingChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnDraggingChanged(e);
            Changes.Add(((bool)e.OldValue!, (bool)e.NewValue!));
        }
    }

    private sealed class ProbeDataGridRowsPresenter : DataGridRowsPresenter
    {
        public void CleanUp(CleanUpVirtualizedItemEventArgs e) =>
            OnCleanUpVirtualizedItem(e);

        public void ChangeViewport(Size oldSize, Size newSize) =>
            OnViewportSizeChanged(oldSize, newSize);
    }

    private sealed class LogicalOwner : FrameworkElement
    {
        public object[] GetLogicalChildren()
        {
            var result = new List<object>();
            IEnumerator enumerator = LogicalChildren;
            while (enumerator.MoveNext())
            {
                result.Add(enumerator.Current!);
            }

            return result.ToArray();
        }
    }
}
