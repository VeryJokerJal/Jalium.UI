using System.Runtime.CompilerServices;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class InputMethodSubscriptionLifetimeTests
{
    [Fact]
    public void ImeAwareControls_AreCollectibleAfterUnloaded()
    {
        var factories = CreateFactories();

        var weakControls = factories
            .Select(static entry => (entry.Name, Reference: LoadUnloadAndRelease(entry.Factory)))
            .ToArray();

        ForceFullCollection();

        foreach (var (name, reference) in weakControls)
        {
            Assert.False(
                reference.TryGetTarget(out _),
                $"{name} remained rooted after its Unloaded lifecycle completed.");
        }

        GC.KeepAlive(factories);
    }

    [Fact]
    public void ImeAwareControls_AreCollectibleWhenAnUnloadedNotificationIsMissed()
    {
        var factories = CreateFactories();
        var weakControls = factories
            .Select(static entry => (entry.Name, Reference: LoadAndRelease(entry.Factory)))
            .ToArray();

        ForceFullCollection();

        foreach (var (name, reference) in weakControls)
        {
            Assert.False(
                reference.TryGetTarget(out _),
                $"{name} was retained by the static input-method events.");
        }

        GC.KeepAlive(factories);
    }

    private static (string Name, Func<FrameworkElement> Factory)[] CreateFactories() =>
    [
        (nameof(EditControl), static () => new EditControl()),
        (nameof(AutoCompleteBox), static () => new AutoCompleteBox()),
        (nameof(NumberBox), static () => new NumberBox()),
        (nameof(PasswordBox), static () => new PasswordBox()),
        (nameof(RichTextBox), static () => new RichTextBox()),
        (nameof(TextBox), static () => new TextBox()),
    ];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FrameworkElement> LoadUnloadAndRelease(Func<FrameworkElement> factory)
    {
        var control = factory();
        control.SetLoadedState(true);
        control.SetLoadedState(false);
        return new WeakReference<FrameworkElement>(control);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FrameworkElement> LoadAndRelease(Func<FrameworkElement> factory)
    {
        var control = factory();
        control.SetLoadedState(true);
        return new WeakReference<FrameworkElement>(control);
    }

    private static void ForceFullCollection()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
