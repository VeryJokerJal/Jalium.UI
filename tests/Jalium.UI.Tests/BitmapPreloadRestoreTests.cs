using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class BitmapPreloadRestoreTests
{
    [Fact]
    public async Task ReclaimedEagerPreloadsShareAnOffThreadRestore()
    {
        var previous = BitmapImage.ResolveDecoder();
        var decoder = new Decoder();
        BitmapImage.SetDecoder(decoder);
        Task? restore = null;
        try
        {
            var image = BitmapImage.FromBytes([1]); image.Freeze();
            int loaded = 0; image.OnImageLoaded += (_, _) => Interlocked.Increment(ref loaded);
            var expected = image.RawPixelData!.ToArray();
            image.ReclaimIdleResources(); Assert.Null(image.RawPixelData);
            int caller = Environment.CurrentManagedThreadId;
            restore = image.PreloadAsync(4, 3);
            Assert.False(restore.IsCompleted);
            await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(caller, decoder.RestoreThread);
            Assert.Same(restore, image.PreloadAsync(4, 3));
            decoder.Release.Set();
            await restore.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(expected, image.RawPixelData);
            Assert.True(image.IsFrozen);
            Assert.True(image.PreloadAsync(4, 3).IsCompletedSuccessfully);
            Assert.Equal(2, decoder.Calls);
            Assert.Equal(0, loaded);
        }
        finally
        {
            decoder.Release.Set();
            if (restore is not null) try { await restore; } catch { }
            BitmapImage.SetDecoder(previous);
        }
    }

    [Fact]
    public void ReassignedSourceCannotBeOverwrittenByAnOldRestore()
    {
        var previous = BitmapImage.ResolveDecoder(); var decoder = new Decoder(); BitmapImage.SetDecoder(decoder);
        Task? restore = null;
        try
        {
            var image = BitmapImage.FromBytes([1]); image.ReclaimIdleResources();
            restore = image.PreloadAsync(4, 3);
            // Keep the mutable source on its owning thread while the decoder
            // is deliberately paused on another thread.
#pragma warning disable xUnit1031
            Assert.True(decoder.Started.Task.Wait(TimeSpan.FromSeconds(5)));
            image.BeginInit(); image.StreamSource = new MemoryStream([3]); image.EndInit();
            var current = image.RawPixelData!.ToArray();
            decoder.Release.Set();
            Assert.ThrowsAny<OperationCanceledException>(() => restore.GetAwaiter().GetResult());
            Assert.Equal(current, image.RawPixelData);
            Assert.All(current, value => Assert.Equal((byte)3, value));
        }
        finally
        {
            decoder.Release.Set();
            if (restore is not null) try { restore.GetAwaiter().GetResult(); } catch { }
#pragma warning restore xUnit1031
            BitmapImage.SetDecoder(previous);
        }
    }

    [Fact]
    public async Task EagerPreloadRetainsCroppedAndScaledPixels()
    {
        var previous = BitmapImage.ResolveDecoder(); var decoder = new Decoder(); BitmapImage.SetDecoder(decoder);
        decoder.Release.Set();
        try
        {
            var image = new BitmapImage(); image.BeginInit();
            image.StreamSource = new MemoryStream([1]); image.SourceRect = new Int32Rect(1, 0, 2, 3);
            image.DecodePixelWidth = 1; image.EndInit(); image.Freeze();
            var pixels = image.RawPixelData!.ToArray(); image.ReclaimIdleResources();
            await image.PreloadAsync(1, 2).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, image.PixelWidth); Assert.Equal(2, image.PixelHeight);
            Assert.Equal(pixels, image.RawPixelData);
        }
        finally { BitmapImage.SetDecoder(previous); }
    }

    [Fact]
    public async Task FailedRestoreCanBeRetried()
    {
        var previous = BitmapImage.ResolveDecoder(); var decoder = new Decoder { FailRestore = true }; BitmapImage.SetDecoder(decoder);
        try
        {
            var image = BitmapImage.FromBytes([1]); image.ReclaimIdleResources(); decoder.Release.Set();
            await Assert.ThrowsAsync<InvalidOperationException>(() => image.PreloadAsync(4, 3));
            Assert.Null(image.RawPixelData);
            decoder.FailRestore = false;
            await image.PreloadAsync(4, 3).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(image.RawPixelData);
            Assert.Equal(3, decoder.Calls);
        }
        finally { BitmapImage.SetDecoder(previous); }
    }

    private sealed class Decoder : INativeImageDecoder
    {
        internal int Calls, RestoreThread;
        internal bool FailRestore;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        {
            int call = Interlocked.Increment(ref Calls);
            if (call == 2)
            {
                RestoreThread = Environment.CurrentManagedThreadId;
                Started.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Restore did not leave the UI thread");
            }
            if (call > 1 && FailRestore) throw new InvalidOperationException("Injected restore failure");
            return new DecodedImage(Enumerable.Repeat(data[0], 48).ToArray(), 4, 3, 16, requestedFormat);
        }
        public DecodedImage Decode(Stream stream, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
        { using var buffer = new MemoryStream(); stream.CopyTo(buffer); return Decode(buffer.ToArray(), requestedFormat); }
        public DecodedImage DecodeFile(string path, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8) => Decode(File.ReadAllBytes(path), requestedFormat);
        public bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height) { width = 4; height = 3; return true; }
    }
}
