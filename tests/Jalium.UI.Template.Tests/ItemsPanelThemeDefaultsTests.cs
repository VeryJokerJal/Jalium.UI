using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ItemsPanelThemeDefaultsTests
{
    public ItemsPanelThemeDefaultsTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("TreeView")]
    [InlineData("TreeSelector")]
    public void ThemePanelsCanReplaceTheDefaultAndExplicitLocalPanelsStillWin(string kind)
    {
        ItemsControl control = kind switch { "TreeView" => new TreeView(), "TreeSelector" => new TreeSelector(), _ => new ListBox() };
        var original = control.ItemsPanel; Assert.NotNull(original);
        var horizontal = new ItemsPanelTemplate(); horizontal.SetVisualTree(() => new VirtualizingStackPanel { Orientation = Orientation.Horizontal, Spacing = 7 });
        var style = new Style(control.GetType()); style.Setters.Add(new Setter(ItemsControl.ItemsPanelProperty, horizontal));
        control.Style = style;
        Assert.Same(horizontal, control.ItemsPanel);
        Assert.Same(DependencyProperty.UnsetValue, control.ReadLocalValue(ItemsControl.ItemsPanelProperty));
        var panel = Assert.IsType<VirtualizingStackPanel>(control.ItemsPanel!.CreatePanel()); Assert.Equal(Orientation.Horizontal, panel.Orientation); Assert.Equal(7, panel.Spacing);
        var fallback = Assert.IsType<VirtualizingStackPanel>(typeof(ItemsControl).GetMethod("CreateItemsPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, null));
        Assert.Equal(Orientation.Horizontal, fallback.Orientation); Assert.Equal(7, fallback.Spacing); Assert.NotSame(panel, fallback);
        var local = new ItemsPanelTemplate { PanelType = typeof(StackPanel) }; control.ItemsPanel = local;
        Assert.Same(local, control.ItemsPanel);
        control.ClearValue(ItemsControl.ItemsPanelProperty); Assert.Same(horizontal, control.ItemsPanel);
        control.Style = null; Assert.Same(original, control.ItemsPanel);
        Assert.IsType<VirtualizingStackPanel>(control.ItemsPanel!.CreatePanel());
    }

    [Fact]
    public void DerivedMetadataCanSupplyItsOwnPanelWithoutAConstructorOverride()
    {
        var control = new HorizontalListBox();
        Assert.Same(DependencyProperty.UnsetValue, control.ReadLocalValue(ItemsControl.ItemsPanelProperty));
        var panel = Assert.IsType<VirtualizingStackPanel>(control.ItemsPanel!.CreatePanel());
        Assert.Equal(Orientation.Horizontal, panel.Orientation); Assert.Equal(5, panel.Spacing);
        var fallback = Assert.IsType<VirtualizingStackPanel>(typeof(ItemsControl).GetMethod("CreateItemsPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, null));
        Assert.Equal(Orientation.Horizontal, fallback.Orientation); Assert.Equal(5, fallback.Spacing);
    }

    private sealed class HorizontalListBox : ListBox
    {
        static HorizontalListBox()
        {
            var template = new ItemsPanelTemplate(); template.SetVisualTree(() => new VirtualizingStackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }); template.Seal();
            ItemsPanelProperty.OverrideMetadata(typeof(HorizontalListBox), new PropertyMetadata(template));
        }
    }
}
