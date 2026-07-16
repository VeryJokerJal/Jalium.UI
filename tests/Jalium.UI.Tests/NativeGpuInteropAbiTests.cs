using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed class NativeGpuInteropAbiTests
{
    [Fact]
    public void AdapterInfo_HasStableFixedWidthUtf16Layout()
    {
        Assert.Equal(288, Marshal.SizeOf<AdapterInfo>());
        Assert.Equal(0, Marshal.OffsetOf<AdapterInfo>(nameof(AdapterInfo.Name)).ToInt32());
        Assert.Equal(256, Marshal.OffsetOf<AdapterInfo>(nameof(AdapterInfo.AdapterType)).ToInt32());
        Assert.Equal(264, Marshal.OffsetOf<AdapterInfo>(nameof(AdapterInfo.DedicatedVideoMemory)).ToInt32());
        Assert.Equal(272, Marshal.OffsetOf<AdapterInfo>(nameof(AdapterInfo.SharedSystemMemory)).ToInt32());
        Assert.Equal(280, Marshal.OffsetOf<AdapterInfo>(nameof(AdapterInfo.VendorId)).ToInt32());
        Assert.Equal(284, Marshal.OffsetOf<AdapterInfo>(nameof(AdapterInfo.DeviceId)).ToInt32());
    }

    [Fact]
    public void SoftwareContext_GetAdapterInfo_ReturnsNullWithoutCorruptingInteropFrame()
    {
        using var context = new RenderContext(RenderBackend.Software);

        Assert.Null(context.GetAdapterInfo());
    }
}
