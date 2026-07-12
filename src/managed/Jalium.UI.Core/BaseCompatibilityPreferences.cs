namespace Jalium.UI;

/// <summary>
/// Controls compatibility behavior shared by Jalium's dispatcher foundation.
/// Preferences become immutable when they are first consumed internally.
/// </summary>
public static class BaseCompatibilityPreferences
{
    private static readonly object s_syncRoot = new();

    private static bool s_isSealed;
    private static bool s_reuseDispatcherSynchronizationContextInstance;
    private static bool s_flowDispatcherSynchronizationContextPriority = true;
    private static bool s_inlineDispatcherSynchronizationContextSend = true;
    private static bool s_matchPackageSignatureMethodToPackagePartDigestMethod = true;
    private static HandleDispatcherRequestProcessingFailureOptions s_handleDispatcherRequestProcessingFailure;

    /// <summary>
    /// Gets or sets whether dispatcher synchronization-context instances are reused.
    /// The modern default is <see langword="false"/>.
    /// </summary>
    public static bool ReuseDispatcherSynchronizationContextInstance
    {
        get => ReadPreference(ref s_reuseDispatcherSynchronizationContextInstance);
        set => SetPreference(ref s_reuseDispatcherSynchronizationContextInstance, value, nameof(ReuseDispatcherSynchronizationContextInstance));
    }

    /// <summary>
    /// Gets or sets whether dispatcher operation priority flows through the
    /// synchronization context. The modern default is <see langword="true"/>.
    /// </summary>
    public static bool FlowDispatcherSynchronizationContextPriority
    {
        get => ReadPreference(ref s_flowDispatcherSynchronizationContextPriority);
        set => SetPreference(ref s_flowDispatcherSynchronizationContextPriority, value, nameof(FlowDispatcherSynchronizationContextPriority));
    }

    /// <summary>
    /// Gets or sets whether same-thread synchronization-context Send calls are
    /// invoked inline. The modern default is <see langword="true"/>.
    /// </summary>
    public static bool InlineDispatcherSynchronizationContextSend
    {
        get => ReadPreference(ref s_inlineDispatcherSynchronizationContextSend);
        set => SetPreference(ref s_inlineDispatcherSynchronizationContextSend, value, nameof(InlineDispatcherSynchronizationContextSend));
    }

    /// <summary>
    /// Gets or sets how the dispatcher reacts when it cannot request processing.
    /// This diagnostic preference intentionally remains mutable after sealing.
    /// </summary>
    public static HandleDispatcherRequestProcessingFailureOptions HandleDispatcherRequestProcessingFailure
    {
        get
        {
            lock (s_syncRoot)
            {
                return s_handleDispatcherRequestProcessingFailure;
            }
        }
        set
        {
            lock (s_syncRoot)
            {
                s_handleDispatcherRequestProcessingFailure = value;
            }
        }
    }

    /// <summary>
    /// Describes how dispatcher request-processing failures are handled.
    /// </summary>
    public enum HandleDispatcherRequestProcessingFailureOptions
    {
        Continue = 0,
        Throw = 1,
        Reset = 2,
    }

    internal static bool GetReuseDispatcherSynchronizationContextInstance()
    {
        return SealAndReadPreference(ref s_reuseDispatcherSynchronizationContextInstance);
    }

    internal static bool GetFlowDispatcherSynchronizationContextPriority()
    {
        return SealAndReadPreference(ref s_flowDispatcherSynchronizationContextPriority);
    }

    internal static bool GetInlineDispatcherSynchronizationContextSend()
    {
        return SealAndReadPreference(ref s_inlineDispatcherSynchronizationContextSend);
    }

    internal static bool MatchPackageSignatureMethodToPackagePartDigestMethod
    {
        get => ReadPreference(ref s_matchPackageSignatureMethodToPackagePartDigestMethod);
        set
        {
            lock (s_syncRoot)
            {
                s_matchPackageSignatureMethodToPackagePartDigestMethod = value;
            }
        }
    }

    internal static void ResetForTests()
    {
        lock (s_syncRoot)
        {
            s_isSealed = false;
            s_reuseDispatcherSynchronizationContextInstance = false;
            s_flowDispatcherSynchronizationContextPriority = true;
            s_inlineDispatcherSynchronizationContextSend = true;
            s_matchPackageSignatureMethodToPackagePartDigestMethod = true;
            s_handleDispatcherRequestProcessingFailure = HandleDispatcherRequestProcessingFailureOptions.Continue;
        }
    }

    private static bool ReadPreference(ref bool preference)
    {
        lock (s_syncRoot)
        {
            return preference;
        }
    }

    private static bool SealAndReadPreference(ref bool preference)
    {
        lock (s_syncRoot)
        {
            s_isSealed = true;
            return preference;
        }
    }

    private static void SetPreference(ref bool preference, bool value, string propertyName)
    {
        lock (s_syncRoot)
        {
            if (s_isSealed)
            {
                throw new InvalidOperationException(
                    $"Cannot set '{propertyName}' after {nameof(BaseCompatibilityPreferences)} has been sealed.");
            }

            preference = value;
        }
    }
}
