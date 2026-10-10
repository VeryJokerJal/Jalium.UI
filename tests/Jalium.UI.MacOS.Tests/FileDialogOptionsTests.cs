using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class FileDialogOptionsTests
{
    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "dialog-options-" + Guid.NewGuid().ToString("N"));
        public string Inside => Path.Combine(Root, "inside");
        public string Outside => Root + "-other";
        public string File => Path.Combine(Inside, "chosen.txt");
        public Files()
        {
            Directory.CreateDirectory(Inside); Directory.CreateDirectory(Outside);
            System.IO.File.WriteAllText(File, "owned test");
        }
        public void Dispose() { Directory.Delete(Root, true); Directory.Delete(Outside, true); }
    }
    private static void WithProvider(Func<PlatformFileDialogOptions, string[]?> show, Action action)
    {
        var previous = PlatformFileDialogs.Show;
        try { PlatformFileDialogs.Show = show; action(); }
        finally { PlatformFileDialogs.Show = previous; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CommonOptionsRemainDistinctAndReachAllThreeNativeChooserKinds(int kind)
    {
        Microsoft.Win32.CommonItemDialog dialog = kind switch
        {
            0 => new Microsoft.Win32.OpenFileDialog(), 1 => new Microsoft.Win32.SaveFileDialog(),
            _ => new Microsoft.Win32.OpenFolderDialog()
        };
        dialog.InitialDirectory = "initial"; dialog.DefaultDirectory = "fallback"; dialog.RootDirectory = "root";
        dialog.ShowHiddenItems = true;
        dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace("custom"));
        var id = new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
        dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace(id));
        WithProvider(options =>
        {
            Assert.Equal("initial", options.InitialDirectory); Assert.Equal("fallback", options.DefaultDirectory);
            Assert.Equal("root", options.RootDirectory); Assert.True(options.ShowHiddenItems);
            Assert.Equal(("custom", Guid.Empty), options.CustomPlaces[0]);
            Assert.Equal((null, id), options.CustomPlaces[1]);
            Assert.Equal(kind == 1, options.Save); Assert.Equal(kind == 2, options.Directory);
            return null;
        }, () => Assert.False(dialog.ShowDialog()));
    }

    [Fact]
    public void ReadOnlyChoiceIsPublishedForVetoAndRetryAndOnlyAcceptedStateSurvives()
    {
        using var files = new Files();
        var dialog = new Microsoft.Win32.OpenFileDialog { ShowReadOnly = true, FileName = "original" };
        int calls = 0;
        dialog.FileOk += (_, args) => { calls++; Assert.True(dialog.ReadOnlyChecked); args.Cancel = calls == 1; };
        WithProvider(options =>
        {
            Assert.True(options.ShowReadOnly); Assert.False(options.ReadOnlyChecked);
            options.ReadOnlyChecked = true;
            Assert.False(options.ValidateSelection!([files.File], 1));
            Assert.False(dialog.ReadOnlyChecked); Assert.Equal("original", dialog.FileName);
            Assert.True(options.ValidateSelection([files.File], 1));
            return [files.File];
        }, () => Assert.True(dialog.ShowDialog()));
        Assert.True(dialog.ReadOnlyChecked); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelingThePanelRestoresReadOnlyEvenAfterAnApprovedCandidate(bool approved)
    {
        using var files = new Files();
        var dialog = new Microsoft.Win32.OpenFileDialog { ReadOnlyChecked = false, FileName = "original" };
        dialog.FileOk += (_, args) => { Assert.True(dialog.ReadOnlyChecked); args.Cancel = !approved; };
        WithProvider(options =>
        {
            options.ReadOnlyChecked = true;
            Assert.Equal(approved, options.ValidateSelection!([files.File], 1));
            return null;
        }, () => Assert.False(dialog.ShowDialog()));
        Assert.False(dialog.ReadOnlyChecked); Assert.Equal("original", dialog.FileName);
    }

    [Fact]
    public void ReadOnlyHandlerMutationIsRetainedAndExceptionRollsBack()
    {
        using var files = new Files();
        var dialog = new Microsoft.Win32.OpenFileDialog();
        int calls = 0;
        dialog.FileOk += (_, _) =>
        {
            Assert.True(dialog.ReadOnlyChecked);
            dialog.ReadOnlyChecked = false;
            if (++calls == 1) throw new InvalidOperationException("owned option failure");
        };
        WithProvider(options =>
        {
            options.ReadOnlyChecked = true;
            Assert.True(options.ValidateSelection!([files.File], 1));
            return [files.File];
        }, () =>
        {
            Assert.Throws<InvalidOperationException>(() => dialog.ShowDialog());
            Assert.False(dialog.ReadOnlyChecked);
            Assert.True(dialog.ShowDialog());
        });
        Assert.False(dialog.ReadOnlyChecked); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvidersWithoutConfirmationStillPublishAndRollbackReadOnlyAtomically(bool cancel)
    {
        using var files = new Files();
        var dialog = new Microsoft.Win32.OpenFileDialog { ShowReadOnly = true };
        dialog.FileOk += (_, args) => { Assert.True(dialog.ReadOnlyChecked); args.Cancel = cancel; };
        WithProvider(options => { options.ReadOnlyChecked = true; return [files.File]; },
            () => Assert.Equal(!cancel, dialog.ShowDialog()));
        Assert.Equal(!cancel, dialog.ReadOnlyChecked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootRejectsAnOutsideBatchBeforeFileOkAndAfterAProviderReturn(bool replayValidation)
    {
        using var files = new Files();
        string outside = Path.Combine(files.Outside, "chosen.txt"); System.IO.File.WriteAllText(outside, "owned test");
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, RootDirectory = files.Root, FileName = "original" };
        int calls = 0; dialog.FileOk += (_, _) => calls++;
        WithProvider(options =>
        {
            if (replayValidation) Assert.False(options.ValidateSelection!([files.File, outside], 1));
            return [files.File, outside];
        }, () => Assert.False(dialog.ShowDialog()));
        Assert.Equal("original", dialog.FileName); Assert.Equal(0, calls);
    }

    [Fact]
    public void AProviderReturningOneExistingAndOneMissingFileCannotPublishAPartialBatch()
    {
        using var files = new Files();
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, FileName = "original" };
        int calls = 0; dialog.FileOk += (_, _) => calls++;
        WithProvider(_ => [files.File, Path.Combine(files.Inside, "absent.txt")], () => Assert.False(dialog.ShowDialog()));
        Assert.Equal("original", dialog.FileName); Assert.Equal(0, calls);
    }

    [Fact]
    public void NavigationChecksRootIdentityAndSeparatorsAndClampsAllInitialCandidates()
    {
        using var files = new Files();
        var navigation = new FileDialogNavigation(files.Root + Path.DirectorySeparatorChar);
        Assert.Equal(files.Root, navigation.Root); Assert.True(navigation.Contains(files.Root));
        Assert.True(navigation.Contains(files.Inside)); Assert.False(navigation.Contains(files.Outside));
        Assert.False(navigation.Contains(Path.Combine(files.Inside, "..", "..", Path.GetFileName(files.Outside))));
        Assert.True(navigation.IsAncestor(Path.GetDirectoryName(files.Root)));
        Assert.False(navigation.IsAncestor(files.Inside));
        Assert.Equal(files.Root, navigation.InitialDirectory(files.Outside, null, files.Outside, files.Outside));
        Assert.Equal(files.Inside, navigation.InitialDirectory(files.Outside, null, files.Inside, files.Outside));
    }

    [Fact]
    public void InitialAndFileDirectoriesTakePrecedenceAndDefaultRemainsAFallback()
    {
        using var files = new Files();
        var navigation = new FileDialogNavigation(null);
        Assert.Equal(files.Inside, navigation.InitialDirectory(files.Inside, null, files.Root, files.Outside));
        Assert.Equal(files.Inside, navigation.InitialDirectory(null, files.File, files.Root, files.Outside));
        Assert.Equal(files.Outside, navigation.InitialDirectory(null, "name.txt", files.Root, files.Outside));
        Assert.Equal(files.Root, navigation.InitialDirectory("missing", "name.txt", files.Root, "missing"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing-root")]
    [InlineData("bad\0root")]
    public void UnusableRootsDoNotInventANavigationRestriction(string? root)
    {
        using var files = new Files();
        var navigation = new FileDialogNavigation(root);
        Assert.Null(navigation.Root); Assert.True(navigation.Contains(files.File));
        Assert.False(navigation.Contains("bad\0name"));
    }

    [Fact]
    public void ShortcutPlacesKeepOrderDeduplicateAndIgnoreMissingOutsideAndUnknownFolders()
    {
        using var files = new Files();
        var navigation = new FileDialogNavigation(files.Root);
        Assert.Equal(new[] { files.Root, files.Inside }, navigation.Places(new (string?, Guid)[]
        {
            (files.Inside, Guid.Empty), (files.Inside + Path.DirectorySeparatorChar, Guid.Empty),
            (files.Root, Guid.Empty), (files.Outside, Guid.Empty), ("missing", Guid.Empty), (null, Guid.NewGuid())
        }));
    }

    [Fact]
    public void KnownDocumentPlaceResolvesToTheCurrentPlatformDirectory()
    {
        string directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var navigation = new FileDialogNavigation(null);
        string[] expected = Directory.Exists(directory) ? [Path.GetFullPath(directory)] : [];
        Assert.Equal(expected, navigation.Places(new (string?, Guid)[] { (null, new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7")) }));
    }

    [Fact]
    public void ResetClearsAllCanonicalOptionAndReadOnlyState()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = "initial", DefaultDirectory = "fallback", RootDirectory = "root",
            ShowHiddenItems = true, ShowReadOnly = true, ReadOnlyChecked = true
        };
        dialog.CustomPlaces.Add(new Microsoft.Win32.FileDialogCustomPlace("custom")); dialog.Reset();
        Assert.Empty(dialog.InitialDirectory); Assert.Empty(dialog.DefaultDirectory); Assert.Empty(dialog.RootDirectory);
        Assert.False(dialog.ShowHiddenItems); Assert.False(dialog.ShowReadOnly); Assert.False(dialog.ReadOnlyChecked); Assert.Empty(dialog.CustomPlaces);
    }
}
