using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;

namespace Jalium.UI.Tests;

public sealed class EditorTextChangeOwnershipTests
{
    [Fact]
    public void TextChange_HasOneCanonicalWpfPublicOwner()
    {
        Assembly assembly = typeof(TextChange).Assembly;
        Type textChange = Assert.Single(
            assembly.GetExportedTypes(),
            type => type.Name == nameof(TextChange));

        Assert.Equal(typeof(TextChange), textChange);
        Assert.Equal("Jalium.UI.Controls", textChange.Namespace);
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Editor.TextChange"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Editor.TextChangeEventArgs"));
    }

    [Fact]
    public void EditorDocument_UsesExplicitDocumentChangeContracts()
    {
        Assert.Equal("Jalium.UI.Controls.Editor", typeof(DocumentChange).Namespace);
        Assert.Equal("Jalium.UI.Controls.Editor", typeof(DocumentChangeEventArgs).Namespace);

        EventInfo changed = typeof(TextDocument).GetEvent(nameof(TextDocument.Changed))!;
        Assert.NotNull(changed);
        Assert.Equal(typeof(EventHandler<DocumentChangeEventArgs>), changed.EventHandlerType);
        EventInfo editChanged = typeof(EditControl).GetEvent(nameof(EditControl.TextChanged))!;
        Assert.NotNull(editChanged);
        Assert.Equal(typeof(EventHandler<DocumentChangeEventArgs>), editChanged.EventHandlerType);

        var document = new TextDocument("abcd");
        DocumentChangeEventArgs? observed = null;
        document.Changed += (_, args) => observed = args;

        document.Replace(1, 2, "XY");

        Assert.NotNull(observed);
        Assert.Equal(1, observed!.Offset);
        Assert.Equal("bc", observed.RemovedText);
        Assert.Equal("XY", observed.InsertedText);
        DocumentChange inverse = observed.Change.CreateInverse();
        Assert.Equal("XY", inverse.RemovedText);
        Assert.Equal("bc", inverse.InsertedText);
    }
}
