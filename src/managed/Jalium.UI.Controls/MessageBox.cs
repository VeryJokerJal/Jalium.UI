using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the buttons that are displayed on a message box.
/// </summary>
public enum MessageBoxButton
{
    OK = 0x00000000,
    OKCancel = 0x00000001,
    AbortRetryIgnore = 0x00000002,
    YesNoCancel = 0x00000003,
    YesNo = 0x00000004,
    RetryCancel = 0x00000005,
    CancelTryContinue = 0x00000006
}

/// <summary>
/// Specifies which message box button a user clicks.
/// </summary>
public enum MessageBoxResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7,
    TryAgain = 10,
    Continue = 11
}

/// <summary>
/// Specifies the icon that is displayed by a message box.
/// </summary>
public enum MessageBoxImage
{
    None = 0,
    Hand = 0x00000010,
    Question = 0x00000020,
    Exclamation = 0x00000030,
    Asterisk = 0x00000040,
    Stop = Hand,
    Error = Hand,
    Warning = Exclamation,
    Information = Asterisk
}

/// <summary>
/// Specifies special display options for a message box.
/// </summary>
[Flags]
public enum MessageBoxOptions
{
    None = 0x00000000,
    ServiceNotification = 0x00200000,
    DefaultDesktopOnly = 0x00020000,
    RightAlign = 0x00080000,
    RtlReading = 0x00100000
}

/// <summary>
/// Displays a message box.
/// </summary>
public sealed class MessageBox
{
    private MessageBox() { }

    /// <summary>
    /// Displays a message box with the specified text.
    /// </summary>
    public static MessageBoxResult Show(string messageBoxText)
    {
        return Show(messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box with the specified text and caption.
    /// </summary>
    public static MessageBoxResult Show(string messageBoxText, string caption)
    {
        return Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box with the specified text, caption, and buttons.
    /// </summary>
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
    {
        return Show(messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box with the specified text, caption, buttons, and icon.
    /// </summary>
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return Show(messageBoxText, caption, button, icon, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box with the specified text, caption, buttons, icon, and default result.
    /// </summary>
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        return Show(messageBoxText, caption, button, icon, defaultResult, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box with all options specified.
    /// </summary>
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
    {
        var hwnd = ShouldForceOwnerless(options) ? IntPtr.Zero : DialogOwnerResolver.Resolve();
        return ShowCore(hwnd, messageBoxText, caption, button, icon, defaultResult, options);
    }

    /// <summary>
    /// Displays a message box in front of the specified window.
    /// </summary>
    public static MessageBoxResult Show(Window owner, string messageBoxText)
    {
        return Show(owner, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box in front of the specified window with the specified text and caption.
    /// </summary>
    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
    {
        return Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box in front of the specified window with the specified text, caption, and buttons.
    /// </summary>
    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
    {
        return Show(owner, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box in front of the specified window with the specified text, caption, buttons, and icon.
    /// </summary>
    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return Show(owner, messageBoxText, caption, button, icon, MessageBoxResult.None, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box in front of the specified window with the specified text, caption, buttons, icon, and default result.
    /// </summary>
    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        return Show(owner, messageBoxText, caption, button, icon, defaultResult, MessageBoxOptions.None);
    }

    /// <summary>
    /// Displays a message box in front of the specified window with all options specified.
    /// </summary>
    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
    {
        var hwnd = ShouldForceOwnerless(options)
            ? IntPtr.Zero
            : DialogOwnerResolver.Resolve(owner?.Handle ?? IntPtr.Zero);
        return ShowCore(hwnd, messageBoxText, caption, button, icon, defaultResult, options);
    }

    private static bool ShouldForceOwnerless(MessageBoxOptions options)
    {
        return (options & (MessageBoxOptions.ServiceNotification | MessageBoxOptions.DefaultDesktopOnly)) != 0;
    }

    private static MessageBoxResult ShowCore(IntPtr owner, string messageBoxText, string caption,
        MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
    {
        uint flags = (uint)button | (uint)icon | (uint)options;

        // Set default button based on defaultResult
        flags |= defaultResult switch
        {
            MessageBoxResult.Cancel when button == MessageBoxButton.OKCancel => 0x00000100u, // MB_DEFBUTTON2
            MessageBoxResult.No when button == MessageBoxButton.YesNo => 0x00000100u,
            MessageBoxResult.No when button == MessageBoxButton.YesNoCancel => 0x00000200u, // MB_DEFBUTTON3
            MessageBoxResult.Cancel when button == MessageBoxButton.YesNoCancel => 0x00000200u,
            MessageBoxResult.Retry when button == MessageBoxButton.AbortRetryIgnore => 0x00000100u,
            MessageBoxResult.Ignore when button == MessageBoxButton.AbortRetryIgnore => 0x00000200u,
            _ => 0u
        };

        int result = MessageBoxW(owner, messageBoxText, caption ?? string.Empty, flags);

        return result switch
        {
            1 => MessageBoxResult.OK,
            2 => MessageBoxResult.Cancel,
            3 => MessageBoxResult.Abort,
            4 => MessageBoxResult.Retry,
            5 => MessageBoxResult.Ignore,
            6 => MessageBoxResult.Yes,
            7 => MessageBoxResult.No,
            10 => MessageBoxResult.TryAgain,
            11 => MessageBoxResult.Continue,
            _ => MessageBoxResult.None
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
