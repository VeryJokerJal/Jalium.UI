using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Navigation;
using Jalium.UI.Resources;
using CanonicalDispatcherUnhandledExceptionEventHandler = Jalium.UI.Threading.DispatcherUnhandledExceptionEventHandler;
using CanonicalNavigationEventArgs = Jalium.UI.Navigation.NavigationEventArgs;
using CanonicalNavigatingCancelEventArgs = Jalium.UI.Navigation.NavigatingCancelEventArgs;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ApplicationResourceNavigationWpfParityTests
{
    [Fact]
    public void Application_ExposesCanonicalResourceAndNavigationContracts()
    {
        Assert.Equal(typeof(StreamResourceInfo),
            typeof(Application).GetMethod(nameof(Application.GetContentStream))!.ReturnType);
        Assert.Equal(typeof(StreamResourceInfo),
            typeof(Application).GetMethod(nameof(Application.GetResourceStream))!.ReturnType);
        Assert.Equal(typeof(StreamResourceInfo),
            typeof(Application).GetMethod(nameof(Application.GetRemoteStream))!.ReturnType);

        Assert.Equal(typeof(CanonicalDispatcherUnhandledExceptionEventHandler),
            typeof(Application).GetEvent(nameof(Application.DispatcherUnhandledException))!.EventHandlerType);
        Assert.Equal(typeof(FragmentNavigationEventHandler),
            typeof(Application).GetEvent(nameof(Application.FragmentNavigation))!.EventHandlerType);
        Assert.Equal(typeof(LoadCompletedEventHandler),
            typeof(Application).GetEvent(nameof(Application.LoadCompleted))!.EventHandlerType);
        Assert.Equal(typeof(NavigatedEventHandler),
            typeof(Application).GetEvent(nameof(Application.Navigated))!.EventHandlerType);
        Assert.Equal(typeof(NavigatingCancelEventHandler),
            typeof(Application).GetEvent(nameof(Application.Navigating))!.EventHandlerType);
        Assert.Equal(typeof(NavigationFailedEventHandler),
            typeof(Application).GetEvent(nameof(Application.NavigationFailed))!.EventHandlerType);
        Assert.Equal(typeof(NavigationProgressEventHandler),
            typeof(Application).GetEvent(nameof(Application.NavigationProgress))!.EventHandlerType);
        Assert.Equal(typeof(NavigationStoppedEventHandler),
            typeof(Application).GetEvent(nameof(Application.NavigationStopped))!.EventHandlerType);

        AssertProtectedVirtual("OnFragmentNavigation", typeof(FragmentNavigationEventArgs));
        AssertProtectedVirtual("OnLoadCompleted", typeof(CanonicalNavigationEventArgs));
        AssertProtectedVirtual("OnNavigated", typeof(CanonicalNavigationEventArgs));
        AssertProtectedVirtual("OnNavigating", typeof(CanonicalNavigatingCancelEventArgs));
        AssertProtectedVirtual("OnNavigationFailed", typeof(NavigationFailedEventArgs));
        AssertProtectedVirtual("OnNavigationProgress", typeof(NavigationProgressEventArgs));
        AssertProtectedVirtual("OnNavigationStopped", typeof(CanonicalNavigationEventArgs));
    }

    [Fact]
    public void ResourceApis_OpenEmbeddedAndLooseContentStreams()
    {
        var previousAssembly = Application.ResourceAssembly;
        var looseFileName = $"jalium-application-content-{Guid.NewGuid():N}.txt";
        var looseFilePath = Path.Combine(AppContext.BaseDirectory, looseFileName);

        try
        {
            Application.ResourceAssembly = typeof(ApplicationResourceNavigationWpfParityTests).Assembly;
            var embedded = Application.GetResourceStream(
                new Uri("/Jalium.UI.Tests;component/TestAssets/ApplicationResource.txt", UriKind.Relative));
            Assert.NotNull(embedded);
            Assert.Equal("text/plain", embedded.ContentType);
            using (var reader = new StreamReader(embedded.Stream!))
            {
                Assert.Equal("Jalium.UI application resource parity", reader.ReadToEnd().Trim());
            }

            File.WriteAllText(looseFilePath, "loose application content");
            var content = Application.GetContentStream(new Uri(looseFileName, UriKind.Relative));
            Assert.NotNull(content);
            Assert.Equal("text/plain", content.ContentType);
            using var contentReader = new StreamReader(content.Stream!);
            Assert.Equal("loose application content", contentReader.ReadToEnd());

            var remote = Application.GetRemoteStream(new Uri(looseFileName, UriKind.Relative));
            Assert.NotNull(remote);
            using var remoteReader = new StreamReader(remote.Stream!);
            Assert.Equal("loose application content", remoteReader.ReadToEnd());
        }
        finally
        {
            Application.ResourceAssembly = previousAssembly;
            File.Delete(looseFilePath);
        }
    }

    [Fact]
    public void LoadComponent_UsesRegisteredXamlRuntimeForBothOverloads()
    {
        var previousAssembly = Application.ResourceAssembly;
        var previousStartupLoader = Application.StartupObjectLoader;
        var previousComponentLoader = Application.ComponentLoader;
        var previousComponentObjectLoader = Application.ComponentObjectLoader;
        try
        {
            Application.ResourceAssembly = typeof(ApplicationResourceNavigationWpfParityTests).Assembly;
            ThemeLoader.Initialize();

            var panel = new StackPanel();
            Application.LoadComponent(
                panel,
                new Uri("/Jalium.UI.Tests;component/TestAssets/StartupStackRoot.xaml", UriKind.Relative));
            Assert.Single(panel.Children);
            Assert.Equal("Startup Stack Root", Assert.IsType<TextBlock>(panel.Children[0]).Text);

            var loaded = Application.LoadComponent(
                new Uri("/Jalium.UI.Tests;component/TestAssets/StartupStackRoot.xaml", UriKind.Relative));
            Assert.IsType<StackPanel>(loaded);
        }
        finally
        {
            Application.ResourceAssembly = previousAssembly;
            Application.StartupObjectLoader = previousStartupLoader;
            Application.ComponentLoader = previousComponentLoader;
            Application.ComponentObjectLoader = previousComponentObjectLoader;
        }
    }

    [Fact]
    public void CookieApis_RoundTripThroughSharedApplicationStore()
    {
        var uri = new Uri($"https://jalium.invalid/{Guid.NewGuid():N}/");
        Application.SetCookie(uri, "parity=value; Path=/");
        Assert.Contains("parity=value", Application.GetCookie(uri), StringComparison.Ordinal);
    }

    [Fact]
    public void FrameNavigation_IsForwardedToApplicationEventsAndOverrides()
    {
        ResetApplicationState();
        try
        {
            var app = new TrackingApplication();
            var frame = new Frame();
            var stopped = 0;
            app.NavigationStopped += (_, _) => stopped++;

            frame.NavigationService.ContentLoader = uri =>
            {
                frame.NavigationService.StopLoading();
                return new TextBlock { Text = uri.OriginalString };
            };

            Assert.True(frame.Navigate(new Uri("page.xaml#target", UriKind.Relative)));
            Assert.Equal(1, app.NavigatingCount);
            Assert.Equal(1, app.NavigatedCount);
            Assert.Equal(1, app.LoadCompletedCount);
            Assert.Equal(1, app.NavigationProgressCount);
            Assert.Equal(1, app.FragmentNavigationCount);
            Assert.Equal(1, app.NavigationStoppedCount);
            Assert.Equal(1, stopped);

            frame.NavigationService.ContentLoader = _ => throw new InvalidOperationException("navigation failure");
            app.NavigationFailed += (_, e) => e.Handled = true;
            Assert.False(frame.Navigate(new Uri("failure.xaml", UriKind.Relative)));
            Assert.Equal(1, app.NavigationFailedCount);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplicationNavigating_CanCancelFrameNavigation()
    {
        ResetApplicationState();
        try
        {
            var app = new TrackingApplication();
            var frame = new Frame();
            frame.NavigationService.ContentLoader = _ => new TextBlock();
            app.Navigating += (_, e) => e.Cancel = true;

            Assert.False(frame.Navigate(new Uri("cancelled.xaml", UriKind.Relative)));
            Assert.Null(frame.Content);
            Assert.Equal(1, app.NavigatingCount);
            Assert.Equal(0, app.NavigatedCount);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void AssertProtectedVirtual(string name, Type parameterType)
    {
        var method = typeof(Application).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [parameterType],
            modifiers: null);
        Assert.NotNull(method);
        Assert.True(method.IsFamily);
        Assert.True(method.IsVirtual);
    }

    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, null);
        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null);
        Application.StartupObjectLoader = null;
    }

    private sealed class TrackingApplication : Application
    {
        public int FragmentNavigationCount { get; private set; }
        public int LoadCompletedCount { get; private set; }
        public int NavigatedCount { get; private set; }
        public int NavigatingCount { get; private set; }
        public int NavigationFailedCount { get; private set; }
        public int NavigationProgressCount { get; private set; }
        public int NavigationStoppedCount { get; private set; }

        protected override void OnFragmentNavigation(FragmentNavigationEventArgs e)
        {
            FragmentNavigationCount++;
            base.OnFragmentNavigation(e);
        }

        protected override void OnLoadCompleted(CanonicalNavigationEventArgs e)
        {
            LoadCompletedCount++;
            base.OnLoadCompleted(e);
        }

        protected override void OnNavigated(CanonicalNavigationEventArgs e)
        {
            NavigatedCount++;
            base.OnNavigated(e);
        }

        protected override void OnNavigating(CanonicalNavigatingCancelEventArgs e)
        {
            NavigatingCount++;
            base.OnNavigating(e);
        }

        protected override void OnNavigationFailed(NavigationFailedEventArgs e)
        {
            NavigationFailedCount++;
            base.OnNavigationFailed(e);
        }

        protected override void OnNavigationProgress(NavigationProgressEventArgs e)
        {
            NavigationProgressCount++;
            base.OnNavigationProgress(e);
        }

        protected override void OnNavigationStopped(CanonicalNavigationEventArgs e)
        {
            NavigationStoppedCount++;
            base.OnNavigationStopped(e);
        }
    }
}
