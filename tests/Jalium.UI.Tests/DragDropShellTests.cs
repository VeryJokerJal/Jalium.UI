using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the Windows Shell drag-and-drop enhancements added for issue #150:
/// the caller-supplied drag image plumbing on the drag source, and the drop
/// description hook exposed to drop targets. The COM / <c>IDropTargetHelper</c>
/// side effects require a live OLE drag and are exercised interactively; these
/// tests pin the managed contract that the platform layer builds on.
/// </summary>
[Collection("Application")]
public class DragDropShellTests
{
    #region DropImageType

    [Theory]
    [InlineData(DropImageType.Invalid, -1)]
    [InlineData(DropImageType.None, 0)]
    [InlineData(DropImageType.Copy, 1)]
    [InlineData(DropImageType.Move, 2)]
    [InlineData(DropImageType.Link, 4)]
    [InlineData(DropImageType.Label, 6)]
    [InlineData(DropImageType.Warning, 7)]
    [InlineData(DropImageType.NoImage, 8)]
    public void DropImageType_MatchesWin32DropImageTypeConstants(DropImageType type, int expected)
    {
        Assert.Equal(expected, (int)type);
    }

    #endregion

    #region Drag image attached properties

    [Fact]
    public void DragImage_AttachedProperty_DefaultsToNull()
    {
        var element = new Border();
        Assert.Null(DragDrop.GetDragImage(element));
    }

    [Fact]
    public void DragImage_AttachedProperty_RoundTrips()
    {
        var element = new Border();
        var image = new object();

        DragDrop.SetDragImage(element, image);

        Assert.Same(image, DragDrop.GetDragImage(element));
    }

    [Fact]
    public void DragImageOffset_AttachedProperty_DefaultsToOrigin()
    {
        var element = new Border();

        var offset = DragDrop.GetDragImageOffset(element);

        Assert.Equal(0.0, offset.X, 6);
        Assert.Equal(0.0, offset.Y, 6);
    }

    [Fact]
    public void DragImageOffset_AttachedProperty_RoundTrips()
    {
        var element = new Border();

        DragDrop.SetDragImageOffset(element, new Point(12, 34));

        var offset = DragDrop.GetDragImageOffset(element);
        Assert.Equal(12.0, offset.X, 6);
        Assert.Equal(34.0, offset.Y, 6);
    }

    #endregion

    #region DoDragDrop image overloads

    [Fact]
    public void DoDragDrop_WithImage_PlumbsPendingImageWithoutOffset()
    {
        var source = new Border();
        var image = new object();

        object? seenImage = null;
        bool seenHasOffset = true;
        Point seenOffset = new(-1, -1);

        RunWithOverride(
            capture: () =>
            {
                seenImage = DragDrop.PendingDragImage;
                seenHasOffset = DragDrop.HasPendingDragImageOffset;
                seenOffset = DragDrop.PendingDragImageOffset;
            },
            body: () => DragDrop.DoDragDrop(source, "payload", DragDropEffects.Copy, image));

        Assert.Same(image, seenImage);
        Assert.False(seenHasOffset);
        Assert.Equal(0.0, seenOffset.X, 6);
        Assert.Equal(0.0, seenOffset.Y, 6);
    }

    [Fact]
    public void DoDragDrop_WithImageAndOffset_PlumbsPendingOffset()
    {
        var source = new Border();
        var image = new object();

        object? seenImage = null;
        bool seenHasOffset = false;
        Point seenOffset = default;

        RunWithOverride(
            capture: () =>
            {
                seenImage = DragDrop.PendingDragImage;
                seenHasOffset = DragDrop.HasPendingDragImageOffset;
                seenOffset = DragDrop.PendingDragImageOffset;
            },
            body: () => DragDrop.DoDragDrop(source, "payload", DragDropEffects.Move, image, new Point(7, 9)));

        Assert.Same(image, seenImage);
        Assert.True(seenHasOffset);
        Assert.Equal(7.0, seenOffset.X, 6);
        Assert.Equal(9.0, seenOffset.Y, 6);
    }

    [Fact]
    public void DoDragDrop_WithImage_ClearsPendingStateAfterCompletion()
    {
        var source = new Border();

        RunWithOverride(
            capture: () => { },
            body: () => DragDrop.DoDragDrop(source, "payload", DragDropEffects.Copy, new object(), new Point(3, 4)));

        Assert.Null(DragDrop.PendingDragImage);
        Assert.False(DragDrop.HasPendingDragImageOffset);
        Assert.Equal(0.0, DragDrop.PendingDragImageOffset.X, 6);
        Assert.Equal(0.0, DragDrop.PendingDragImageOffset.Y, 6);
    }

    #endregion

    #region Drop description hook

    [Fact]
    public void SetDropDescription_InvokesInstalledHookWithArguments()
    {
        var args = new DragEventArgs(DragDrop.DropEvent, new DataObject(), DragDropKeyStates.None, DragDropEffects.Copy, default);

        DropImageType seenType = DropImageType.Invalid;
        string? seenMessage = null;
        string? seenInsert = null;
        int calls = 0;

        args.DropDescriptionSetter = (type, message, insert) =>
        {
            seenType = type;
            seenMessage = message;
            seenInsert = insert;
            calls++;
        };

        args.SetDropDescription(DropImageType.Copy, "复制到 %1", "文档");

        Assert.Equal(1, calls);
        Assert.Equal(DropImageType.Copy, seenType);
        Assert.Equal("复制到 %1", seenMessage);
        Assert.Equal("文档", seenInsert);
    }

