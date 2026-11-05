using System.ComponentModel;
using System.Windows.Input;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;
[Collection("Application")]
public sealed class MenuItemAutomationInvocationTests
{
    [Fact]
    public void InvokeExecutesCommandOnceAndClosesItsOwningMenu()
    {
        var command = new CounterCommand();
        var parameter = new object ();
        var item = new MenuItem
        {
            Header = "Run",
            Command = command,
            CommandParameter = parameter
        };
        var menu = Open(item);
        int clicks = 0;
        item.Click += (_, _) => clicks++;
        Invoke(item);
        Assert.Equal(1, clicks);
        Assert.Equal(1, command.Count);
        Assert.Same(parameter, command.LastParameter);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void CheckStateChangesBeforeClickAndStaysOpenIsRespected()
    {
        var item = new MenuItem
        {
            Header = "Check",
            IsCheckable = true,
            StaysOpenOnClick = true
        };
        var menu = Open(item);
        bool checkedAtClick = false;
        item.Click += (_, _) => checkedAtClick = item.IsChecked;
        Invoke(item);
        Assert.True(checkedAtClick);
        Assert.True(item.IsChecked);
        Assert.True(menu.IsOpen);
        menu.IsOpen = false;
    }

    [Fact]
    public void DisabledInvocationHasNoSideEffects()
    {
        var command = new CounterCommand();
        var item = new MenuItem
        {
            Header = "Disabled",
            Command = command,
            IsEnabled = false,
            IsCheckable = true
        };
        var menu = Open(item);
        int clicks = 0;
        item.Click += (_, _) => clicks++;
        Assert.Throws<InvalidOperationException>(() => Invoke(item));
        Assert.Equal(0, clicks);
        Assert.Equal(0, command.Count);
        Assert.False(item.IsChecked);
        Assert.True(menu.IsOpen);
        menu.IsOpen = false;
    }

    [Fact]
    public void RoutedCommandTargetAndVirtualClickPipelineAreUsed()
    {
        var target = new Button();
        var command = new RoutedCommand("AutomationMenuCommand", typeof(MenuItemAutomationInvocationTests));
        var parameter = new object ();
        int executed = 0;
        target.CommandBindings.Add(new CommandBinding(command, (_, args) =>
        {
            Assert.Same(parameter, args.Parameter);
            executed++;
        }, (_, args) => args.CanExecute = true));
        var item = new DerivedItem
        {
            Header = "Target",
            CommandTarget = target,
            CommandParameter = parameter,
            Command = command
        };
        var menu = Open(item);
        Invoke(item);
        Assert.Equal(1, item.VirtualClicks);
        Assert.Equal(1, executed);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void CanExecuteChangesControlAvailabilityAndExecution()
    {
        var command = new CounterCommand();
        var item = new MenuItem
        {
            Header = "Command",
            Command = command
        };
        var menu = Open(item);
        command.CanRun = false;
        command.Notify();
        Assert.False(item.IsEnabled);
        Assert.Throws<InvalidOperationException>(() => Invoke(item));
        Assert.True(menu.IsOpen);
        command.CanRun = true;
        command.Notify();
        Invoke(item);
        Assert.Equal(1, command.Count);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void InvokeAndTogglePreserveTheCallersCheckedBinding()
    {
        var source = new CheckSource();
        var item = new MenuItem
        {
            Header = "Bound",
            IsCheckable = true,
            StaysOpenOnClick = true
        };
        BindingOperations.SetBinding(item, MenuItem.IsCheckedProperty, new Binding(nameof(CheckSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
        Invoke(item);
        Assert.True(source.Value);
        Assert.NotNull(BindingOperations.GetBindingExpression(item, MenuItem.IsCheckedProperty));
        ((IToggleProvider)new MenuItemAutomationPeer(item)).Toggle();
        Assert.False(source.Value);
        Assert.NotNull(BindingOperations.GetBindingExpression(item, MenuItem.IsCheckedProperty));
        source.Value = true;
        Assert.True(item.IsChecked);
    }

    private static ContextMenu Open(MenuItem item)
    {
        var menu = new ContextMenu();
        menu.Items.Add(item);
        menu.IsOpen = true;
        return menu;
    }

    private static void Invoke(MenuItem item) => ((IInvokeProvider)new MenuItemAutomationPeer(item)).Invoke();
    private sealed class DerivedItem : MenuItem
    {
        public int VirtualClicks { get; private set; }

        protected override void OnClick()
        {
            VirtualClicks++;
            base.OnClick();
        }
    }

    private sealed class CounterCommand : ICommand
    {
        public bool CanRun { get; set; } = true;
        public int Count { get; private set; }
        public object? LastParameter { get; private set; }

        public bool CanExecute(object? parameter) => CanRun;
        public void Execute(object? parameter)
        {
            Count++;
            LastParameter = parameter;
        }

        public event EventHandler? CanExecuteChanged;
        public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CheckSource : INotifyPropertyChanged
    {
        private bool _value;
        public bool Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
