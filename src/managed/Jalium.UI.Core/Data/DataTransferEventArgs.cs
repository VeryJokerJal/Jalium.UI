namespace Jalium.UI.Data;

/// <summary>
/// Provides data for the <see cref="Binding.TargetUpdated"/> and
/// <see cref="Binding.SourceUpdated"/> routed events.
/// </summary>
public sealed class DataTransferEventArgs : RoutedEventArgs
{
    public DataTransferEventArgs(DependencyObject targetObject, DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(targetObject);
        ArgumentNullException.ThrowIfNull(property);
        TargetObject = targetObject;
        Property = property;
    }

    public DependencyObject TargetObject { get; }

    public DependencyProperty Property { get; }

    public object? Item { get; set; }

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        ((EventHandler<DataTransferEventArgs>)genericHandler)(genericTarget, this);
    }
}
