using Jalium.UI.Interop;

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
}
