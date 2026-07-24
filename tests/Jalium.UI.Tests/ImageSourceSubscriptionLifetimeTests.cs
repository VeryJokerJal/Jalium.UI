using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class ImageSourceSubscriptionLifetimeTests
{
    [Fact]
    public void CachedSourceDoesNotKeepImageControlAlive()
    {
        var source = new TestImageSource();
        var imageReference = CreateImageReference(source);

        ForceFullCollection();

        Assert.False(imageReference.IsAlive);
        GC.KeepAlive(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateImageReference(ImageSource source)
    {
        var image = new Image { Source = source };
        return new WeakReference(image);
    }

    private static void ForceFullCollection()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class TestImageSource : ImageSource
    {
        public override double Width => 16;
        public override double Height => 12;
        public override nint NativeHandle => nint.Zero;
        public override ImageMetadata? Metadata => null;
    }
}
