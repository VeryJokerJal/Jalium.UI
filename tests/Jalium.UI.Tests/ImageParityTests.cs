using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using ImageControl = Jalium.UI.Controls.Image;

namespace Jalium.UI.Tests;

public sealed class ImageParityTests
{
    [Fact]
    public void SurfaceMatchesWpfUriDpiAndFailureContracts()
    {
        Assert.Contains(typeof(IUriContext), typeof(ImageControl).GetInterfaces());
        Assert.Equal(typeof(FrameworkElement).Assembly, typeof(IUriContext).Assembly);
        Assert.Same(
            typeof(IUriContext),
            Type.GetType("Jalium.UI.Markup.IUriContext, Jalium.UI.Xaml", throwOnError: true));
        Assert.Equal("Jalium.UI", typeof(Jalium.UI.ExceptionRoutedEventArgs).Namespace);

        var baseUri = typeof(ImageControl).GetProperty(
            "BaseUri",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(baseUri);
        Assert.Equal(typeof(Uri), baseUri!.PropertyType);
        Assert.True(baseUri.GetMethod!.IsFamily);
        Assert.True(baseUri.SetMethod!.IsFamily);
        Assert.True(baseUri.GetMethod.IsVirtual);
        Assert.True(baseUri.SetMethod.IsVirtual);

        var onDpiChanged = typeof(ImageControl).GetMethod(
            "OnDpiChanged",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new[] { typeof(DpiScale), typeof(DpiScale) },
            null);
        Assert.NotNull(onDpiChanged);
        Assert.True(onDpiChanged!.IsFamily);
        Assert.True(onDpiChanged.IsVirtual);
        Assert.False(onDpiChanged.IsFinal);
        Assert.NotEqual(onDpiChanged, onDpiChanged.GetBaseDefinition());

        Assert.Equal(RoutingStrategy.Bubble, ImageControl.DpiChangedEvent.RoutingStrategy);
        Assert.Equal(typeof(DpiChangedEventHandler), ImageControl.DpiChangedEvent.HandlerType);
        Assert.Equal(RoutingStrategy.Bubble, ImageControl.ImageFailedEvent.RoutingStrategy);
        Assert.Equal(
            typeof(EventHandler<Jalium.UI.ExceptionRoutedEventArgs>),
            ImageControl.ImageFailedEvent.HandlerType);

        Assert.Equal(
            typeof(DpiChangedEventHandler),
            typeof(ImageControl).GetEvent(nameof(ImageControl.DpiChanged))!.EventHandlerType);
        Assert.Equal(
            typeof(EventHandler<Jalium.UI.ExceptionRoutedEventArgs>),
            typeof(ImageControl).GetEvent(nameof(ImageControl.ImageFailed))!.EventHandlerType);
    }

    [Fact]
    public void BaseUri_ExplicitAndProtectedContractsShareStateAndFlowToSource()
    {
        var image = new ProbeImage();
        var context = (IUriContext)image;
        var firstBase = new Uri("file:///C:/assets/");

        Assert.Null(context.BaseUri);

        context.BaseUri = firstBase;

        Assert.Same(firstBase, image.ExposedBaseUri);

        var source = new UriAwareImageSource();
        image.Source = source;

        Assert.Same(firstBase, source.BaseUri);

        var sourceBase = new Uri("file:///D:/source-owned/");
        var sourceWithContext = new UriAwareImageSource { BaseUri = sourceBase };
        image.Source = sourceWithContext;
        image.ExposedBaseUri = new Uri("file:///E:/new-owner/");

        Assert.Same(sourceBase, sourceWithContext.BaseUri);
        Assert.Same(image.ExposedBaseUri, context.BaseUri);
    }

    [Fact]
    public void DpiChanged_FiresOnceOnFirstMeasureThenBubblesExplicitChanges()
    {
        var parent = new StackPanel();
        var image = new ProbeImage();
        parent.Children.Add(image);
        var imageEvents = new List<DpiChangedEventArgs>();
        var parentEvents = new List<DpiChangedEventArgs>();
        image.DpiChanged += (_, e) => imageEvents.Add(e);
        parent.AddHandler(
            ImageControl.DpiChangedEvent,
            new DpiChangedEventHandler((_, e) => parentEvents.Add(e)));

        image.Measure(new Size(100, 100));

        var initial = Assert.Single(imageEvents);
        Assert.Equal(initial.OldDpi, initial.NewDpi);
        Assert.Same(image, initial.Source);
        Assert.Single(parentEvents);

        image.Measure(new Size(120, 120));
        Assert.Single(imageEvents);

        var oldDpi = new DpiScale(1.25, 1.5);
        var newDpi = new DpiScale(2, 2.25);
        image.RaiseDpiChanged(oldDpi, newDpi);

        Assert.Equal(2, imageEvents.Count);
        Assert.Equal(2, parentEvents.Count);
        Assert.Equal(oldDpi, imageEvents[1].OldDpi);
        Assert.Equal(newDpi, imageEvents[1].NewDpi);
        Assert.Same(image, imageEvents[1].Source);
        Assert.Same(imageEvents[1], parentEvents[1]);
    }

    [Fact]
    public void ImageFailed_ClearsSourceBeforeHandlerAndBubblesOriginalException()
    {
        var parent = new StackPanel();
        var image = new ImageControl();
        parent.Children.Add(image);
        var source = new FailingImageSource();
        var failure = new InvalidDataException("decode failed");
        var order = new List<string>();
        Jalium.UI.ExceptionRoutedEventArgs? received = null;

        image.ImageFailed += (_, e) =>
        {
            order.Add("image");
            Assert.Null(image.Source);
            received = e;
        };
        parent.AddHandler(
            ImageControl.ImageFailedEvent,
            new EventHandler<Jalium.UI.ExceptionRoutedEventArgs>((_, _) => order.Add("parent")));
        image.Source = source;

        source.Fail(failure);

        Assert.Equal(new[] { "image", "parent" }, order);
        Assert.NotNull(received);
        Assert.Same(failure, received!.ErrorException);
        Assert.Same(image, received.Source);
        Assert.Same(ImageControl.ImageFailedEvent, received.RoutedEvent);
    }

    [Fact]
    public void ImageFailed_IgnoresDetachedSourcesAndReplaysPendingBitmapFailure()
    {
        var image = new ImageControl();
        var detached = new FailingImageSource();
        var replacement = new UriAwareImageSource();
        var failures = new List<Exception>();
        image.ImageFailed += (_, e) => failures.Add(e.ErrorException);
        image.Source = detached;
        image.Source = replacement;

        detached.Fail(new InvalidOperationException("stale"));

        Assert.Empty(failures);
        Assert.Same(replacement, image.Source);

        var bitmap = new BitmapImage
        {
            UriSource = new Uri("__jalium_missing_image_parity__.png", UriKind.Relative)
        };

        image.Source = bitmap;

        var failure = Assert.Single(failures);
        Assert.IsType<FileNotFoundException>(failure);
        Assert.Null(image.Source);
    }

    private sealed class ProbeImage : ImageControl
    {
        public Uri? ExposedBaseUri
        {
            get => BaseUri;
            set => BaseUri = value;
        }

        public void RaiseDpiChanged(DpiScale oldDpi, DpiScale newDpi) =>
            OnDpiChanged(oldDpi, newDpi);
    }

    private sealed class UriAwareImageSource : ImageSource, IUriContext
    {
        public Uri? BaseUri { get; set; }

        public override double Width => 16;

        public override double Height => 12;

        public override nint NativeHandle => nint.Zero;

        public override ImageMetadata? Metadata => null;
    }

    private sealed class FailingImageSource : ImageSource
    {
        public override double Width => 16;

        public override double Height => 12;

        public override nint NativeHandle => nint.Zero;

        public override ImageMetadata? Metadata => null;

        public void Fail(Exception exception) => ReportLoadFailure(exception);
    }
}
