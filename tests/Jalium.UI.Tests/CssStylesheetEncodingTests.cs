using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssStylesheetEncodingTests
{
    private sealed class Resources(Dictionary<string, (byte[] Bytes, string? ContentType)> files)
        : ICssResourceResolver
    {
        public List<Uri> Requests { get; } = [];

        public ValueTask<CssResource> ResolveAsync(Uri uri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(uri);
            var resource = files[uri.ToString()];
            return ValueTask.FromResult(new CssResource(uri,
                new MemoryStream(resource.Bytes), resource.ContentType));
        }
    }

    [Fact]
    public async Task BomTransportAndCharsetDeclarationDecodeInPriorityOrder()
    {
        static byte[] WithBom(Encoding encoding, string text) =>
            [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
        var cp1252 = Encoding.GetEncoding(1252);
        var resources = new Resources(new()
        {
            ["https://example.test/bom.css"] =
                (WithBom(Encoding.UTF8, ".café {width:31px}"),
                    "text/css; charset=windows-1252"),
            ["https://example.test/transport.css"] =
                (cp1252.GetBytes("@charset \"utf-8\"; .café {width:32px}"),
                    "text/css; charset=windows-1252"),
            ["https://example.test/declared.css"] =
                (cp1252.GetBytes("@charset \"windows-1252\"; .café {width:33px}"),
                    "text/css"),
            ["https://example.test/alias.css"] =
                (cp1252.GetBytes("@charset \"iso-8859-1\"; .euro€ {width:34px}"),
                    null),
            ["https://example.test/utf16.css"] =
                (WithBom(Encoding.Unicode, ".café {width:35px}"),
                    "text/css; charset=windows-1252"),
        });

        foreach (var (name, className, width) in new[]
                 {
                     ("bom", "café", 31d), ("transport", "café", 32d),
                     ("declared", "café", 33d), ("alias", "euro€", 34d),
                     ("utf16", "café", 35d),
                 })
        {
            var sheet = await CssStyleSheet.LoadAsync(
                new Uri($"https://example.test/{name}.css"), resources);
            var border = new Border();
            Css.SetClass(border, className);
            Css.GetStyleSheets(border).Add(sheet);
            border.Measure(new(100, 100));
            border.UpdateLayout();
            Assert.Equal(width, border.Width);
        }
        Assert.Equal(5, resources.Requests.Count);
    }

    [Fact]
    public async Task ImportUsesItsParentStylesheetEncodingWhenItHasNoOwnHint()
    {
        var cp1252 = Encoding.GetEncoding(1252);
        var resources = new Resources(new()
        {
            ["https://example.test/main.css"] =
                (cp1252.GetBytes("@charset \"windows-1252\"; @import 'child.css';"), null),
            ["https://example.test/child.css"] =
                (cp1252.GetBytes(".café {width:39px}"), "text/css"),
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"), resources);
        var border = new Border();
        Css.SetClass(border, "café");
        Css.GetStyleSheets(border).Add(sheet);
        border.Measure(new(100, 100));
        border.UpdateLayout();
        Assert.Equal(39, border.Width);
        Assert.Equal(2, resources.Requests.Count);
    }

    [Fact]
    public async Task SharedImportIsFetchedOnceAndDecodedPerParentEncoding()
    {
        var cp1252 = Encoding.GetEncoding(1252);
        var resources = new Resources(new()
        {
            ["https://example.test/main.css"] =
                (Encoding.UTF8.GetBytes("@import 'latin.css'; @import 'utf.css';"), null),
            ["https://example.test/latin.css"] =
                (Encoding.UTF8.GetBytes("@charset \"windows-1252\"; @import 'shared.css';"), null),
            ["https://example.test/utf.css"] =
                (Encoding.UTF8.GetBytes("@charset \"utf-8\"; @import 'shared.css';"), null),
            ["https://example.test/shared.css"] =
                (cp1252.GetBytes(".café {width:40px}"), null),
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"), resources);
        Assert.Equal(2, sheet.Rules.Length);
        Assert.Equal(4, resources.Requests.Count);

        foreach (var className in new[] { "café", "caf\uFFFD" })
        {
            var border = new Border();
            Css.SetClass(border, className);
            Css.GetStyleSheets(border).Add(sheet);
            border.Measure(new(100, 100));
            border.UpdateLayout();
            Assert.Equal(40, border.Width);
        }
    }

    [Fact]
    public async Task ExplicitNonCssContentTypeRejectsAnImportWithoutDroppingRootRules()
    {
        var resources = new Resources(new()
        {
            ["https://example.test/main.css"] =
                (Encoding.UTF8.GetBytes("@import 'other.css'; .root{width:41px}"), "text/css"),
            ["https://example.test/other.css"] =
                (Encoding.UTF8.GetBytes(".other{width:99px}"), "text/plain"),
        });
        var sheet = await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"), resources);
        Assert.Single(sheet.Rules);
        Assert.Contains(sheet.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Content-Type", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => CssStyleSheet.LoadAsync(
            new Uri("https://example.test/other.css"), resources));
    }

    [Fact]
    public void CharsetDeclarationMustBeginAtByteZeroWithExactSyntax()
    {
        var cp1252 = Encoding.GetEncoding(1252);
        var exact = CssStyleSheetEncoding.Decode(
            cp1252.GetBytes("@charset \"windows-1252\";.café{}"));
        Assert.Contains("café", exact.Text);
        Assert.Equal("windows-1252", exact.EncodingName);

        var leadingSpace = CssStyleSheetEncoding.Decode(
            cp1252.GetBytes(" @charset \"windows-1252\";.café{}"));
        Assert.Equal("UTF-8", leadingSpace.EncodingName);
        Assert.DoesNotContain("café", leadingSpace.Text);

        using var stream = new MemoryStream(
            cp1252.GetBytes("@charset \"windows-1252\";.café{width:42px}"));
        var sheet = CssStyleSheet.FromStream(stream);
        Assert.Single(sheet.Rules);
    }

    [Theory]
    [InlineData("iso-8859-10", "ISO-8859-10", "\u0104")]
    [InlineData("iso-8859-14", "ISO-8859-14", "\u1E02")]
    [InlineData("iso-8859-16", "ISO-8859-16", "\u0104")]
    [InlineData("latin1", "windows-1252", "\u00A1")]
    public void WebEncodingLabelsUseTheirSpecifiedSingleByteIndexes(
        string label, string canonicalName, string expected)
    {
        var decoded = CssStyleSheetEncoding.Decode([0xA1], $"text/css; charset={label}");
        Assert.Equal(canonicalName, decoded.EncodingName);
        Assert.Equal(expected, decoded.Text);
    }

    [Fact]
    public async Task LegacySingleByteIndexAppliesAStylesheetSelector()
    {
        var bytes = Encoding.ASCII.GetBytes(".a?{width:47px}");
        bytes[2] = 0xA1; // ISO-8859-10 pointer 33 is U+0104.
        var resources = new Resources(new()
        {
            ["https://example.test/nordic.css"] =
                (bytes, "text/css; charset=iso-8859-10"),
        });
        var sheet = await CssStyleSheet.LoadAsync(
            new Uri("https://example.test/nordic.css"), resources);
        var border = new Border();
        Css.SetClass(border, "a\u0104");
        Css.GetStyleSheets(border).Add(sheet);
        border.Measure(new(100, 100));
        border.UpdateLayout();
        Assert.Equal(47, border.Width);
    }

    [Fact]
    public void SpecialWebEncodingsAndInvalidLabelsDoNotUseDotNetAliases()
    {
        var userDefined = CssStyleSheetEncoding.Decode(
            [0x41, 0x80, 0xFF], "text/css; charset=x-user-defined");
        Assert.Equal("x-user-defined", userDefined.EncodingName);
        Assert.Equal("A\uF780\uF7FF", userDefined.Text);

        var replacement = CssStyleSheetEncoding.Decode(
            [0x41, 0x80, 0xFF], "text/css; charset=iso-2022-kr");
        Assert.Equal("replacement", replacement.EncodingName);
        Assert.Equal("\uFFFD", replacement.Text);

        var invalid = CssStyleSheetEncoding.Decode(
            Encoding.UTF8.GetBytes("café"), "text/css; charset=utf-7");
        Assert.Equal("UTF-8", invalid.EncodingName);
        Assert.Equal("café", invalid.Text);
    }

    [Fact]
    public void QuotedCharsetLabelTrimsInnerAsciiWhitespace()
    {
        var decoded = CssStyleSheetEncoding.Decode(
            [0xA1], "text/css; charset=\" \tiso-8859-10\t \"");
        Assert.Equal("ISO-8859-10", decoded.EncodingName);
        Assert.Equal("\u0104", decoded.Text);
    }

    [Fact]
    public void EncodingLabelsUseAsciiOnlyCaseMatching()
    {
        var decoded = CssStyleSheetEncoding.Decode(
            [0x80], "text/css; charset=\"GBK\"");
        Assert.Equal("UTF-8", decoded.EncodingName);
        Assert.Equal("\uFFFD", decoded.Text);
    }

    [Fact]
    public void Utf32PrefixesAreNotCssByteOrderMarks()
    {
        var littleEndian = CssStyleSheetEncoding.Decode(
            [0xFF, 0xFE, 0x00, 0x00, 0x61, 0x00, 0x00, 0x00]);
        Assert.Equal("UTF-16LE", littleEndian.EncodingName);
        Assert.Equal("\0a\0", littleEndian.Text);

        var bigEndian = CssStyleSheetEncoding.Decode([0x00, 0x00, 0xFE, 0xFF]);
        Assert.Equal("UTF-8", bigEndian.EncodingName);
        Assert.Equal("\0\0\uFFFD\uFFFD", bigEndian.Text);
    }

    [Theory]
    [InlineData("gbk")]
    [InlineData("gb18030")]
    public void ChineseEncodingLabelsShareTheStandardDecoder(string label)
    {
        string Decode(params byte[] bytes) => CssStyleSheetEncoding.Decode(
            bytes, $"text/css; charset={label}").Text;

        Assert.Equal("\u0080", Decode(0x81, 0x30, 0x81, 0x30));
        Assert.Equal("€", Decode(0x80));
        Assert.Equal("\u3000", Decode(0xA3, 0xA0));
        Assert.Equal("😀", Decode(0x94, 0x39, 0xFC, 0x36));
        Assert.Equal("\uE7C7", Decode(0x81, 0x35, 0xF4, 0x37));
    }

    [Fact]
    public void Gb18030InvalidSequencesRestoreAsciiAndRespectRangeLimits()
    {
        string Decode(params byte[] bytes) => CssStyleSheetEncoding.Decode(
            bytes, "text/css; charset=gb18030").Text;

        Assert.Equal("\uFFFD\"", Decode(0x81, 0x22));
        Assert.Equal("\uFFFD0\"", Decode(0x81, 0x30, 0x22));
        Assert.Equal("\uFFFD0\uFFFD\"", Decode(0x81, 0x30, 0x81, 0x22));
        Assert.Equal("\uFFFD", Decode(0x81, 0x30, 0x81));
        Assert.Equal("\uFFFD", Decode(0x84, 0x31, 0xA5, 0x30));
        Assert.Equal("\U00010000", Decode(0x90, 0x30, 0x81, 0x30));
    }

    [Theory]
    [InlineData("gbk")]
    [InlineData("gb18030")]
    public async Task ChineseFourByteSelectorMatchesInAStylesheet(string label)
    {
        var bytes = Encoding.ASCII.GetBytes(".a????{width:48px}");
        new byte[] { 0x94, 0x39, 0xFC, 0x36 }.CopyTo(bytes, 2);
        var resources = new Resources(new()
        {
            ["https://example.test/chinese.css"] =
                (bytes, $"text/css; charset={label}"),
        });
        var sheet = await CssStyleSheet.LoadAsync(
            new Uri("https://example.test/chinese.css"), resources);
        var border = new Border();
        Css.SetClass(border, "a😀");
        Css.GetStyleSheets(border).Add(sheet);
        border.Measure(new(100, 100));
        border.UpdateLayout();
        Assert.Equal(48, border.Width);
    }

    [Fact]
    public void Big5UsesTheStandardIndexAndFourTwoCodePointMappings()
    {
        string Decode(params byte[] bytes) => CssStyleSheetEncoding.Decode(
            bytes, "text/css; charset=big5").Text;

        Assert.Equal("一", Decode(0xA4, 0x40));
        Assert.Equal("\u00CA\u0304", Decode(0x88, 0x62));
        Assert.Equal("\u00CA\u030C", Decode(0x88, 0x64));
        Assert.Equal("\u00EA\u0304", Decode(0x88, 0xA3));
        Assert.Equal("\u00EA\u030C", Decode(0x88, 0xA5));
        Assert.Equal("\U0002910D", Decode(0xFE, 0xFD));
        Assert.Equal("\uFFFD\"", Decode(0x81, 0x22));
        Assert.Equal("\uFFFD", Decode(0x81));
    }

    [Fact]
    public async Task Big5CombiningSelectorMatchesInAStylesheet()
    {
        var bytes = Encoding.ASCII.GetBytes(".a??{width:49px}");
        new byte[] { 0x88, 0x62 }.CopyTo(bytes, 2);
        var resources = new Resources(new()
        {
            ["https://example.test/big5.css"] =
                (bytes, "text/css; charset=big5"),
        });
        var sheet = await CssStyleSheet.LoadAsync(
            new Uri("https://example.test/big5.css"), resources);
        var border = new Border();
        Css.SetClass(border, "a\u00CA\u0304");
        Css.GetStyleSheets(border).Add(sheet);
        border.Measure(new(100, 100));
        border.UpdateLayout();
        Assert.Equal(49, border.Width);
    }

    [Fact]
    public void EucKrUsesTheStandardIndexAndRestoresAscii()
    {
        string Decode(params byte[] bytes) => CssStyleSheetEncoding.Decode(
            bytes, "text/css; charset=euc-kr").Text;

        Assert.Equal("\uAC02", Decode(0x81, 0x41));
        Assert.Equal("\uFFFD\"", Decode(0x81, 0x22));
        Assert.Equal("\uFFFD@", Decode(0x81, 0x40));
        Assert.Equal("\uFFFDA", Decode(0xFE, 0x41));
        Assert.Equal("\uFFFD", Decode(0xFE, 0xFE));
        Assert.Equal("\uFFFD", Decode(0x81));
    }

    [Fact]
    public void EucJpUsesBothJisIndexesAndHalfwidthKatakana()
    {
        string Decode(params byte[] bytes) => CssStyleSheetEncoding.Decode(
            bytes, "text/css; charset=euc-jp").Text;

        Assert.Equal("\u3000", Decode(0xA1, 0xA1));
        Assert.Equal("\uFF61", Decode(0x8E, 0xA1));
        Assert.Equal("\u02D8", Decode(0x8F, 0xA2, 0xAF));
        Assert.Equal("\uFFFD\"", Decode(0x8F, 0x22));
        Assert.Equal("\uFFFD", Decode(0x8F));
    }

    [Fact]
    public void ShiftJisUsesJisIndexAndPrivateUsePointerRange()
    {
        string Decode(params byte[] bytes) => CssStyleSheetEncoding.Decode(
            bytes, "text/css; charset=shift_jis").Text;

        Assert.Equal("\u3000", Decode(0x81, 0x40));
        Assert.Equal("\u0080", Decode(0x80));
        Assert.Equal("\uFF61", Decode(0xA1));
        Assert.Equal("\uE000", Decode(0xF0, 0x40));
        Assert.Equal("\uFFFD\"", Decode(0x82, 0x22));
        Assert.Equal("\uFFFD", Decode(0x82));
    }

    [Theory]
    [InlineData("euc-kr", 0x81, 0x41, "a\uAC02")]
    [InlineData("euc-jp", 0xA4, 0xA2, "aあ")]
    [InlineData("shift_jis", 0x82, 0xA0, "aあ")]
    public async Task EastAsianTwoByteSelectorMatchesInAStylesheet(
        string label, byte first, byte second, string className)
    {
        var bytes = Encoding.ASCII.GetBytes(".a??{width:50px}");
        bytes[2] = first;
        bytes[3] = second;
        var resources = new Resources(new()
        {
            ["https://example.test/east-asian.css"] =
                (bytes, $"text/css; charset={label}"),
        });
        var sheet = await CssStyleSheet.LoadAsync(
            new Uri("https://example.test/east-asian.css"), resources);
        var border = new Border();
        Css.SetClass(border, className);
        Css.GetStyleSheets(border).Add(sheet);
        border.Measure(new(100, 100));
        border.UpdateLayout();
        Assert.Equal(50, border.Width);
    }

    [Fact]
    public void Iso2022JpUsesItsEscapeStatesAndJisIndex()
    {
        string Decode(params byte[] bytes) => CssStyleSheetEncoding.Decode(
            bytes, "text/css; charset=iso-2022-jp").Text;

        Assert.Equal("aあb", Decode(0x61, 0x1B, 0x24, 0x42, 0x24, 0x22,
            0x1B, 0x28, 0x42, 0x62));
        Assert.Equal("¥‾", Decode(0x1B, 0x28, 0x4A, 0x5C, 0x7E));
        Assert.Equal("\uFF61", Decode(0x1B, 0x28, 0x49, 0x21));
        Assert.Equal("\uFFFD¥", Decode(0x1B, 0x28, 0x4A,
            0x1B, 0x28, 0x4A, 0x5C));
        Assert.Equal("\uFFFD$X", Decode(0x1B, 0x24, 0x58));
        Assert.Equal("\uFFFD$", Decode(0x1B, 0x24));
        Assert.Equal("\uFFFD", Decode(0x1B));
        Assert.Equal("\uFFFD\uFFFD", Decode(0x0E, 0x0F));
        Assert.Equal("\uFFFD", Decode(0x1B, 0x24, 0x42, 0x24));
    }

    [Fact]
    public async Task Iso2022JpSelectorMatchesInAStylesheet()
    {
        var prefix = Encoding.ASCII.GetBytes(".a");
        var suffix = Encoding.ASCII.GetBytes("{width:51px}");
        byte[] bytes = [.. prefix, 0x1B, 0x24, 0x42, 0x24, 0x22,
            0x1B, 0x28, 0x42, .. suffix];
        var resources = new Resources(new()
        {
            ["https://example.test/iso-2022-jp.css"] =
                (bytes, "text/css; charset=iso-2022-jp"),
        });
        var sheet = await CssStyleSheet.LoadAsync(
            new Uri("https://example.test/iso-2022-jp.css"), resources);
        var border = new Border();
        Css.SetClass(border, "aあ");
        Css.GetStyleSheets(border).Add(sheet);
        border.Measure(new(100, 100));
        border.UpdateLayout();
        Assert.Equal(51, border.Width);
    }

    [Fact]
    public void EveryStandardLabelHasAnAvailableDecoder()
    {
        Assert.Equal(228, CssStyleSheetEncodingData.Labels.Count);
        foreach (var (label, canonicalName) in CssStyleSheetEncodingData.Labels)
        {
            var decoded = CssStyleSheetEncoding.Decode([0x41], $"text/css; charset=\"{label}\"");
            Assert.Equal(canonicalName, decoded.EncodingName);
            Assert.NotEmpty(decoded.Text);
        }
    }
}
