namespace Jalium.UI.Input;

/// <summary>
/// Provides a standard set of media related commands.
/// </summary>
public static class MediaCommands
{
    private static RoutedUICommand? _play;
    private static RoutedUICommand? _pause;
    private static RoutedUICommand? _stop;
    private static RoutedUICommand? _record;
    private static RoutedUICommand? _nextTrack;
    private static RoutedUICommand? _previousTrack;
    private static RoutedUICommand? _fastForward;
    private static RoutedUICommand? _rewind;
    private static RoutedUICommand? _channelUp;
    private static RoutedUICommand? _channelDown;
    private static RoutedUICommand? _togglePlayPause;
    private static RoutedUICommand? _increaseVolume;
    private static RoutedUICommand? _decreaseVolume;
    private static RoutedUICommand? _muteVolume;
    private static RoutedUICommand? _increaseTreble;
    private static RoutedUICommand? _decreaseTreble;
    private static RoutedUICommand? _increaseBass;
    private static RoutedUICommand? _decreaseBass;
    private static RoutedUICommand? _boostBass;
    private static RoutedUICommand? _increaseMicrophoneVolume;
    private static RoutedUICommand? _decreaseMicrophoneVolume;
    private static RoutedUICommand? _muteMicrophoneVolume;
    private static RoutedUICommand? _toggleMicrophoneOnOff;
    private static RoutedUICommand? _select;

    /// <summary>Gets the Play command.</summary>
    public static RoutedUICommand Play => _play ??= new RoutedUICommand(
        "Play", "Play", typeof(MediaCommands));

    /// <summary>Gets the Pause command.</summary>
    public static RoutedUICommand Pause => _pause ??= new RoutedUICommand(
        "Pause", "Pause", typeof(MediaCommands));

    /// <summary>Gets the Stop command.</summary>
    public static RoutedUICommand Stop => _stop ??= new RoutedUICommand(
        "Stop", "Stop", typeof(MediaCommands));

    /// <summary>Gets the Record command.</summary>
    public static RoutedUICommand Record => _record ??= new RoutedUICommand(
        "Record", "Record", typeof(MediaCommands));

    /// <summary>Gets the NextTrack command.</summary>
    public static RoutedUICommand NextTrack => _nextTrack ??= new RoutedUICommand(
        "Next Track", "NextTrack", typeof(MediaCommands));

    /// <summary>Gets the PreviousTrack command.</summary>
    public static RoutedUICommand PreviousTrack => _previousTrack ??= new RoutedUICommand(
        "Previous Track", "PreviousTrack", typeof(MediaCommands));

    /// <summary>Gets the FastForward command.</summary>
    public static RoutedUICommand FastForward => _fastForward ??= new RoutedUICommand(
        "Fast Forward", "FastForward", typeof(MediaCommands));

    /// <summary>Gets the Rewind command.</summary>
    public static RoutedUICommand Rewind => _rewind ??= new RoutedUICommand(
        "Rewind", "Rewind", typeof(MediaCommands));

    /// <summary>Gets the ChannelUp command.</summary>
    public static RoutedUICommand ChannelUp => _channelUp ??= new RoutedUICommand(
        "Channel Up", "ChannelUp", typeof(MediaCommands));

    /// <summary>Gets the ChannelDown command.</summary>
    public static RoutedUICommand ChannelDown => _channelDown ??= new RoutedUICommand(
        "Channel Down", "ChannelDown", typeof(MediaCommands));

    /// <summary>Gets the TogglePlayPause command.</summary>
    public static RoutedUICommand TogglePlayPause => _togglePlayPause ??= new RoutedUICommand(
        "Toggle Play Pause", "TogglePlayPause", typeof(MediaCommands));

    /// <summary>Gets the IncreaseVolume command.</summary>
    public static RoutedUICommand IncreaseVolume => _increaseVolume ??= new RoutedUICommand(
        "Increase Volume", "IncreaseVolume", typeof(MediaCommands));

    /// <summary>Gets the DecreaseVolume command.</summary>
    public static RoutedUICommand DecreaseVolume => _decreaseVolume ??= new RoutedUICommand(
        "Decrease Volume", "DecreaseVolume", typeof(MediaCommands));

    /// <summary>Gets the MuteVolume command.</summary>
    public static RoutedUICommand MuteVolume => _muteVolume ??= new RoutedUICommand(
        "Mute Volume", "MuteVolume", typeof(MediaCommands));

    /// <summary>Gets the IncreaseTreble command.</summary>
    public static RoutedUICommand IncreaseTreble => _increaseTreble ??= new RoutedUICommand(
        "Increase Treble", "IncreaseTreble", typeof(MediaCommands));

    /// <summary>Gets the DecreaseTreble command.</summary>
    public static RoutedUICommand DecreaseTreble => _decreaseTreble ??= new RoutedUICommand(
        "Decrease Treble", "DecreaseTreble", typeof(MediaCommands));

    /// <summary>Gets the IncreaseBass command.</summary>
    public static RoutedUICommand IncreaseBass => _increaseBass ??= new RoutedUICommand(
        "Increase Bass", "IncreaseBass", typeof(MediaCommands));

    /// <summary>Gets the DecreaseBass command.</summary>
    public static RoutedUICommand DecreaseBass => _decreaseBass ??= new RoutedUICommand(
        "Decrease Bass", "DecreaseBass", typeof(MediaCommands));

    /// <summary>Gets the BoostBass command.</summary>
    public static RoutedUICommand BoostBass => _boostBass ??= new RoutedUICommand(
        "Boost Bass", "BoostBass", typeof(MediaCommands));

    /// <summary>Gets the IncreaseMicrophoneVolume command.</summary>
    public static RoutedUICommand IncreaseMicrophoneVolume => _increaseMicrophoneVolume ??= new RoutedUICommand(
        "Increase Microphone Volume", "IncreaseMicrophoneVolume", typeof(MediaCommands));

    /// <summary>Gets the DecreaseMicrophoneVolume command.</summary>
    public static RoutedUICommand DecreaseMicrophoneVolume => _decreaseMicrophoneVolume ??= new RoutedUICommand(
        "Decrease Microphone Volume", "DecreaseMicrophoneVolume", typeof(MediaCommands));

    /// <summary>Gets the MuteMicrophoneVolume command.</summary>
    public static RoutedUICommand MuteMicrophoneVolume => _muteMicrophoneVolume ??= new RoutedUICommand(
        "Mute Microphone Volume", "MuteMicrophoneVolume", typeof(MediaCommands));

    /// <summary>Gets the ToggleMicrophoneOnOff command.</summary>
    public static RoutedUICommand ToggleMicrophoneOnOff => _toggleMicrophoneOnOff ??= new RoutedUICommand(
        "Toggle Microphone On Off", "ToggleMicrophoneOnOff", typeof(MediaCommands));

    /// <summary>Gets the Select command.</summary>
    public static RoutedUICommand Select => _select ??= new RoutedUICommand(
        "Select", "Select", typeof(MediaCommands));
}
