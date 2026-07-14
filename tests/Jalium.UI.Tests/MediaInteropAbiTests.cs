using System.Runtime.InteropServices;
using Jalium.UI.Media.Native;

namespace Jalium.UI.Tests;

public sealed class MediaInteropAbiTests
{
    [Fact]
    public void DecoderAndRendererGpuDescriptorsHaveIdenticalV2Layout()
    {
        const int expectedSize = 216;
        Assert.Equal(
            expectedSize,
            Marshal.SizeOf<NativeMediaInterop.NativeVideoDecoderGpuDescriptor>());
        Assert.Equal(
            expectedSize,
            Marshal.SizeOf<NativeVideoSurfaceInterop.NativeVideoSurfaceDescriptor>());

        Assert.Equal(
            56,
            Marshal.OffsetOf<NativeMediaInterop.NativeVideoDecoderGpuDescriptor>(
                nameof(NativeMediaInterop.NativeVideoDecoderGpuDescriptor.Plane0)).ToInt32());
        Assert.Equal(
            184,
            Marshal.OffsetOf<NativeMediaInterop.NativeVideoDecoderGpuDescriptor>(
                nameof(NativeMediaInterop.NativeVideoDecoderGpuDescriptor.AcquireFenceFd)).ToInt32());
        Assert.Equal(
            192,
            Marshal.OffsetOf<NativeMediaInterop.NativeVideoDecoderGpuDescriptor>(
                nameof(NativeMediaInterop.NativeVideoDecoderGpuDescriptor.LifetimeContext)).ToInt32());

        Assert.Equal(
            Marshal.OffsetOf<NativeMediaInterop.NativeVideoDecoderGpuDescriptor>(
                nameof(NativeMediaInterop.NativeVideoDecoderGpuDescriptor.LifetimeReleaseCallback)),
            Marshal.OffsetOf<NativeVideoSurfaceInterop.NativeVideoSurfaceDescriptor>(
                nameof(NativeVideoSurfaceInterop.NativeVideoSurfaceDescriptor.LifetimeReleaseCallback)));
    }
}
