using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class MenuBaseWpfParityTests
{
    [Fact]
    public void PublicSurface_ExposesItemContainerTemplateSelectorAndMouseHook()
    {
        var type = typeof(MenuBase);
        var property = type.GetProperty(
            nameof(MenuBase.ItemContainerTemplateSelector),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var method = type.GetMethod(
            "HandleMouseButton",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(MouseButtonEventArgs)],
            modifiers: null);

        Assert.Equal(typeof(DependencyProperty), type.GetField(
            nameof(MenuBase.ItemContainerTemplateSelectorProperty),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)?.FieldType);
        Assert.Equal(typeof(ItemContainerTemplateSelector), property?.PropertyType);
        Assert.NotNull(property?.GetMethod);
        Assert.NotNull(property?.SetMethod);
        Assert.IsAssignableFrom<ItemContainerTemplateSelector>(
            MenuBase.ItemContainerTemplateSelectorProperty.GetMetadata(typeof(MenuBase)).DefaultValue);

        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void ItemContainerTemplateSelector_CreatesAValidMenuContainer()
    {
        var template = new Jalium.UI.Controls.ItemContainerTemplate();
        template.SetVisualTree(() => new Separator());
        var selector = new ProbeTemplateSelector(template);
        var menu = new ProbeMenuBase
        {
            UsesItemContainerTemplate = true,
            ItemContainerTemplateSelector = selector,
        };
        object item = new();

        var container = menu.GenerateContainer(item);

        Assert.IsType<Separator>(container);
        Assert.Same(item, selector.LastItem);
        Assert.Same(menu, selector.LastParent);
    }

    [Fact]
    public void DefaultItemContainerTemplateSelector_ResolvesImplicitTemplateByItemType()
    {
        var template = new Jalium.UI.Controls.ItemContainerTemplate();
        template.SetVisualTree(() => new MenuItem { Header = "implicit" });
        var menu = new ProbeMenuBase
        {
            UsesItemContainerTemplate = true,
        };
        menu.Resources[new ItemContainerTemplateKey(typeof(TestItemBase))] = template;

        var container = Assert.IsType<MenuItem>(menu.GenerateContainer(new TestItem()));

        Assert.Equal("implicit", container.Header);
    }

    [Fact]
    public void ItemContainerTemplateSelector_RejectsAnInvalidContainerType()
    {
        var template = new Jalium.UI.Controls.ItemContainerTemplate();
        template.SetVisualTree(() => new Border());
        var menu = new ProbeMenuBase
        {
            UsesItemContainerTemplate = true,
            ItemContainerTemplateSelector = new ProbeTemplateSelector(template),
        };

        Assert.Throws<InvalidOperationException>(() => menu.GenerateContainer(new object()));
    }

    [Fact]
    public void MouseDownAndMouseUp_InvokeHandleMouseButton()
    {
        var menu = new ProbeMenuBase();
        var child = new Border();
        menu.AttachForTest(child);
        var down = CreateMouseEvent(UIElement.MouseDownEvent, MouseButtonState.Pressed);
        var up = CreateMouseEvent(UIElement.MouseUpEvent, MouseButtonState.Released);

        child.RaiseEvent(down);
        child.RaiseEvent(up);

        Assert.Collection(
            menu.HandledMouseEvents,
            item => Assert.Same(down, item),
            item => Assert.Same(up, item));
    }

    private static MouseButtonEventArgs CreateMouseEvent(RoutedEvent routedEvent, MouseButtonState state)
    {
        return new MouseButtonEventArgs(
            routedEvent,
            new Point(3, 4),
            MouseButton.Left,
            state,
            clickCount: 1,
            leftButton: state,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);
    }

    private sealed class ProbeMenuBase : MenuBase
    {
        public List<MouseButtonEventArgs> HandledMouseEvents { get; } = [];

        public FrameworkElement GenerateContainer(object item) => GetContainerForItem(item);

        public void AttachForTest(UIElement child) => AddVisualChild(child);

        protected override void HandleMouseButton(MouseButtonEventArgs e)
        {
            HandledMouseEvents.Add(e);
            base.HandleMouseButton(e);
        }
    }

    private sealed class ProbeTemplateSelector(DataTemplate template) : ItemContainerTemplateSelector
    {
        public object? LastItem { get; private set; }
        public ItemsControl? LastParent { get; private set; }

        public override DataTemplate? SelectTemplate(object? item, ItemsControl parentItemsControl)
        {
            LastItem = item;
            LastParent = parentItemsControl;
            return template;
        }
    }

    private abstract class TestItemBase
    {
    }

    private sealed class TestItem : TestItemBase
    {
    }
}
