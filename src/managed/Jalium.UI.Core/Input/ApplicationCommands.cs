namespace Jalium.UI.Input;

// Key code constants for reference
// A-Z = 65-90, 0-9 = 48-57, F1-F12 = 112-123
// Delete = 46, Insert = 45, Home = 36, End = 35, PageUp = 33, PageDown = 34
// Left/Up/Right/Down = 37/38/39/40, Tab = 9, Enter = 13, Escape = 27, Space = 32

// ModifierKeys flags: None=0, Alt=1, Control=2, Shift=4, Windows=8

/// <summary>
/// Provides a standard set of application related commands.
/// </summary>
public static class ApplicationCommands
{
    private static RoutedUICommand? _cut;
    private static RoutedUICommand? _copy;
    private static RoutedUICommand? _paste;
    private static RoutedUICommand? _delete;
    private static RoutedUICommand? _undo;
    private static RoutedUICommand? _redo;
    private static RoutedUICommand? _selectAll;
    private static RoutedUICommand? _new;
    private static RoutedUICommand? _open;
    private static RoutedUICommand? _save;
    private static RoutedUICommand? _saveAs;
    private static RoutedUICommand? _close;
    private static RoutedUICommand? _print;
    private static RoutedUICommand? _cancelPrint;
    private static RoutedUICommand? _printPreview;
    private static RoutedUICommand? _find;
    private static RoutedUICommand? _replace;
    private static RoutedUICommand? _help;
    private static RoutedUICommand? _properties;
    private static RoutedUICommand? _contextMenu;
    private static RoutedUICommand? _correctionList;
    private static RoutedUICommand? _stop;
    private static RoutedUICommand? _notACommand;

    /// <summary>Gets the Cut command.</summary>
    public static RoutedUICommand Cut => _cut ??= new RoutedUICommand(
        "Cut", "Cut", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(88, 2) }); // X, Ctrl

    /// <summary>Gets the Copy command.</summary>
    public static RoutedUICommand Copy => _copy ??= new RoutedUICommand(
        "Copy", "Copy", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(67, 2) }); // C, Ctrl

    /// <summary>Gets the Paste command.</summary>
    public static RoutedUICommand Paste => _paste ??= new RoutedUICommand(
        "Paste", "Paste", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(86, 2) }); // V, Ctrl

    /// <summary>Gets the Delete command.</summary>
    public static RoutedUICommand Delete => _delete ??= new RoutedUICommand(
        "Delete", "Delete", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(46, 0) }); // Delete, None

    /// <summary>Gets the Undo command.</summary>
    public static RoutedUICommand Undo => _undo ??= new RoutedUICommand(
        "Undo", "Undo", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(90, 2) }); // Z, Ctrl

    /// <summary>Gets the Redo command.</summary>
    public static RoutedUICommand Redo => _redo ??= new RoutedUICommand(
        "Redo", "Redo", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(89, 2), new KeyGesture(90, 6) }); // Y,Ctrl / Z,Ctrl+Shift

    /// <summary>Gets the SelectAll command.</summary>
    public static RoutedUICommand SelectAll => _selectAll ??= new RoutedUICommand(
        "Select All", "SelectAll", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(65, 2) }); // A, Ctrl

    /// <summary>Gets the New command.</summary>
    public static RoutedUICommand New => _new ??= new RoutedUICommand(
        "New", "New", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(78, 2) }); // N, Ctrl

    /// <summary>Gets the Open command.</summary>
    public static RoutedUICommand Open => _open ??= new RoutedUICommand(
        "Open", "Open", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(79, 2) }); // O, Ctrl

    /// <summary>Gets the Save command.</summary>
    public static RoutedUICommand Save => _save ??= new RoutedUICommand(
        "Save", "Save", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(83, 2) }); // S, Ctrl

    /// <summary>Gets the SaveAs command.</summary>
    public static RoutedUICommand SaveAs => _saveAs ??= new RoutedUICommand(
        "Save As", "SaveAs", typeof(ApplicationCommands));

    /// <summary>Gets the Close command.</summary>
    public static RoutedUICommand Close => _close ??= new RoutedUICommand(
        "Close", "Close", typeof(ApplicationCommands));

    /// <summary>Gets the Print command.</summary>
    public static RoutedUICommand Print => _print ??= new RoutedUICommand(
        "Print", "Print", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(80, 2) }); // P, Ctrl

    /// <summary>Gets the command that cancels an active print operation.</summary>
    public static RoutedUICommand CancelPrint => _cancelPrint ??= new RoutedUICommand(
        "Cancel Print", "CancelPrint", typeof(ApplicationCommands));

    /// <summary>Gets the PrintPreview command.</summary>
    public static RoutedUICommand PrintPreview => _printPreview ??= new RoutedUICommand(
        "Print Preview", "PrintPreview", typeof(ApplicationCommands));

    /// <summary>Gets the Find command.</summary>
    public static RoutedUICommand Find => _find ??= new RoutedUICommand(
        "Find", "Find", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(70, 2) }); // F, Ctrl

    /// <summary>Gets the Replace command.</summary>
    public static RoutedUICommand Replace => _replace ??= new RoutedUICommand(
        "Replace", "Replace", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(72, 2) }); // H, Ctrl

    /// <summary>Gets the Help command.</summary>
    public static RoutedUICommand Help => _help ??= new RoutedUICommand(
        "Help", "Help", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(112, 0) }); // F1, None

    /// <summary>Gets the Properties command.</summary>
    public static RoutedUICommand Properties => _properties ??= new RoutedUICommand(
        "Properties", "Properties", typeof(ApplicationCommands));

    /// <summary>Gets the ContextMenu command.</summary>
    public static RoutedUICommand ContextMenu => _contextMenu ??= new RoutedUICommand(
        "Context Menu", "ContextMenu", typeof(ApplicationCommands));

    /// <summary>Gets the CorrectionList command.</summary>
    public static RoutedUICommand CorrectionList => _correctionList ??= new RoutedUICommand(
        "Correction List", "CorrectionList", typeof(ApplicationCommands));

    /// <summary>Gets the Stop command.</summary>
    public static RoutedUICommand Stop => _stop ??= new RoutedUICommand(
        "Stop", "Stop", typeof(ApplicationCommands),
        new InputGestureCollection { new KeyGesture(27, 0) }); // Escape, None

    /// <summary>Gets the NotACommand command (placeholder).</summary>
    public static RoutedUICommand NotACommand => _notACommand ??= new RoutedUICommand(
        "", "NotACommand", typeof(ApplicationCommands));
}

