using System.Collections;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Shared helpers for the two-source VisualChildrenCount desync regression tests.
///
/// Background: <see cref="DependencyObject"/> exposes a public cached
/// <c>VisualChildrenCount</c> field plus a <c>GetVisualChild</c> delegate that routes to
/// the live virtual accessor. Any tree walk that reads the loop bound from the cached
/// field while fetching children through the delegate crashes with
/// <see cref="ArgumentOutOfRangeException"/> the moment the field goes stale. These
/// helpers assert the invariant that keeps that walk safe.
/// </summary>
internal static class VisualTreeTestHelpers
{
    /// <summary>
    /// Asserts the visual-children-count shim is self-consistent: the cached field equals
    /// the live virtual count, and every index in [0, cachedField) is walkable through the
    /// public delegate accessor without throwing — i.e. exactly the field-bound + delegate
    /// pair that crashes when the field is stale.
    /// </summary>
    public static void AssertShimConsistent(DependencyObject d)
    {
        int cached = d.VisualChildrenCount;              // public cached field
        int live = VisualTreeHelper.GetChildrenCount(d); // live virtual (via internal accessor)

        Assert.True(
            cached == live,
            $"Stale VisualChildrenCount shim on {d.GetType().Name}: cached field={cached}, live virtual={live}. {DumpState(d)}");

        for (int i = 0; i < cached; i++)
        {
            // The delegate accessor dispatches to the live virtual GetVisualChild; with a
            // stale-high cached bound this throws ArgumentOutOfRangeException at i == live.
            var child = d.GetVisualChild(i);
            Assert.True(child != null, $"GetVisualChild({i}) returned null on {d.GetType().Name}. {DumpState(d)}");
        }
    }

    /// <summary>Reads a private/internal instance field, walking the base-type chain.</summary>
    public static object? PrivateFieldValue(object o, string name)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) return f.GetValue(o);
        }
        return null;
    }

    /// <summary>Depth-first search for the first descendant of type <typeparamref name="T"/>.</summary>
    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (child != null)
            {
                var nested = FindDescendant<T>(child);
                if (nested != null)
                    return nested;
            }
        }
        return null;
    }

    /// <summary>
    /// Resets process-global Application/theme state so theme-dependent tests are not at the
    /// mercy of xUnit's per-process random collection order.
    /// </summary>
    public static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static string DumpState(DependencyObject d)
    {
        var children = PrivateFieldValue(d, "_children") as IList;
        return $"_children.Count={children?.Count}, _templateRoot={(PrivateFieldValue(d, "_templateRoot") == null ? "null" : "SET")}, " +
               $"_contentElement={(PrivateFieldValue(d, "_contentElement") == null ? "null" : "SET")}, " +
               $"_isTransitioning={PrivateFieldValue(d, "_isTransitioning")}";
    }
}

/// <summary>ListBox that exposes its items host so tests can force a re-realization pass.</summary>
internal sealed class ShimTestListBox : ListBox
{
    public Panel? Host => ItemsHost;
}
