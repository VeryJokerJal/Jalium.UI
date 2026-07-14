using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Shell;
using ControlsItemContainerTemplate = Jalium.UI.Controls.ItemContainerTemplate;
using ControlsItemsPanelTemplate = Jalium.UI.Controls.ItemsPanelTemplate;

namespace Jalium.UI.Tests;

#pragma warning disable WPF0001 // Window.ThemeMode intentionally mirrors WPF's experimental API.

public sealed class ControlsShellFinalGapTests
{
    [Fact]
    public void WindowExposesCanonicalShellDependencyPropertiesEventsAndHooks()
    {
        AssertField<DependencyProperty>(typeof(Window), nameof(Window.AllowsTransparencyProperty));
        AssertField<DependencyProperty>(typeof(Window), nameof(Window.IconProperty));
        AssertField<DependencyProperty>(typeof(Window), nameof(Window.IsActiveProperty));
        AssertField<DependencyProperty>(typeof(Window), nameof(Window.TaskbarItemInfoProperty));
        AssertField<RoutedEvent>(typeof(Window), nameof(Window.DpiChangedEvent));
        Assert.True(Window.IsActiveProperty.ReadOnly);
        Assert.Same(Window.IconProperty, Window.WindowIconProperty);

        Assert.Equal(typeof(ImageSource), typeof(Window).GetProperty(nameof(Window.Icon))!.PropertyType);
        Assert.Equal(typeof(TaskbarItemInfo), typeof(Window).GetProperty(nameof(Window.TaskbarItemInfo))!.PropertyType);
        Assert.Equal(typeof(ThemeMode), typeof(Window).GetProperty(nameof(Window.ThemeMode))!.PropertyType);

        PropertyInfo logicalChildren = typeof(Window).GetProperty(
            "LogicalChildren",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        Assert.Equal(typeof(IEnumerator), logicalChildren.PropertyType);
        Assert.True(logicalChildren.GetMethod!.IsFamilyOrAssembly);
        Assert.True(logicalChildren.GetMethod.IsVirtual);

        MethodInfo boundaryFeedback = typeof(Window).GetMethod(
            "OnManipulationBoundaryFeedback",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(ManipulationBoundaryFeedbackEventArgs)],
            modifiers: null)!;
        Assert.True(boundaryFeedback.IsFamily);
        Assert.True(boundaryFeedback.IsVirtual);
        Assert.Equal(typeof(UIElement), boundaryFeedback.GetBaseDefinition().DeclaringType);

        var window = new ProbeWindow
        {
            AllowsTransparency = true,
            Icon = new DrawingImage(),
            TaskbarItemInfo = new TaskbarItemInfo(),
            ThemeMode = ThemeMode.Dark,
            Content = "content",
        };
        Assert.True(window.AllowsTransparency);
        Assert.Same(window.Icon, window.WindowIcon);
        Assert.Same(window.TaskbarItemInfo, window.GetValue(Window.TaskbarItemInfoProperty));
        Assert.Equal(ThemeMode.Dark, window.ThemeMode);
        Assert.Contains("content", window.GetLogicalChildren());

        DpiChangedEventArgs? observed = null;
        window.DpiChanged += (_, e) => observed = e;
        window.RaiseDpiChanged(new DpiScale(1, 1), new DpiScale(1.5, 1.5));
        Assert.NotNull(observed);
        Assert.Same(Window.DpiChangedEvent, observed.RoutedEvent);
    }

