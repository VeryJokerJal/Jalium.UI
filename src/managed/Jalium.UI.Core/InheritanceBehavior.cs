namespace Jalium.UI;

/// <summary>
/// Specifies boundaries used by inherited-property and resource lookup.
/// </summary>
public enum InheritanceBehavior
{
    /// <summary>Continue normal lookup through the current element and its ancestors.</summary>
    Default = 0,

    /// <summary>Skip the current element and its ancestors, then continue at application resources.</summary>
    SkipToAppNow = 1,

    /// <summary>Query the current element, then continue at application resources.</summary>
    SkipToAppNext = 2,

    /// <summary>Skip the current element and its ancestors, then continue at theme resources.</summary>
    SkipToThemeNow = 3,

    /// <summary>Query the current element, then continue at theme resources.</summary>
    SkipToThemeNext = 4,

    /// <summary>Skip the current element and stop all further lookup.</summary>
    SkipAllNow = 5,

    /// <summary>Query the current element, then stop all further lookup.</summary>
    SkipAllNext = 6,
}
