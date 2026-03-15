using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class VulkanBackendSmokeTests
{
    [Fact]
    public void VulkanContext_CanCreateBasicResources_WhenExperimentalBackendAvailable()
    {
        const string envName = "JALIUM_EXPERIMENTAL_VULKAN";
        var previous = Environment.GetEnvironmentVariable(envName);

        try
        {
            Environment.SetEnvironmentVariable(envName, "1");

            if (NativeMethods.IsBackendAvailable(RenderBackend.Vulkan) == 0)
            {
                return;
            }

            using var context = new RenderContext(RenderBackend.Vulkan);
            Assert.Equal(RenderBackend.Vulkan, context.Backend);

            using var brush = context.CreateSolidBrush(1f, 0f, 0f, 1f);
            Assert.True(brush.IsValid);

            using var format = context.CreateTextFormat("Segoe UI", 14f);
            Assert.True(format.IsValid);

            var metrics = format.MeasureText("Jalium", 1000f, 1000f);
            Assert.True(metrics.LineHeight > 0f);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void BitmapImage_FromPixels_PreservesRawPixelMetadata()
    {
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF
        };

        var image = BitmapImage.FromPixels(pixels, 2, 1);

        Assert.Equal(2d, image.Width);
        Assert.Equal(1d, image.Height);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(1, image.PixelHeight);
        Assert.Equal(8, image.PixelStride);
        Assert.NotNull(image.RawPixelData);
        Assert.Equal(pixels, image.RawPixelData);
    }

    [Fact]
    public void VulkanContext_CanCreateBitmapFromRawPixels_WhenExperimentalBackendAvailable()
    {
        const string envName = "JALIUM_EXPERIMENTAL_VULKAN";
        var previous = Environment.GetEnvironmentVariable(envName);

        try
        {
            Environment.SetEnvironmentVariable(envName, "1");

            if (NativeMethods.IsBackendAvailable(RenderBackend.Vulkan) == 0)
            {
                return;
            }

            using var context = new RenderContext(RenderBackend.Vulkan);
            var pixels = new byte[]
            {
                0x00, 0x00, 0xFF, 0xFF,
                0x00, 0xFF, 0x00, 0xFF,
                0xFF, 0x00, 0x00, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF
            };

            using var bitmap = context.CreateBitmapFromPixels(pixels, 2, 2);

            Assert.True(bitmap.IsValid);
            Assert.Equal(2u, bitmap.Width);
            Assert.Equal(2u, bitmap.Height);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void VulkanContext_CanCreateBitmapFromEncodedBmp_WhenExperimentalBackendAvailable()
    {
        const string envName = "JALIUM_EXPERIMENTAL_VULKAN";
        var previous = Environment.GetEnvironmentVariable(envName);

        try
        {
            Environment.SetEnvironmentVariable(envName, "1");

            if (NativeMethods.IsBackendAvailable(RenderBackend.Vulkan) == 0)
            {
                return;
            }

            using var context = new RenderContext(RenderBackend.Vulkan);
            byte[] bmpBytes =
            [
                0x42, 0x4D, 0x3A, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x36, 0x00, 0x00, 0x00, 0x28, 0x00,
                0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00,
                0x00, 0x00, 0x01, 0x00, 0x20, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x13, 0x0B,
                0x00, 0x00, 0x13, 0x0B, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0xFF, 0xFF
            ];

            using var bitmap = context.CreateBitmap(bmpBytes);

            Assert.True(bitmap.IsValid);
            Assert.Equal(1u, bitmap.Width);
            Assert.Equal(1u, bitmap.Height);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }
}
