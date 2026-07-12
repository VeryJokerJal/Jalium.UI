using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a lightweight audio playback TriggerAction used to play .wav files.
/// </summary>
public sealed class SoundPlayerAction : TriggerAction, IDisposable
{
    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    private bool _disposed;

    /// <summary>Identifies the Source dependency property.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(Uri),
            typeof(SoundPlayerAction),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the audio source URI.
    /// </summary>
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <inheritdoc />
    internal override void Invoke(FrameworkElement? element)
    {
        if (_disposed || Source == null)
            return;

        try
        {
            var path = Source.IsAbsoluteUri ? Source.LocalPath : Source.OriginalString;
            PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
        }
        catch
        {
            // Silently ignore audio playback errors
        }
    }

    /// <summary>Stops playback and releases this action's playback state.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                PlaySound(null, IntPtr.Zero, 0);
            }
            catch
            {
                // Playback cleanup is best effort.
            }
        }

        GC.SuppressFinalize(this);
    }
}
