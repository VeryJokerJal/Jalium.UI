using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public class D3DImageTests
{
    [Fact]
    public void D3DImage_DefaultState_ShouldBeEmptyAndUnlocked()
    {
        var image = new D3DImage();

        Assert.Equal(0, image.PixelWidth);
        Assert.Equal(0, image.PixelHeight);
        Assert.True(image.IsFrontBufferAvailable);
        Assert.False(image.IsLocked);
        Assert.Equal(nint.Zero, image.NativeHandle);
    }

    [Fact]
    public void D3DImage_SetBackBuffer_ShouldRequireLockAndRetainWpfState()
    {
        using var image = new D3DImage();

        Assert.Throws<InvalidOperationException>(() =>
            image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, new IntPtr(42), enableSoftwareFallback: true));

        image.Lock();
        image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, new IntPtr(42), enableSoftwareFallback: true);
        image.Unlock();

        Assert.True(image.IsFrontBufferAvailable);
        Assert.True(image.IsSoftwareFallbackEnabled);
        Assert.Equal(new IntPtr(42), image.NativeHandle);
        Assert.Equal(D3DResourceType.IDirect3DSurface9, image.ResourceType);
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
    public void D3DImage_TryLockWithAutomaticDuration_ShouldThrow()
    {
        var image = new D3DImage();

        Assert.Throws<ArgumentOutOfRangeException>(() => image.TryLock(Duration.Automatic));
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

    [Fact]
    public void LegacyMediaD3DTypes_AreNotPublic()
    {
        var exported = typeof(D3DImage).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exported, type => type.FullName is "Jalium.UI.Media.D3DImage" or "Jalium.UI.Media.D3DResourceType");
        Assert.Contains(exported, type => type == typeof(D3DImage));
        Assert.Contains(exported, type => type == typeof(D3DResourceType));
    }
}
