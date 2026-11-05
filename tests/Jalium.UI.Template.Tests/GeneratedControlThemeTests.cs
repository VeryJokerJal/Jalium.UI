using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class GeneratedControlThemeTests
{
    [Fact]
    public void ACompiledLibraryThemeResolvesSourcesRelativeToItsOwnDocument()
    {
        var themes = new GeneratedControlThemes();
        Assert.True(themes.TryGetValue(typeof(GeneratedLibraryControl), out var resource));
        Assert.IsType<Style>(resource);
        ThemeManager.RegisterControlThemes(themes);
        var control = new GeneratedLibraryControl();
        control.UpdateDefaultStyle();
        Assert.NotNull(control.Template);
        control.ApplyTemplate();
        var root = Assert.IsType<Border>(control.FindName("PART_Root"));
        Assert.Equal(.75, root.Opacity);
        Assert.Equal(19, Jalium.UI.Documents.TextElement.GetFontSize(root));
        Assert.True(GeneratedControlProperties.GetEnabled(control));
        Assert.IsType<ColumnDefinition>(control.FindName("PART_Column"));
        Assert.IsType<Image>(control.FindName("PART_Logo"));
        Assert.True(VisualStateManager.GoToState(control, "Ready", false));
        Assert.Equal("Ready", VisualStateManager.GetCurrentStateName(control, "TestStates"));
        Assert.Equal(.5, root.Opacity);
        Assert.True(VisualStateManager.GoToState(control, "Active", false));
        Assert.Equal("Active", VisualStateManager.GetCurrentStateName(control, "OtherStates"));
        Assert.True(VisualStateManager.GoToState(control, "Empty", false));
        Assert.Equal(.75, root.Opacity);
        Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(root, "TestStates"));
        control.Template = null;
        Assert.Null(VisualStateManager.GetCurrentStateName(root, "TestStates"));
        Assert.Null(control.FindName("PART_Column"));
        Assert.Null(control.FindName("PART_Logo"));
    }

    [Fact]
    public void RuntimeTemplatesAttachEveryDeclaredVisualStateGroup()
    {
        var root = Assert.IsType<Border>(Jalium.UI.Markup.XamlReader.Parse("""
            <Border xmlns="http://schemas.jalium.ui/2024" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <VisualStateManager.VisualStateGroups>
                    <VisualStateGroup x:Name="First"><VisualState x:Name="One" /></VisualStateGroup>
                    <VisualStateGroup x:Name="Second"><VisualState x:Name="Two" /></VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
            </Border>
            """));
        Assert.True(VisualStateManager.GoToElementState(root, "One", false));
        Assert.True(VisualStateManager.GoToElementState(root, "Two", false));
        Assert.Equal("One", VisualStateManager.GetCurrentStateName(root, "First"));
        Assert.Equal("Two", VisualStateManager.GetCurrentStateName(root, "Second"));
    }

    [Fact]
    public void CompiledTemplateRetainsNamespacesForCustomAncestorBindings()
    {
        ThemeManager.RegisterControlThemes(new GeneratedControlThemes());
        var control = new GeneratedLibraryControl { Tag = "First" };
        control.Measure(new Size(400, 200));
        control.Arrange(new Rect(0, 0, 400, 200));
        foreach (var name in new[] { "PART_AncestorText", "PART_TypedAncestorText" })
        {
            var text = Assert.IsType<TextBlock>(control.FindName(name));
            Assert.Equal(typeof(GeneratedLibraryControl), text.GetBindingExpression(TextBlock.TextProperty)!.ParentBinding.RelativeSource!.AncestorType);
            Assert.Equal("First", text.Text);
        }
        control.Tag = "Updated";
        foreach (var name in new[] { "PART_AncestorText", "PART_TypedAncestorText" })
            Assert.Equal("Updated", Assert.IsType<TextBlock>(control.FindName(name)).Text);
    }

    [Theory]
    [InlineData("local:GeneratedLibraryControl")]
    [InlineData("{x:Type local:GeneratedLibraryControl}")]
    public void RuntimeTemplateResolvesTheCustomAncestorNamespace(string ancestorType)
    {
        var template = Assert.IsType<ControlTemplate>(Jalium.UI.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.jalium.ui/2024"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:local="clr-namespace:Jalium.UI.Tests;assembly=Jalium.UI.Template.Tests"
                             TargetType="local:GeneratedLibraryControl">
                <TextBlock x:Name="value" Text="{Binding Tag, RelativeSource={RelativeSource AncestorType=__ANCESTOR__}}" />
            </ControlTemplate>
            """.Replace("__ANCESTOR__", ancestorType)));

        var control = new GeneratedLibraryControl { Template = template, Tag = "Runtime" };
        control.Measure(new Size(100, 40));
        Assert.Equal("Runtime", Assert.IsType<TextBlock>(control.FindName("value")).Text);
    }

    [Fact]
    public void ADeferredNamespaceSnapshotSurvivesParentScopeChanges()
    {
        var context = Jalium.UI.Markup.XamlBuilder.BeginComponent(new Border(), sourceAssembly: typeof(GeneratedLibraryControl).Assembly);
        var previous = Jalium.UI.Markup.XamlBuilder.PushNamespaces(context, ["local"], ["clr-namespace:Jalium.UI.Tests"]);
        var snapshot = Jalium.UI.Markup.XamlBuilder.CreateTemplateContext(context);
        Jalium.UI.Markup.XamlBuilder.PushNamespaces(context, ["local"], ["clr-namespace:Jalium.UI.Controls;assembly=Jalium.UI.Controls"]);
        Jalium.UI.Markup.XamlBuilder.PopNamespaces(context, previous);
        var text = new TextBlock();
        Jalium.UI.Markup.XamlBuilder.SetCompiledBinding(text, "Text", "Tag", ["RelativeSource"],
            ["{RelativeSource AncestorType=local:GeneratedLibraryControl}"], snapshot);
        Assert.Equal(typeof(GeneratedLibraryControl), text.GetBindingExpression(TextBlock.TextProperty)!.ParentBinding.RelativeSource!.AncestorType);
    }
}

public partial class GeneratedControlThemes : ResourceDictionary
{
    public GeneratedControlThemes() => InitializeComponent();
}

public sealed class GeneratedLibraryControl : Control { }

public static class GeneratedControlProperties
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(GeneratedControlProperties), new PropertyMetadata(false));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty)!;
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
}
