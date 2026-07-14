using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Interactivity;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class InteractivityCanonicalOwnershipTests
{
    [Fact]
    public void InteractivityTriggerNamesDoNotDuplicateWpfRootTypes()
    {
        Assembly assembly = typeof(Interaction).Assembly;

        Assert.Null(assembly.GetType("Jalium.UI.Interactivity.EventTrigger", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Interactivity.TriggerBase", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Interactivity.TriggerBase`1", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Interactivity.TriggerAction", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Interactivity.TriggerAction`1", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Interactivity.TriggerCollection", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Interactivity.TriggerActionCollection", throwOnError: false));

        Assert.NotNull(assembly.GetType("Jalium.UI.EventTrigger", throwOnError: false));
        Assert.NotNull(assembly.GetType("Jalium.UI.TriggerBase", throwOnError: false));
        Assert.NotNull(assembly.GetType("Jalium.UI.TriggerAction", throwOnError: false));
        Assert.Equal(typeof(BehaviorTriggerActionCollection), typeof(BehaviorTriggerBase).GetProperty(nameof(BehaviorTriggerBase.Actions))!.PropertyType);
        Assert.Equal(typeof(BehaviorTriggerCollection), typeof(Interaction).GetMethod(nameof(Interaction.GetTriggers))!.ReturnType);
    }

    [Fact]
    public void XamlRegistryKeepsRootEventTriggerAndRegistersBehaviorEventTriggerSeparately()
    {
        const string presentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        object rootTrigger = XamlReader.Parse($"<EventTrigger xmlns=\"{presentationNamespace}\" />");
        object behaviorTrigger = XamlReader.Parse($"<BehaviorEventTrigger xmlns=\"{presentationNamespace}\" />");

        Assert.IsType<Jalium.UI.EventTrigger>(rootTrigger);
        Assert.IsType<BehaviorEventTrigger>(behaviorTrigger);
    }

    [Fact]
    public void RenamedBehaviorTriggerPreservesCommandAndMethodActions()
    {
        var owner = new Button();
        var source = new EventSource();
        var command = new RecordingCommand();
        var receiver = new MethodReceiver();
        var trigger = new BehaviorEventTrigger
        {
            EventName = nameof(EventSource.Fired),
            SourceObject = source,
        };
        trigger.Actions.Add(new InvokeCommandAction
        {
            Command = command,
            PassEventArgsToCommand = true,
        });
        trigger.Actions.Add(new CallMethodAction
        {
            TargetObject = receiver,
            MethodName = nameof(MethodReceiver.Record),
        });

        BehaviorTriggerCollection triggers = Interaction.GetTriggers(owner);
        Assert.Same(triggers, Interaction.GetTriggers(owner));
        triggers.Add(trigger);
        Assert.Same(owner, trigger.AssociatedObject);

        var args = new EventArgs();
        source.Raise(args);

        Assert.Same(args, command.Parameter);
        Assert.Same(args, receiver.Parameter);

        triggers.Remove(trigger);
        source.Raise(new EventArgs());
        Assert.Equal(1, command.ExecutionCount);
        Assert.Equal(1, receiver.InvocationCount);
    }

    private sealed class EventSource
    {
        public event EventHandler? Fired;

        public void Raise(EventArgs args) => Fired?.Invoke(this, args);
    }

    private sealed class RecordingCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecutionCount { get; private set; }

        public object? Parameter { get; private set; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            ExecutionCount++;
            Parameter = parameter;
        }
    }

    private sealed class MethodReceiver
    {
        public int InvocationCount { get; private set; }

        public EventArgs? Parameter { get; private set; }

        public void Record(EventArgs parameter)
        {
            InvocationCount++;
            Parameter = parameter;
        }
    }
}
