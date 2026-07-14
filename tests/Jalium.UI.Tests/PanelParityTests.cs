using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class PanelParityTests
{
    [Fact]
    public void SurfaceMatchesWpfAccessibilityAndVirtualContracts()
    {
        AssertProtectedInternalVirtualProperty("HasLogicalOrientation", typeof(bool));
        AssertProtectedInternalVirtualProperty("LogicalOrientation", typeof(Orientation));

        var internalChildren = typeof(Panel).GetProperty(
            "InternalChildren",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(internalChildren);
        Assert.Equal(typeof(UIElementCollection), internalChildren!.PropertyType);
        Assert.True(internalChildren.GetMethod!.IsFamilyOrAssembly);
        Assert.False(internalChildren.GetMethod.IsVirtual);

        foreach (var name in new[]
                 {
                     nameof(Panel.HasLogicalOrientationPublic),
                     nameof(Panel.LogicalOrientationPublic),
                 })
        {
            var property = typeof(Panel).GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Assert.NotNull(property);
            Assert.True(property!.CanRead);
            Assert.False(property.CanWrite);
            Assert.False(property.GetMethod!.IsVirtual);
        }

        var isItemsHost = typeof(Panel).GetProperty(
            nameof(Panel.IsItemsHost),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(isItemsHost);
        Assert.True(isItemsHost!.GetMethod!.IsPublic);
        Assert.True(isItemsHost.SetMethod!.IsPublic);

        AssertProtectedVirtualMethod(
            "CreateUIElementCollection",
            typeof(UIElementCollection),
            typeof(FrameworkElement));
        AssertProtectedVirtualMethod(
            "OnIsItemsHostChanged",
            typeof(void),
            typeof(bool),
            typeof(bool));

        var shouldSerialize = typeof(Panel).GetMethod(
            nameof(Panel.ShouldSerializeChildren),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(shouldSerialize);
        Assert.Equal(typeof(bool), shouldSerialize!.ReturnType);
        Assert.False(shouldSerialize.IsVirtual);
        Assert.Equal(
            EditorBrowsableState.Never,
            shouldSerialize.GetCustomAttribute<EditorBrowsableAttribute>()!.State);

        var metadata = Assert.IsType<FrameworkPropertyMetadata>(
            Panel.IsItemsHostProperty.GetMetadata(typeof(Panel)));
        Assert.False(metadata.IsDataBindingAllowed);
        Assert.False((bool)metadata.DefaultValue!);
    }

    [Fact]
    public void ChildrenAreCreatedLazilyThroughTheVirtualFactory()
    {
        var panel = new ProbePanel();

        Assert.Equal(0, panel.CreateCollectionCalls);
        Assert.False(panel.ShouldSerializeChildren());
        Assert.Equal(0, panel.CreateCollectionCalls);

        var children = panel.Children;

        Assert.Equal(1, panel.CreateCollectionCalls);
        Assert.Same(panel, panel.LastFactoryLogicalParent);
        Assert.Same(children, panel.ExposedInternalChildren);
        Assert.Equal(1, panel.CreateCollectionCalls);

        var child = new Border();
        children.Add(child);

        Assert.True(panel.ShouldSerializeChildren());
        Assert.Same(panel, child.Parent);
        Assert.Same(panel, child.VisualParent);
        Assert.Equal(new object[] { child }, panel.GetLogicalChildren());

        children.Remove(child);

        Assert.False(panel.ShouldSerializeChildren());
        Assert.Null(child.Parent);
        Assert.Null(child.VisualParent);
        Assert.Empty(panel.GetLogicalChildren());
    }

    [Fact]
    public void CollectionFactoryCanAssignASeparateLogicalParent()
    {
        var logicalOwner = new LogicalOwner();
        var panel = new ProbePanel { LogicalOwner = logicalOwner };
        var child = new Border();

        panel.Children.Add(child);

        Assert.Same(panel, child.VisualParent);
        Assert.Same(logicalOwner, child.Parent);
        Assert.Equal(new object[] { child }, logicalOwner.GetLogicalChildren());
        Assert.Empty(panel.GetLogicalChildren());

        panel.Children.Clear();

        Assert.Null(child.VisualParent);
        Assert.Null(child.Parent);
        Assert.Empty(logicalOwner.GetLogicalChildren());
    }

    [Fact]
    public void ItemsHostChangesUseTheVirtualHookAndSuppressSerialization()
    {
        var panel = new ProbePanel();
        var child = new Border();
        panel.Children.Add(child);

        panel.IsItemsHost = true;

        Assert.Equal(1, panel.ItemsHostChangedCalls);
        Assert.False(panel.LastOldIsItemsHost);
        Assert.True(panel.LastNewIsItemsHost);
        Assert.False(panel.ShouldSerializeChildren());
        Assert.Same(child, Assert.Single(panel.Children));
        Assert.Empty(panel.GetLogicalChildren());

        panel.IsItemsHost = true;
        Assert.Equal(1, panel.ItemsHostChangedCalls);

        panel.IsItemsHost = false;

        Assert.Equal(2, panel.ItemsHostChangedCalls);
        Assert.True(panel.LastOldIsItemsHost);
        Assert.False(panel.LastNewIsItemsHost);
        Assert.True(panel.ShouldSerializeChildren());
        Assert.Equal(new object[] { child }, panel.GetLogicalChildren());
    }

    [Fact]
    public void PublicOrientationPropertiesDelegateToProtectedVirtualValues()
    {
        var defaultPanel = new ProbePanel();
        Assert.False(defaultPanel.HasLogicalOrientationPublic);
        Assert.Equal(Orientation.Vertical, defaultPanel.LogicalOrientationPublic);

        var orientedPanel = new ProbePanel
        {
            OverrideHasLogicalOrientation = true,
            OverrideLogicalOrientation = Orientation.Horizontal,
        };

        Assert.True(orientedPanel.HasLogicalOrientationPublic);
        Assert.Equal(Orientation.Horizontal, orientedPanel.LogicalOrientationPublic);
    }

    private static void AssertProtectedInternalVirtualProperty(string name, Type propertyType)
    {
        var property = typeof(Panel).GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(propertyType, property!.PropertyType);
        Assert.True(property.GetMethod!.IsFamilyOrAssembly);
        Assert.True(property.GetMethod.IsVirtual);
        Assert.False(property.GetMethod.IsFinal);
        Assert.False(property.CanWrite);
    }

    private static void AssertProtectedVirtualMethod(
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = typeof(Panel).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            parameterTypes,
            null);
        Assert.NotNull(method);
        Assert.Equal(returnType, method!.ReturnType);
        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);
    }

    private sealed class ProbePanel : Panel
    {
        public int CreateCollectionCalls { get; private set; }
        public FrameworkElement? LastFactoryLogicalParent { get; private set; }
        public FrameworkElement? LogicalOwner { get; init; }
        public int ItemsHostChangedCalls { get; private set; }
        public bool LastOldIsItemsHost { get; private set; }
        public bool LastNewIsItemsHost { get; private set; }
        public bool OverrideHasLogicalOrientation { get; init; }
        public Orientation OverrideLogicalOrientation { get; init; } = Orientation.Vertical;

        public UIElementCollection ExposedInternalChildren => InternalChildren;

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

        protected override UIElementCollection CreateUIElementCollection(FrameworkElement logicalParent)
        {
            CreateCollectionCalls++;
            LastFactoryLogicalParent = logicalParent;
            return new UIElementCollection(this, LogicalOwner ?? logicalParent);
        }

        protected override void OnIsItemsHostChanged(bool oldIsItemsHost, bool newIsItemsHost)
        {
            ItemsHostChangedCalls++;
            LastOldIsItemsHost = oldIsItemsHost;
            LastNewIsItemsHost = newIsItemsHost;
            base.OnIsItemsHostChanged(oldIsItemsHost, newIsItemsHost);
        }

        protected internal override bool HasLogicalOrientation => OverrideHasLogicalOrientation;

        protected internal override Orientation LogicalOrientation => OverrideLogicalOrientation;
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
