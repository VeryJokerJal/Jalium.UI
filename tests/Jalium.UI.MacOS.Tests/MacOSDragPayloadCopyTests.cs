using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.Tests;

public sealed class MacOSDragPayloadCopyTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void MissingDataDoesNotAllocateOrCopy(uint size) =>
        Assert.Null(NativePlatformWindow.CopyMacOSDragData(0, size));

    [Theory]
    [InlineData((uint)ClipboardPlatform.MaxClipboardPayloadBytes + 1)]
    [InlineData(int.MaxValue)]
    [InlineData(uint.MaxValue)]
    public void OversizedNativePayloadIsRejectedBeforeDereferencingItsPointer(uint size) =>
        Assert.Null(NativePlatformWindow.CopyMacOSDragData((nint)1, size));

    [Fact]
    public void PresentEmptyPayloadRemainsDistinctFromMissingData() =>
        Assert.Empty(Assert.IsType<byte[]>(NativePlatformWindow.CopyMacOSDragData((nint)1, 0)));

    [Fact]
    public void ValidPayloadIsCopiedAndSurvivesNativeBufferRelease()
    {
        byte[] expected = [0, 0xff, 0x23, 0x80];
        nint buffer = Marshal.AllocHGlobal(expected.Length);
        byte[]? actual;
        try
        {
            Marshal.Copy(expected, 0, buffer, expected.Length);
            actual = NativePlatformWindow.CopyMacOSDragData(buffer, (uint)expected.Length);
            Marshal.WriteByte(buffer, 1, 0);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        Assert.Equal(expected, actual);
    }
}
