using System.Reflection;

namespace Jalium.UI.Tests;

internal static class DependencyObjectSubscriptionProbe
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static int Count(DependencyObject source, string eventName)
    {
        // Inspect actual delegates without allocating optional state on a target
        // that has never needed an observer. Support both event storage layouts.
        object owner = source;
        var field = typeof(DependencyObject).GetField(eventName, Fields);
        if (field == null)
        {
            var optionalState = typeof(DependencyObject).GetField("_optionalState", Fields);
            Assert.NotNull(optionalState);
            var state = optionalState.GetValue(source); if (state == null) return 0;
            owner = state;
            var name = eventName switch { "PropertyChangedInternal" => "PropertyChanged", "ValueSourceInvalidatedInternal" => "ValueSourceInvalidated", _ => eventName };
            field = state.GetType().GetField(name, Fields); Assert.NotNull(field);
        }
        return (field.GetValue(owner) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}
