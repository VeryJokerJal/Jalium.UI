namespace Jalium.UI;

/// <summary>
/// Specifies where an assembly's theme resource dictionaries are stored.
/// </summary>
public enum ResourceDictionaryLocation
{
    /// <summary>No resource dictionary exists.</summary>
    None = 0,

    /// <summary>The resource dictionary is in the assembly that defines the themed types.</summary>
    SourceAssembly = 1,

    /// <summary>The resource dictionary is in an external theme assembly.</summary>
    ExternalAssembly = 2,
}