/// <summary>
/// Provides a standard set of component-level commands.
/// </summary>
public static class ComponentCommands
{
    private static RoutedUICommand? _scrollPageUp;
    private static RoutedUICommand? _scrollPageDown;
    private static RoutedUICommand? _scrollPageLeft;
    private static RoutedUICommand? _scrollPageRight;
    private static RoutedUICommand? _moveUp;
    private static RoutedUICommand? _moveDown;
    private static RoutedUICommand? _moveLeft;
    private static RoutedUICommand? _moveRight;
    private static RoutedUICommand? _moveToHome;
    private static RoutedUICommand? _moveToEnd;
    private static RoutedUICommand? _moveToPageUp;
    private static RoutedUICommand? _moveToPageDown;
    private static RoutedUICommand? _selectToHome;
    private static RoutedUICommand? _selectToEnd;
    private static RoutedUICommand? _selectToPageUp;
    private static RoutedUICommand? _selectToPageDown;
    private static RoutedUICommand? _extendSelectionUp;
    private static RoutedUICommand? _extendSelectionDown;
    private static RoutedUICommand? _extendSelectionLeft;
    private static RoutedUICommand? _extendSelectionRight;
    private static RoutedUICommand? _moveFocusBack;
    private static RoutedUICommand? _moveFocusDown;
    private static RoutedUICommand? _moveFocusForward;
    private static RoutedUICommand? _moveFocusPageDown;
    private static RoutedUICommand? _moveFocusPageUp;
    private static RoutedUICommand? _moveFocusUp;
    private static RoutedUICommand? _scrollByLine;

    /// <summary>Gets the ScrollPageUp command.</summary>
    public static RoutedUICommand ScrollPageUp => _scrollPageUp ??= new RoutedUICommand(
        "Scroll Page Up", "ScrollPageUp", typeof(ComponentCommands));

