using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Begins a storyboard when its owning trigger is invoked.
/// </summary>
[RuntimeNameProperty(nameof(Name))]
[ContentProperty(nameof(Storyboard))]
public sealed class BeginStoryboard : TriggerAction
{
    private HandoffBehavior _handoffBehavior = HandoffBehavior.SnapshotAndReplace;
    private string? _name;

    public static readonly DependencyProperty StoryboardProperty =
        DependencyProperty.Register(
            nameof(Storyboard),
            typeof(Storyboard),
            typeof(BeginStoryboard),
            new PropertyMetadata(null));

    public BeginStoryboard()
    {
    }

    [DefaultValue(null)]
    public Storyboard? Storyboard
    {
        get => (Storyboard?)GetValue(StoryboardProperty);
        set
        {
            CheckSealed();
            SetValue(StoryboardProperty, value);
        }
    }

    [DefaultValue(HandoffBehavior.SnapshotAndReplace)]
    public HandoffBehavior HandoffBehavior
    {
        get => _handoffBehavior;
        set
        {
            CheckSealed();
            if (value is not HandoffBehavior.SnapshotAndReplace and not HandoffBehavior.Compose)
            {
                throw new ArgumentException("The handoff behavior is not valid.", nameof(value));
            }

            _handoffBehavior = value;
        }
    }

    [DefaultValue(null)]
    public string? Name
    {
        get => _name;
        set
        {
            CheckSealed();
            _name = value;
        }
    }

    internal override void Invoke(FrameworkElement? element)
    {
        Storyboard? storyboard = Storyboard;
        if (storyboard is null)
        {
            return;
        }

        if (element is null)
        {
            storyboard.Begin();
        }
        else
        {
            storyboard.Begin(element, HandoffBehavior, isControllable: Name is not null);
        }
    }
}

/// <summary>
/// Base class for trigger actions that control a named storyboard.
/// </summary>
public abstract class ControllableStoryboardAction : TriggerAction
{
    private string? _beginStoryboardName;

    internal ControllableStoryboardAction()
    {
    }

    [DefaultValue(null)]
    public string? BeginStoryboardName
    {
        get => _beginStoryboardName;
        set
        {
            CheckSealed();
            _beginStoryboardName = value;
        }
    }

    internal override void Invoke(FrameworkElement? element)
    {
        // Trigger-level name resolution is owned by the style/template engine.
        // The overload below remains the behavior-bearing path once resolved.
    }

    internal abstract void Invoke(FrameworkElement? element, Storyboard storyboard);
}

public sealed class PauseStoryboard : ControllableStoryboardAction
{
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        if (element is null)
        {
            storyboard.Pause();
        }
        else
        {
            storyboard.Pause(element);
        }
    }
}

public sealed class ResumeStoryboard : ControllableStoryboardAction
{
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        if (element is null)
        {
            storyboard.Resume();
        }
        else
        {
            storyboard.Resume(element);
        }
    }
}

public sealed class StopStoryboard : ControllableStoryboardAction
{
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        if (element is null)
        {
            storyboard.Stop();
        }
        else
        {
            storyboard.Stop(element);
        }
    }
}

public sealed class SeekStoryboard : ControllableStoryboardAction
{
    private TimeSpan _offset = TimeSpan.Zero;
    private TimeSeekOrigin _origin = TimeSeekOrigin.BeginTime;

    public TimeSpan Offset
    {
        get => _offset;
        set
        {
            CheckSealed();
            _offset = value;
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeOffset() => _offset != TimeSpan.Zero;

    [DefaultValue(TimeSeekOrigin.BeginTime)]
    public TimeSeekOrigin Origin
    {
        get => _origin;
        set
        {
            CheckSealed();
            if (value is not TimeSeekOrigin.BeginTime and not TimeSeekOrigin.Duration)
            {
                throw new ArgumentException("The seek origin is not valid.", nameof(value));
            }

            _origin = value;
        }
    }

    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        if (element is null)
        {
            storyboard.Seek(Offset, Origin);
        }
        else
        {
            storyboard.Seek(element, Offset, Origin);
        }
    }
}

public sealed class RemoveStoryboard : ControllableStoryboardAction
{
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        if (element is null)
        {
            storyboard.Remove();
        }
        else
        {
            storyboard.Remove(element);
        }
    }
}

public sealed class SetStoryboardSpeedRatio : ControllableStoryboardAction
{
    private double _speedRatio = 1.0;

    [DefaultValue(1.0)]
    public double SpeedRatio
    {
        get => _speedRatio;
        set
        {
            CheckSealed();
            _speedRatio = value;
        }
    }

    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        if (element is null)
        {
            storyboard.SetSpeedRatio(SpeedRatio);
        }
        else
        {
            storyboard.SetSpeedRatio(element, SpeedRatio);
        }
    }
}

public sealed class SkipStoryboardToFill : ControllableStoryboardAction
{
    internal override void Invoke(FrameworkElement? element, Storyboard storyboard)
    {
        if (element is null)
        {
            storyboard.SkipToFill();
        }
        else
        {
            storyboard.SkipToFill(element);
        }
    }
}
