using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ResourceMarkupExtensionTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void StaticResource_InSetter_ShouldResolveDuringXamlParse()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="StaticBrush" Color="#FF102030" />
              <Style TargetType="Border">
                <Setter Property="Background" Value="{StaticResource ResourceKey=StaticBrush}" />
              </Style>
            </ResourceDictionary>
            """;

        var dictionary = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));
        var style = Assert.IsType<Style>(dictionary[typeof(Border)]);
        var setter = Assert.Single(style.Setters);

        var resolvedBrush = Assert.IsType<SolidColorBrush>(setter.Value);
        var sourceBrush = Assert.IsType<SolidColorBrush>(dictionary["StaticBrush"]);

        Assert.Same(sourceBrush, resolvedBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0x10, 0x20, 0x30), resolvedBrush.Color);
    }

    [Fact]
    public void StaticResource_ObjectElementInResourceDictionary_ShouldResolveDuringXamlParse()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="StaticBrush" Color="#FF102030" />
              <StaticResource x:Key="AliasedBrush" ResourceKey="StaticBrush" />
            </ResourceDictionary>
            """;

        var dictionary = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));

        var sourceBrush = Assert.IsType<SolidColorBrush>(dictionary["StaticBrush"]);
        var aliasedBrush = Assert.IsType<SolidColorBrush>(dictionary["AliasedBrush"]);

        Assert.Same(sourceBrush, aliasedBrush);
    }

    [Fact]
    public void DynamicResource_InSetter_ShouldUpdateWhenResourceChanges()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            const string xaml = """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="DynamicBrush" Color="#FF112233" />
                  <Style TargetType="Border">
                    <Setter Property="Background" Value="{DynamicResource ResourceKey=DynamicBrush}" />
                  </Style>
                </ResourceDictionary>
                """;

            app.Resources = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));
            var style = Assert.IsType<Style>(app.Resources[typeof(Border)]);

            var border = new Border
            {
                Width = 100,
                Height = 30,
                Style = style
            };

            var root = new StackPanel();
            root.Children.Add(border);

            var window = new Window
            {
                Content = root
            };
            app.MainWindow = window;

            var initial = Assert.IsType<SolidColorBrush>(border.Background);
            Assert.Equal(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), initial.Color);

            var updatedBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x44, 0x55, 0x66));
            app.Resources["DynamicBrush"] = updatedBrush;

            var actual = Assert.IsType<SolidColorBrush>(border.Background);
            Assert.Same(updatedBrush, actual);
            Assert.Equal(Color.FromArgb(0xFF, 0x44, 0x55, 0x66), actual.Color);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DynamicResource_ObjectElementInSetterValue_ShouldUpdateWhenResourceChanges()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            const string xaml = """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <SolidColorBrush x:Key="DynamicBrush" Color="#FF112233" />
                  <Style TargetType="Border">
                    <Setter Property="Background">
                      <Setter.Value>
                        <DynamicResource ResourceKey="DynamicBrush" />
                      </Setter.Value>
                    </Setter>
                  </Style>
                </ResourceDictionary>
                """;

            app.Resources = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));
            var style = Assert.IsType<Style>(app.Resources[typeof(Border)]);

            var border = new Border
            {
                Width = 100,
                Height = 30,
                Style = style
            };

            var root = new StackPanel();
            root.Children.Add(border);

            var window = new Window
            {
                Content = root
            };
            app.MainWindow = window;

            var initial = Assert.IsType<SolidColorBrush>(border.Background);
            Assert.Equal(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), initial.Color);

            var updatedBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x44, 0x55, 0x66));
            app.Resources["DynamicBrush"] = updatedBrush;

            var actual = Assert.IsType<SolidColorBrush>(border.Background);
            Assert.Same(updatedBrush, actual);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
