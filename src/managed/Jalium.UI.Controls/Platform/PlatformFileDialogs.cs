namespace Jalium.UI.Controls.Platform;

/// <summary>Options passed to an OS-specific native file panel.</summary>
public sealed record PlatformFileDialogOptions(
    bool Save, bool Directory, bool Multiple,
    string? Title, string? InitialDirectory, string? FileName,
    string? DefaultExtension, bool AddExtension, bool DereferenceLinks,
    bool CreateDirectories, (string Name, string Pattern)[] Filters, int FilterIndex)
{
    /// <summary>The native owner handle, or zero for an application-modal panel.</summary>
    public nint Owner { get; init; }

    /// <summary>
    /// Validates all selected paths before closing the panel. False keeps it open.
    /// The filter index is one-based. Providers invoke this once per confirmation.
    /// </summary>
    public Func<string[], int, bool>? ValidateSelection { get; init; }

    /// <summary>The accepted native panel selection, using a one-based filter index.</summary>
    public int SelectedFilterIndex { get; set; } = FilterIndex;
}

/// <summary>Entry point supplied by platform packages that reference their OS UI SDK.</summary>
public static class PlatformFileDialogs
{
    /// <summary>Returns selected filesystem paths, or null when the panel is cancelled.</summary>
    public static Func<PlatformFileDialogOptions, string[]?>? Show { get; set; }
}
