using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

public sealed class MacOSDragResourceLimitTests
{
    [Fact]
    public void ManyFormatsBoundEnumerationMetadataAndReads()
    {
        int enumerated = 0, reads = 0;
        IEnumerable<string> Formats()
        {
            while (true) { enumerated++; yield return $"application/x-{enumerated}"; }
        }
        var data = new MacOSDragDataObject(Formats(), (_, _) => { reads++; return []; });
        Assert.Equal(MacOSDragDataObject.MaxFormats, enumerated);
        Assert.Equal(MacOSDragDataObject.MaxFormats, data.GetFormats(false).Length);
        Assert.Equal(0, reads);
        data.Snapshot(); data.Snapshot();
        Assert.Equal(MacOSDragDataObject.MaxFormats, reads);
        Assert.Null(data.GetData("application/x-65", false));
    }

    [Fact]
    public void DuplicateFloodAlsoBoundsEnumeration()
    {
        int enumerated = 0;
        IEnumerable<string> Formats()
        {
            while (true) { enumerated++; yield return "application/x-duplicate"; }
        }
        var data = new MacOSDragDataObject(Formats(), _ => []);
        Assert.Equal(MacOSDragDataObject.MaxFormatEntries, enumerated);
        Assert.Single(data.GetFormats(false));
    }

    [Fact]
    public void LazyReadsAndSnapshotShareBudgetAndStopAtExactLimit()
    {
        var budgets = new List<int>();
        var data = new MacOSDragDataObject(["application/a", "application/b", "application/c"], (_, remaining) =>
        {
            budgets.Add(remaining); return new byte[4];
        }, maxBytes: 8);
        Assert.IsType<byte[]>(data.GetData("application/b", false));
        data.Snapshot();
        Assert.Equal([8, 4], budgets);
        Assert.IsType<byte[]>(data.GetData("application/a", false));
        Assert.Null(data.GetData("application/c", false));
        data.Snapshot(); Assert.Equal(2, budgets.Count);
    }

    [Fact]
    public void OverBudgetPayloadIsRejectedAndSmallerAlternativeSurvives()
    {
        var budgets = new List<int>();
        var data = new MacOSDragDataObject(["application/a", "application/b", "application/c"], (mime, remaining) =>
        {
            budgets.Add(remaining);
            return new byte[mime == "application/b" ? 6 : 3];
        }, maxBytes: 8);
        data.Snapshot();
        Assert.Equal([8, 5, 5], budgets);
        Assert.Equal(new byte[3], data.GetData("application/a", false));
        Assert.Null(data.GetData("application/b", false));
        Assert.Equal(new byte[3], data.GetData("application/c", false));
        data.Snapshot();
        Assert.Equal(3, budgets.Count);
    }

    [Fact]
    public void MissingEmptyFailedAndDuplicateFormatsDoNotSpendBudgetOrRetry()
    {
        var reads = new List<string>();
        var data = new MacOSDragDataObject(
            ["application/missing", "application/fail", "application/empty", "APPLICATION/EMPTY", "application/ok"],
            (mime, remaining) =>
            {
                reads.Add(mime); Assert.Equal(4, remaining);
                return mime switch
                {
                    "application/missing" => null,
                    "application/fail" => throw new InvalidOperationException("provider failed"),
                    "application/empty" => [],
                    _ => new byte[4]
                };
            }, maxBytes: 4);
        data.Snapshot(); data.Snapshot();
        Assert.Equal(4, reads.Count);
        Assert.Empty(Assert.IsType<byte[]>(data.GetData("application/empty", false)));
        Assert.Null(data.GetData("application/fail", false));
        Assert.IsType<byte[]>(data.GetData("application/ok", false));
    }

    [Fact]
    public void DeferredConversionsSurviveSnapshotAndNativeRetirement()
    {
        var encoded = new Dictionary<string, byte[]>
        {
            ["text/uri-list"] = System.Text.Encoding.UTF8.GetBytes("file:///tmp/中文%F0%9F%99%82.txt\r\nfile:///tmp/second.txt\r\n"),
            ["text/plain"] = System.Text.Encoding.UTF8.GetBytes("中文🙂"),
            ["text/html"] = System.Text.Encoding.UTF8.GetBytes("<b>中文🙂</b>"),
            ["application/custom"] = [1, 2, 3]
        };
        bool retired = false;
        var data = new MacOSDragDataObject(encoded.Keys, (mime, remaining) =>
        {
            Assert.False(retired); Assert.True(encoded[mime].Length <= remaining);
            return encoded[mime];
        });
        data.Snapshot(); retired = true;
        Assert.Equal(new[] { "/tmp/中文🙂.txt", "/tmp/second.txt" }, data.GetData(DataFormats.FileDrop));
        Assert.Equal("中文🙂", data.GetData(DataFormats.UnicodeText));
        Assert.Equal("<b>中文🙂</b>", data.GetData(DataFormats.Html));
        Assert.Equal(encoded["application/custom"], data.GetData("application/custom", false));
    }

    [Theory]
    [InlineData(256 * 1024 * 1024 - 1)]
    [InlineData(256 * 1024 * 1024)]
    public void NearPerItemLimitIsRejectedBeforeCopyWhenAggregateBudgetIsSmaller(int size)
    {
        // Invalid pointer proves that no giant allocation or dereference occurs.
        Assert.Null(NativePlatformWindow.CopyMacOSDragData((nint)1, (uint)size, 16));
    }

    [Fact]
    public void DetachRetainsCachedDataAndNeverInvokesProviderAgain()
    {
        int reads = 0;
        var data = new MacOSDragDataObject(["application/a", "application/b"], (_, _) => { reads++; return [1]; });
        data.GetData("application/a", false); data.Detach(); data.Snapshot();
        Assert.Equal(new byte[] { 1 }, data.GetData("application/a", false));
        Assert.Null(data.GetData("application/b", false));
        Assert.Equal(1, reads);
    }
}
