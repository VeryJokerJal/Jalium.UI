using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSRoutedCommandRequeryTests
{
    [Fact]
    public void GlobalRequeryRefreshesTheActualCommandSourceState()
    {
        bool allowed = false;
        var command = new RoutedCommand("Refresh", typeof(MacOSRoutedCommandRequeryTests));
        var target = new Border();
        target.CommandBindings.Add(new CommandBinding(command, (_, _) => { }, (_, e) => e.CanExecute = allowed));
        var button = new Button { CommandTarget = target, Command = command };
        Assert.False(button.IsEnabled);
        allowed = true;
        CommandManager.InvalidateRequerySuggested();
        Assert.True(button.IsEnabled);
        allowed = false;
        command.RaiseCanExecuteChanged();
        Assert.False(button.IsEnabled);
    }

    [Fact]
    public void ExplicitRaiseAndRemovalKeepCommandEventIdentity()
    {
        var first = new RoutedCommand();
        var second = new RoutedCommand();
        int a = 0, b = 0;
        EventHandler handler = (sender, _) => { Assert.Same(first, sender); a++; };
        EventHandler other = (sender, _) => { Assert.Same(second, sender); b++; };
        first.CanExecuteChanged += handler;
        second.CanExecuteChanged += other;
        first.RaiseCanExecuteChanged();
        Assert.Equal((1, 0), (a, b));
        CommandManager.InvalidateRequerySuggested();
        Assert.Equal((2, 1), (a, b));
        first.CanExecuteChanged -= handler;
        second.CanExecuteChanged -= other;
        CommandManager.InvalidateRequerySuggested();
        Assert.Equal((2, 1), (a, b));
        GC.KeepAlive(handler);
        GC.KeepAlive(other);
    }

    [Fact]
    public void ALongLivedCommandDoesNotKeepAControlListenerAlive()
    {
        var command = new RoutedCommand();
        var listener = AddListener(command);
        Collect();
        Assert.False(listener.IsAlive);
        command.RaiseCanExecuteChanged();
        GC.KeepAlive(command);
    }

    [Fact]
    public void TheGlobalRequeryRegistryDoesNotKeepCommandsAlive()
    {
        var command = CreateCommand();
        Collect();
        Assert.False(command.IsAlive);
        CommandManager.InvalidateRequerySuggested();
    }

    private sealed class Listener
    {
        internal void OnChanged(object? sender, EventArgs e) { }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddListener(RoutedCommand command)
    {
        var listener = new Listener();
        command.CanExecuteChanged += listener.OnChanged;
        return new WeakReference(listener);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateCommand() => new(new RoutedCommand());
    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
