using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class D3DImageTests
{
    [Fact]
    public void D3DImage_DefaultState_ShouldBeEmptyAndUnlocked()
    {
        var image = new D3DImage();

        Assert.Equal(0, image.PixelWidth);
        Assert.Equal(0, image.PixelHeight);
        Assert.False(image.IsFrontBufferAvailable);
        Assert.False(image.IsLocked);
        Assert.Equal(nint.Zero, image.NativeHandle);
    }

    [Fact]
    public void D3DImage_SetBackBuffer_ShouldUpdateAvailabilityAndRaiseEvent()
    {
        var image = new D3DImage();
        var eventCount = 0;
        image.IsFrontBufferAvailableChanged += (_, _) => eventCount++;

        image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, new IntPtr(1234), enableSoftwareFallback: true);

        Assert.True(image.IsFrontBufferAvailable);
        Assert.True(image.IsSoftwareFallbackEnabled);
        Assert.Equal(new IntPtr(1234), image.NativeHandle);
        Assert.Equal(1, eventCount);

        image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);

        Assert.False(image.IsFrontBufferAvailable);
        Assert.Equal(nint.Zero, image.NativeHandle);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void D3DImage_LockLifecycle_ShouldTrackState()
    {
        var image = new D3DImage();

        image.Lock();
        Assert.True(image.IsLocked);

        image.Unlock();
        Assert.False(image.IsLocked);
    }

    [Fact]
    public void D3DImage_UnlockWithoutLock_ShouldThrow()
    {
        var image = new D3DImage();

        Assert.Throws<InvalidOperationException>(() => image.Unlock());
    }

    [Fact]
    public void D3DImage_TryLockWithInvalidTimeout_ShouldThrow()
    {
        var image = new D3DImage();

        Assert.Throws<ArgumentOutOfRangeException>(() => image.TryLock(TimeSpan.FromMilliseconds(-2)));
    }

    [Fact]
    public void D3DImage_SetPixelSize_ShouldUpdateDimensions()
    {
        var image = new D3DImage();

        image.SetPixelSize(320, 180);

        Assert.Equal(320, image.PixelWidth);
        Assert.Equal(180, image.PixelHeight);
        Assert.Equal(320d, image.Width);
        Assert.Equal(180d, image.Height);
    }
}
