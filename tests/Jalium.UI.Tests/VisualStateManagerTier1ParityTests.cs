using System.Collections;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class VisualStateManagerTier1ParityTests
{
    [Fact]
    public void VisualStateTypes_ShouldMatchWpfInheritanceAndCollectionSurface()
    {
        Assert.False(typeof(VisualState).IsSealed);
        Assert.False(typeof(VisualStateGroup).IsSealed);
        Assert.False(typeof(VisualTransition).IsSealed);
        Assert.False(typeof(VisualStateManager).IsSealed);

        Assert.IsAssignableFrom<DependencyObject>(new VisualState());
        Assert.IsAssignableFrom<DependencyObject>(new VisualStateGroup());
        Assert.IsAssignableFrom<DependencyObject>(new VisualTransition());
        Assert.IsAssignableFrom<DependencyObject>(new VisualStateManager());

        Assert.Equal(typeof(IList), typeof(VisualStateGroup).GetProperty(nameof(VisualStateGroup.States))!.PropertyType);
        Assert.Equal(typeof(IList), typeof(VisualStateGroup).GetProperty(nameof(VisualStateGroup.Transitions))!.PropertyType);
        Assert.Equal(typeof(IList), typeof(VisualStateManager).GetMethod(nameof(VisualStateManager.GetVisualStateGroups))!.ReturnType);
    }

    [Fact]
    public void GoToState_ShouldRaiseChangingThenChanged_WithWpfEventData()
    {
        var root = new Button();
        var normal = new VisualState("Normal");
        var pressed = new VisualState("Pressed");
        var group = new VisualStateGroup("CommonStates");
        group.States.Add(normal);
        group.States.Add(pressed);
        VisualStateManager.SetVisualStateGroups(root, new ArrayList { group });

        var notifications = new List<(string EventName, object? Sender, VisualStateChangedEventArgs Args)>();
        group.CurrentStateChanging += (sender, args) =>
        {
            Assert.Same(args.OldState, group.CurrentState);
            notifications.Add(("Changing", sender, args));
        };
        group.CurrentStateChanged += (sender, args) =>
        {
            Assert.Same(args.NewState, group.CurrentState);
            notifications.Add(("Changed", sender, args));
        };

        Assert.True(VisualStateManager.GoToState(root, normal.Name, useTransitions: false));
        Assert.True(VisualStateManager.GoToState(root, pressed.Name, useTransitions: false));
        Assert.True(VisualStateManager.GoToState(root, pressed.Name, useTransitions: false));

        Assert.Collection(
            notifications,
            item => AssertNotification(item, "Changing", root, null, normal, root),
            item => AssertNotification(item, "Changed", root, null, normal, root),
            item => AssertNotification(item, "Changing", root, normal, pressed, root),
            item => AssertNotification(item, "Changed", root, normal, pressed, root));
    }

    [Fact]
    public void CustomManager_ShouldRaiseEventsThroughProtectedHelpers()
    {
        var root = new Button();
        var oldState = new VisualState("Old");
        var newState = new VisualState("New");
        var group = new VisualStateGroup("CommonStates");
        var manager = new RecordingVisualStateManager();
        var eventNames = new List<string>();
        VisualStateChangedEventArgs? changing = null;
        VisualStateChangedEventArgs? changed = null;

        group.CurrentStateChanging += (_, args) =>
        {
            eventNames.Add("Changing");
            changing = args;
        };
        group.CurrentStateChanged += (_, args) =>
        {
            eventNames.Add("Changed");
            changed = args;
        };

        manager.RaiseChanging(group, oldState, newState, root, root);
        manager.RaiseChanged(group, oldState, newState, root, root);

        Assert.Equal(new[] { "Changing", "Changed" }, eventNames);
        Assert.NotNull(changing);
        Assert.NotNull(changed);
        Assert.Same(oldState, changing.OldState);
        Assert.Same(newState, changing.NewState);
        Assert.Same(root, changing.Control);
        Assert.Same(root, changing.StateGroupsRoot);
        Assert.Same(oldState, changed.OldState);
        Assert.Same(newState, changed.NewState);
    }

    [Fact]
    public void GoToElementState_ShouldReportNullControl()
    {
        var root = new Button();
        var state = new VisualState("Visible");
        var group = new VisualStateGroup("VisibilityStates");
        group.States.Add(state);
        VisualStateManager.SetVisualStateGroups(root, new ArrayList { group });

        VisualStateChangedEventArgs? changed = null;
        group.CurrentStateChanged += (_, args) => changed = args;

        Assert.True(VisualStateManager.GoToElementState(root, state.Name, useTransitions: false));
        Assert.NotNull(changed);
        Assert.Null(changed.Control);
        Assert.Same(root, changed.StateGroupsRoot);
        Assert.Same(state, changed.NewState);
    }

    [Fact]
    public void GoToState_ShouldRouteUnknownStateToCustomManager()
    {
        var root = new Button();
        var group = new VisualStateGroup("CommonStates");
        group.States.Add(new VisualState("Normal"));
        VisualStateManager.SetVisualStateGroups(root, new ArrayList { group });

        var manager = new RecordingVisualStateManager { Result = true };
        VisualStateManager.SetCustomVisualStateManager(root, manager);

        Assert.Same(manager, VisualStateManager.GetCustomVisualStateManager(root));
        Assert.True(VisualStateManager.GoToState(root, "CustomOnly", useTransitions: true));
        Assert.Equal(1, manager.CallCount);
        Assert.Same(root, manager.Control);
        Assert.Same(root, manager.StateGroupsRoot);
        Assert.Equal("CustomOnly", manager.StateName);
        Assert.Null(manager.Group);
        Assert.Null(manager.State);
        Assert.True(manager.UseTransitions);
    }

    [Fact]
    public void CustomManager_ShouldBeAbleToDelegateKnownElementStateToBase()
    {
        var root = new Button();
        var state = new VisualState("Ready");
        var group = new VisualStateGroup("CommonStates");
        group.States.Add(state);
        VisualStateManager.SetVisualStateGroups(root, new ArrayList { group });

        var manager = new RecordingVisualStateManager { DelegateToBase = true };
        VisualStateManager.SetCustomVisualStateManager(root, manager);

        Assert.True(VisualStateManager.GoToElementState(root, state.Name, useTransitions: false));
        Assert.Null(manager.Control);
        Assert.Same(group, manager.Group);
        Assert.Same(state, manager.State);
        Assert.Same(state, group.CurrentState);
    }

    [Fact]
    public void GoToState_ShouldRejectNullStateNameLikeWpf()
    {
        var root = new Button();
        VisualStateManager.SetVisualStateGroups(root, new ArrayList());

        Assert.Throws<ArgumentNullException>(() => VisualStateManager.GoToState(root, null!, useTransitions: false));
        Assert.Throws<ArgumentNullException>(() => VisualStateManager.GoToElementState(root, null!, useTransitions: false));
    }

    private static void AssertNotification(
        (string EventName, object? Sender, VisualStateChangedEventArgs Args) notification,
        string eventName,
        FrameworkElement sender,
        VisualState? oldState,
        VisualState newState,
        FrameworkElement control)
    {
        Assert.Equal(eventName, notification.EventName);
        Assert.Same(sender, notification.Sender);
        Assert.Same(oldState, notification.Args.OldState);
        Assert.Same(newState, notification.Args.NewState);
        Assert.Same(control, notification.Args.Control);
        Assert.Same(sender, notification.Args.StateGroupsRoot);
    }

    private sealed class RecordingVisualStateManager : VisualStateManager
    {
        public bool Result { get; init; }

        public bool DelegateToBase { get; init; }

        public int CallCount { get; private set; }

        public FrameworkElement? Control { get; private set; }

        public FrameworkElement? StateGroupsRoot { get; private set; }

        public string? StateName { get; private set; }

        public VisualStateGroup? Group { get; private set; }

        public VisualState? State { get; private set; }

        public bool UseTransitions { get; private set; }

        public void RaiseChanging(
            VisualStateGroup group,
            VisualState? oldState,
            VisualState newState,
            FrameworkElement? control,
            FrameworkElement? stateGroupsRoot)
        {
            RaiseCurrentStateChanging(group, oldState, newState, control, stateGroupsRoot);
        }

        public void RaiseChanged(
            VisualStateGroup group,
            VisualState? oldState,
            VisualState newState,
            FrameworkElement? control,
            FrameworkElement? stateGroupsRoot)
        {
            RaiseCurrentStateChanged(group, oldState, newState, control, stateGroupsRoot);
        }

        protected override bool GoToStateCore(
            FrameworkElement? control,
            FrameworkElement stateGroupsRoot,
            string stateName,
            VisualStateGroup? group,
            VisualState? state,
            bool useTransitions)
        {
            CallCount++;
            Control = control;
            StateGroupsRoot = stateGroupsRoot;
            StateName = stateName;
            Group = group;
            State = state;
            UseTransitions = useTransitions;

            return DelegateToBase
                ? base.GoToStateCore(control, stateGroupsRoot, stateName, group, state, useTransitions)
                : Result;
        }
    }
}
