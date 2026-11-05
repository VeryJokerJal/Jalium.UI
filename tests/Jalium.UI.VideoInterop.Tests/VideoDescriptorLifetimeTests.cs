using System.Runtime.InteropServices;
using Jalium.UI.Media;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.VideoInterop.Tests;

public sealed class VideoDescriptorLifetimeTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Configure(int mode, int kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Counter(int index);
    private static readonly Configure Set;
    private static readonly Counter Get;

    static VideoDescriptorLifetimeTests()
    {
        var path = Environment.GetEnvironmentVariable("JALIUM_VIDEO_INTEROP_TEST_SHIM");
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException("Set JALIUM_VIDEO_INTEROP_TEST_SHIM to the CMake-built video-interop-shim test library.");
        var handle = NativeLibrary.Load(path);
        NativeLibrary.SetDllImportResolver(typeof(NativeVideoDecoder).Assembly,
            (name, _, _) => name is "jalium.native.media" or "jalium.native.core" ? handle : nint.Zero);
        Set = Marshal.GetDelegateForFunctionPointer<Configure>(NativeLibrary.GetExport(handle, "jalium_video_test_configure"));
        Get = Marshal.GetDelegateForFunctionPointer<Counter>(NativeLibrary.GetExport(handle, "jalium_video_test_counter"));
    }

    private static NativeVideoDecoder Open(int mode = 0, int kind = 3)
    {
        Set(mode, kind);
        var decoder = new NativeVideoDecoder();
        decoder.Open(new Uri("file:///video-interop-fixture.mp4"));
        return decoder;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void GpuReadReleasesDescriptorButRetainsFrameThroughSurface(int kind)
    {
        using var decoder = Open(kind: kind);
        Assert.True(decoder.MayHaveGpuOutput);
        Assert.Equal(GpuVideoFrameReadResult.Frame, decoder.TryReadGpuFrame((nint)1, out var surface, out var pts));
        Assert.NotNull(surface);
        Assert.Equal(TimeSpan.FromMilliseconds(123), pts);
        Assert.Equal((NativeVideoSurfaceKind)kind, surface!.Kind);
        Assert.Equal(1, Get(0));
        Assert.Equal(1, Get(2));
        Assert.Equal(1 << 16, Get(5));
        decoder.Dispose();
        Assert.Equal(1, Get(2));
        surface.Dispose();
        surface.Dispose();
        Assert.Equal(0, Get(2));
        Assert.Equal(1, Get(4));
    }

    [Fact]
    public void LegacyAcquisitionAlsoReleasesItsDescriptor()
    {
        using var decoder = Open();
        using var surface = decoder.AcquireGpuSurface((nint)1);
        Assert.NotNull(surface);
        Assert.Equal(1, Get(0));
        Assert.Equal(1, Get(2));
    }

    [Theory]
    [InlineData(1, 1)] // renderer rejects the import
    [InlineData(2, 0)] // invalid dimensions reject before entering native wrap
    public void RejectedGpuFrameReleasesDescriptorAndFallsBackAtSameTimestamp(int mode, int wrapCalls)
    {
        using var decoder = Open(mode);
        Assert.Equal(GpuVideoFrameReadResult.FellBackToCpu, decoder.TryReadGpuFrame((nint)1, out var surface, out _));
        Assert.Null(surface);
        Assert.Equal(1, Get(0));
        Assert.Equal(wrapCalls, Get(1));
        Assert.Equal(0, Get(2));
        Assert.Equal(1, Get(3));
        Assert.False(decoder.MayHaveGpuOutput);
        Assert.True(decoder.TryReadFrame(out var frame));
        Assert.NotNull(frame);
        frame!.Dispose();
    }

    [Fact]
    public void InvalidLegacyDescriptorIsReleasedBeforeReturningNull()
    {
        using var decoder = Open(2);
        Assert.Null(decoder.AcquireGpuSurface((nint)1));
        Assert.Equal(1, Get(0));
        Assert.Equal(0, Get(2));
    }

    [Fact]
    public void UnsupportedGpuPathDoesNotConsumeOrDisableCpuOutput()
    {
        using var decoder = Open(3);
        Assert.Equal(GpuVideoFrameReadResult.NotSupported, decoder.TryReadGpuFrame((nint)1, out _, out _));
        Assert.False(decoder.MayHaveGpuOutput);
        Assert.Equal(0, Get(0));
        Assert.Equal(0, Get(3));
    }

    [Fact]
    public void GpuEndOfStreamDoesNotTriggerFallback()
    {
        using var decoder = Open(4);
        Assert.Equal(GpuVideoFrameReadResult.EndOfStream, decoder.TryReadGpuFrame((nint)1, out _, out _));
        Assert.Equal(0, Get(0));
        Assert.Equal(0, Get(3));
    }

    [Fact]
    public void DecoderFailureUsesCpuFallback()
    {
        using var decoder = Open(5);
        Assert.Equal(GpuVideoFrameReadResult.FellBackToCpu, decoder.TryReadGpuFrame((nint)1, out _, out _));
        Assert.Equal(1, Get(3));
    }

    [Fact]
    public void FailedCpuRecoveryIsReportedToCaller()
    {
        using var decoder = Open(6);
        var error = Assert.Throws<NativeMediaException>(() => decoder.TryReadGpuFrame((nint)1, out _, out _));
        Assert.Equal("jalium_video_decoder_disable_gpu_output", error.Operation);
        Assert.Equal(NativeMediaStatus.PlatformError, error.Status);
    }
}
