using System.Collections.ObjectModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using static Jalium.UI.Tests.VisualTreeTestHelpers;

namespace Jalium.UI.Tests;

/// <summary>
/// WPF parity: a user Style — explicit or implicit, with or without BasedOn — layers ON TOP
/// of the theme default style. Properties the user style does not set (most critically the
/// control Template) keep falling back to the theme defaults, so styling a control never
/// makes it lose its visual tree. (Regression: an explicit Style used to remove the theme
/// style including its Template setter, so styled controls rendered blank — and, on the
/// virtualization path, the resulting template teardown stranded the cached
/// VisualChildrenCount and crashed. See <see cref="StaleVisualChildrenCountRegressionTests"/>.)
/// </summary>
[Collection("Application")]
public class ImplicitStyleWpfBehaviorTests
{
    [Fact]
    public void ExplicitStyle_WithoutTemplateSetter_KeepsThemeTemplate()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var styled = new Button { Content = "styled" };
            styled.Style = new Style(typeof(Button)); // empty style — no setters at all

            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(styled);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.NotNull(styled.Template); // an explicit Style must not strip the theme template
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ExplicitStyle_SetterOverridesThemeSetter_OthersFallBack()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var explicitBackground = new SolidColorBrush(Color.FromRgb(0x17, 0x28, 0x3D));
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, explicitBackground));

            var styled = new Button { Content = "styled", Style = style };
            var plain = new Button { Content = "plain" };

            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(styled);
            host.Children.Add(plain);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            // Explicit style setter beats the theme setter for the property it sets…
            Assert.Same(explicitBackground, styled.Background);
            // …while unset properties still come from the theme defaults.
            Assert.NotNull(styled.Template);
            Assert.Same(plain.Template, styled.Template);
            Assert.Equal(plain.MinHeight, styled.MinHeight);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // The reported scenario, stated as the positive semantic guarantee: an explicit
    // ItemContainerStyle with no BasedOn keeps the container's theme template (not blank).
    [Fact]
    public void ListBoxItem_ExplicitItemContainerStyleNoBasedOn_KeepsThemeTemplate_HeightWins()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var overrideStyle = new Style(typeof(ListBoxItem));
            overrideStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 24.0));

            var items = new ObservableCollection<string>();
            for (var i = 0; i < 50; i++) items.Add($"Item {i}");
            var lb = new ListBox
            {
                Width = 320,
                Height = 240,
                ItemsSource = items,
                ItemContainerStyle = overrideStyle,
            };

            lb.Measure(new Size(320, 240));
            lb.Arrange(new Rect(0, 0, 320, 240));

            Assert.Null(overrideStyle.BasedOn); // no BasedOn
            var container = lb.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.NotNull(container);
            Assert.NotNull(((Control)container!).Template);              // theme template kept as bottom layer
            Assert.NotNull(FindDescendant<ContentPresenter>(container)); // content site exists — not blank
            Assert.Equal(24.0, ((FrameworkElement)container).Height);    // explicit Height setter wins
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // A theme swap after an explicit style is set must re-resolve the theme bottom layer
    // (exercising the now-unguarded resource-broadcast walk + template teardown/rebuild)
    // without crashing, while the explicit top layer survives.
    [Fact]
    public void ExplicitStyleNoBasedOn_ThemeSwap_KeepsTemplate_AndExplicitSetter()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var startTheme = ThemeManager.CurrentTheme;
            var otherTheme = startTheme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;

            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 24.0));
            var item = new ListBoxItem { Content = "x", Style = style };

            var host = new StackPanel { Width = 320, Height = 240 };
            host.Children.Add(item);
            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            Assert.NotNull(item.Template);
            Assert.Equal(24.0, item.Height);

            var ex = Record.Exception(() =>
            {
                ThemeManager.ApplyTheme(otherTheme);
                host.Measure(new Size(320, 240));
                host.Arrange(new Rect(0, 0, 320, 240));
            });
            Assert.Null(ex);

            Assert.Equal(otherTheme, ThemeManager.CurrentTheme); // swap actually happened
            Assert.NotNull(item.Template);                       // theme bottom layer re-resolved
            Assert.Equal(24.0, item.Height);                     // explicit top layer preserved
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
