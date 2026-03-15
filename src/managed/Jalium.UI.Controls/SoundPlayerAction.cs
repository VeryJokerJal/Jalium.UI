using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a lightweight audio playback TriggerAction used to play .wav files.
/// </summary>
public sealed class SoundPlayerAction : TriggerAction
{
    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    /// <summary>
    /// Gets or sets the audio source URI.
    /// </summary>
    public Uri? Source { get; set; }

    /// <inheritdoc />
    protected override void Invoke(FrameworkElement? element)
    {
        if (Source == null)
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
}
