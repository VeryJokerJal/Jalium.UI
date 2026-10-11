using System.Text;
using Jalium.UI.Controls.Platform;
using Xunit;

namespace Jalium.UI.MacOS.Tests;

public class MacOSClipboardFileBatchTests
{
    public static TheoryData<string[]> FileBatches => new()
    {
        new[] { "/tmp/one file.txt", "/tmp/中文🙂.txt" },
        new[] { "/tmp/folder/", "/tmp/folder/inside.txt" },
        new[] { "/tmp/duplicate.txt", "/tmp/duplicate.txt", "/tmp/last.txt" },
        new[] { "/tmp/hash#percent%.txt", "/tmp/query?name.txt" },
        new[] { "/tmp/new\nline.txt", "/tmp/carriage\rreturn.txt" },
        new[] { "/tmp/e\u0301.txt", "/tmp/é.txt", "/tmp/👨‍👩‍👧‍👦.txt" }
    };

    [Theory]
    [MemberData(nameof(FileBatches))]
    public void FileBatchRetainsOrderedPathsThroughTheSharedUriRepresentation(string[] files)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, files);
        data.SetData(DataFormats.UnicodeText, "附带文本🙂");
        data.SetData(DataFormats.Html, "<b>附带🙂</b>");
        var representations = ClipboardPlatform.BuildCrossPlatformRepresentations(data);
        byte[] uris = representations["text/uri-list"];
        Assert.Equal(files, ClipboardPlatform.DecodeCrossPlatformRepresentation(DataFormats.FileDrop, uris));
        Assert.Equal("附带文本🙂", Encoding.UTF8.GetString(representations["text/plain;charset=utf-8"]));
        Assert.Equal("<b>附带🙂</b>", Encoding.UTF8.GetString(representations["text/html"]));
        Assert.EndsWith("\r\n", Encoding.UTF8.GetString(uris));
    }

    [Theory]
    [MemberData(nameof(FileBatches))]
    public void DragSnapshotKeepsEveryFileAfterTheNativeReaderRetires(string[] files)
    {
        byte[] uris = Encoding.UTF8.GetBytes(ClipboardPlatform.BuildUriList(files));
        bool retired = false; int calls = 0;
        var data = new MacOSDragDataObject(["text/uri-list"], _ =>
        {
            Assert.False(retired); calls++; return uris;
        });
        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
        data.Snapshot(); retired = true;
        Assert.Equal(files, data.GetData(DataFormats.FileDrop));
        Assert.Equal(files, data.GetData("FileNameW"));
        Assert.Equal(uris, data.GetData("text/uri-list", false));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MixedUrisRetainFilesInTheirOriginalOrder()
    {
        byte[] uris = Encoding.UTF8.GetBytes("\uFEFF# comment\r\nhttps://example.test/\r\nfile:///tmp/first%20file.txt\r\n" +
            "relative/path\r\nfile:///tmp/中文%F0%9F%99%82.txt\r\nfile:///tmp/first%20file.txt\r\n");
        Assert.Equal(new[] { "/tmp/first file.txt", "/tmp/中文🙂.txt", "/tmp/first file.txt" }, ClipboardPlatform.ParseUriList(uris));
    }
}
