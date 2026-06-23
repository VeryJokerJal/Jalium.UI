namespace Jalium.UI;

/// <summary>
/// Result codes returned by Jalium native rendering APIs.
/// </summary>
public enum JaliumResult
{
    Ok = 0,
    InvalidArgument = 1,
    OutOfMemory = 2,
    NotSupported = 3,
    DeviceLost = 4,
    BackendNotAvailable = 5,
    InitializationFailed = 6,
    ResourceCreationFailed = 7,
    InvalidState = 8,

    /// <summary>
    /// Present submission failed transiently (e.g. DXGI_ERROR_INVALID_CALL
    /// during a mode change) on a healthy device: the frame's GPU work was
    /// submitted but never reached the screen. Surfaced (instead of swallowed
    /// as Ok) only under external present pacing, where the consumed present
    /// credit must be returned — a failed Present never signals the
    /// frame-latency waitable. Handled by repainting; deliberately NOT
    /// classified recoverable so it never triggers a render-target rebuild.
    /// </summary>
    PresentFailed = 9,

    /// <summary>
    /// A resize (or similar swap-chain operation) was refused by the native
    /// backend because a command list is still open and referencing the
    /// resources it would free (a cross-thread render in flight, or a frame
    /// left open). This is NOT a failure: the caller re-stashes the request and
    /// retries at the next safe point. It never throws and never triggers a
    /// render-target rebuild. Guards against the #921
    /// OBJECT_DELETED_WHILE_STILL_IN_USE use-after-free during window resize.
    /// </summary>
    Busy = 10,

    Unknown = 99
}

/// <summary>
/// Converts native result codes to <see cref="JaliumResult"/>.
/// </summary>
public static class JaliumResultMapper
{
    /// <summary>
    /// Maps a native integer result code to <see cref="JaliumResult"/>.
    /// Unknown values are mapped to <see cref="JaliumResult.Unknown"/>.
    /// </summary>
    public static JaliumResult FromCode(int resultCode)
    {
        return resultCode switch
        {
            0 => JaliumResult.Ok,
            1 => JaliumResult.InvalidArgument,
            2 => JaliumResult.OutOfMemory,
            3 => JaliumResult.NotSupported,
            4 => JaliumResult.DeviceLost,
            5 => JaliumResult.BackendNotAvailable,
            6 => JaliumResult.InitializationFailed,
            7 => JaliumResult.ResourceCreationFailed,
            8 => JaliumResult.InvalidState,
            9 => JaliumResult.PresentFailed,
            10 => JaliumResult.Busy,
            99 => JaliumResult.Unknown,
            _ => JaliumResult.Unknown
        };
    }
}
