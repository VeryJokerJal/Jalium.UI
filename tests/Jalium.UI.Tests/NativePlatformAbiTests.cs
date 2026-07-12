using System.Reflection;
using System.Runtime.InteropServices;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

public sealed class NativePlatformAbiTests
{
    [Fact]
    public void PlatformValues_PreserveExistingAbiAndAppendWayland()
    {
        Assert.Equal(2, (int)NativePlatform.LinuxX11);
        Assert.Equal(5, (int)NativePlatform.LinuxWayland);
    }

    [Fact]
    public void SurfaceDescriptor_HasStableNativeLayout()
    {
        Assert.Equal(0, Marshal.OffsetOf<NativeSurfaceDescriptor>(nameof(NativeSurfaceDescriptor.Platform)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<NativeSurfaceDescriptor>(nameof(NativeSurfaceDescriptor.Kind)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<NativeSurfaceDescriptor>(nameof(NativeSurfaceDescriptor.Handle0)).ToInt32());
        Assert.Equal(8 + nint.Size, Marshal.OffsetOf<NativeSurfaceDescriptor>(nameof(NativeSurfaceDescriptor.Handle1)).ToInt32());
        Assert.Equal(8 + (2 * nint.Size), Marshal.OffsetOf<NativeSurfaceDescriptor>(nameof(NativeSurfaceDescriptor.Handle2)).ToInt32());
        Assert.Equal(8 + (3 * nint.Size), Marshal.SizeOf<NativeSurfaceDescriptor>());
    }

    [Fact]
    public void WindowParams_HasStableFixedWidthTitleLayout()
    {
        Assert.Equal(0, Marshal.OffsetOf<NativePlatformWindowParams>(nameof(NativePlatformWindowParams.Title)).ToInt32());
        Assert.Equal(nint.Size, Marshal.OffsetOf<NativePlatformWindowParams>(nameof(NativePlatformWindowParams.X)).ToInt32());
        Assert.Equal(nint.Size + 16, Marshal.OffsetOf<NativePlatformWindowParams>(nameof(NativePlatformWindowParams.Style)).ToInt32());

        int unalignedParentOffset = nint.Size + 20;
        int parentOffset = (unalignedParentOffset + nint.Size - 1) & ~(nint.Size - 1);
        Assert.Equal(parentOffset, Marshal.OffsetOf<NativePlatformWindowParams>(nameof(NativePlatformWindowParams.ParentHandle)).ToInt32());
        Assert.Equal(parentOffset + nint.Size, Marshal.SizeOf<NativePlatformWindowParams>());
    }

    [Fact]
    public void DragDataItem_HasStablePointerAndLengthLayout()
    {
        Assert.Equal(0, Marshal.OffsetOf<NativeDragDataItem>(nameof(NativeDragDataItem.MimeType)).ToInt32());
        Assert.Equal(nint.Size, Marshal.OffsetOf<NativeDragDataItem>(nameof(NativeDragDataItem.Data)).ToInt32());
        Assert.Equal(2 * nint.Size, Marshal.OffsetOf<NativeDragDataItem>(nameof(NativeDragDataItem.DataSize)).ToInt32());
        int expectedSize = ((2 * nint.Size + sizeof(uint)) + nint.Size - 1) & ~(nint.Size - 1);
        Assert.Equal(expectedSize, Marshal.SizeOf<NativeDragDataItem>());
    }

    [Fact]
    public void DragEvents_AreAppendedWithoutRenumberingExistingEvents()
    {
        Assert.Equal(70, (int)PlatformEventType.DispatcherWake);
        Assert.Equal(80, (int)PlatformEventType.DragEnter);
        Assert.Equal(81, (int)PlatformEventType.DragOver);
        Assert.Equal(82, (int)PlatformEventType.DragLeave);
        Assert.Equal(83, (int)PlatformEventType.Drop);
        Assert.Equal(84, (int)PlatformEventType.DragFinished);
        Assert.Equal(99, (int)PlatformEventType.Quit);
    }

    [Theory]
    [InlineData("WindowSetTitle")]
    [InlineData("ClipboardSetText")]
    public void TextEntryPoints_ExplicitlyMarshalUtf16(string methodName)
    {
        MethodInfo method = typeof(NativeMethods).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Native method {methodName} was not found.");

        LibraryImportAttribute attribute = method.GetCustomAttribute<LibraryImportAttribute>()
            ?? throw new InvalidOperationException($"Native method {methodName} has no LibraryImport attribute.");

        Assert.Equal(StringMarshalling.Utf16, attribute.StringMarshalling);
    }

    [Theory]
    [InlineData(0x4E2D, "中")]
    [InlineData(0x1F600, "😀")]
    public void LinuxCharacterCodepoints_AreConvertedWithRune(uint codepoint, string expected)
    {
        Assert.Equal(expected, NativePlatformWindow.CodepointToText(codepoint));
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0x110000)]
    [InlineData(0x7F)]
    public void InvalidOrControlCodepoints_AreRejected(uint codepoint)
    {
        Assert.Null(NativePlatformWindow.CodepointToText(codepoint));
    }
}
