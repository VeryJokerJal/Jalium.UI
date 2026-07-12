namespace Jalium.UI;

/// <summary>
/// Indicates whether a system is connected to AC power.
/// </summary>
public enum PowerLineStatus
{
    /// <summary>The system is offline.</summary>
    Offline = 0x00,

    /// <summary>The system is online.</summary>
    Online = 0x01,

    /// <summary>The system power-line status is unknown.</summary>
    Unknown = 0xFF,
}
