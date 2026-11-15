using System.Windows.Input;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class MenuFlyoutAutomationInvocationTests
{
    [Fact]
    public void InvokeRunsClickAndCommandOnceAndHonorsDisabledState()
    {
        var command = new CounterCommand();
        var item = new MenuFlyoutItem { Text = "性能探查器", Command = command };
        var clicks = 0;
        item.Click += (_, _) => clicks++;
        var invoke = (IInvokeProvider)new MenuFlyoutItemAutomationPeer(item);
        invoke.Invoke();
        Assert.Equal(1, clicks);
        Assert.Equal(1, command.Count);
        item.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(() => invoke.Invoke());
        Assert.Equal(1, clicks);
        Assert.Equal(1, command.Count);
    }

    [Fact]
    public void InvokeWithoutCommandRunsClickAndUpdatesToggleBeforeTheHandler()
    {
        var item = new ToggleMenuFlyoutItem { Text = "显示网格" };
        var checkedAtClick = false;
        item.Click += (_, _) => checkedAtClick = item.IsChecked;
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item)).Invoke();
        Assert.True(item.IsChecked);
        Assert.True(checkedAtClick);
    }

    private sealed class CounterCommand : ICommand
    {
        public int Count { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Count++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
