using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public class MenuContextWpfParityTests
{
    [Fact]
    public void MenuItem_PublicSurface_ExposesWpfCommandStateTemplateAndVirtualHooks()
    {
        var type = typeof(MenuItem);
        foreach (var fieldName in new[]
        {
            nameof(MenuItem.CommandProperty), nameof(MenuItem.CommandParameterProperty),
            nameof(MenuItem.CommandTargetProperty), nameof(MenuItem.IsHighlightedProperty),
            nameof(MenuItem.IsPressedProperty), nameof(MenuItem.IsSuspendingPopupAnimationProperty),
            nameof(MenuItem.ItemContainerTemplateSelectorProperty),
            nameof(MenuItem.UsesItemContainerTemplateProperty),
        })
        {
            Assert.Equal(typeof(DependencyProperty),
                type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)?.FieldType);
        }

        Assert.True(MenuItem.IsHighlightedProperty.ReadOnly);
        Assert.True(MenuItem.IsPressedProperty.ReadOnly);
        Assert.True(MenuItem.IsSuspendingPopupAnimationProperty.ReadOnly);
        Assert.True(type.GetProperty(nameof(MenuItem.IsHighlighted))!.SetMethod!.IsFamily);
        Assert.True(type.GetProperty(nameof(MenuItem.IsPressed))!.SetMethod!.IsFamily);
        Assert.Null(type.GetProperty(nameof(MenuItem.IsSuspendingPopupAnimation))!.SetMethod);

        foreach (var methodName in new[]
        {
            "OnChecked", "OnUnchecked", "OnSubmenuOpened", "OnSubmenuClosed", "OnClick",
        })
        {
            var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.NotNull(method);
            Assert.True(method!.IsFamily);
            Assert.True(method.IsVirtual);
        }

        var keys = new[]
        {
            MenuItem.SeparatorStyleKey, MenuItem.SubmenuHeaderTemplateKey,
            MenuItem.SubmenuItemTemplateKey, MenuItem.TopLevelHeaderTemplateKey,
            MenuItem.TopLevelItemTemplateKey,
        };
        Assert.All(keys, key => Assert.IsAssignableFrom<ResourceKey>(key));
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void MenuItem_Click_RaisesStateEventsAndExecutesCommandOnlyWhenAllowed()
    {
        var command = new TestCommand { CanRun = false };
        var item = new ProbeMenuItem
        {
            IsCheckable = true,
            Command = command,
            CommandParameter = "payload",
        };

        Assert.False(item.IsEnabled);
        command.CanRun = true;
        command.RaiseCanExecuteChanged();
        Assert.True(item.IsEnabled);

        var clickCount = 0;
        item.Click += (_, _) => clickCount++;
        item.InvokeClick();

        Assert.True(item.IsChecked);
        Assert.Equal(1, item.CheckedCalls);
        Assert.Equal(1, clickCount);
        Assert.Equal(1, command.ExecuteCount);
        Assert.Equal("payload", command.LastParameter);

        item.IsChecked = false;
        Assert.Equal(1, item.UncheckedCalls);
    }

    [Fact]
    public void MenuItem_RoutedCommand_UsesExplicitCommandTarget()
    {
        var target = new Border();
        var command = new RoutedCommand("MenuCommand", typeof(MenuContextWpfParityTests));
        object parameter = new();
        object? executedParameter = null;

        target.CommandBindings.Add(new CommandBinding(
            command,
            (_, e) => executedParameter = e.Parameter,
            (_, e) => e.CanExecute = ReferenceEquals(parameter, e.Parameter)));

        var item = new ProbeMenuItem
        {
            CommandTarget = target,
            CommandParameter = parameter,
            Command = command,
        };

        Assert.True(item.IsEnabled);
        item.InvokeClick();
        Assert.Same(parameter, executedParameter);
    }

    [Fact]
    public void MenuItem_ItemContainerTemplateSelector_IsOverridableAndCreatesSubmenuContainer()
    {
        Assert.True(typeof(ItemContainerTemplateSelector).IsAbstract);
        Assert.True(typeof(ItemContainerTemplateSelector).GetMethod(nameof(ItemContainerTemplateSelector.SelectTemplate))!.IsVirtual);

        var template = new Jalium.UI.Controls.ItemContainerTemplate();
        template.SetVisualTree(() => new MenuItem { Header = "generated" });
        var selector = new TestContainerTemplateSelector(template);
        var item = new MenuItem
        {
            UsesItemContainerTemplate = true,
            ItemContainerTemplateSelector = selector,
        };
        item.Items.Add(new object());

        item.IsSubmenuOpen = true;

        Assert.Equal(1, selector.CallCount);
        Assert.Same(item, selector.LastParent);
    }

    [Fact]
    public void ContextMenu_PlacementSurface_ForwardsToPopupAndUsesVirtualEventHooks()
    {
        CustomPopupPlacementCallback callback = (_, _, _) => [];
        var menu = new ProbeContextMenu
        {
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = callback,
            PlacementRectangle = new Rect(1, 2, 30, 40),
            HasDropShadow = true,
        };
        menu.Items.Add(new MenuItem { Header = "Open" });

        menu.IsOpen = true;

        var popup = Assert.IsType<Popup>(typeof(ContextMenu)
            .GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(menu));
        Assert.Same(callback, popup.CustomPopupPlacementCallback);
        Assert.Equal(menu.PlacementRectangle, popup.PlacementRectangle);
        Assert.True(popup.AllowsTransparency);
        Assert.Equal(1, menu.OpenedCalls);

        menu.IsOpen = false;
        Assert.Equal(1, menu.ClosedCalls);
    }

    private sealed class ProbeMenuItem : MenuItem
    {
        public int CheckedCalls { get; private set; }
        public int UncheckedCalls { get; private set; }

        public void InvokeClick() => OnClick();

        protected override void OnChecked(RoutedEventArgs e)
        {
            CheckedCalls++;
            base.OnChecked(e);
        }

        protected override void OnUnchecked(RoutedEventArgs e)
        {
            UncheckedCalls++;
            base.OnUnchecked(e);
        }
    }

    private sealed class ProbeContextMenu : ContextMenu
    {
        public int OpenedCalls { get; private set; }
        public int ClosedCalls { get; private set; }

        protected override void OnOpened(RoutedEventArgs e)
        {
            OpenedCalls++;
            base.OnOpened(e);
        }

        protected override void OnClosed(RoutedEventArgs e)
        {
            ClosedCalls++;
            base.OnClosed(e);
        }
    }

    private sealed class TestContainerTemplateSelector(Jalium.UI.Controls.ItemContainerTemplate template)
        : ItemContainerTemplateSelector
    {
        public int CallCount { get; private set; }
        public ItemsControl? LastParent { get; private set; }

        public override DataTemplate? SelectTemplate(object? item, ItemsControl parentItemsControl)
        {
            CallCount++;
            LastParent = parentItemsControl;
            return template;
        }
    }

    private sealed class TestCommand : ICommand
    {
        public bool CanRun { get; set; }
        public int ExecuteCount { get; private set; }
        public object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => CanRun;

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
