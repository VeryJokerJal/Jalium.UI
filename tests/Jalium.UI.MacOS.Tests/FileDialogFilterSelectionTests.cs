using Jalium.UI.MacOS;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public class FileDialogFilterSelectionTests
{
    [Theory]
    [InlineData("txt")]
    [InlineData(".png")]
    public void DefaultExtensionWithoutAFilterDoesNotRestrictSelectableFiles(string extension)
    {
        var selection = new FileDialogFilterSelection([], 1, extension);
        Assert.Empty(selection.Extensions);
    }

    [Theory]
    [InlineData("*.txt", "png", "note", true, "note.txt")]
    [InlineData("*.png;*.jpeg", "txt", "note", true, "note.png")]
    [InlineData("*.tar.gz", "zip", "note", true, "note.tar.gz")]
    [InlineData("*.*", ".txt", "note", true, "note.txt")]
    [InlineData("*", " .txt ", "note", true, "note.txt")]
    [InlineData("*.*", "", "note", true, "note")]
    [InlineData("*.*", ".", "note", true, "note")]
    [InlineData("*.*", "txt/other", "note", true, "note")]
    [InlineData("*.txt", "png", "note", false, "note")]
    [InlineData("*.txt", "png", "note.custom", true, "note.custom")]
    [InlineData("*.txt", "png", ".profile", true, ".profile")]
    [InlineData("*.txt", "png", "note.", true, "note.txt")]
    [InlineData("*.txt", "png", "", true, "")]
    [InlineData("report-*.txt", "json", "note", true, "note.json")]
    public void SavingUsesSelectedFilterThenFallbackAndPreservesExplicitExtensions(
        string pattern, string? fallback, string name, bool addExtension, string expected)
    {
        var selection = new FileDialogFilterSelection([("Files", pattern)], 1, fallback);
        Assert.Equal(expected, selection.AppendExtension(name, addExtension));
    }

    [Theory]
    [InlineData("txt", "note.txt")]
    [InlineData(null, "note")]
    [InlineData("", "note")]
    public void SavingWithoutAFilterUsesOnlyTheDefaultExtension(string? extension, string expected)
        => Assert.Equal(expected, new FileDialogFilterSelection([], 1, extension).AppendExtension("note", true));

    [Theory]
    [InlineData("*.tar.gz", "archive.tar.gz", true)]
    [InlineData("*.tar.gz", "archive.gz", false)]
    [InlineData("*.txt;*.md", "NOTE.TXT", true)]
    [InlineData("*.txt;*.md", "note.png", false)]
    [InlineData("report-?.txt", "report-1.txt", true)]
    [InlineData("report-?.txt", "report-12.txt", false)]
    [InlineData("README", "README", true)]
    [InlineData("README", "README.txt", false)]
    [InlineData("*.*", "README", true)]
    [InlineData("*;*.png", "README", true)]
    public void OpenFileMatchingRetainsCompoundExtensionsAndFileNamePatterns(string pattern, string name, bool expected)
        => Assert.Equal(expected, new FileDialogFilterSelection([("Files", pattern)], 1, null).MatchesFileName(name));

    [Fact]
    public void ChangingFiltersUpdatesTheExtensionAndMatchingTogether()
    {
        var selection = new FileDialogFilterSelection([("Images", "*.png"), ("Text", "*.txt"), ("All", "*.*")], 1, "json");
        Assert.Equal("note.png", selection.AppendExtension("note", true));
        selection.Select(3);
        Assert.True(selection.MatchesFileName("note.custom"));
        Assert.Equal("note.json", selection.AppendExtension("note", true));
        selection.Select(2);
        Assert.False(selection.MatchesFileName("note.png"));
        Assert.Equal("note.txt", selection.AppendExtension("note", true));
        selection.Select(1);
        Assert.Equal("note.png", selection.AppendExtension("note", true));
    }

    [Fact]
    public void WildcardAndInvalidExtensionsAreNotPublishedAsNativeTypeRestrictions()
    {
        var selection = new FileDialogFilterSelection([("Files", "*. ;*.?;*.;*.txt;*.txt/other;*.tar.gz")], 1, null);
        Assert.Equal(new[] { "txt", "tar.gz" }, selection.Extensions);
    }

    [Fact]
    public void SwitchingFiltersUpdatesExtensionsAndOneBasedIndex()
    {
        var selection = new FileDialogFilterSelection([("Images", "*.png;*.PNG"), ("Text", "*.txt"), ("All", "*.*")], 1, null);
        Assert.Equal(new[] { "png" }, selection.Extensions);
        selection.Select(2);
        Assert.Equal(2, selection.Index);
        Assert.Equal(new[] { "txt" }, selection.Extensions);
        selection.Select(3);
        Assert.Empty(selection.Extensions);
        selection.Select(1);
        Assert.Equal(new[] { "png" }, selection.Extensions);
    }

    [Fact]
    public void AcceptedIndexIsReturnedButCancellationPreservesIndex()
    {
        var previous = Controls.Platform.PlatformFileDialogs.Show;
        try
        {
            var dialog = new TestDialog { FilterIndex = 1, CheckFileExists = false, CheckPathExists = false };
            Controls.Platform.PlatformFileDialogs.Show = options =>
            {
                options.SelectedFilterIndex = 2;
                return ["chosen.txt"];
            };
            Assert.True(dialog.ShowDialog(IntPtr.Zero));
            Assert.Equal(2, dialog.FilterIndex);
            Controls.Platform.PlatformFileDialogs.Show = options =>
            {
                options.SelectedFilterIndex = 1;
                return null;
            };
            Assert.False(dialog.ShowDialog(IntPtr.Zero));
            Assert.Equal(2, dialog.FilterIndex);
        }
        finally { Controls.Platform.PlatformFileDialogs.Show = previous; }
    }

    private sealed class TestDialog : Controls.FileDialog
    {
        public override bool? ShowDialog(IntPtr owner) => ShowMacOSDialog(false, false, false);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(99, 2)]
    public void InitialIndexIsClamped(int initial, int expected)
    {
        var selection = new FileDialogFilterSelection([("Images", "*.png"), ("Text", "*.txt")], initial, null);
        Assert.Equal(expected, selection.Index);
    }
}