    /// <summary>Gets the ScrollPageDown command.</summary>
    public static RoutedUICommand ScrollPageDown => _scrollPageDown ??= new RoutedUICommand(
        "Scroll Page Down", "ScrollPageDown", typeof(ComponentCommands));

    /// <summary>Gets the ScrollPageLeft command.</summary>
    public static RoutedUICommand ScrollPageLeft => _scrollPageLeft ??= new RoutedUICommand(
        "Scroll Page Left", "ScrollPageLeft", typeof(ComponentCommands));

    /// <summary>Gets the ScrollPageRight command.</summary>
    public static RoutedUICommand ScrollPageRight => _scrollPageRight ??= new RoutedUICommand(
        "Scroll Page Right", "ScrollPageRight", typeof(ComponentCommands));

    /// <summary>Gets the MoveUp command.</summary>
    public static RoutedUICommand MoveUp => _moveUp ??= new RoutedUICommand(
        "Move Up", "MoveUp", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(38, 0) }); // Up, None

    /// <summary>Gets the MoveDown command.</summary>
    public static RoutedUICommand MoveDown => _moveDown ??= new RoutedUICommand(
        "Move Down", "MoveDown", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(40, 0) }); // Down, None

    /// <summary>Gets the MoveLeft command.</summary>
    public static RoutedUICommand MoveLeft => _moveLeft ??= new RoutedUICommand(
        "Move Left", "MoveLeft", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(37, 0) }); // Left, None

    /// <summary>Gets the MoveRight command.</summary>
    public static RoutedUICommand MoveRight => _moveRight ??= new RoutedUICommand(
        "Move Right", "MoveRight", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(39, 0) }); // Right, None

    /// <summary>Gets the MoveToHome command.</summary>
    public static RoutedUICommand MoveToHome => _moveToHome ??= new RoutedUICommand(
        "Move To Home", "MoveToHome", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(36, 0) }); // Home, None

    /// <summary>Gets the MoveToEnd command.</summary>
    public static RoutedUICommand MoveToEnd => _moveToEnd ??= new RoutedUICommand(
        "Move To End", "MoveToEnd", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(35, 0) }); // End, None

    /// <summary>Gets the MoveToPageUp command.</summary>
    public static RoutedUICommand MoveToPageUp => _moveToPageUp ??= new RoutedUICommand(
        "Move To Page Up", "MoveToPageUp", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(33, 0) }); // PageUp, None

    /// <summary>Gets the MoveToPageDown command.</summary>
    public static RoutedUICommand MoveToPageDown => _moveToPageDown ??= new RoutedUICommand(
        "Move To Page Down", "MoveToPageDown", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(34, 0) }); // PageDown, None

    /// <summary>Gets the SelectToHome command.</summary>
    public static RoutedUICommand SelectToHome => _selectToHome ??= new RoutedUICommand(
        "Select To Home", "SelectToHome", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(36, 4) }); // Home, Shift

    /// <summary>Gets the SelectToEnd command.</summary>
    public static RoutedUICommand SelectToEnd => _selectToEnd ??= new RoutedUICommand(
        "Select To End", "SelectToEnd", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(35, 4) }); // End, Shift

    /// <summary>Gets the SelectToPageUp command.</summary>
    public static RoutedUICommand SelectToPageUp => _selectToPageUp ??= new RoutedUICommand(
        "Select To Page Up", "SelectToPageUp", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(33, 4) }); // PageUp, Shift

    /// <summary>Gets the SelectToPageDown command.</summary>
    public static RoutedUICommand SelectToPageDown => _selectToPageDown ??= new RoutedUICommand(
        "Select To Page Down", "SelectToPageDown", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(34, 4) }); // PageDown, Shift

    /// <summary>Gets the ExtendSelectionUp command.</summary>
    public static RoutedUICommand ExtendSelectionUp => _extendSelectionUp ??= new RoutedUICommand(
        "Extend Selection Up", "ExtendSelectionUp", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(38, 4) }); // Up, Shift

    /// <summary>Gets the ExtendSelectionDown command.</summary>
    public static RoutedUICommand ExtendSelectionDown => _extendSelectionDown ??= new RoutedUICommand(
        "Extend Selection Down", "ExtendSelectionDown", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(40, 4) }); // Down, Shift

    /// <summary>Gets the ExtendSelectionLeft command.</summary>
    public static RoutedUICommand ExtendSelectionLeft => _extendSelectionLeft ??= new RoutedUICommand(
        "Extend Selection Left", "ExtendSelectionLeft", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(37, 4) }); // Left, Shift

    /// <summary>Gets the ExtendSelectionRight command.</summary>
    public static RoutedUICommand ExtendSelectionRight => _extendSelectionRight ??= new RoutedUICommand(
        "Extend Selection Right", "ExtendSelectionRight", typeof(ComponentCommands),
        new InputGestureCollection { new KeyGesture(39, 4) }); // Right, Shift

    public static RoutedUICommand MoveFocusBack => _moveFocusBack ??= new RoutedUICommand(
        "Move Focus Back", "MoveFocusBack", typeof(ComponentCommands));

    public static RoutedUICommand MoveFocusDown => _moveFocusDown ??= new RoutedUICommand(
        "Move Focus Down", "MoveFocusDown", typeof(ComponentCommands));

    public static RoutedUICommand MoveFocusForward => _moveFocusForward ??= new RoutedUICommand(
        "Move Focus Forward", "MoveFocusForward", typeof(ComponentCommands));

    public static RoutedUICommand MoveFocusPageDown => _moveFocusPageDown ??= new RoutedUICommand(
        "Move Focus Page Down", "MoveFocusPageDown", typeof(ComponentCommands));

    public static RoutedUICommand MoveFocusPageUp => _moveFocusPageUp ??= new RoutedUICommand(
        "Move Focus Page Up", "MoveFocusPageUp", typeof(ComponentCommands));

    public static RoutedUICommand MoveFocusUp => _moveFocusUp ??= new RoutedUICommand(
        "Move Focus Up", "MoveFocusUp", typeof(ComponentCommands));

    public static RoutedUICommand ScrollByLine => _scrollByLine ??= new RoutedUICommand(
        "Scroll By Line", "ScrollByLine", typeof(ComponentCommands));
}

