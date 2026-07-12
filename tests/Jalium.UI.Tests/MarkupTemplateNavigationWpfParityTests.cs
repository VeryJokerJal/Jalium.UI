using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Navigation;

namespace Jalium.UI.Tests;

public sealed class MarkupTemplateNavigationWpfParityTests
{
    [Fact]
    public void ArrayAndTypeExtensionsExposeCanonicalConstructionAndContentContracts()
    {
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(ArrayExtension)));
        Assert.Equal(typeof(IList), typeof(ArrayExtension).GetProperty(nameof(ArrayExtension.Items))!.PropertyType);

        var arrayExtension = new ArrayExtension(new[] { 2, 4, 8 });
        Assert.Equal(typeof(int), arrayExtension.Type);
        Assert.Equal(new object[] { 2, 4, 8 }, arrayExtension.Items.Cast<object>());
        Assert.Equal(new[] { 2, 4, 8 }, Assert.IsType<int[]>(arrayExtension.ProvideValue(null!)));

        var strings = new ArrayExtension(typeof(string));
        strings.AddText("first");
        ((IAddChild)strings).AddChild("second");
        Assert.Equal(new[] { "first", "second" }, Assert.IsType<string[]>(strings.ProvideValue(null!)));

        var typeExtension = new TypeExtension(typeof(Button));
        Assert.Equal(typeof(Button), typeExtension.Type);
        Assert.Equal(typeof(Button), typeExtension.ProvideValue(null!));

        Assert.Throws<ArgumentNullException>(() => new ArrayExtension((Array)null!));
        Assert.Throws<ArgumentNullException>(() => new ArrayExtension((Type)null!));
        Assert.Throws<ArgumentNullException>(() => new TypeExtension((Type)null!));
    }

    [Fact]
    public void ControlTemplateHookHasWpfShapeAndReceivesTransitions()
    {
        MethodInfo hook = typeof(Control).GetMethod(
            "OnTemplateChanged",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            [typeof(ControlTemplate), typeof(ControlTemplate)],
            null)!;
        Assert.True(hook.IsFamily);
        Assert.True(hook.IsVirtual);

        var control = new ProbeControl();
        var first = new ControlTemplate();
        var second = new ControlTemplate();
        control.Template = first;
        control.Template = second;

        Assert.Equal(2, control.Changes.Count);
        Assert.Null(control.Changes[0].OldTemplate);
        Assert.Same(first, control.Changes[0].NewTemplate);
        Assert.Same(first, control.Changes[1].OldTemplate);
        Assert.Same(second, control.Changes[1].NewTemplate);
    }

    [Fact]
    public void NavigationServiceResolvesFrameForDescendants()
    {
        var frame = new Frame();
        var panel = new StackPanel();
        var child = new Border();
        panel.Children.Add(child);
        frame.Content = panel;

        Assert.Same(frame.NavigationService, NavigationService.GetNavigationService(frame));
        Assert.Same(frame.NavigationService, NavigationService.GetNavigationService(child));
        Assert.Null(NavigationService.GetNavigationService(new Border()));
        Assert.Throws<ArgumentNullException>(() => NavigationService.GetNavigationService(null!));
    }

    private sealed class ProbeControl : Control
    {
        public List<(ControlTemplate? OldTemplate, ControlTemplate? NewTemplate)> Changes { get; } = new();

        protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
        {
            Changes.Add((oldTemplate, newTemplate));
            base.OnTemplateChanged(oldTemplate, newTemplate);
        }
    }
}
