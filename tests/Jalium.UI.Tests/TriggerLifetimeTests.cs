using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class TriggerLifetimeTests
{
    [Fact]
    public void Trigger_DoesNotRootElementThroughSharedState()
    {
        var trigger = new Trigger
        {
            Property = LifetimeProbe.FlagProperty,
            Value = true,
        };
        trigger.Setters.Add(new Setter(LifetimeProbe.TokenProperty, "active"));

        AssertAttachedElementIsCollectible(trigger);
    }

    [Fact]
    public void MultiTrigger_DoesNotRootElementThroughSharedState()
    {
        var trigger = new MultiTrigger();
        trigger.Conditions.Add(new Condition(LifetimeProbe.FlagProperty, true));
        trigger.Setters.Add(new Setter(LifetimeProbe.TokenProperty, "active"));

        AssertAttachedElementIsCollectible(trigger);
    }

    [Fact]
    public void DataTrigger_DoesNotRootElementThroughSharedState()
    {
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(LifetimeViewModel.Flag)),
            Value = true,
        };
        trigger.Setters.Add(new Setter(LifetimeProbe.TokenProperty, "active"));

        AssertAttachedElementIsCollectible(trigger);
    }

    [Fact]
    public void MultiDataTrigger_DoesNotRootElementThroughSharedState()
    {
        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding(nameof(LifetimeViewModel.Flag)),
            Value = true,
        });
        trigger.Setters.Add(new Setter(LifetimeProbe.TokenProperty, "active"));

        AssertAttachedElementIsCollectible(trigger);
    }

    private static void AssertAttachedElementIsCollectible(TriggerBase trigger)
    {
        var weakElement = AttachAndRelease(trigger);
        ForceFullCollection();

        Assert.False(
            weakElement.TryGetTarget(out _),
            $"{trigger.GetType().Name} retained an element after all external references were released.");
        GC.KeepAlive(trigger);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<LifetimeProbe> AttachAndRelease(TriggerBase trigger)
    {
        var element = new LifetimeProbe
        {
            DataContext = new LifetimeViewModel { Flag = true },
            Flag = true,
        };
        trigger.AttachForTest(element);
        Assert.Equal("active", element.Token);
        return new WeakReference<LifetimeProbe>(element);
    }

    private static void ForceFullCollection()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class LifetimeProbe : FrameworkElement
    {
        public static readonly DependencyProperty FlagProperty =
            DependencyProperty.Register(
                nameof(Flag),
                typeof(bool),
                typeof(LifetimeProbe),
                new PropertyMetadata(false));

        public static readonly DependencyProperty TokenProperty =
            DependencyProperty.Register(
                nameof(Token),
                typeof(string),
                typeof(LifetimeProbe),
                new PropertyMetadata("baseline"));

        public bool Flag
        {
            get => (bool)GetValue(FlagProperty)!;
            set => SetValue(FlagProperty, value);
        }

        public string Token
        {
            get => (string)GetValue(TokenProperty)!;
            set => SetValue(TokenProperty, value);
        }
    }

    private sealed class LifetimeViewModel : INotifyPropertyChanged
    {
        private bool _flag;

        public bool Flag
        {
            get => _flag;
            set
            {
                if (_flag == value)
                    return;

                _flag = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flag)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
