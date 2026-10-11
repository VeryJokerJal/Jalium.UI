using System.Text;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDragRepresentationTests
{
    [Theory]
    [InlineData("https://example.test/中文?q=🙂\r\n")]
    [InlineData("# file:///tmp/comment.txt\r\nhttps://example.test/\r\n")]
    [InlineData("mailto:someone@example.test\nrelative/path\ninvalid uri\n")]
    [InlineData("")]
    public void NonFileUrisDoNotAdvertiseAFileDrop(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int reads = 0;
        var data = new MacOSDragDataObject(["text/uri-list"], _ => { reads++; return bytes; });
        Assert.True(data.GetDataPresent("text/uri-list", false));
        Assert.Equal(0, reads);
        Assert.False(data.GetDataPresent(DataFormats.FileDrop));
        Assert.False(data.GetDataPresent("FileName"));
        Assert.False(data.GetDataPresent("FileNameW"));
        Assert.DoesNotContain(DataFormats.FileDrop, data.GetFormats());
        Assert.DoesNotContain(DataFormats.FileDrop, data.GetFormats(false));
        Assert.Null(data.GetData(DataFormats.FileDrop));
        Assert.Null(data.GetData("FileNameW"));
        Assert.Equal(bytes, Assert.IsType<byte[]>(data.GetData("text/uri-list", false)));
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData("text/uri-list", "FileDrop")]
    [InlineData("TEXT/URI-LIST;charset=utf-8", "filedrop")]
    [InlineData("text/uri-list", "FileName")]
    [InlineData("text/uri-list", "FileNameW")]
    public void FileAliasQueriesResolveOnlyTheUriRepresentation(string mime, string query)
    {
        var reads = new List<string>();
        var data = new MacOSDragDataObject(["text/html", mime, "text/plain"], type =>
        {
            reads.Add(type);
            return Encoding.UTF8.GetBytes("# comment\r\nhttps://example.test/\r\nfile:///tmp/one%20file.txt\r\nfile:///tmp/中文%F0%9F%99%82.txt\r\n");
        });
        Assert.True(data.GetDataPresent(query));
        Assert.Equal([mime], reads);
        Assert.Equal(["/tmp/one file.txt", "/tmp/中文🙂.txt"], Assert.IsType<string[]>(data.GetData(query)));
        Assert.Contains(DataFormats.FileDrop, data.GetFormats(false));
        Assert.Contains("FileName", data.GetFormats());
        Assert.Contains("FileNameW", data.GetFormats());
        Assert.False(data.GetDataPresent("FileNameW", false));
        Assert.Equal([mime], reads);
    }

    [Fact]
    public void FileGetterClassifiesTheRepresentationBeforeAnyAvailabilityQuery()
    {
        var data = new MacOSDragDataObject(["text/uri-list"], _ => Encoding.UTF8.GetBytes("file:///tmp/owned.txt\r\n"));
        Assert.Equal(["/tmp/owned.txt"], Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop, false)));
        Assert.True(data.GetDataPresent(DataFormats.FileDrop, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumeratingFormatsClassifiesUrisOnceWithoutReadingText(bool autoConvert)
    {
        var reads = new List<string>();
        var data = new MacOSDragDataObject(["text/plain", "text/uri-list"], mime =>
        {
            reads.Add(mime);
            return Encoding.UTF8.GetBytes("file:///tmp/owned.txt\r\n");
        });
        Assert.Contains(DataFormats.FileDrop, data.GetFormats(autoConvert));
        Assert.Contains(DataFormats.FileDrop, data.GetFormats(autoConvert));
        Assert.Equal(["text/uri-list"], reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotPreservesTheActualUriKindsAfterOwnerRetires(bool files)
    {
        bool retired = false;
        int reads = 0;
        string uri = files ? "file:///tmp/保留.txt\r\n" : "https://example.test/保留\r\n";
        var data = new MacOSDragDataObject(["text/uri-list", "text/plain"], mime =>
        {
            Assert.False(retired);
            reads++;
            return Encoding.UTF8.GetBytes(mime == "text/plain" ? "保留🙂" : uri);
        });
        data.Snapshot(); retired = true;
        Assert.Equal(files, data.GetDataPresent(DataFormats.FileDrop));
        Assert.Equal(files, data.GetFormats().Contains(DataFormats.FileDrop));
        Assert.Equal(Encoding.UTF8.GetBytes(uri), Assert.IsType<byte[]>(data.GetData("text/uri-list", false)));
        Assert.Equal("保留🙂", data.GetData(DataFormats.Text));
        if (files) Assert.Equal(["/tmp/保留.txt"], Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));
        else Assert.Null(data.GetData(DataFormats.FileDrop));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void MissingOrDetachedUrisCannotClaimFilesOrRestartTheProvider()
    {
        int reads = 0;
        var missing = new MacOSDragDataObject(["text/uri-list"], _ => { reads++; return null; });
        Assert.False(missing.GetDataPresent(DataFormats.FileDrop));
        Assert.DoesNotContain(DataFormats.FileDrop, missing.GetFormats());
        missing.Detach(); Assert.Null(missing.GetData("text/uri-list"));
        Assert.Equal(1, reads);
        var detached = new MacOSDragDataObject(["text/uri-list"], _ => throw new InvalidOperationException("retired provider"));
        detached.Detach();
        Assert.False(detached.GetDataPresent(DataFormats.FileDrop));
        Assert.DoesNotContain(DataFormats.FileDrop, detached.GetFormats());
    }

    [Fact]
    public void FailedFirstUriRepresentationDoesNotHideAValidAlternative()
    {
        int reads = 0;
        var data = new MacOSDragDataObject(["text/uri-list", "text/uri-list;charset=utf-8"], mime =>
        {
            reads++;
            return Encoding.UTF8.GetBytes(mime == "text/uri-list" ? "https://example.test/" : "file:///tmp/owned.txt");
        });
        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
        Assert.Equal(["/tmp/owned.txt"], Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void CachedFilesSurviveDetachAndExplicitApplicationDataRemainsAuthoritative()
    {
        var data = new MacOSDragDataObject(["text/uri-list"], _ => Encoding.UTF8.GetBytes("file:///tmp/owned.txt"));
        Assert.True(data.GetDataPresent(DataFormats.FileDrop)); data.Detach();
        Assert.Equal(["/tmp/owned.txt"], Assert.IsType<string[]>(data.GetData("FileNameW")));
        var assigned = new MacOSDragDataObject(["text/uri-list"], _ => throw new InvalidOperationException("explicit data must win"));
        assigned.SetData(DataFormats.FileDrop, Array.Empty<string>());
        Assert.True(assigned.GetDataPresent(DataFormats.FileDrop));
        Assert.Empty(Assert.IsType<string[]>(assigned.GetData(DataFormats.FileDrop)));
    }

    [Fact]
    public void InvalidAvailabilityQueriesDoNotReadTheUriProvider()
    {
        var data = new MacOSDragDataObject(["text/uri-list"], _ => throw new InvalidOperationException("invalid query"));
        Assert.False(data.GetDataPresent((string)null!));
        Assert.False(data.GetDataPresent(""));
    }
}
