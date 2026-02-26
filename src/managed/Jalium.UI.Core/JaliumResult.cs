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
            99 => JaliumResult.Unknown,
            _ => JaliumResult.Unknown
        };
    }
}
