using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Data;

/// <summary>Provides process-wide services for bindings and collection synchronization.</summary>
public static class BindingOperations
{
    private static readonly object SynchronizationGate = new();
    private static readonly ConditionalWeakTable<IEnumerable, SynchronizationRegistration> Synchronization = new();
    private static readonly object DisconnectedSourceValue = new DisconnectedSourceMarker();

    public static object DisconnectedSource => DisconnectedSourceValue;

    public static event EventHandler<CollectionRegisteringEventArgs>? CollectionRegistering;

    public static event EventHandler<CollectionViewRegisteringEventArgs>? CollectionViewRegistering;

    public static BindingExpressionBase SetBinding(
        DependencyObject target,
        DependencyProperty dp,
        BindingBase binding)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(binding);
        return target.SetBinding(dp, binding);
    }

    public static void ClearBinding(DependencyObject target, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        target.ClearBinding(dp);
    }

    public static void ClearAllBindings(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.ClearAllBindings();
    }

    public static bool IsDataBound(DependencyObject target, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        return target.GetBindingExpression(dp) is not null;
    }

    public static BindingExpression? GetBindingExpression(DependencyObject target, DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp) as BindingExpression;

    public static BindingExpressionBase? GetBindingExpressionBase(
        DependencyObject target,
        DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dp);
        return target.GetBindingExpression(dp);
    }

    public static BindingBase? GetBindingBase(DependencyObject target, DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp) switch
        {
            BindingExpression expression => expression.ParentBinding,
            MultiBindingExpression expression => expression.ParentMultiBinding,
            PriorityBindingExpression expression => expression.ParentPriorityBinding,
            _ => null,
        };

    public static Binding? GetBinding(DependencyObject target, DependencyProperty dp) =>
        GetBindingBase(target, dp) as Binding;

    public static MultiBinding? GetMultiBinding(DependencyObject target, DependencyProperty dp) =>
        GetBindingBase(target, dp) as MultiBinding;

    public static MultiBindingExpression? GetMultiBindingExpression(
        DependencyObject target,
        DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp) as MultiBindingExpression;

    public static PriorityBinding? GetPriorityBinding(DependencyObject target, DependencyProperty dp) =>
        GetBindingBase(target, dp) as PriorityBinding;

    public static PriorityBindingExpression? GetPriorityBindingExpression(
        DependencyObject target,
        DependencyProperty dp) =>
        GetBindingExpressionBase(target, dp) as PriorityBindingExpression;

    public static void EnableCollectionSynchronization(IEnumerable collection, object lockObject)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(lockObject);
        SetSynchronization(collection, new SynchronizationRegistration(lockObject, Callback: null));
    }

    public static void EnableCollectionSynchronization(
        IEnumerable collection,
        object context,
        CollectionSynchronizationCallback synchronizationCallback)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(synchronizationCallback);
        SetSynchronization(collection, new SynchronizationRegistration(context, synchronizationCallback));
    }

    public static void DisableCollectionSynchronization(IEnumerable collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        lock (SynchronizationGate)
        {
            Synchronization.Remove(collection);
        }
    }

    public static void AccessCollection(IEnumerable collection, Action accessMethod, bool writeAccess)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(accessMethod);

        SynchronizationRegistration? registration;
        lock (SynchronizationGate)
        {
            Synchronization.TryGetValue(collection, out registration);
        }

        if (registration is null)
        {
            accessMethod();
        }
        else if (registration.Callback is not null)
        {
            registration.Callback(collection, registration.Context, accessMethod, writeAccess);
        }
        else
        {
            lock (registration.Context)
            {
                accessMethod();
            }
        }
    }

    public static ReadOnlyCollection<BindingExpressionBase> GetSourceUpdatingBindings(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var expressions = EnumerateTree(root)
            .SelectMany(static node => node.GetBindingExpressionsInternal())
            .Where(static expression => expression.IsActive)
            .Distinct()
            .ToList();
        return expressions.AsReadOnly();
    }

    public static ReadOnlyCollection<BindingGroup> GetSourceUpdatingBindingGroups(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var groups = EnumerateTree(root)
            .OfType<FrameworkElement>()
            .Select(static element => element.BindingGroup)
            .Where(static group => group is { IsDirty: true })
            .Cast<BindingGroup>()
            .Distinct()
            .ToList();
        return groups.AsReadOnly();
    }

    internal static void RegisterCollectionView(CollectionView view, IEnumerable source)
    {
        CollectionRegistering?.Invoke(null, new CollectionRegisteringEventArgs(source, view));
        CollectionViewRegistering?.Invoke(null, new CollectionViewRegisteringEventArgs(view));
    }

    private static void SetSynchronization(
        IEnumerable collection,
        SynchronizationRegistration registration)
    {
        lock (SynchronizationGate)
        {
            Synchronization.Remove(collection);
            Synchronization.Add(collection, registration);
        }
        CollectionRegistering?.Invoke(null, new CollectionRegisteringEventArgs(collection));
    }

    private static IEnumerable<DependencyObject> EnumerateTree(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        pending.Push(root);

        while (pending.Count > 0)
        {
            DependencyObject current = pending.Pop();
            if (!visited.Add(current))
                continue;
            yield return current;

            if (current is Visual visual)
            {
                for (int index = visual.VisualChildrenCount - 1; index >= 0; index--)
                {
                    if (visual.GetVisualChild(index) is DependencyObject child)
                        pending.Push(child);
                }
            }

            if (current is FrameworkElement frameworkElement)
            {
                IEnumerator logicalChildren = frameworkElement.LogicalChildren;
                while (logicalChildren.MoveNext())
                {
                    if (logicalChildren.Current is DependencyObject child)
                        pending.Push(child);
                }
            }
        }
    }

    private sealed record SynchronizationRegistration(
        object Context,
        CollectionSynchronizationCallback? Callback);

    private sealed class DisconnectedSourceMarker
    {
        public override string ToString() => "{DisconnectedSource}";
    }
}
