using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Threading;
using ThreadingDispatcherUnhandledExceptionEventHandler = Jalium.UI.Threading.DispatcherUnhandledExceptionEventHandler;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ApplicationStyleResourceWpfParityTests
{
    [Fact]
    public void Application_HasWpfDispatcherAndAmbientShape()
    {
        Assert.True(typeof(DispatcherObject).IsAssignableFrom(typeof(Application)));
        Assert.True(typeof(IQueryAmbient).IsAssignableFrom(typeof(Application)));
        Assert.NotNull(typeof(Application)
            .GetProperty(nameof(Application.Resources))!
            .GetCustomAttribute<AmbientAttribute>());

        Assert.Equal(
            typeof(object),
            typeof(Application).GetMethod(nameof(Application.FindResource), [typeof(object)])!.ReturnType);
        Assert.Equal(
            typeof(object),
            typeof(Application).GetMethod(nameof(Application.TryFindResource), [typeof(object)])!.ReturnType);
        Assert.Equal(
            typeof(EventHandler),
            typeof(Application).GetEvent(nameof(Application.Activated))!.EventHandlerType);
        Assert.Equal(
            typeof(EventHandler),
            typeof(Application).GetEvent(nameof(Application.Deactivated))!.EventHandlerType);
        Assert.Equal(
            typeof(ThreadingDispatcherUnhandledExceptionEventHandler),
            typeof(Application).GetEvent(nameof(Application.DispatcherUnhandledException))!.EventHandlerType);
    }

    [Fact]
    public void Application_AmbientResourcesAreAvailableOnlyAfterDictionaryExists()
    {
        ResetApplicationState();

        try
        {
            var app = new Application();
            var ambient = (IQueryAmbient)app;

            app.Resources = null!;
            Assert.False(ambient.IsAmbientPropertyAvailable(nameof(Application.Resources)));
            Assert.False(ambient.IsAmbientPropertyAvailable("Other"));

            _ = app.Resources;
            Assert.True(ambient.IsAmbientPropertyAvailable(nameof(Application.Resources)));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Style_AmbientAvailabilityMatchesWpfBackingValueRules()
    {
        var style = new Style();
        var ambient = (IQueryAmbient)style;

        Assert.False(ambient.IsAmbientPropertyAvailable(nameof(Style.Resources)));
        Assert.False(ambient.IsAmbientPropertyAvailable(nameof(Style.BasedOn)));
        Assert.True(ambient.IsAmbientPropertyAvailable(nameof(Style.TargetType)));

        Assert.NotNull(typeof(Style)
            .GetProperty(nameof(Style.Resources))!
            .GetCustomAttribute<AmbientAttribute>());

        _ = style.Resources;
        Assert.True(ambient.IsAmbientPropertyAvailable(nameof(Style.Resources)));

        style.Resources = null!;
        Assert.False(ambient.IsAmbientPropertyAvailable(nameof(Style.Resources)));

        style.BasedOn = new Style();
        Assert.True(ambient.IsAmbientPropertyAvailable(nameof(Style.BasedOn)));
    }

    [Fact]
    public void Application_ResourceLookupUsesApplicationThenThemeThenSystemFallback()
    {
        ResetApplicationState();

        try
        {
            var app = new Application
            {
                Resources = new ResourceDictionary()
            };
            var themeResources = new ResourceDictionary
            {
                ["layered-key"] = "theme"
            };
            app.Resources.MergedDictionaries.Add(themeResources);

            Assert.Equal("theme", app.TryFindResource("layered-key"));

            app.Resources["layered-key"] = "application";
            Assert.Equal("application", app.FindResource("layered-key"));

            Assert.Same(SystemColors.ControlBrush, app.TryFindResource(SystemColors.ControlBrushKey));
            Assert.Same(
                SystemColors.WindowBrush,
                new Border().TryFindResource(SystemColors.WindowBrushKey));

            var systemOverride = new object();
            app.Resources[SystemColors.ControlBrushKey] = systemOverride;
            Assert.Same(systemOverride, app.TryFindResource(SystemColors.ControlBrushKey));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Application_FindResourceThrowsWpfExceptionForMissingKey()
    {
        ResetApplicationState();

        try
        {
            var app = new Application
            {
                Resources = new ResourceDictionary()
            };
            var key = new object();

            Assert.Null(app.TryFindResource(key));
            var exception = Assert.Throws<ResourceReferenceKeyNotFoundException>(
                () => app.FindResource(key));
            Assert.Same(key, exception.Key);
            Assert.Throws<ArgumentNullException>(() => app.TryFindResource(null!));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Application_ActivationRaisersAndPlatformStateAreDeduplicated()
    {
        ResetApplicationState();

        try
        {
            var app = new TestApplication();
            var activated = 0;
            var deactivated = 0;
            app.Activated += (_, _) => activated++;
            app.Deactivated += (_, _) => deactivated++;

            app.RaiseActivated();
            app.RaiseDeactivated();
            Assert.Equal(1, activated);
            Assert.Equal(1, deactivated);

            app.SetPlatformActivationState(true);
            app.SetPlatformActivationState(true);
            app.SetPlatformActivationState(false);
            app.SetPlatformActivationState(false);

            Assert.Equal(2, activated);
            Assert.Equal(2, deactivated);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DispatcherUnhandledException_AddAndRemoveForwardToApplicationDispatcher()
    {
        ResetApplicationState();

        try
        {
            var app = new Application();
            ThreadingDispatcherUnhandledExceptionEventHandler handler = (_, e) => e.Handled = true;
            var dispatcherEventField = typeof(Jalium.UI.Dispatcher).GetField(
                nameof(Jalium.UI.Dispatcher.UnhandledException),
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var legacyDispatcher = app.Dispatcher.LegacyDispatcher;

            app.DispatcherUnhandledException += handler;
            try
            {
                var subscribed = (Delegate?)dispatcherEventField.GetValue(legacyDispatcher);
                Assert.Contains(handler, subscribed!.GetInvocationList());
            }
            finally
            {
                app.DispatcherUnhandledException -= handler;
            }

            var unsubscribed = (Delegate?)dispatcherEventField.GetValue(legacyDispatcher);
            Assert.DoesNotContain(handler, unsubscribed?.GetInvocationList() ?? []);
        }
        finally
        {
            ResetApplicationState();
        }
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

    private sealed class TestApplication : Application
    {
        public void RaiseActivated() => OnActivated(EventArgs.Empty);

        public void RaiseDeactivated() => OnDeactivated(EventArgs.Empty);
    }
}
