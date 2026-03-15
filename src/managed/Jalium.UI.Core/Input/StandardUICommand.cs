namespace Jalium.UI.Input;

/// <summary>
/// Specifies the standard commands that can be used with StandardUICommand.
/// </summary>
public enum StandardUICommandKind
{
    None,
    Cut,
    Copy,
    Paste,
    SelectAll,
    Delete,
    Share,
    Save,
    Open,
    Close,
    Undo,
    Redo,
    Forward,
    Backward
}

/// <summary>
/// Derives from XamlUICommand, adding a set of standard platform commands
/// with pre-defined properties (icon, label, keyboard accelerator, description).
/// </summary>
public sealed class StandardUICommand : XamlUICommand
{
    // Modifier key constants (matching Jalium.UI.Input.ModifierKeys values)
    private const int ModNone = 0;
    private const int ModCtrl = 2;
    private const int ModAlt = 1;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Kind dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StandardUICommandKind), typeof(StandardUICommand),
            new PropertyMetadata(StandardUICommandKind.None, OnKindChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the platform command (with pre-defined properties) that this StandardUICommand represents.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public StandardUICommandKind Kind
    {
        get => (StandardUICommandKind)(GetValue(KindProperty) ?? StandardUICommandKind.None);
        set => SetValue(KindProperty, value);
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the StandardUICommand class.
    /// </summary>
    public StandardUICommand()
    {
    }

    /// <summary>
    /// Initializes a new instance of the StandardUICommand class with the specified kind.
    /// </summary>
    public StandardUICommand(StandardUICommandKind kind)
    {
        Kind = kind;
    }

    #endregion

    #region Private Methods

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StandardUICommand command)
        {
            command.UpdateFromKind((StandardUICommandKind)(e.NewValue ?? StandardUICommandKind.None));
        }
    }

    private void UpdateFromKind(StandardUICommandKind kind)
    {
        switch (kind)
        {
            case StandardUICommandKind.Cut:
                Label = "Cut";
                IconSource = "\uE8C6";
                Description = "Cut the selection and put it on the Clipboard";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(88, ModCtrl)); // X, Ctrl
                break;

            case StandardUICommandKind.Copy:
                Label = "Copy";
                IconSource = "\uE8C8";
                Description = "Copy the selection and put it on the Clipboard";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(67, ModCtrl)); // C, Ctrl
                break;

            case StandardUICommandKind.Paste:
                Label = "Paste";
                IconSource = "\uE77F";
                Description = "Insert the contents of the Clipboard";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(86, ModCtrl)); // V, Ctrl
                break;

            case StandardUICommandKind.SelectAll:
                Label = "Select All";
                IconSource = "\uE8B3";
                Description = "Select all content";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(65, ModCtrl)); // A, Ctrl
                break;

            case StandardUICommandKind.Delete:
                Label = "Delete";
                IconSource = "\uE74D";
                Description = "Delete the selected item";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(46, ModNone)); // Delete
                break;

            case StandardUICommandKind.Share:
                Label = "Share";
                IconSource = "\uE72D";
                Description = "Share content with others";
                break;

            case StandardUICommandKind.Save:
                Label = "Save";
                IconSource = "\uE74E";
                Description = "Save the current document";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(83, ModCtrl)); // S, Ctrl
                break;

            case StandardUICommandKind.Open:
                Label = "Open";
                IconSource = "\uE8E5";
                Description = "Open a file";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(79, ModCtrl)); // O, Ctrl
                break;

            case StandardUICommandKind.Close:
                Label = "Close";
                IconSource = "\uE8BB";
                Description = "Close the current document";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(87, ModCtrl)); // W, Ctrl
                break;

            case StandardUICommandKind.Undo:
                Label = "Undo";
                IconSource = "\uE7A7";
                Description = "Undo the last action";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(90, ModCtrl)); // Z, Ctrl
                break;

            case StandardUICommandKind.Redo:
                Label = "Redo";
                IconSource = "\uE7A6";
                Description = "Redo the last undone action";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(89, ModCtrl)); // Y, Ctrl
                break;

            case StandardUICommandKind.Forward:
                Label = "Forward";
                IconSource = "\uE72A";
                Description = "Navigate forward";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(39, ModAlt)); // Right, Alt
                break;

            case StandardUICommandKind.Backward:
                Label = "Back";
                IconSource = "\uE72B";
                Description = "Navigate backward";
                KeyboardAccelerators.Clear();
                KeyboardAccelerators.Add(new KeyGesture(37, ModAlt)); // Left, Alt
                break;

            case StandardUICommandKind.None:
            default:
                break;
        }
    }

    #endregion
}
