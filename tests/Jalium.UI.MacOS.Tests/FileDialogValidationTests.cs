using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class FileDialogValidationTests
{
    private static string PathFor(string name) => Path.Combine(AppContext.BaseDirectory, name);

    private static void WithProvider(Func<PlatformFileDialogOptions, string[]?> show, Action action)
    {
        var previous = PlatformFileDialogs.Show;
        try { PlatformFileDialogs.Show = show; action(); }
        finally { PlatformFileDialogs.Show = previous; }
    }

    [Fact]
    public void CanceledConfirmationRestoresStateAndCanBeRetriedBeforeTheProviderReturns()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = PathFor("original.png"), Filter = "Images|*.png|Text|*.txt", FilterIndex = 1
        };
        int confirmations = 0;
        dialog.FileOk += (_, args) =>
        {
            confirmations++;
            Assert.Equal(PathFor(confirmations == 1 ? "rejected.txt" : "accepted.txt"), dialog.FileName);
            Assert.Equal(2, dialog.FilterIndex);
            if (confirmations == 1)
            {
                dialog.FileName = "handler mutation";
                dialog.FilterIndex = 99;
                args.Cancel = true;
            }
        };
        WithProvider(options =>
        {
            Assert.NotNull(options.ValidateSelection);
            Assert.False(options.ValidateSelection!([PathFor("rejected.txt")], 2));
            Assert.Equal(PathFor("original.png"), dialog.FileName);
            Assert.Equal(1, dialog.FilterIndex);
            Assert.True(options.ValidateSelection([PathFor("accepted.txt")], 2));
            options.SelectedFilterIndex = 2;
            return [PathFor("accepted.txt")];
        }, () => Assert.True(dialog.ShowDialog((nint)0x145)));
        Assert.Equal(2, confirmations);
        Assert.Equal(PathFor("accepted.txt"), dialog.FileName);
        Assert.Equal(2, dialog.FilterIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelingThePanelRestoresOriginalNamesAndFilterEvenAfterValidation(bool approved)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = PathFor("original.png"), FilterIndex = 1 };
        int count = 0;
        dialog.FileOk += (_, args) => { count++; args.Cancel = !approved; };
        WithProvider(options =>
        {
            Assert.Equal(approved, options.ValidateSelection!([PathFor("candidate.txt")], 2));
            options.SelectedFilterIndex = 2;
            return null;
        }, () => Assert.False(dialog.ShowDialog((nint)0x145)));
        Assert.Equal(PathFor("original.png"), dialog.FileName);
        Assert.Equal(1, dialog.FilterIndex);
        Assert.Equal(1, count);
    }

    [Fact]
    public void AcceptedHandlerChangesAreRetainedWithoutRaisingFileOkTwice()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog();
        int count = 0;
        dialog.FileOk += (_, _) => { count++; dialog.FileName = PathFor("handler.txt"); dialog.FilterIndex = 3; };
        WithProvider(options =>
        {
            string[] paths = [PathFor("candidate.txt")];
            Assert.True(options.ValidateSelection!(paths, 2));
            paths[0] = "provider mutation";
            options.SelectedFilterIndex = 2;
            return [PathFor("candidate.txt")];
        }, () => Assert.True(dialog.ShowDialog((nint)0x145)));
        Assert.Equal(1, count);
        Assert.Equal(PathFor("handler.txt"), dialog.FileName);
        Assert.Equal(3, dialog.FilterIndex);
    }

    [Fact]
    public void MultipleOpenPathsArePublishedTogetherInOneCancelableEvent()
    {
        string[] paths = [typeof(FileDialogValidationTests).Assembly.Location, typeof(Window).Assembly.Location];
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        int count = 0;
        dialog.FileOk += (_, _) => { count++; Assert.Equal(paths, dialog.FileNames); };
        WithProvider(options =>
        {
            Assert.True(options.Multiple);
            Assert.True(options.ValidateSelection!(paths, 1));
            return paths;
        }, () => Assert.True(dialog.ShowDialog((nint)0x145)));
        Assert.Equal(1, count);
        Assert.Equal(paths, dialog.FileNames);
    }

    [Fact]
    public void MissingOpenPathRejectsTheWholeSelectionWithoutRaisingFileOk()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        int count = 0;
        dialog.FileOk += (_, _) => count++;
        WithProvider(options =>
        {
            Assert.False(options.ValidateSelection!([typeof(Window).Assembly.Location, PathFor("absent-file.txt")], 1));
            return null;
        }, () => Assert.False(dialog.ShowDialog((nint)0x145)));
        Assert.Equal(0, count);
        Assert.Empty(dialog.FileNames);
    }

    [Fact]
    public void FolderOkCanRejectThenAcceptTheCompleteFolderSelection()
    {
        string[] paths = [AppContext.BaseDirectory, Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!];
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true, FolderName = "original" };
        int count = 0;
        dialog.FolderOk += (_, args) => { count++; Assert.Equal(paths, dialog.FolderNames); args.Cancel = count == 1; };
        WithProvider(options =>
        {
            Assert.True(options.Directory);
            Assert.False(options.ValidateSelection!(paths, 1));
            Assert.Equal("original", dialog.FolderName);
            Assert.True(options.ValidateSelection(paths, 1));
            return paths;
        }, () => Assert.True(dialog.ShowDialog()));
        Assert.Equal(2, count);
        Assert.Equal(paths, dialog.FolderNames);
    }

    [Fact]
    public void ExceptionsRestoreStateAndTheSameDialogCanBeShownAgain()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = PathFor("original.png"), FilterIndex = 1 };
        int count = 0;
        dialog.FileOk += (_, _) => { if (++count == 1) throw new InvalidOperationException("validation failure"); };
        WithProvider(options =>
        {
            Assert.True(options.ValidateSelection!([PathFor("accepted.txt")], 2));
            return [PathFor("accepted.txt")];
        }, () =>
        {
            Assert.Equal("validation failure", Assert.Throws<InvalidOperationException>(() => dialog.ShowDialog((nint)0x145)).Message);
            Assert.Equal(PathFor("original.png"), dialog.FileName);
            Assert.Equal(1, dialog.FilterIndex);
            Assert.True(dialog.ShowDialog((nint)0x145));
        });
        Assert.Equal(2, count);
    }

    [Fact]
    public void ReenteringTheSameInstanceIsRejectedWithoutBlockingLaterUse()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog();
        int count = 0;
        dialog.FileOk += (_, _) =>
        {
            count++;
            Assert.Throws<InvalidOperationException>(() => dialog.ShowDialog((nint)0x145));
        };
        WithProvider(options =>
        {
            Assert.True(options.ValidateSelection!([PathFor("accepted.txt")], 1));
            return [PathFor("accepted.txt")];
        }, () => { Assert.True(dialog.ShowDialog((nint)0x145)); Assert.True(dialog.ShowDialog((nint)0x145)); });
        Assert.Equal(2, count);
    }

    [Fact]
    public void AProviderCannotOverrideARejectedConfirmationOrRaiseFileOkAgain()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = PathFor("original.png"), FilterIndex = 1 };
        int count = 0;
        dialog.FileOk += (_, args) => { count++; args.Cancel = true; };
        WithProvider(options =>
        {
            Assert.False(options.ValidateSelection!([PathFor("candidate.txt")], 2));
            return [PathFor("candidate.txt")];
        }, () => Assert.False(dialog.ShowDialog((nint)0x145)));
        Assert.Equal(1, count);
        Assert.Equal(PathFor("original.png"), dialog.FileName);
        Assert.Equal(1, dialog.FilterIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeOwnerIsForwardedToOpenAndSavePanels(bool save)
    {
        Microsoft.Win32.FileDialog dialog = save ? new Microsoft.Win32.SaveFileDialog() : new Microsoft.Win32.OpenFileDialog();
        WithProvider(options => { Assert.Equal((nint)0x145, options.Owner); return null; },
            () => Assert.False(dialog.ShowDialog((nint)0x145)));
    }

    [Fact]
    public void LegacyFolderBrowserAlsoForwardsItsOwner()
    {
        var dialog = new Controls.FolderBrowserDialog();
        WithProvider(options => { Assert.Equal((nint)0x145, options.Owner); return null; },
            () => Assert.False(dialog.ShowDialog((nint)0x145)));
    }
}
