namespace Jalium.UI;

/// <summary>
/// Controls compatibility behavior used by Jalium's presentation core.
/// Preferences become immutable when first consumed by framework code.
/// </summary>
public static class CoreCompatibilityPreferences
{
    private static readonly object s_syncRoot = new();

    private static bool s_isSealed;
    private static bool s_isAltKeyRequiredInAccessKeyDefaultScope;
    private static bool s_includeAllInkInBoundingBox = true;
    private static bool? s_enableMultiMonitorDisplayClipping;

    /// <summary>
    /// Gets or sets whether the Alt key is required in the default access-key scope.
    /// </summary>
    public static bool IsAltKeyRequiredInAccessKeyDefaultScope
    {
        get
        {
            lock (s_syncRoot)
            {
                return s_isAltKeyRequiredInAccessKeyDefaultScope;
            }
        }
        set => SetBooleanPreference(
            ref s_isAltKeyRequiredInAccessKeyDefaultScope,
            value,
            nameof(IsAltKeyRequiredInAccessKeyDefaultScope));
    }

    /// <summary>
    /// Gets or sets whether rendering is clipped independently for each monitor.
    /// Reading this property consumes and seals all core compatibility preferences.
    /// </summary>
    public static bool? EnableMultiMonitorDisplayClipping
    {
        get => GetEnableMultiMonitorDisplayClipping();
        set
        {
            lock (s_syncRoot)
            {
                ThrowIfSealed(nameof(EnableMultiMonitorDisplayClipping));
                s_enableMultiMonitorDisplayClipping = value;
            }
        }
    }

    internal static bool TargetsAtLeast_Desktop_V4_5 => true;

    internal static bool GetIsAltKeyRequiredInAccessKeyDefaultScope()
    {
        lock (s_syncRoot)
        {
            s_isSealed = true;
            return s_isAltKeyRequiredInAccessKeyDefaultScope;
        }
    }

    internal static bool IncludeAllInkInBoundingBox
    {
        get
        {
            lock (s_syncRoot)
            {
                return s_includeAllInkInBoundingBox;
            }
        }
        set => SetBooleanPreference(
            ref s_includeAllInkInBoundingBox,
            value,
            nameof(IncludeAllInkInBoundingBox));
    }

    internal static bool GetIncludeAllInkInBoundingBox()
    {
        lock (s_syncRoot)
        {
            s_isSealed = true;
            return s_includeAllInkInBoundingBox;
        }
    }

    internal static bool? GetEnableMultiMonitorDisplayClipping()
    {
        lock (s_syncRoot)
        {
            s_isSealed = true;
            return s_enableMultiMonitorDisplayClipping;
        }
    }

    internal static void ResetForTests()
    {
        lock (s_syncRoot)
        {
            s_isSealed = false;
            s_isAltKeyRequiredInAccessKeyDefaultScope = false;
            s_includeAllInkInBoundingBox = true;
            s_enableMultiMonitorDisplayClipping = null;
        }
    }

    private static void SetBooleanPreference(ref bool preference, bool value, string propertyName)
    {
        lock (s_syncRoot)
        {
            ThrowIfSealed(propertyName);
            preference = value;
        }
    }

    private static void ThrowIfSealed(string propertyName)
    {
        if (s_isSealed)
        {
            throw new InvalidOperationException(
                $"Cannot set '{propertyName}' after {nameof(CoreCompatibilityPreferences)} has been sealed.");
        }
    }
}
