using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ContentDialogTemplateTests
{
    [Fact]
    public void DefaultTitleTypographyUsesAttachedTextPropertiesAndCustomTemplatesKeepTheirOwnFont()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        var defaults = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
        var dialog = new ContentDialog { Title = "Review document", Visibility = Visibility.Visible, CloseButtonText = "Close" };
        dialog.Resources.MergedDictionaries.Add(defaults);
        Layout(dialog);
        var presenter = Assert.IsType<ContentPresenter>(dialog.FindName("PART_TitleHost"));
        var text = Assert.IsType<TextBlock>(VisualTreeHelper.GetChild(presenter, 0));
        Assert.Equal(19, text.FontSize);
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        var custom = new DataTemplate();
        custom.SetVisualTree(() => new TextBlock { Text = "Custom title", FontSize = 11 });
        dialog.TitleTemplate = custom;
        Layout(dialog);
        Assert.Equal(11, Assert.IsType<TextBlock>(VisualTreeHelper.GetChild(presenter, 0)).FontSize);
        dialog.Title = null;
        Assert.Equal(Visibility.Collapsed, presenter.Visibility);
    }

    [Theory]
    [InlineData(245, 320)]
    [InlineData(390, 844)]
    [InlineData(960, 600)]
    public void DefaultCardFitsItsHostAndRestoresItsMinimumAfterGrowing(double width, double height)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        var defaults = ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
        var dialog = new ContentDialog
        {
            Title = "Review settings", Visibility = Visibility.Visible,
            PrimaryButtonText = "Apply", SecondaryButtonText = "Preview", CloseButtonText = "Cancel",
            Content = new TextBlock { Text = string.Join("\n", Enumerable.Repeat("A long document preview with wrapped content.", 50)), TextWrapping = TextWrapping.Wrap }
        };
        dialog.Resources.MergedDictionaries.Add(defaults);
        LayoutAt(width, height);
        var card = Assert.IsType<Border>(dialog.FindName("PART_DialogCard"));
        Assert.InRange(card.ActualWidth, 1, width - 48 + 1);
        Assert.InRange(card.ActualHeight, 1, height - 48 + 1);
        var content = Assert.IsType<ScrollViewer>(dialog.FindName("PART_ContentScrollViewer"));
        Assert.True(content.ActualHeight > 0);
        Assert.True(content.ScrollableHeight > 0);
        LayoutAt(960, 600);
        Assert.Equal(280, card.MinWidth);
        LayoutAt(245, 320);
        Assert.InRange(card.ActualWidth, 1, 198);
        Assert.InRange(card.ActualHeight, 1, 273);
        void LayoutAt(double w, double h)
        {
            dialog.Measure(new Size(w, h)); dialog.Arrange(new Rect(0, 0, w, h)); dialog.UpdateLayout();
        }
    }

    private static void Layout(UIElement element)
    {
        element.Measure(new Size(390, 844)); element.Arrange(new Rect(0, 0, 390, 844)); element.UpdateLayout();
    }
}
