namespace Jalium.UI;

/// <summary>
/// Controls compatibility switches used by presentation-framework features.
/// Values become immutable after the framework first consumes any switch.
/// </summary>
public static class FrameworkCompatibilityPreferences
{
    private static readonly object s_syncRoot = new();
    private static bool s_isSealed;
    private static bool s_areInactiveSelectionHighlightBrushKeysSupported = true;
    private static bool s_keepTextBoxDisplaySynchronizedWithTextProperty = true;
    private static bool s_shouldThrowOnCopyOrCutFailure;

    public static bool AreInactiveSelectionHighlightBrushKeysSupported
    {
        get => Read(ref s_areInactiveSelectionHighlightBrushKeysSupported);
        set => Write(
            ref s_areInactiveSelectionHighlightBrushKeysSupported,
            value,
            nameof(AreInactiveSelectionHighlightBrushKeysSupported));
    }

    public static bool KeepTextBoxDisplaySynchronizedWithTextProperty
    {
        get => Read(ref s_keepTextBoxDisplaySynchronizedWithTextProperty);
        set => Write(
            ref s_keepTextBoxDisplaySynchronizedWithTextProperty,
            value,
            nameof(KeepTextBoxDisplaySynchronizedWithTextProperty));
    }

    public static bool ShouldThrowOnCopyOrCutFailure
    {
        get => Read(ref s_shouldThrowOnCopyOrCutFailure);
        set => Write(
            ref s_shouldThrowOnCopyOrCutFailure,
            value,
            nameof(ShouldThrowOnCopyOrCutFailure));
    }

    internal static bool GetAreInactiveSelectionHighlightBrushKeysSupported()
        => SealAndRead(ref s_areInactiveSelectionHighlightBrushKeysSupported);

    internal static bool GetKeepTextBoxDisplaySynchronizedWithTextProperty()
        => SealAndRead(ref s_keepTextBoxDisplaySynchronizedWithTextProperty);

    internal static bool GetShouldThrowOnCopyOrCutFailure()
        => SealAndRead(ref s_shouldThrowOnCopyOrCutFailure);

    internal static void ResetForTests()
    {
        lock (s_syncRoot)
        {
            s_isSealed = false;
            s_areInactiveSelectionHighlightBrushKeysSupported = true;
            s_keepTextBoxDisplaySynchronizedWithTextProperty = true;
            s_shouldThrowOnCopyOrCutFailure = false;
        }
    }

    private static bool Read(ref bool value)
    {
        lock (s_syncRoot)
        {
            return value;
        }
    }

    private static bool SealAndRead(ref bool value)
    {
        lock (s_syncRoot)
        {
            s_isSealed = true;
            return value;
        }
    }

    private static void Write(ref bool target, bool value, string propertyName)
    {
        lock (s_syncRoot)
        {
            if (s_isSealed)
            {
                throw new InvalidOperationException(
                    $"Cannot set '{propertyName}' after {nameof(FrameworkCompatibilityPreferences)} has been sealed.");
            }

            target = value;
        }
    }
}