    [Fact]
    public void ClearDropDescription_InvokesHookWithInvalidType()
    {
        var args = new DragEventArgs(DragDrop.DropEvent, new DataObject(), DragDropKeyStates.None, DragDropEffects.Copy, default);

        DropImageType seenType = DropImageType.Copy;
        args.DropDescriptionSetter = (type, _, _) => seenType = type;

        args.ClearDropDescription();

        Assert.Equal(DropImageType.Invalid, seenType);
    }

    [Fact]
    public void SetDropDescription_WithoutHook_IsSilentNoOp()
    {
        // In-app drags never install a native hook; the call must not throw.
        var args = new DragEventArgs(DragDrop.DropEvent, new DataObject(), DragDropKeyStates.None, DragDropEffects.Copy, default);

        var exception = Record.Exception(() => args.SetDropDescription(DropImageType.Move, "move here"));

        Assert.Null(exception);
    }

    #endregion

    #region DoShellDragDrop (real OLE drag-out routing)

    [Fact]
    public void DoShellDragDrop_RoutesToShellOverride_AndWrapsBareData()
    {
        var source = new Border();
        IDataObject? seen = null;

        var previous = DragDrop.DoShellDragDropOverride;
        DragDrop.DoShellDragDropOverride = (_, data, _) => { seen = data; return DragDropEffects.Move; };
        try
        {
            var result = DragDrop.DoShellDragDrop(source, "hello", DragDropEffects.Move);

            Assert.Equal(DragDropEffects.Move, result);
            Assert.NotNull(seen);
            Assert.Equal("hello", seen!.GetData(DataFormats.UnicodeText));
        }
        finally { DragDrop.DoShellDragDropOverride = previous; }
    }

    [Fact]
    public void DoShellDragDrop_PlumbsPendingImage_AndClearsAfter()
    {
        var source = new Border();
        var image = new object();

        object? seenImage = null;
        Point seenOffset = default;

        var previous = DragDrop.DoShellDragDropOverride;
        DragDrop.DoShellDragDropOverride = (_, _, _) =>
        {
            seenImage = DragDrop.PendingDragImage;
            seenOffset = DragDrop.PendingDragImageOffset;
            return DragDropEffects.Copy;
        };
        try
        {
            DragDrop.DoShellDragDrop(source, "payload", DragDropEffects.Copy, image, new Point(5, 6));
        }
        finally { DragDrop.DoShellDragDropOverride = previous; }

        Assert.Same(image, seenImage);
        Assert.Equal(5.0, seenOffset.X, 6);
        Assert.Equal(6.0, seenOffset.Y, 6);

        // Pending state must not leak to the next drag.
        Assert.Null(DragDrop.PendingDragImage);
        Assert.Equal(0.0, DragDrop.PendingDragImageOffset.X, 6);
        Assert.Equal(0.0, DragDrop.PendingDragImageOffset.Y, 6);
    }

    [Fact]
    public void DoShellDragDrop_FallsBackToManagedOverride_WhenNoShellSource()
    {
        var source = new Border();
        bool managedCalled = false;

        var previousShell = DragDrop.DoShellDragDropOverride;
        var previousManaged = DragDrop.DoDragDropOverride;
        DragDrop.DoShellDragDropOverride = null;
        DragDrop.DoDragDropOverride = (_, _, _) => { managedCalled = true; return DragDropEffects.Link; };
        try
        {
            var result = DragDrop.DoShellDragDrop(source, "payload", DragDropEffects.Link);

            Assert.True(managedCalled);
            Assert.Equal(DragDropEffects.Link, result);
        }
        finally
        {
            DragDrop.DoShellDragDropOverride = previousShell;
            DragDrop.DoDragDropOverride = previousManaged;
        }
    }

    [Fact]
    public void DoShellDragDrop_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DragDrop.DoShellDragDrop(null!, "x", DragDropEffects.Copy));
    }

    [Fact]
    public void DoShellDragDrop_NullData_Throws()
    {
        var source = new Border();
        Assert.Throws<ArgumentNullException>(
            () => DragDrop.DoShellDragDrop(source, null!, DragDropEffects.Copy));
    }

    #endregion

    /// <summary>
    /// Installs a temporary <see cref="DragDrop.DoDragDropOverride"/> that runs
    /// <paramref name="capture"/> (observing the pending drag-image statics) and
    /// returns a copy effect, invokes <paramref name="body"/>, then restores the
    /// previous override so global state never leaks to other tests.
    /// </summary>
    private static void RunWithOverride(Action capture, Action body)
    {
        var previous = DragDrop.DoDragDropOverride;
        DragDrop.DoDragDropOverride = (_, _, _) =>
        {
            capture();
            return DragDropEffects.Copy;
        };
        try
        {
            body();
        }
        finally
        {
            DragDrop.DoDragDropOverride = previous;
        }
    }
}