/// <summary>
/// Provides a standard set of navigation commands.
/// </summary>
public static class NavigationCommands
{
    private static RoutedUICommand? _browseBack;
    private static RoutedUICommand? _browseForward;
    private static RoutedUICommand? _browseHome;
    private static RoutedUICommand? _browseStop;
    private static RoutedUICommand? _refresh;
    private static RoutedUICommand? _favorites;
    private static RoutedUICommand? _search;
    private static RoutedUICommand? _increaseZoom;
    private static RoutedUICommand? _decreaseZoom;
    private static RoutedUICommand? _zoom;
    private static RoutedUICommand? _nextPage;
    private static RoutedUICommand? _previousPage;
    private static RoutedUICommand? _firstPage;
    private static RoutedUICommand? _lastPage;
    private static RoutedUICommand? _goToPage;
    private static RoutedUICommand? _navigateJournal;

    /// <summary>Gets the BrowseBack command.</summary>
    public static RoutedUICommand BrowseBack => _browseBack ??= new RoutedUICommand(
        "Browse Back", "BrowseBack", typeof(NavigationCommands));

    /// <summary>Gets the BrowseForward command.</summary>
    public static RoutedUICommand BrowseForward => _browseForward ??= new RoutedUICommand(
        "Browse Forward", "BrowseForward", typeof(NavigationCommands));

    /// <summary>Gets the BrowseHome command.</summary>
    public static RoutedUICommand BrowseHome => _browseHome ??= new RoutedUICommand(
        "Browse Home", "BrowseHome", typeof(NavigationCommands));

    /// <summary>Gets the BrowseStop command.</summary>
    public static RoutedUICommand BrowseStop => _browseStop ??= new RoutedUICommand(
        "Browse Stop", "BrowseStop", typeof(NavigationCommands));

    /// <summary>Gets the Refresh command.</summary>
    public static RoutedUICommand Refresh => _refresh ??= new RoutedUICommand(
        "Refresh", "Refresh", typeof(NavigationCommands),
        new InputGestureCollection { new KeyGesture(116, 0) }); // F5, None

    /// <summary>Gets the Favorites command.</summary>
    public static RoutedUICommand Favorites => _favorites ??= new RoutedUICommand(
        "Favorites", "Favorites", typeof(NavigationCommands));

