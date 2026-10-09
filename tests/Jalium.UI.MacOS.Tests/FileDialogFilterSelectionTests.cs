using Jalium.UI.MacOS;

namespace Jalium.UI.Tests;

public class FileDialogFilterSelectionTests
{
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
