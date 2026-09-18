using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssDocumentNodeTests
{
    static CssDocumentNodeTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private static void Flush(DependencyObject element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);

    [Fact]
    public void InlineStyles_ApplyToRunWithoutRewritingItsText()
    {
        var run = new Run("Original text");
        Css.SetStyle(run, "font-size:24px; font-weight:bold; color:#123456");
        Assert.Equal(24, run.FontSize);
        Assert.Equal(700, run.FontWeight.ToOpenTypeWeight());
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), Assert.IsType<SolidColorBrush>(run.Foreground).Color);
        Assert.Equal("Original text", run.Text);
    }

    [Fact]
    public void DocumentSheets_UseTheSameCascadeSelectorsAndVariables()
    {
        var first = new Run("one"); var second = new Run("two");
        var paragraph = new Paragraph(); paragraph.Inlines.Add(first); paragraph.Inlines.Add(second);
        var document = new FlowDocument(); document.Blocks.Add(paragraph);
        Css.SetStyleSheet(document, ":root { --text-size:22px } Run { font-size:var(--text-size) } Paragraph > Run:first-child {color:red} Run + Run {color:blue}");
        Flush(document);
        Assert.Equal(22, first.FontSize); Assert.Equal(22, second.FontSize);
        Assert.Equal(Color.FromRgb(255, 0, 0), Assert.IsType<SolidColorBrush>(first.Foreground).Color);
        Assert.Equal(Color.FromRgb(0, 0, 255), Assert.IsType<SolidColorBrush>(second.Foreground).Color);
    }

    [Fact]
    public void DocumentMutationAndClassChanges_ReevaluateStyles()
    {
        var paragraph = new Paragraph(); var first = new Run("one"); paragraph.Inlines.Add(first);
        var document = new FlowDocument(); document.Blocks.Add(paragraph);
        Css.SetStyleSheet(document, "Run:last-child {font-size:30px} Run.selected {font-weight:bold}");
        Flush(document); Assert.Equal(30, first.FontSize);
        var second = new Run("two"); paragraph.Inlines.Add(second); Flush(document);
        Assert.Equal(14, first.FontSize); Assert.Equal(30, second.FontSize);
        Css.SetClass(first, "selected"); Flush(document); Assert.Equal(700, first.FontWeight.ToOpenTypeWeight());
        paragraph.Inlines.Remove(second); Flush(document); Assert.Equal(30, first.FontSize); Assert.Equal(14, second.FontSize);
    }

    [Fact]
    public void LocalDocumentValues_RetainPrecedence()
    {
        var run = new Run("text") { FontSize = 18 };
        Css.SetStyle(run, "font-size:24px"); Assert.Equal(18, run.FontSize);
        run.ClearValue(TextElement.FontSizeProperty); Assert.Equal(24, run.FontSize);
        Css.SetStyle(run, ""); Assert.Equal(14, run.FontSize);
    }

    [Fact]
    public void DocumentCss_IsUsableFromXamlMarkup()
    {
        const string markup = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          Css.StyleSheet="Run {font-size:22px} .accent {color:red}">
                <Paragraph><Run Css.Class="accent" Text="Hello" /></Paragraph>
            </FlowDocument>
            """;
        var document = Assert.IsType<FlowDocument>(Jalium.UI.Markup.XamlReader.Parse(markup));
        Flush(document);
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Blocks));
        var run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines));
        Assert.Equal(22, run.FontSize);
        Assert.Equal(Color.FromRgb(255, 0, 0), Assert.IsType<SolidColorBrush>(run.Foreground).Color);
    }

    [Fact]
    public void ADocumentBinding_RemainsAttachedWhileCssChanges()
    {
        var run = new Run("text");
        var source = new FontSource();
        run.SetBinding(TextElement.FontSizeProperty, new Jalium.UI.Data.Binding(nameof(FontSource.Size)) { Source = source });
        Css.SetStyle(run, "font-size:32px");
        Assert.Equal(18, run.FontSize);
        source.Size = 25; Assert.Equal(25, run.FontSize);
        Css.SetStyle(run, "font-size:12px"); Assert.Equal(25, run.FontSize);
    }

    private sealed class FontSource : System.ComponentModel.INotifyPropertyChanged
    {
        private double _size = 18;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public double Size { get => _size; set { _size = value; PropertyChanged?.Invoke(this, new(nameof(Size))); } }
    }

    [Fact]
    public void TextBlockInlines_InheritScopedStylesAndDetachCleanly()
    {
        var block = new Jalium.UI.Controls.TextBlock();
        var run = new Run("hello"); block.Inlines.Add(run);
        Css.SetStyleSheet(block, "Run {font-size:24px; color:red}"); Flush(block);
        Assert.Equal(24, run.FontSize);
        Assert.Same(block, ((FrameworkContentElement)run).Parent);
        block.Inlines.Remove(run); Flush(block);
        Assert.Null(((FrameworkContentElement)run).Parent);
        Assert.Equal(14, run.FontSize);
    }
}
