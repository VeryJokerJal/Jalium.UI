using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI;

/// <summary>
/// Describes the Fluent theme mode to apply to an application or window.
/// </summary>
[Experimental("WPF0001")]
public readonly struct ThemeMode : IEquatable<ThemeMode>
{
    private readonly string? _value;

    /// <summary>
    /// Initializes a new theme mode with the specified value.
    /// </summary>
    /// <param name="value">The theme mode name.</param>
    public ThemeMode(string value)
    {
        _value = value;
    }

    /// <summary>Gets the mode that does not request a Fluent theme.</summary>
    public static ThemeMode None => default;

    /// <summary>Gets the light theme mode.</summary>
    public static ThemeMode Light => new("Light");

    /// <summary>Gets the dark theme mode.</summary>
    public static ThemeMode Dark => new("Dark");

    /// <summary>Gets the mode that follows the system theme.</summary>
    public static ThemeMode System => new("System");

    /// <summary>
    /// Gets the theme mode name. The default value is <c>None</c>.
    /// </summary>
    public string Value => _value ?? "None";

    /// <inheritdoc />
    public bool Equals(ThemeMode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ThemeMode other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value is not null ? StringComparer.Ordinal.GetHashCode(_value) : 0;

    /// <summary>Determines whether two theme modes are equal.</summary>
    public static bool operator ==(ThemeMode left, ThemeMode right) => left.Equals(right);

    /// <summary>Determines whether two theme modes are different.</summary>
    public static bool operator !=(ThemeMode left, ThemeMode right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Value;
}