    /// <summary>Gets the Search command.</summary>
    public static RoutedUICommand Search => _search ??= new RoutedUICommand(
        "Search", "Search", typeof(NavigationCommands));

    /// <summary>Gets the IncreaseZoom command.</summary>
    public static RoutedUICommand IncreaseZoom => _increaseZoom ??= new RoutedUICommand(
        "Increase Zoom", "IncreaseZoom", typeof(NavigationCommands));

    /// <summary>Gets the DecreaseZoom command.</summary>
    public static RoutedUICommand DecreaseZoom => _decreaseZoom ??= new RoutedUICommand(
        "Decrease Zoom", "DecreaseZoom", typeof(NavigationCommands));

    /// <summary>Gets the Zoom command.</summary>
    public static RoutedUICommand Zoom => _zoom ??= new RoutedUICommand(
        "Zoom", "Zoom", typeof(NavigationCommands));

    /// <summary>Gets the NextPage command.</summary>
    public static RoutedUICommand NextPage => _nextPage ??= new RoutedUICommand(
        "Next Page", "NextPage", typeof(NavigationCommands));

    /// <summary>Gets the PreviousPage command.</summary>
    public static RoutedUICommand PreviousPage => _previousPage ??= new RoutedUICommand(
        "Previous Page", "PreviousPage", typeof(NavigationCommands));

    /// <summary>Gets the FirstPage command.</summary>
    public static RoutedUICommand FirstPage => _firstPage ??= new RoutedUICommand(
        "First Page", "FirstPage", typeof(NavigationCommands));

    /// <summary>Gets the LastPage command.</summary>
    public static RoutedUICommand LastPage => _lastPage ??= new RoutedUICommand(
        "Last Page", "LastPage", typeof(NavigationCommands));

    /// <summary>Gets the GoToPage command.</summary>
    public static RoutedUICommand GoToPage => _goToPage ??= new RoutedUICommand(
        "Go To Page", "GoToPage", typeof(NavigationCommands));

    /// <summary>Gets the NavigateJournal command.</summary>
    public static RoutedUICommand NavigateJournal => _navigateJournal ??= new RoutedUICommand(
        "Navigate Journal", "NavigateJournal", typeof(NavigationCommands));
}

/// <summary>
/// Provides a standard set of editing commands.
/// </summary>
internal static class EditingCommands
{
    private static RoutedUICommand? _toggleInsert;
    private static RoutedUICommand? _delete;
    private static RoutedUICommand? _backspace;
    private static RoutedUICommand? _deleteNextWord;
    private static RoutedUICommand? _deletePreviousWord;
    private static RoutedUICommand? _enterParagraphBreak;
    private static RoutedUICommand? _enterLineBreak;
    private static RoutedUICommand? _tabForward;
    private static RoutedUICommand? _tabBackward;
    private static RoutedUICommand? _moveUpByLine;
    private static RoutedUICommand? _moveDownByLine;
    private static RoutedUICommand? _moveLeftByCharacter;
    private static RoutedUICommand? _moveRightByCharacter;
    private static RoutedUICommand? _moveLeftByWord;
    private static RoutedUICommand? _moveRightByWord;
    private static RoutedUICommand? _moveToLineStart;
    private static RoutedUICommand? _moveToLineEnd;
    private static RoutedUICommand? _moveToDocumentStart;
    private static RoutedUICommand? _moveToDocumentEnd;
    private static RoutedUICommand? _selectUpByLine;
    private static RoutedUICommand? _selectDownByLine;
    private static RoutedUICommand? _selectLeftByCharacter;
    private static RoutedUICommand? _selectRightByCharacter;
    private static RoutedUICommand? _selectLeftByWord;
    private static RoutedUICommand? _selectRightByWord;
    private static RoutedUICommand? _selectToLineStart;
    private static RoutedUICommand? _selectToLineEnd;
    private static RoutedUICommand? _selectToDocumentStart;
    private static RoutedUICommand? _selectToDocumentEnd;
    private static RoutedUICommand? _toggleBold;
    private static RoutedUICommand? _toggleItalic;
    private static RoutedUICommand? _toggleUnderline;

