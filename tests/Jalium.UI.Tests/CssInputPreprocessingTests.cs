using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssInputPreprocessingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StylesheetTextAndBytesFilterNullAndLineEndings(bool fromStream)
    {
        const string text = ".a\0b{width:61px}\r\n.c{width:62px}\f.d{width:63px}";
        var sheet = fromStream
            ? CssStyleSheet.FromStream(new MemoryStream(Encoding.UTF8.GetBytes(text)))
            : CssStyleSheet.Parse(text);

        foreach (var (className, width) in new[]
                 {
                     ("a\uFFFDb", 61d), ("c", 62d), ("d", 63d),
                 })
        {
            var border = new Border();
            Css.SetClass(border, className);
            Css.GetStyleSheets(border).Add(sheet);
            border.Measure(new(100, 100));
            border.UpdateLayout();
            Assert.Equal(width, border.Width);
        }
    }

    [Fact]
    public void UnpairedSurrogatesAreReplacedAndValidPairIsPreserved()
    {
        var sheet = CssStyleSheet.Parse(
            ".a\uD800b{width:64px}.v😀{width:65px}.c\uDC00d{width:66px}");
        foreach (var (className, width) in new[]
                 {
                     ("a\uFFFDb", 64d), ("v😀", 65d), ("c\uFFFDd", 66d),
                 })
        {
            var border = new Border();
            Css.SetClass(border, className);
            Css.GetStyleSheets(border).Add(sheet);
            border.Measure(new(100, 100));
            border.UpdateLayout();
            Assert.Equal(width, border.Width);
        }
    }

    [Fact]
    public void CrLfCrAndFormFeedCountAsSingleDiagnosticLines()
    {
        var sheet = CssStyleSheet.Parse(
            ".a{width:1px}\r\n.b{width:}\r.c{width:}\f.d{width:}");
        Assert.Equal([2, 3, 4], sheet.Diagnostics
            .Where(diagnostic => diagnostic.Message.Contains("empty value", StringComparison.Ordinal))
            .Select(diagnostic => diagnostic.Line));
    }

    [Fact]
    public void InlineDeclarationsUseTheSameInputFiltering()
    {
        var diagnostics = new List<CssParseDiagnostic>();
        var declarations = CssParser.ParseInlineDeclarations(
            "--x:\0;\r--y:\uD800;\f--z:😀;\r\nwidth:;", diagnostics);
        Assert.Equal(["\uFFFD", "\uFFFD", "😀"],
            declarations.Select(declaration => declaration.RawValue));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Line == 4 && diagnostic.Message.Contains("empty value", StringComparison.Ordinal));
    }
}
