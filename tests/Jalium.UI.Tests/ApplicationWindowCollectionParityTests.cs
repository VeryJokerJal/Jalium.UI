using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

#pragma warning disable WPF0001 // Application.ThemeMode intentionally mirrors the experimental WPF API.

[Collection("Application")]
public sealed class ApplicationWindowCollectionParityTests
{
    [Fact]
    public void WindowCollection_PublicConstructorCreatesEmptyCollection()
    {
        var windows = new WindowCollection();

        Assert.Empty(windows);
        Assert.False(windows.IsSynchronized);
        Assert.Same(windows.SyncRoot, windows.SyncRoot);
        Assert.IsAssignableFrom<ICollection>(windows);
        Assert.IsAssignableFrom<IEnumerable>(windows);
    }

    [Fact]
    public void WindowCollection_LiveProviderSupportsIndexerEnumerationAndCopyTo()
    {
        var source = new List<Window>();
        var windows = new WindowCollection(() => source);
        var first = new Window();
        var second = new Window();

        source.Add(first);
        Assert.Single(windows);
        Assert.Same(first, windows[0]);

        source.Add(second);
        Assert.Equal(new[] { first, second }, windows.Cast<Window>());

        var typedCopy = new Window[3];
        windows.CopyTo(typedCopy, 1);
        Assert.Null(typedCopy[0]);
        Assert.Same(first, typedCopy[1]);
        Assert.Same(second, typedCopy[2]);

        var untypedCopy = new object[2];
        ((ICollection)windows).CopyTo(untypedCopy, 0);
        Assert.Same(first, untypedCopy[0]);
        Assert.Same(second, untypedCopy[1]);
    }

    [Fact]
    public void Window_OwnedWindowsIsStableAndTracksOwnerChangesWithoutHandles()
    {
        var owner = new Window();
        var child = new Window();
        var ownedWindows = owner.OwnedWindows;

        Assert.Same(ownedWindows, owner.OwnedWindows);
        Assert.Empty(ownedWindows);

        child.Owner = owner;

        Assert.Single(ownedWindows);
        Assert.Same(child, ownedWindows[0]);

        child.Owner = null;

        Assert.Empty(ownedWindows);
    }

    [Fact]
    public void Application_ParityPropertiesAreStableAndRoundTripWithoutHandles()
    {
        ResetApplicationState();
        var originalResourceAssembly = Application.ResourceAssembly;

        try
        {
            var app = new Application();

            Assert.Same(app.Properties, app.Properties);
            app.Properties["answer"] = 42;
            Assert.Equal(42, app.Properties["answer"]);

            Assert.Same(app.Windows, app.Windows);
            Assert.Empty(app.Windows);

            var unshownWindow = new Window();
            Assert.DoesNotContain(unshownWindow, app.Windows.Cast<Window>());

            Application.ResourceAssembly = typeof(Window).Assembly;
            Assert.Same(typeof(Window).Assembly, Application.ResourceAssembly);

            app.ThemeMode = ThemeMode.Light;
            Assert.Equal(ThemeMode.Light, app.ThemeMode);
            Assert.Equal(ThemeVariant.Light, ThemeManager.CurrentTheme);

            app.ThemeMode = new ThemeMode("Custom");
            Assert.Equal(new ThemeMode("Custom"), app.ThemeMode);
        }
        finally
        {
            Application.ResourceAssembly = originalResourceAssembly;
            ResetApplicationState();
        }
    }

    [Fact]
    public void PublicApiSignaturesMatchWpfShape()
    {
        var collectionType = typeof(WindowCollection);

        Assert.True(collectionType.IsSealed);
        Assert.NotNull(collectionType.GetConstructor(Type.EmptyTypes));
        Assert.Equal(typeof(Window), collectionType.GetProperty("Item")!.PropertyType);
        Assert.NotNull(collectionType.GetMethod(nameof(WindowCollection.CopyTo), new[] { typeof(Window[]), typeof(int) }));
        Assert.NotNull(collectionType.GetMethod(nameof(WindowCollection.GetEnumerator), Type.EmptyTypes));

        Assert.Equal(typeof(IDictionary), typeof(Application).GetProperty(nameof(Application.Properties))!.PropertyType);
        Assert.Equal(typeof(Assembly), typeof(Application).GetProperty(nameof(Application.ResourceAssembly))!.PropertyType);
        Assert.Equal(typeof(ThemeMode), typeof(Application).GetProperty(nameof(Application.ThemeMode))!.PropertyType);
        Assert.Equal(typeof(WindowCollection), typeof(Application).GetProperty(nameof(Application.Windows))!.PropertyType);
        Assert.Equal(typeof(WindowCollection), typeof(Window).GetProperty(nameof(Window.OwnedWindows))!.PropertyType);
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
}

#pragma warning restore WPF0001
