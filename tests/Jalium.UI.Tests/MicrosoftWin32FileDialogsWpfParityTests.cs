using System.ComponentModel;
using System.Reflection;
using CanonicalCommonDialog = Microsoft.Win32.CommonDialog;
using CanonicalCommonItemDialog = Microsoft.Win32.CommonItemDialog;
using CanonicalFileDialog = Microsoft.Win32.FileDialog;
using CanonicalFileDialogCustomPlace = Microsoft.Win32.FileDialogCustomPlace;
using CanonicalFileDialogCustomPlaces = Microsoft.Win32.FileDialogCustomPlaces;
using CanonicalOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using CanonicalOpenFolderDialog = Microsoft.Win32.OpenFolderDialog;
using CanonicalSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Jalium.UI.Tests;

public class MicrosoftWin32FileDialogsWpfParityTests
{
    [Fact]
    public void CanonicalTypes_HaveWpfHierarchyAndSurface()
    {
        Assert.True(typeof(CanonicalCommonDialog).IsAbstract);
        Assert.Equal(typeof(CanonicalCommonDialog), typeof(CanonicalCommonItemDialog).BaseType);
        Assert.Equal(typeof(CanonicalCommonItemDialog), typeof(CanonicalFileDialog).BaseType);
        Assert.Equal(typeof(CanonicalFileDialog), typeof(CanonicalOpenFileDialog).BaseType);
        Assert.Equal(typeof(CanonicalFileDialog), typeof(CanonicalSaveFileDialog).BaseType);
        Assert.Equal(typeof(CanonicalCommonItemDialog), typeof(CanonicalOpenFolderDialog).BaseType);
        Assert.True(typeof(CanonicalOpenFileDialog).IsSealed);
        Assert.True(typeof(CanonicalSaveFileDialog).IsSealed);
        Assert.True(typeof(CanonicalOpenFolderDialog).IsSealed);

        Assert.Equal(typeof(CancelEventHandler),
            typeof(CanonicalFileDialog).GetEvent(nameof(CanonicalFileDialog.FileOk))!.EventHandlerType);
        Assert.Equal(typeof(CancelEventHandler),
            typeof(CanonicalOpenFolderDialog).GetEvent(nameof(CanonicalOpenFolderDialog.FolderOk))!.EventHandlerType);
        Assert.Equal(typeof(Stream),
            typeof(CanonicalOpenFileDialog).GetMethod(nameof(CanonicalOpenFileDialog.OpenFile))!.ReturnType);
        Assert.Equal(typeof(Stream[]),
            typeof(CanonicalOpenFileDialog).GetMethod(nameof(CanonicalOpenFileDialog.OpenFiles))!.ReturnType);
        Assert.Equal(typeof(Stream),
            typeof(CanonicalSaveFileDialog).GetMethod(nameof(CanonicalSaveFileDialog.OpenFile))!.ReturnType);

        MethodInfo ownerOverload = typeof(CanonicalCommonDialog).GetMethod(
            nameof(CanonicalCommonDialog.ShowDialog),
            [typeof(Jalium.UI.Controls.Window)])!;
        Assert.Equal(typeof(bool?), ownerOverload.ReturnType);
    }

    [Fact]
    public void FileDialogs_DefaultAndResetStateMatchesWpf()
    {
        var open = new CanonicalOpenFileDialog();
        Assert.True(open.AddExtension);
        Assert.True(open.AddToRecent);
        Assert.True(open.CheckFileExists);
        Assert.True(open.CheckPathExists);
        Assert.True(open.CreateExpectedDefaults());

        open.DefaultExt = ".txt";
        open.Filter = "Text|*.txt";
        open.FilterIndex = 3;
        open.FileName = @"C:\temp\example.txt";
        open.Multiselect = true;
        open.ForcePreviewPane = true;
        open.Reset();

        Assert.Equal(string.Empty, open.DefaultExt);
        Assert.Equal(string.Empty, open.Filter);
        Assert.Equal(1, open.FilterIndex);
        Assert.Equal(string.Empty, open.FileName);
        Assert.Empty(open.FileNames);
        Assert.False(open.Multiselect);
        Assert.False(open.ForcePreviewPane);
        Assert.True(open.CheckFileExists);

        var save = new CanonicalSaveFileDialog();
        Assert.True(save.CreateTestFile);
        Assert.True(save.OverwritePrompt);
        Assert.False(save.CreatePrompt);
        Assert.False(save.CheckFileExists);
    }

    [Fact]
    public void FilterAndCustomPlaces_UseWpfContracts()
    {
        var dialog = new CanonicalOpenFileDialog();
        Assert.Throws<ArgumentException>(() => dialog.Filter = "Missing pattern");
        dialog.Filter = "Text|*.txt|All files|*.*";
        Assert.Equal("Text|*.txt|All files|*.*", dialog.Filter);

        var pathPlace = new CanonicalFileDialogCustomPlace((string)null!);
        Assert.Equal(string.Empty, pathPlace.Path);
        Assert.Equal(Guid.Empty, pathPlace.KnownFolder);

        CanonicalFileDialogCustomPlace documents = CanonicalFileDialogCustomPlaces.Documents;
        Assert.Equal(new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), documents.KnownFolder);
        Assert.Null(documents.Path);
        Assert.NotSame(documents, CanonicalFileDialogCustomPlaces.Documents);

        PropertyInfo knownFolder = typeof(CanonicalFileDialogCustomPlace)
            .GetProperty(nameof(CanonicalFileDialogCustomPlace.KnownFolder))!;
        Assert.NotNull(knownFolder.SetMethod);
        Assert.True(knownFolder.SetMethod!.IsPrivate);
    }
}

internal static class MicrosoftWin32FileDialogTestExtensions
{
    internal static bool CreateExpectedDefaults(this CanonicalOpenFileDialog dialog) =>
        dialog.DefaultDirectory == string.Empty &&
        dialog.InitialDirectory == string.Empty &&
        dialog.RootDirectory == string.Empty &&
        dialog.Title == string.Empty &&
        dialog.ValidateNames &&
        dialog.DereferenceLinks &&
        !dialog.ShowHiddenItems &&
        dialog.CustomPlaces.Count == 0;
}
