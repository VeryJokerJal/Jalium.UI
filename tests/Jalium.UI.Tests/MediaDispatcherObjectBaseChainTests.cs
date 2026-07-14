using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

public sealed class MediaDispatcherObjectBaseChainTests
{
    [Fact]
    public void WpfMediaRuntimeTypes_HaveCanonicalBaseTypes()
    {
        Type[] dispatcherObjects =
        [
            typeof(DrawingContext),
            typeof(StreamGeometryContext),
            typeof(BitmapDecoder),
            typeof(BitmapEncoder),
            typeof(BitmapPalette),
            typeof(Clock),
            typeof(ClockController),
        ];

        Assert.All(dispatcherObjects, type => Assert.Equal(typeof(DispatcherObject), type.BaseType));
        Assert.Equal(typeof(SystemException), typeof(AnimationException).BaseType);
    }

    [Fact]
    public void StreamGeometryContext_UsesTheWpfAbstractContractAndInternalImplementation()
    {
        Type contextType = typeof(StreamGeometryContext);

        Assert.True(contextType.IsAbstract);
        Assert.False(contextType.IsSealed);
        Assert.Equal(typeof(DispatcherObject), contextType.BaseType);
        Assert.Empty(contextType.GetConstructors());
        Assert.Null(contextType.GetMethod(nameof(IDisposable.Dispose)));
        Assert.True(contextType.GetMethod(nameof(StreamGeometryContext.Close))!.IsVirtual);

        string[] commandMethods =
        [
            nameof(StreamGeometryContext.BeginFigure),
            nameof(StreamGeometryContext.LineTo),
            nameof(StreamGeometryContext.PolyLineTo),
            nameof(StreamGeometryContext.BezierTo),
            nameof(StreamGeometryContext.PolyBezierTo),
            nameof(StreamGeometryContext.QuadraticBezierTo),
            nameof(StreamGeometryContext.PolyQuadraticBezierTo),
            nameof(StreamGeometryContext.ArcTo),
        ];
        Assert.All(commandMethods, name =>
            Assert.True(contextType.GetMethod(name)!.IsAbstract));

        using StreamGeometryContext context = new StreamGeometry().Open();
        Assert.True(context.GetType().IsNotPublic);
        Assert.True(context.GetType().IsSealed);
    }

    [Fact]
    public void ConstructedMediaRuntimeObjects_AreBoundToTheCurrentDispatcher()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

        using StreamGeometryContext geometryContext = new StreamGeometry().Open();
        var palette = new BitmapPalette([Colors.Black, Colors.White]);
        Clock clock = new DoubleAnimation().CreateClock(hasControllableRoot: true);

        Assert.Same(dispatcher, geometryContext.Dispatcher);
        Assert.Same(dispatcher, palette.Dispatcher);
        Assert.Same(dispatcher, clock.Dispatcher);
        Assert.Same(dispatcher, clock.Controller.Dispatcher);
    }
}
