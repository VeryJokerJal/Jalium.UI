using System.Text;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssDataUriLoadingTests
{
    [Theory]
    [InlineData("中文", false)]
    [InlineData("中文", true)]
    [InlineData("🙂", false)]
    [InlineData("🙂", true)]
    [InlineData("中🙂e\u0301𠀀", false)]
    [InlineData("中🙂e\u0301𠀀", true)]
    public async Task PublicLoadPreservesUnicodeScalars(string text, bool userEscaped)
    {
        var sheet = await CssStyleSheet.LoadAsync(DataUri(Quote(text), userEscaped));
        AssertValue(sheet, text);
    }

    [Theory]
    [InlineData("a+b%20c%25🙂", "a+b c%🙂")]
    [InlineData("%E4%B8%AD🙂%F0%9F%99%82", "中🙂🙂")]
    [InlineData("a%2G%🙂", "a%2G%🙂")]
    public async Task RawUnicodeAndPercentOctetsDecodeTogether(string encoded, string expected)
    {
        var sheet = await CssStyleSheet.LoadAsync(DataUri(Quote(encoded), true));
        AssertValue(sheet, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Base64RetainsTheSameUnicode(bool omitPadding)
    {
        const string text = "中文🙂𠀀";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(":root{--label:" + Quote(text) + ";}"));
        if (omitPadding) encoded = encoded.TrimEnd('=');
        var sheet = await CssStyleSheet.LoadAsync(new Uri("data:text/css;charset=utf-8;base64," + encoded));
        AssertValue(sheet, text);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("%%")]
    [InlineData("中")]
    public async Task InvalidBase64StillFailsAsAControlledDataError(string encoded)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CssStyleSheet.LoadAsync(new Uri("data:text/css;base64," + encoded)));
    }

    [Fact]
    public async Task LargeRawUnicodeBelowTheExistingQuotaReturnsTheFullValue()
    {
        // QA-443: the former per-character stackalloc terminates the process
        // for this six-megabyte payload, below the existing 64 MB quota.
        var text = new string('中', 2_000_000);
        var sheet = await CssStyleSheet.LoadAsync(DataUri(Quote(text), true));
        AssertValue(sheet, text);
    }

    [Fact]
    public async Task DecodedByteQuotaStillRejectsAnOversizedUnicodePayload()
    {
        var text = new string('中', 21_333_334); // 64,000,002 UTF-8 bytes before CSS syntax.
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CssStyleSheet.LoadAsync(DataUri(Quote(text), true)));
        Assert.Contains("too large", error.Message);
    }

    [Fact]
    public async Task AlreadyCancelledLoadDoesNotDecodeTheData()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CssStyleSheet.LoadAsync(DataUri(Quote("🙂"), true), cancellation.Token));
    }

    private static Uri DataUri(string value, bool userEscaped)
    {
        var address = "data:text/css;charset=utf-8,:root{--label:" + value + ";}";
#pragma warning disable SYSLIB0013 // This supported legacy Uri shape is the public regression input.
        return userEscaped ? new Uri(address, true) : new Uri(address);
#pragma warning restore SYSLIB0013
    }

    private static string Quote(string value) => "\"" + value + "\"";

    private static void AssertValue(CssStyleSheet sheet, string expected)
    {
        Assert.Empty(sheet.Diagnostics);
        var declaration = Assert.Single(Assert.Single(sheet.Rules).Declarations);
        Assert.Equal(Quote(expected), declaration.RawValue);
    }
}
