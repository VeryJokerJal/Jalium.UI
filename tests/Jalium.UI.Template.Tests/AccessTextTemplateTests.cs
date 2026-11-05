using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class AccessTextTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplatePublishesParsedTextAndHasOptionalTextPart()
    {
        var text = Create("_打开 __文件");
        Assert.IsAssignableFrom<Control>(text);
        var part = Assert.IsType<TextBlock>(text.FindName("PART_TextDisplay"));
        Assert.Equal("打开 _文件", text.DisplayText);
        Assert.Equal(text.DisplayText, part.Text);
        Assert.Equal('打', text.AccessKey);
        Assert.Equal(0, text.AccessKeyIndex);
        Assert.True(text.DesiredSize.Width > 0);
        Assert.Throws<InvalidOperationException>(() => text.SetValue(AccessText.DisplayTextProperty, "override"));
        text.Text = "plain";
        Assert.Equal(-1, text.AccessKeyIndex);
        Assert.Equal("plain", part.Text);
    }

    [Fact]
    public void TextAndTypographyBindingsSurviveTemplateReplacement()
    {
        var model = new Model { Text = "_Save" };
        var text = Create("");
        BindingOperations.SetBinding(text, AccessText.TextProperty, new Binding(nameof(Model.Text)) { Source = model });
        var expression = BindingOperations.GetBindingExpression(text, AccessText.TextProperty);
        var retired = Assert.IsType<TextBlock>(text.FindName("PART_TextDisplay"));
        var template = new ControlTemplate(typeof(AccessText));
        template.SetVisualTree(() => new Border());
        text.Template = template; Layout(text);
        Assert.Null(text.FindName("PART_TextDisplay"));
        var retiredText = retired.Text;
        model.Text = "_Open";
        Assert.Equal("Open", text.DisplayText);
        Assert.Equal(retiredText, retired.Text);
        text.ClearValue(Control.TemplateProperty); Layout(text);
        Assert.Equal("Open", Assert.IsType<TextBlock>(text.FindName("PART_TextDisplay")).Text);
        Assert.Same(expression, BindingOperations.GetBindingExpression(text, AccessText.TextProperty));
    }

    [Fact]
    public void InheritedTypographyAndForegroundReachTheThemePart()
    {
        var text = Create("_Read");
        var parent = new Button { FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Brushes.Blue, Content = text };
        parent.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(parent);
        var part = Assert.IsType<TextBlock>(text.FindName("PART_TextDisplay"));
        Assert.Equal(28, text.FontSize);
        Assert.Equal(28, part.FontSize);
        Assert.Equal(FontWeights.Bold, part.FontWeight);
        Assert.Same(Brushes.Blue, part.Foreground);
        parent.Foreground = Brushes.Red;
        Assert.Same(Brushes.Red, part.Foreground);
        parent.FontSize = 32; Layout(parent);
        Assert.Equal(32, part.FontSize);
        Assert.True(part.RenderSize.Height >= 32);
        text.Foreground = Brushes.Lime;
        Assert.Same(Brushes.Lime, part.Foreground);
    }

    [Fact]
    public void WrappingAndZeroFontUseTheTemplatePart()
    {
        var text = Create("_A long label with several words and 中文内容");
        text.Width = 100; text.FontSize = 24; text.TextWrapping = TextWrapping.Wrap;
        Layout(text);
        var part = Assert.IsType<TextBlock>(text.FindName("PART_TextDisplay"));
        Assert.True(part.RenderSize.Height > 24);
        text.FontSize = 0; Layout(text);
        Assert.Equal(0, part.FontSize);
        text.FontSize = 16; Layout(text);
        Assert.Equal(16, part.FontSize);
    }
    private static AccessText Create(string value)
    {
        var text = new AccessText { Text = value };
        text.Resources.MergedDictionaries.Add(Defaults.Value); Layout(text); return text;
    }
    private static void Layout(UIElement element)
    { element.Measure(new Size(200, double.PositiveInfinity)); element.Arrange(new Rect(0, 0, 200, element.DesiredSize.Height)); element.UpdateLayout(); }
    private sealed class Model : INotifyPropertyChanged
    {
        private string _text = "";
        public string Text { get => _text; set { _text = value; PropertyChanged?.Invoke(this, new(nameof(Text))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