    /// <summary>Gets the ToggleInsert command.</summary>
    public static RoutedUICommand ToggleInsert => _toggleInsert ??= new RoutedUICommand(
        "Toggle Insert", "ToggleInsert", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(45, 0) }); // Insert, None

    /// <summary>Gets the Delete command.</summary>
    public static RoutedUICommand Delete => _delete ??= new RoutedUICommand(
        "Delete", "Delete", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(46, 0) }); // Delete, None

    /// <summary>Gets the Backspace command.</summary>
    public static RoutedUICommand Backspace => _backspace ??= new RoutedUICommand(
        "Backspace", "Backspace", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(8, 0) }); // Back, None

    /// <summary>Gets the DeleteNextWord command.</summary>
    public static RoutedUICommand DeleteNextWord => _deleteNextWord ??= new RoutedUICommand(
        "Delete Next Word", "DeleteNextWord", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(46, 2) }); // Delete, Ctrl

    /// <summary>Gets the DeletePreviousWord command.</summary>
    public static RoutedUICommand DeletePreviousWord => _deletePreviousWord ??= new RoutedUICommand(
        "Delete Previous Word", "DeletePreviousWord", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(8, 2) }); // Back, Ctrl

    /// <summary>Gets the EnterParagraphBreak command.</summary>
    public static RoutedUICommand EnterParagraphBreak => _enterParagraphBreak ??= new RoutedUICommand(
        "Enter Paragraph Break", "EnterParagraphBreak", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(13, 0) }); // Enter, None

    /// <summary>Gets the EnterLineBreak command.</summary>
    public static RoutedUICommand EnterLineBreak => _enterLineBreak ??= new RoutedUICommand(
        "Enter Line Break", "EnterLineBreak", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(13, 4) }); // Enter, Shift

    /// <summary>Gets the TabForward command.</summary>
    public static RoutedUICommand TabForward => _tabForward ??= new RoutedUICommand(
        "Tab Forward", "TabForward", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(9, 0) }); // Tab, None

    /// <summary>Gets the TabBackward command.</summary>
    public static RoutedUICommand TabBackward => _tabBackward ??= new RoutedUICommand(
        "Tab Backward", "TabBackward", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(9, 4) }); // Tab, Shift

    /// <summary>Gets the MoveUpByLine command.</summary>
    public static RoutedUICommand MoveUpByLine => _moveUpByLine ??= new RoutedUICommand(
        "Move Up By Line", "MoveUpByLine", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(38, 0) }); // Up, None

    /// <summary>Gets the MoveDownByLine command.</summary>
    public static RoutedUICommand MoveDownByLine => _moveDownByLine ??= new RoutedUICommand(
        "Move Down By Line", "MoveDownByLine", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(40, 0) }); // Down, None

    /// <summary>Gets the MoveLeftByCharacter command.</summary>
    public static RoutedUICommand MoveLeftByCharacter => _moveLeftByCharacter ??= new RoutedUICommand(
        "Move Left By Character", "MoveLeftByCharacter", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(37, 0) }); // Left, None

    /// <summary>Gets the MoveRightByCharacter command.</summary>
    public static RoutedUICommand MoveRightByCharacter => _moveRightByCharacter ??= new RoutedUICommand(
        "Move Right By Character", "MoveRightByCharacter", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(39, 0) }); // Right, None

    /// <summary>Gets the MoveLeftByWord command.</summary>
    public static RoutedUICommand MoveLeftByWord => _moveLeftByWord ??= new RoutedUICommand(
        "Move Left By Word", "MoveLeftByWord", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(37, 2) }); // Left, Ctrl

    /// <summary>Gets the MoveRightByWord command.</summary>
    public static RoutedUICommand MoveRightByWord => _moveRightByWord ??= new RoutedUICommand(
        "Move Right By Word", "MoveRightByWord", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(39, 2) }); // Right, Ctrl

    /// <summary>Gets the MoveToLineStart command.</summary>
    public static RoutedUICommand MoveToLineStart => _moveToLineStart ??= new RoutedUICommand(
        "Move To Line Start", "MoveToLineStart", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(36, 0) }); // Home, None

    /// <summary>Gets the MoveToLineEnd command.</summary>
    public static RoutedUICommand MoveToLineEnd => _moveToLineEnd ??= new RoutedUICommand(
        "Move To Line End", "MoveToLineEnd", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(35, 0) }); // End, None

    /// <summary>Gets the MoveToDocumentStart command.</summary>
    public static RoutedUICommand MoveToDocumentStart => _moveToDocumentStart ??= new RoutedUICommand(
        "Move To Document Start", "MoveToDocumentStart", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(36, 2) }); // Home, Ctrl

    /// <summary>Gets the MoveToDocumentEnd command.</summary>
    public static RoutedUICommand MoveToDocumentEnd => _moveToDocumentEnd ??= new RoutedUICommand(
        "Move To Document End", "MoveToDocumentEnd", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(35, 2) }); // End, Ctrl

    /// <summary>Gets the SelectUpByLine command.</summary>
    public static RoutedUICommand SelectUpByLine => _selectUpByLine ??= new RoutedUICommand(
        "Select Up By Line", "SelectUpByLine", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(38, 4) }); // Up, Shift

    /// <summary>Gets the SelectDownByLine command.</summary>
    public static RoutedUICommand SelectDownByLine => _selectDownByLine ??= new RoutedUICommand(
        "Select Down By Line", "SelectDownByLine", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(40, 4) }); // Down, Shift

    /// <summary>Gets the SelectLeftByCharacter command.</summary>
    public static RoutedUICommand SelectLeftByCharacter => _selectLeftByCharacter ??= new RoutedUICommand(
        "Select Left By Character", "SelectLeftByCharacter", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(37, 4) }); // Left, Shift

    /// <summary>Gets the SelectRightByCharacter command.</summary>
    public static RoutedUICommand SelectRightByCharacter => _selectRightByCharacter ??= new RoutedUICommand(
        "Select Right By Character", "SelectRightByCharacter", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(39, 4) }); // Right, Shift

    /// <summary>Gets the SelectLeftByWord command.</summary>
    public static RoutedUICommand SelectLeftByWord => _selectLeftByWord ??= new RoutedUICommand(
        "Select Left By Word", "SelectLeftByWord", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(37, 6) }); // Left, Ctrl+Shift

    /// <summary>Gets the SelectRightByWord command.</summary>
    public static RoutedUICommand SelectRightByWord => _selectRightByWord ??= new RoutedUICommand(
        "Select Right By Word", "SelectRightByWord", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(39, 6) }); // Right, Ctrl+Shift

    /// <summary>Gets the SelectToLineStart command.</summary>
    public static RoutedUICommand SelectToLineStart => _selectToLineStart ??= new RoutedUICommand(
        "Select To Line Start", "SelectToLineStart", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(36, 4) }); // Home, Shift

    /// <summary>Gets the SelectToLineEnd command.</summary>
    public static RoutedUICommand SelectToLineEnd => _selectToLineEnd ??= new RoutedUICommand(
        "Select To Line End", "SelectToLineEnd", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(35, 4) }); // End, Shift

    /// <summary>Gets the SelectToDocumentStart command.</summary>
    public static RoutedUICommand SelectToDocumentStart => _selectToDocumentStart ??= new RoutedUICommand(
        "Select To Document Start", "SelectToDocumentStart", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(36, 6) }); // Home, Ctrl+Shift

    /// <summary>Gets the SelectToDocumentEnd command.</summary>
    public static RoutedUICommand SelectToDocumentEnd => _selectToDocumentEnd ??= new RoutedUICommand(
        "Select To Document End", "SelectToDocumentEnd", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(35, 6) }); // End, Ctrl+Shift

    /// <summary>Gets the ToggleBold command.</summary>
    public static RoutedUICommand ToggleBold => _toggleBold ??= new RoutedUICommand(
        "Toggle Bold", "ToggleBold", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(66, 2) }); // B, Ctrl

    /// <summary>Gets the ToggleItalic command.</summary>
    public static RoutedUICommand ToggleItalic => _toggleItalic ??= new RoutedUICommand(
        "Toggle Italic", "ToggleItalic", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(73, 2) }); // I, Ctrl

    /// <summary>Gets the ToggleUnderline command.</summary>
    public static RoutedUICommand ToggleUnderline => _toggleUnderline ??= new RoutedUICommand(
        "Toggle Underline", "ToggleUnderline", typeof(EditingCommands),
        new InputGestureCollection { new KeyGesture(85, 2) }); // U, Ctrl
}
