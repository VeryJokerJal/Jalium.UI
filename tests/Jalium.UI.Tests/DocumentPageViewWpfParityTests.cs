using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using ITextContainer = Jalium.UI.Documents.ITextContainer;

namespace Jalium.UI.Tests;

public sealed class DocumentPageViewWpfParityTests
{
    [Fact]
    public void TierOneSurface_HasWpfLifetimeServiceAndPageConnectionContracts()
    {
        var type = typeof(DocumentPageView);

        Assert.Contains(typeof(IDisposable), type.GetInterfaces());
        Assert.Contains(typeof(IServiceProvider), type.GetInterfaces());

        var isDisposed = type.GetProperty(
            "IsDisposed",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(isDisposed);
        Assert.Equal(typeof(bool), isDisposed!.PropertyType);
        Assert.True(isDisposed.GetMethod!.IsFamily);
        Assert.Null(isDisposed.SetMethod);

        var dispose = type.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        Assert.NotNull(dispose);
        Assert.True(dispose!.IsFamily);
        Assert.False(dispose.IsVirtual);
        Assert.Equal(typeof(void), dispose.ReturnType);

        var getService = type.GetMethod(
            "GetService",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(Type)],
            modifiers: null);
        Assert.NotNull(getService);
        Assert.True(getService!.IsFamily);
        Assert.False(getService.IsVirtual);
        Assert.Equal(typeof(object), getService.ReturnType);

        AssertExplicitInterfaceMethod(type, "System.IDisposable.Dispose", typeof(void));
        AssertExplicitInterfaceMethod(
            type,
            "System.IServiceProvider.GetService",
            typeof(object),
            typeof(Type));

        AssertEvent(type, nameof(DocumentPageView.PageConnected));
        AssertEvent(type, nameof(DocumentPageView.PageDisconnected));

        Assert.Same(
            DocumentPage.Missing,
            new DocumentPageView().DocumentPage);
        Assert.Equal(
            StretchDirection.DownOnly,
            DocumentPageView.StretchDirectionProperty.GetMetadata(type).DefaultValue);
    }

    [Fact]
    public void PageLifecycle_ConnectsOnArrangeAndDisconnectsImmediatelyWhenReplaced()
    {
        var paginator = new TestPaginator(3, new Size(100, 120));
        var view = new ProbeDocumentPageView();
        var notifications = new List<string>();
        view.PageConnected += (_, _) => notifications.Add("connected");
        view.PageDisconnected += (_, _) => notifications.Add("disconnected");

        view.DocumentPaginator = paginator;
        Assert.Same(DocumentPage.Missing, view.DocumentPage);

        var desiredSize = view.CallMeasureOverride(new Size(200, 200));

        Assert.Equal(new Size(100, 120), desiredSize);
        Assert.NotSame(DocumentPage.Missing, view.DocumentPage);
        Assert.Equal(1, paginator.GetPageCallCount);
        Assert.Empty(notifications);

        view.CallMeasureOverride(new Size(200, 200));
        view.CallArrangeOverride(desiredSize);
        view.CallArrangeOverride(desiredSize);

        Assert.Equal(1, paginator.GetPageCallCount);
        Assert.Equal(["connected"], notifications);

        view.PageNumber = 1;

        Assert.Same(DocumentPage.Missing, view.DocumentPage);
        Assert.Equal(["connected", "disconnected"], notifications);

        desiredSize = view.CallMeasureOverride(new Size(200, 200));
        view.CallArrangeOverride(desiredSize);

        Assert.Equal(2, paginator.GetPageCallCount);
        Assert.Equal(["connected", "disconnected", "connected"], notifications);

        view.DocumentPaginator = null;

        Assert.Same(DocumentPage.Missing, view.DocumentPage);
        Assert.Equal(
            ["connected", "disconnected", "connected", "disconnected"],
            notifications);
    }

