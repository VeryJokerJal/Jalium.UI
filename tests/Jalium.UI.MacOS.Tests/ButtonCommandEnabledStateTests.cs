using System.ComponentModel;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class ButtonCommandEnabledStateTests
{
    [Fact]
    public void CommandMustNotOverrideAnExplicitDisableOrInputGate()
    {
        var button = new Button { IsEnabled = false, Command = new Command() };
        Assert.False(button.IsEnabled);
        Assert.False(button.CanRespondToInput());
        Assert.Equal(false, button.ReadLocalValue(UIElement.IsEnabledProperty));
    }

    [Fact]
    public void CommandStateAndBindingRemainIndependentAcrossUpdatesAndRemoval()
    {
        var model = new EnabledModel();
        var command = new Command();
        var button = new Button();
        var binding = button.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(EnabledModel.Enabled)) { Source = model });
        var changes = new List<bool>();
        button.IsEnabledChanged += (_, e) => changes.Add((bool)e.NewValue!);
        button.Command = command;
        Assert.False(button.IsEnabled);
        Assert.Same(binding, button.GetBindingExpression(UIElement.IsEnabledProperty));
        model.Enabled = true;
        Assert.True(button.IsEnabled);
        command.Enabled = false;
        command.Notify();
        Assert.False(button.IsEnabled);
        Assert.Same(binding, button.GetBindingExpression(UIElement.IsEnabledProperty));
        Assert.Contains(false, changes);
        model.Enabled = false;
        command.Enabled = true;
        command.Notify();
        Assert.False(button.IsEnabled);
        model.Enabled = true;
        Assert.True(button.IsEnabled);
        command.Enabled = false;
        command.Notify();
        button.Command = null;
        Assert.True(button.IsEnabled);
        Assert.Same(binding, button.GetBindingExpression(UIElement.IsEnabledProperty));
    }

    [Fact]
    public void ADelayedCommandNotificationCanHealOnlyItsOwnDisable()
    {
        var command = new Command { Enabled = false };
        var button = new Button { Command = command };
        Assert.False(button.IsEnabled);
        command.Enabled = true; // no notification yet
        Assert.True(button.CanRespondToInput());
        Assert.True(button.IsEnabled);
        button.IsEnabled = false;
        Assert.False(button.CanRespondToInput());
        Assert.False(button.IsEnabled);
    }

    private sealed class Command : ICommand
    {
        public bool Enabled { get; set; } = true;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => Enabled;
        public void Execute(object? parameter) { }
        public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    private sealed class EnabledModel : INotifyPropertyChanged
    {
        private bool _enabled;
        public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