    [Fact]
    public void PrintDialogUsesNullableUnsignedAndSelectionContracts()
    {
        Assert.Equal(typeof(uint), typeof(PrintDialog).GetProperty(nameof(PrintDialog.MinPage))!.PropertyType);
        Assert.Equal(typeof(uint), typeof(PrintDialog).GetProperty(nameof(PrintDialog.MaxPage))!.PropertyType);
        Assert.Equal(typeof(bool), typeof(PrintDialog).GetProperty(nameof(PrintDialog.SelectedPagesEnabled))!.PropertyType);
        Assert.Equal(
            typeof(bool?),
            typeof(PrintDialog).GetMethod(nameof(PrintDialog.ShowDialog), Type.EmptyTypes)!.ReturnType);

        var dialog = new PrintDialog
        {
            MinPage = 0,
            MaxPage = 40,
            SelectedPagesEnabled = true,
        };
        Assert.Equal(1u, dialog.MinPage);
        Assert.Equal(40u, dialog.MaxPage);
        Assert.True(dialog.SelectedPagesEnabled);

        ConstructorInfo serializationConstructor = typeof(PrintDialogException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(SerializationInfo), typeof(StreamingContext)],
            modifiers: null)!;
        Assert.True(serializationConstructor.IsFamily);
    }

    [Fact]
    public void ScrollContentPresenterOwnsContentScrollAndFindsItsAdornerLayer()
    {
        Assert.Equal(
            "Jalium.UI.Controls.ScrollContentPresenter",
            typeof(ScrollContentPresenter).FullName);
        AssertField<DependencyProperty>(
            typeof(ScrollContentPresenter),
            nameof(ScrollContentPresenter.CanContentScrollProperty));
        Assert.Same(ScrollViewer.CanContentScrollProperty, ScrollContentPresenter.CanContentScrollProperty);
        Assert.Equal(
            typeof(bool),
            typeof(ScrollContentPresenter).GetProperty(nameof(ScrollContentPresenter.CanContentScroll))!.PropertyType);
        Assert.Equal(
            typeof(AdornerLayer),
            typeof(ScrollContentPresenter).GetProperty(nameof(ScrollContentPresenter.AdornerLayer))!.PropertyType);

        var presenter = new ScrollContentPresenter { CanContentScroll = true };
        var decorator = new AdornerDecorator { Child = presenter };
        Assert.True(ScrollViewer.GetCanContentScroll(presenter));
        Assert.Same(decorator.AdornerLayer, presenter.AdornerLayer);
    }

    [Fact]
    public void StatusBarUsesContainerTemplateContractsAndSeparatorKey()
    {
        Assert.Equal("Jalium.UI.Controls.Primitives.StatusBar", typeof(StatusBar).FullName);
        Assert.Equal("Jalium.UI.Controls.Primitives.StatusBarItem", typeof(StatusBarItem).FullName);
        Assert.Null(typeof(StatusBar).Assembly.GetType("Jalium.UI.Controls.StatusBar"));
        Assert.Null(typeof(StatusBar).Assembly.GetType("Jalium.UI.Controls.StatusBarItem"));
        AssertField<DependencyProperty>(typeof(StatusBar), nameof(StatusBar.ItemContainerTemplateSelectorProperty));
        AssertField<DependencyProperty>(typeof(StatusBar), nameof(StatusBar.UsesItemContainerTemplateProperty));
        Assert.Same(MenuBase.ItemContainerTemplateSelectorProperty, StatusBar.ItemContainerTemplateSelectorProperty);
        Assert.Same(MenuBase.UsesItemContainerTemplateProperty, StatusBar.UsesItemContainerTemplateProperty);
        Assert.Equal(typeof(ResourceKey), typeof(StatusBar).GetProperty(nameof(StatusBar.SeparatorStyleKey))!.PropertyType);
        Assert.Null(typeof(StatusBar).GetProperty("SeparatorBrush", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(StatusBar).GetField("SeparatorBrushProperty", BindingFlags.Public | BindingFlags.Static));
        Assert.Null(typeof(StatusBarItem).GetProperty("Separator", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(StatusBarItem).GetField("SeparatorProperty", BindingFlags.Public | BindingFlags.Static));

        var template = new ControlsItemContainerTemplate();
        template.SetVisualTree(static () => new Jalium.UI.Controls.Primitives.StatusBarItem());
        var statusBar = new ProbeStatusBar
        {
            UsesItemContainerTemplate = true,
            ItemContainerTemplateSelector = new FixedContainerTemplateSelector(template),
        };

        Assert.IsType<Jalium.UI.Controls.Primitives.StatusBarItem>(statusBar.CreateContainer("ready"));
        ComponentResourceKey separatorKey = Assert.IsType<ComponentResourceKey>(StatusBar.SeparatorStyleKey);
        Assert.Equal(typeof(StatusBar), separatorKey.TypeInTargetAssembly);
        Assert.Equal(nameof(StatusBar.SeparatorStyleKey), separatorKey.ResourceId);
        Assert.Same(typeof(StatusBar), Jalium.UI.Markup.XamlTypeRegistry.GetType(nameof(StatusBar)));
        Assert.Same(typeof(StatusBarItem), Jalium.UI.Markup.XamlTypeRegistry.GetType(nameof(StatusBarItem)));
    }

    [Fact]
    public void ItemsPresenterNotifiesTheWpfTemplateChangeHook()
    {
        MethodInfo method = typeof(ItemsPresenter).GetMethod(
            "OnTemplateChanged",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(ControlsItemsPanelTemplate), typeof(ControlsItemsPanelTemplate)],
            modifiers: null)!;
        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);

        var presenter = new ProbeItemsPresenter();
        var oldTemplate = new ControlsItemsPanelTemplate { PanelType = typeof(StackPanel) };
        var newTemplate = new ControlsItemsPanelTemplate { PanelType = typeof(WrapPanel) };
        presenter.NotifyTemplateChanged(oldTemplate, newTemplate);
        Assert.Same(oldTemplate, presenter.OldTemplate);
        Assert.Same(newTemplate, presenter.NewTemplate);
    }

    private static void AssertField<T>(Type owner, string name)
    {
        FieldInfo field = owner.GetField(name, BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal(typeof(T), field.FieldType);
        Assert.True(field.IsInitOnly);
    }

    private sealed class ProbeWindow : Window
    {
        public object?[] GetLogicalChildren()
        {
            var values = new List<object?>();
            IEnumerator enumerator = LogicalChildren;
            while (enumerator.MoveNext())
            {
                values.Add(enumerator.Current);
            }

            return values.ToArray();
        }

        public void RaiseDpiChanged(DpiScale oldDpi, DpiScale newDpi) =>
            OnDpiChanged(new DpiChangedEventArgs(oldDpi, newDpi));
    }

    private sealed class ProbeStatusBar : StatusBar
    {
        public FrameworkElement CreateContainer(object item) => GetContainerForItem(item);
    }

    private sealed class FixedContainerTemplateSelector(ControlsItemContainerTemplate template)
        : ItemContainerTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object? item, ItemsControl parentItemsControl) => template;
    }

    private sealed class ProbeItemsPresenter : ItemsPresenter
    {
        public ControlsItemsPanelTemplate? OldTemplate { get; private set; }
        public ControlsItemsPanelTemplate? NewTemplate { get; private set; }

        protected override void OnTemplateChanged(
            ControlsItemsPanelTemplate? oldTemplate,
            ControlsItemsPanelTemplate? newTemplate)
        {
            OldTemplate = oldTemplate;
            NewTemplate = newTemplate;
            base.OnTemplateChanged(oldTemplate, newTemplate);
        }
    }
}

#pragma warning restore WPF0001