    [Fact]
    public void Dispose_IsIdempotentAndGuardsPaginatorServicesAndLayoutLikeWpf()
    {
        var paginator = new TestPaginator(2, new Size(100, 120));
        var view = new ProbeDocumentPageView
        {
            DocumentPaginator = paginator,
        };
        var disconnected = 0;
        view.PageDisconnected += (_, _) => disconnected++;
        var desiredSize = view.CallMeasureOverride(new Size(200, 200));
        view.CallArrangeOverride(desiredSize);

        view.CallDispose();
        ((IDisposable)view).Dispose();

        Assert.True(view.DisposedValue);
        Assert.Null(view.DocumentPaginator);
        Assert.Same(DocumentPage.Missing, view.DocumentPage);
        Assert.Equal(1, disconnected);

        Assert.Throws<ObjectDisposedException>(() => view.DocumentPaginator = paginator);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            view.CallMeasureOverride(new Size(100, 100));
        });
        Assert.Throws<ObjectDisposedException>(() =>
        {
            view.CallArrangeOverride(new Size(100, 100));
        });

        view.PageNumber = 1;
        Assert.Equal(1, view.PageNumber);
    }

    [Fact]
    public void GetService_ForwardsOnlyTheDocumentTextContainerService()
    {
        var sentinel = new object();
        var paginator = new TestPaginator(1, new Size(100, 120), sentinel);
        var view = new ProbeDocumentPageView
        {
            DocumentPaginator = paginator,
        };

        Assert.Throws<ArgumentNullException>(() => view.CallGetService(null!));
        Assert.Null(view.CallGetService(typeof(string)));
        Assert.Null(paginator.LastRequestedServiceType);

        Assert.Same(sentinel, view.CallGetService(typeof(ITextContainer)));
        Assert.Equal(typeof(ITextContainer), paginator.LastRequestedServiceType);
        Assert.Same(sentinel, ((IServiceProvider)view).GetService(typeof(ITextContainer)));

        ((IDisposable)view).Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            ((IServiceProvider)view).GetService(typeof(ITextContainer)));
    }

    private static void AssertExplicitInterfaceMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.True(method!.IsPrivate);
        Assert.True(method.IsVirtual);
        Assert.True(method.IsFinal);
        Assert.Equal(returnType, method.ReturnType);
    }

    private static void AssertEvent(Type type, string name)
    {
        var eventInfo = type.GetEvent(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(EventHandler), eventInfo!.EventHandlerType);
        Assert.True(eventInfo.AddMethod!.IsPublic);
        Assert.True(eventInfo.RemoveMethod!.IsPublic);
    }

    private sealed class ProbeDocumentPageView : DocumentPageView
    {
        public bool DisposedValue => IsDisposed;

        public void CallDispose() => base.Dispose();

        public Size CallMeasureOverride(Size availableSize) => base.MeasureOverride(availableSize);

        public Size CallArrangeOverride(Size finalSize) => base.ArrangeOverride(finalSize);

        public object? CallGetService(Type serviceType) => base.GetService(serviceType);
    }

    private sealed class TestPaginator : DocumentPaginator, IServiceProvider
    {
        private readonly object? _textContainerService;

        public TestPaginator(int pageCount, Size pageSize, object? textContainerService = null)
        {
            PageCount = pageCount;
            PageSize = pageSize;
            _textContainerService = textContainerService;
        }

        public override int PageCount { get; }

        public override bool IsPageCountValid => true;

        public override Size PageSize { get; set; }

        public int GetPageCallCount { get; private set; }

        public Type? LastRequestedServiceType { get; private set; }

        public override DocumentPage GetPage(int pageNumber)
        {
            GetPageCallCount++;
            return pageNumber >= 0 && pageNumber < PageCount
                ? new DocumentPage { Size = PageSize }
                : DocumentPage.Missing;
        }

        object? IServiceProvider.GetService(Type serviceType)
        {
            LastRequestedServiceType = serviceType;
            return _textContainerService;
        }
    }
}
