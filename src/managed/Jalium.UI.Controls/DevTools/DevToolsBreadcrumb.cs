using System.Windows.Input;
using System.Collections.Specialized;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>An immutable path entry that does not keep an inspected ancestor alive.</summary>
public sealed class DevToolsBreadcrumbNode
{
    private readonly WeakReference<Visual> _target;
    static DevToolsBreadcrumbNode()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsBreadcrumbNode), nameof(Text), value => ((DevToolsBreadcrumbNode)value).Text);
        PropertyAccessorRegistry.Register(typeof(DevToolsBreadcrumbNode), nameof(Hint), value => ((DevToolsBreadcrumbNode)value).Hint);
        PropertyAccessorRegistry.Register(typeof(DevToolsBreadcrumbNode), nameof(IsCurrent), value => ((DevToolsBreadcrumbNode)value).IsCurrent);
        PropertyAccessorRegistry.Register(typeof(DevToolsBreadcrumbNode), nameof(HasPrevious), value => ((DevToolsBreadcrumbNode)value).HasPrevious);
    }
    public DevToolsBreadcrumbNode(Visual target, bool isCurrent = false, bool hasPrevious = false)
    {
        ArgumentNullException.ThrowIfNull(target); _target = new(target);
        Text = target.GetType().Name;
        Hint = target is FrameworkElement { Name.Length: > 0 } element ? Text + " #" + element.Name : Text;
        IsCurrent = isCurrent; HasPrevious = hasPrevious;
    }
    public string Text { get; }
    public string Hint { get; }
    public bool IsCurrent { get; }
    public bool HasPrevious { get; }
    public bool TryGetTarget(out Visual? target) => _target.TryGetTarget(out target);
    public override string ToString() => Text;
}

public sealed class DevToolsBreadcrumbNavigationEventArgs : RoutedEventArgs
{
    public DevToolsBreadcrumbNavigationEventArgs(RoutedEvent routedEvent, Visual target) : base(routedEvent) => Target = target;
    public Visual Target { get; }
    protected override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<DevToolsBreadcrumbNavigationEventArgs> typed) typed(target, this);
        else base.InvokeEventHandler(handler, target);
    }
}

/// <summary>A wrapping, command-driven ancestor path with replaceable item and container templates.</summary>
public class DevToolsBreadcrumb : ItemsControl
{
    private double _arrangedWidth = double.NaN;
    private readonly NavigationCommand _defaultCommand;
    static DevToolsBreadcrumb() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsBreadcrumb), new PropertyMetadata(typeof(DevToolsBreadcrumb)));
    public DevToolsBreadcrumb() { _defaultCommand = new NavigationCommand(this); SetCurrentValue(NavigateCommandProperty, _defaultCommand); }
    public static readonly DependencyProperty NavigateCommandProperty = DependencyProperty.Register(nameof(NavigateCommand), typeof(ICommand), typeof(DevToolsBreadcrumb), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey ItemMaxWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ItemMaxWidth), typeof(double), typeof(DevToolsBreadcrumb), new PropertyMetadata(double.PositiveInfinity));
    public static readonly DependencyProperty ItemMaxWidthProperty = ItemMaxWidthPropertyKey.DependencyProperty;
    public static readonly RoutedEvent NavigationRequestedEvent = EventManager.RegisterRoutedEvent(nameof(NavigationRequested), RoutingStrategy.Bubble, typeof(EventHandler<DevToolsBreadcrumbNavigationEventArgs>), typeof(DevToolsBreadcrumb));
    public ICommand? NavigateCommand { get => (ICommand?)GetValue(NavigateCommandProperty); set => SetValue(NavigateCommandProperty, value); }
    public double ItemMaxWidth => (double)GetValue(ItemMaxWidthProperty)!;
    public event EventHandler<DevToolsBreadcrumbNavigationEventArgs> NavigationRequested { add => AddHandler(NavigationRequestedEvent, value); remove => RemoveHandler(NavigationRequestedEvent, value); }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); _defaultCommand.RaiseAvailabilityChanged(); }
    internal override void OnTemplateContentClearing() { base.OnTemplateContentClearing(); _defaultCommand.RaiseAvailabilityChanged(); }
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) { base.OnItemsChanged(e); _defaultCommand?.RaiseAvailabilityChanged(); }
    private bool CanNavigate(object? parameter)
    {
        if (parameter is not DevToolsBreadcrumbNode { IsCurrent: false } node || !Items.Contains(node) || !node.TryGetTarget(out _) || ItemsHost == null) return false;
        for (Visual? current = ItemsHost; current != null; current = current.VisualParent) if (ReferenceEquals(current, this)) return true;
        return false;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        if (double.IsFinite(width) && double.IsFinite(_arrangedWidth) && _arrangedWidth > 0) width = Math.Min(width, _arrangedWidth);
        SetValue(ItemMaxWidthPropertyKey, double.IsFinite(width) ? Math.Max(0, width) : double.PositiveInfinity);
        return base.MeasureOverride(availableSize);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _arrangedWidth = finalSize.Width; SetValue(ItemMaxWidthPropertyKey, Math.Max(0, finalSize.Width)); return base.ArrangeOverride(finalSize);
    }
    private sealed class NavigationCommand(DevToolsBreadcrumb owner) : ICommand
    {
        // Entries are immutable snapshots; invocation rechecks weak-target liveness.
        public event EventHandler? CanExecuteChanged;
        public void RaiseAvailabilityChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        public bool CanExecute(object? parameter) => owner.CanNavigate(parameter);
        public void Execute(object? parameter)
        {
            if (CanExecute(parameter) && parameter is DevToolsBreadcrumbNode node && node.TryGetTarget(out var target))
                owner.RaiseEvent(new DevToolsBreadcrumbNavigationEventArgs(NavigationRequestedEvent, target!));
        }
    }
}
