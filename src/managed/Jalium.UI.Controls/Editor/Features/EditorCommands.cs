using System.Collections.Generic;

namespace Jalium.UI.Controls;

/// <summary>
/// Logical command categories for editor commands.
/// </summary>
public enum EditorCommandCategory
{
    Navigation,
    Selection,
    Editing,
    Search,
    Clipboard,
    Folding,
    Formatting,
    View,
    Accessibility,
    Bookmark,
    Macro
}

public readonly record struct EditorCommandMetadata(string Id, EditorCommandCategory Category, string Description);

/// <summary>
/// Centralized command identifiers and metadata.
/// Values are stable and backward-compatible with existing tests.
/// </summary>
public static class EditorCommands
{
    // Editing
    public const string Undo = "Edit.Undo";
    public const string Redo = "Edit.Redo";
    public const string Cut = "Edit.Cut";
    public const string Copy = "Edit.Copy";
    public const string Paste = "Edit.Paste";
    public const string Delete = "Edit.Delete";
    public const string SelectAll = "Edit.SelectAll";

    public const string Find = "Edit.Find";
    public const string FindNext = "Edit.FindNext";
    public const string FindPrevious = "Edit.FindPrevious";
    public const string FindAll = "Edit.FindAll";
    public const string FindNextResult = "Edit.FindNextResult";
    public const string FindPreviousResult = "Edit.FindPreviousResult";
    public const string Replace = "Edit.Replace";
    public const string ReplaceNext = "Edit.ReplaceNext";
    public const string ReplaceSelection = "Edit.ReplaceSelection";
    public const string ReplaceAll = "Edit.ReplaceAll";

    public const string GoToLine = "Edit.GoToLine";
    public const string GoToDefinition = "Edit.GoToDefinition";
    public const string GoToMatchingBracket = "Edit.GoToMatchingBracket";

    // Navigation
    public const string CaretLeft = "Edit.CaretLeft";
    public const string CaretRight = "Edit.CaretRight";
    public const string CaretUp = "Edit.CaretUp";
    public const string CaretDown = "Edit.CaretDown";
    public const string WordLeft = "Edit.WordLeft";
    public const string WordRight = "Edit.WordRight";
    public const string LineStart = "Edit.LineStart";
    public const string LineEnd = "Edit.LineEnd";
    public const string DocumentStart = "Edit.DocumentStart";
    public const string DocumentEnd = "Edit.DocumentEnd";
    public const string PageUp = "Edit.PageUp";
    public const string PageDown = "Edit.PageDown";
    public const string CaretDocumentTop = "Edit.CaretDocumentTop";
    public const string CaretDocumentBottom = "Edit.CaretDocumentBottom";
    public const string DocumentTop = "Edit.DocumentTop";
    public const string DocumentBottom = "Edit.DocumentBottom";
    public const string LineUp = "Edit.LineUp";
    public const string LineDown = "Edit.LineDown";
    public const string JumpToNextParagraph = "Edit.JumpToNextParagraph";
    public const string JumpToPreviousParagraph = "Edit.JumpToPreviousParagraph";

    // Selection
    public const string SelectLeft = "Edit.SelectLeft";
    public const string SelectRight = "Edit.SelectRight";
    public const string SelectUp = "Edit.SelectUp";
    public const string SelectDown = "Edit.SelectDown";
    public const string SelectWordLeft = "Edit.SelectWordLeft";
    public const string SelectWordRight = "Edit.SelectWordRight";
    public const string SelectLineStart = "Edit.SelectLineStart";
    public const string SelectLineEnd = "Edit.SelectLineEnd";
    public const string SelectDocumentStart = "Edit.SelectDocumentStart";
    public const string SelectDocumentEnd = "Edit.SelectDocumentEnd";
    public const string SelectPageUp = "Edit.SelectPageUp";
    public const string SelectPageDown = "Edit.SelectPageDown";
    public const string SelectCurrentWord = "Edit.SelectCurrentWord";
    public const string SelectCurrentLine = "Edit.SelectCurrentLine";
    public const string SelectCurrentParagraph = "Edit.SelectCurrentParagraph";
    public const string SelectLine = "Edit.SelectLine";
    public const string SelectAllLines = "Edit.SelectAllLines";
    public const string SelectLineBlock = "Edit.SelectLineBlock";
    public const string SelectMatchingBracket = "Edit.SelectMatchingBracket";
    public const string ClearSelection = "Edit.ClearSelection";
    public const string ToggleColumnSelection = "Edit.ToggleColumnSelection";
    public const string SelectBookmark = "Edit.SelectBookmark";

    // Editing helpers
    public const string DeleteLeft = "Edit.DeleteLeft";
    public const string DeleteRight = "Edit.DeleteRight";
    public const string DeleteWordLeft = "Edit.DeleteWordLeft";
    public const string DeleteWordRight = "Edit.DeleteWordRight";
    public const string InsertNewLine = "Edit.InsertNewLine";
    public const string InsertTab = "Edit.InsertTab";
    public const string Unindent = "Edit.Unindent";
    public const string IndentLine = "Edit.IndentLine";
    public const string UnindentLine = "Edit.UnindentLine";
    public const string DuplicateLine = "Edit.DuplicateLine";
    public const string DuplicateLineOrSelection = "Edit.DuplicateLineOrSelection";
    public const string DeleteLine = "Edit.DeleteLine";
    public const string DeleteLineOrSelection = "Edit.DeleteLineOrSelection";
    public const string MoveLineUp = "Edit.MoveLineUp";
    public const string MoveLineDown = "Edit.MoveLineDown";
    public const string PasteFromHistory = "Edit.PasteFromHistory";
    public const string InsertCurrentDateTime = "Edit.InsertCurrentDateTime";
    public const string InsertSnippet = "Edit.InsertSnippet";
    public const string ToggleCaseUpper = "Edit.ToggleCaseUpper";
    public const string ToggleCaseLower = "Edit.ToggleCaseLower";
    public const string ToggleCaseTitle = "Edit.ToggleCaseTitle";
    public const string NormalizeIndent = "Edit.NormalizeIndent";

    // Block/Format
    public const string CommentLine = "Edit.CommentLine";
    public const string UncommentLine = "Edit.UncommentLine";
    public const string ToggleLineComment = "Edit.ToggleLineComment";
    public const string ToggleBlockComment = "Edit.ToggleBlockComment";
    public const string WrapSelectionWithBlock = "Edit.WrapSelectionWithBlock";
    public const string UnwrapSelectionFromBlock = "Edit.UnwrapSelectionFromBlock";
    public const string InsertPair = "Edit.InsertPair";
    public const string DeletePair = "Edit.DeletePair";
    public const string SurroundWith = "Edit.SurroundWith";

    // Folding
    public const string ToggleFold = "Edit.ToggleFold";
    public const string FoldAll = "Edit.FoldAll";
    public const string UnfoldAll = "Edit.UnfoldAll";
    public const string FoldCurrentBlock = "Edit.FoldCurrentBlock";
    public const string ExpandCurrentBlock = "Edit.ExpandCurrentBlock";
    public const string ToggleFoldAll = "Edit.ToggleFoldAll";

    // Search/replace panel commands
    public const string ShowFindDialog = "Edit.ShowFindDialog";
    public const string ShowReplaceDialog = "Edit.ShowReplaceDialog";
    public const string SearchFromSelection = "Edit.SearchFromSelection";
    public const string SearchSelectionResult = "Edit.SearchSelectionResult";
    public const string NextSearchResult = "Edit.NextSearchResult";
    public const string PreviousSearchResult = "Edit.PreviousSearchResult";

    // Scrolling
    public const string ScrollToCaret = "Edit.ScrollToCaret";
    public const string ScrollLineUp = "Edit.ScrollLineUp";
    public const string ScrollLineDown = "Edit.ScrollLineDown";
    public const string ScrollPageUp = "Edit.ScrollPageUp";
    public const string ScrollPageDown = "Edit.ScrollPageDown";

    // Bookmarks
    public const string ToggleBookmark = "Edit.ToggleBookmark";
    public const string NextBookmark = "Edit.NextBookmark";
    public const string PreviousBookmark = "Edit.PreviousBookmark";

    // Multi-cursor / experimental
    public const string AddSecondaryCaret = "Edit.AddSecondaryCaret";
    public const string ClearSecondaryCarets = "Edit.ClearSecondaryCarets";
    public const string MultiCursorSelectUp = "Edit.MultiCursorSelectUp";
    public const string MultiCursorSelectDown = "Edit.MultiCursorSelectDown";
    public const string ToggleMultiCaretMode = "Edit.ToggleMultiCaretMode";

    // Navigation state
    public const string UndoLocation = "Edit.UndoLocation";
    public const string RedoLocation = "Edit.RedoLocation";

    // Accessibility
    public const string ToggleInsertOverwrite = "Edit.ToggleInsertOverwrite";
    public const string ToggleHighContrast = "Edit.ToggleHighContrast";
    public const string ToggleAccessibilityMode = "Edit.ToggleAccessibilityMode";

    public static IReadOnlyDictionary<string, EditorCommandMetadata> Metadata { get; } =
        new Dictionary<string, EditorCommandMetadata>
        {
            [Undo] = new(EditorCommands.Undo, EditorCommandCategory.Editing, "Undo last edit"),
            [Redo] = new(EditorCommands.Redo, EditorCommandCategory.Editing, "Redo last undone edit"),
            [Cut] = new(EditorCommands.Cut, EditorCommandCategory.Clipboard, "Cut selection"),
            [Copy] = new(EditorCommands.Copy, EditorCommandCategory.Clipboard, "Copy selection"),
            [Paste] = new(EditorCommands.Paste, EditorCommandCategory.Clipboard, "Paste clipboard text"),
            [Delete] = new(EditorCommands.Delete, EditorCommandCategory.Editing, "Delete selection or next char"),
            [SelectAll] = new(EditorCommands.SelectAll, EditorCommandCategory.Selection, "Select all content"),

            [Find] = new(EditorCommands.Find, EditorCommandCategory.Search, "Find text"),
            [FindNext] = new(EditorCommands.FindNext, EditorCommandCategory.Search, "Find next"),
            [FindPrevious] = new(EditorCommands.FindPrevious, EditorCommandCategory.Search, "Find previous"),
            [FindAll] = new(EditorCommands.FindAll, EditorCommandCategory.Search, "Find all matches"),
            [FindNextResult] = new(EditorCommands.FindNextResult, EditorCommandCategory.Search, "Find next match"),
            [FindPreviousResult] = new(EditorCommands.FindPreviousResult, EditorCommandCategory.Search, "Find previous match"),
            [Replace] = new(EditorCommands.Replace, EditorCommandCategory.Search, "Replace current match"),
            [ReplaceNext] = new(EditorCommands.ReplaceNext, EditorCommandCategory.Search, "Replace next match"),
            [ReplaceSelection] = new(EditorCommands.ReplaceSelection, EditorCommandCategory.Search, "Replace selection"),
            [ReplaceAll] = new(EditorCommands.ReplaceAll, EditorCommandCategory.Search, "Replace all matches"),

            [GoToLine] = new(EditorCommands.GoToLine, EditorCommandCategory.Navigation, "Go to line"),
            [GoToDefinition] = new(EditorCommands.GoToDefinition, EditorCommandCategory.Navigation, "Go to definition"),
            [GoToMatchingBracket] = new(EditorCommands.GoToMatchingBracket, EditorCommandCategory.Navigation, "Jump to matching bracket"),

            [CaretLeft] = new(EditorCommands.CaretLeft, EditorCommandCategory.Navigation, "Move caret left"),
            [CaretRight] = new(EditorCommands.CaretRight, EditorCommandCategory.Navigation, "Move caret right"),
            [CaretUp] = new(EditorCommands.CaretUp, EditorCommandCategory.Navigation, "Move caret up"),
            [CaretDown] = new(EditorCommands.CaretDown, EditorCommandCategory.Navigation, "Move caret down"),
            [WordLeft] = new(EditorCommands.WordLeft, EditorCommandCategory.Navigation, "Move caret left by word"),
            [WordRight] = new(EditorCommands.WordRight, EditorCommandCategory.Navigation, "Move caret right by word"),
            [LineStart] = new(EditorCommands.LineStart, EditorCommandCategory.Navigation, "Move to line start"),
            [LineEnd] = new(EditorCommands.LineEnd, EditorCommandCategory.Navigation, "Move to line end"),
            [DocumentStart] = new(EditorCommands.DocumentStart, EditorCommandCategory.Navigation, "Move to document start"),
            [DocumentEnd] = new(EditorCommands.DocumentEnd, EditorCommandCategory.Navigation, "Move to document end"),
            [PageUp] = new(EditorCommands.PageUp, EditorCommandCategory.Navigation, "Move up one page"),
            [PageDown] = new(EditorCommands.PageDown, EditorCommandCategory.Navigation, "Move down one page"),

            [SelectLeft] = new(EditorCommands.SelectLeft, EditorCommandCategory.Selection, "Select left"),
            [SelectRight] = new(EditorCommands.SelectRight, EditorCommandCategory.Selection, "Select right"),
            [SelectUp] = new(EditorCommands.SelectUp, EditorCommandCategory.Selection, "Select up"),
            [SelectDown] = new(EditorCommands.SelectDown, EditorCommandCategory.Selection, "Select down"),
            [SelectWordLeft] = new(EditorCommands.SelectWordLeft, EditorCommandCategory.Selection, "Select word left"),
            [SelectWordRight] = new(EditorCommands.SelectWordRight, EditorCommandCategory.Selection, "Select word right"),
            [SelectLineStart] = new(EditorCommands.SelectLineStart, EditorCommandCategory.Selection, "Select line start"),
            [SelectLineEnd] = new(EditorCommands.SelectLineEnd, EditorCommandCategory.Selection, "Select line end"),
            [SelectDocumentStart] = new(EditorCommands.SelectDocumentStart, EditorCommandCategory.Selection, "Select to document start"),
            [SelectDocumentEnd] = new(EditorCommands.SelectDocumentEnd, EditorCommandCategory.Selection, "Select to document end"),
            [SelectPageUp] = new(EditorCommands.SelectPageUp, EditorCommandCategory.Selection, "Select page up"),
            [SelectPageDown] = new(EditorCommands.SelectPageDown, EditorCommandCategory.Selection, "Select page down"),
            [SelectCurrentWord] = new(EditorCommands.SelectCurrentWord, EditorCommandCategory.Selection, "Select current word"),
            [SelectCurrentLine] = new(EditorCommands.SelectCurrentLine, EditorCommandCategory.Selection, "Select current line"),
            [SelectCurrentParagraph] = new(EditorCommands.SelectCurrentParagraph, EditorCommandCategory.Selection, "Select current paragraph"),
            [SelectLine] = new(EditorCommands.SelectLine, EditorCommandCategory.Selection, "Select line"),
            [SelectAllLines] = new(EditorCommands.SelectAllLines, EditorCommandCategory.Selection, "Select block of lines"),
            [SelectLineBlock] = new(EditorCommands.SelectLineBlock, EditorCommandCategory.Selection, "Select line block"),
            [SelectMatchingBracket] = new(EditorCommands.SelectMatchingBracket, EditorCommandCategory.Selection, "Select matching bracket content"),
            [ClearSelection] = new(EditorCommands.ClearSelection, EditorCommandCategory.Selection, "Clear selection"),
            [ToggleColumnSelection] = new(EditorCommands.ToggleColumnSelection, EditorCommandCategory.Selection, "Toggle column selection"),

            [DeleteLeft] = new(EditorCommands.DeleteLeft, EditorCommandCategory.Editing, "Delete previous"),
            [DeleteRight] = new(EditorCommands.DeleteRight, EditorCommandCategory.Editing, "Delete next"),
            [DeleteWordLeft] = new(EditorCommands.DeleteWordLeft, EditorCommandCategory.Editing, "Delete previous word"),
            [DeleteWordRight] = new(EditorCommands.DeleteWordRight, EditorCommandCategory.Editing, "Delete next word"),
            [InsertNewLine] = new(EditorCommands.InsertNewLine, EditorCommandCategory.Editing, "Insert line break"),
            [InsertTab] = new(EditorCommands.InsertTab, EditorCommandCategory.Editing, "Insert tab"),
            [Unindent] = new(EditorCommands.Unindent, EditorCommandCategory.Editing, "Unindent"),
            [IndentLine] = new(EditorCommands.IndentLine, EditorCommandCategory.Editing, "Indent lines"),
            [UnindentLine] = new(EditorCommands.UnindentLine, EditorCommandCategory.Editing, "Unindent lines"),
            [DuplicateLine] = new(EditorCommands.DuplicateLine, EditorCommandCategory.Editing, "Duplicate current line"),
            [DuplicateLineOrSelection] = new(EditorCommands.DuplicateLineOrSelection, EditorCommandCategory.Editing, "Duplicate line or selection"),
            [DeleteLine] = new(EditorCommands.DeleteLine, EditorCommandCategory.Editing, "Delete current line"),
            [DeleteLineOrSelection] = new(EditorCommands.DeleteLineOrSelection, EditorCommandCategory.Editing, "Delete current line or selection"),
            [MoveLineUp] = new(EditorCommands.MoveLineUp, EditorCommandCategory.Editing, "Move line up"),
            [MoveLineDown] = new(EditorCommands.MoveLineDown, EditorCommandCategory.Editing, "Move line down"),
            [PasteFromHistory] = new(EditorCommands.PasteFromHistory, EditorCommandCategory.Clipboard, "Paste from history"),
            [InsertCurrentDateTime] = new(EditorCommands.InsertCurrentDateTime, EditorCommandCategory.Editing, "Insert current date and time"),
            [InsertSnippet] = new(EditorCommands.InsertSnippet, EditorCommandCategory.Editing, "Insert snippet"),
            [ToggleCaseUpper] = new(EditorCommands.ToggleCaseUpper, EditorCommandCategory.Editing, "Convert selection to upper case"),
            [ToggleCaseLower] = new(EditorCommands.ToggleCaseLower, EditorCommandCategory.Editing, "Convert selection to lower case"),
            [ToggleCaseTitle] = new(EditorCommands.ToggleCaseTitle, EditorCommandCategory.Editing, "Convert selection to title case"),
            [NormalizeIndent] = new(EditorCommands.NormalizeIndent, EditorCommandCategory.Editing, "Normalize indentation"),

            [CommentLine] = new(EditorCommands.CommentLine, EditorCommandCategory.Formatting, "Comment line"),
            [UncommentLine] = new(EditorCommands.UncommentLine, EditorCommandCategory.Formatting, "Uncomment line"),
            [ToggleLineComment] = new(EditorCommands.ToggleLineComment, EditorCommandCategory.Formatting, "Toggle line comment"),
            [ToggleBlockComment] = new(EditorCommands.ToggleBlockComment, EditorCommandCategory.Formatting, "Toggle block comment"),
            [WrapSelectionWithBlock] = new(EditorCommands.WrapSelectionWithBlock, EditorCommandCategory.Formatting, "Wrap selection with block comment"),
            [UnwrapSelectionFromBlock] = new(EditorCommands.UnwrapSelectionFromBlock, EditorCommandCategory.Formatting, "Unwrap block comment"),
            [InsertPair] = new(EditorCommands.InsertPair, EditorCommandCategory.Editing, "Insert matching pair"),
            [DeletePair] = new(EditorCommands.DeletePair, EditorCommandCategory.Editing, "Delete matching pair"),
            [SurroundWith] = new(EditorCommands.SurroundWith, EditorCommandCategory.Formatting, "Surround selection"),

            [ToggleFold] = new(EditorCommands.ToggleFold, EditorCommandCategory.Folding, "Toggle current fold"),
            [FoldAll] = new(EditorCommands.FoldAll, EditorCommandCategory.Folding, "Fold all blocks"),
            [UnfoldAll] = new(EditorCommands.UnfoldAll, EditorCommandCategory.Folding, "Unfold all blocks"),
            [FoldCurrentBlock] = new(EditorCommands.FoldCurrentBlock, EditorCommandCategory.Folding, "Fold current block"),
            [ExpandCurrentBlock] = new(EditorCommands.ExpandCurrentBlock, EditorCommandCategory.Folding, "Expand current block"),
            [ToggleFoldAll] = new(EditorCommands.ToggleFoldAll, EditorCommandCategory.Folding, "Toggle all folds"),

            [ShowFindDialog] = new(EditorCommands.ShowFindDialog, EditorCommandCategory.Search, "Open find panel"),
            [ShowReplaceDialog] = new(EditorCommands.ShowReplaceDialog, EditorCommandCategory.Search, "Open replace panel"),
            [SearchFromSelection] = new(EditorCommands.SearchFromSelection, EditorCommandCategory.Search, "Find selected text"),
            [SearchSelectionResult] = new(EditorCommands.SearchSelectionResult, EditorCommandCategory.Search, "Find next selected match"),
            [NextSearchResult] = new(EditorCommands.NextSearchResult, EditorCommandCategory.Search, "Next search result"),
            [PreviousSearchResult] = new(EditorCommands.PreviousSearchResult, EditorCommandCategory.Search, "Previous search result"),

            [ScrollToCaret] = new(EditorCommands.ScrollToCaret, EditorCommandCategory.View, "Scroll caret into view"),
            [ScrollLineUp] = new(EditorCommands.ScrollLineUp, EditorCommandCategory.View, "Scroll one line up"),
            [ScrollLineDown] = new(EditorCommands.ScrollLineDown, EditorCommandCategory.View, "Scroll one line down"),
            [ScrollPageUp] = new(EditorCommands.ScrollPageUp, EditorCommandCategory.View, "Scroll page up"),
            [ScrollPageDown] = new(EditorCommands.ScrollPageDown, EditorCommandCategory.View, "Scroll page down"),

            [ToggleBookmark] = new(EditorCommands.ToggleBookmark, EditorCommandCategory.Bookmark, "Toggle bookmark"),
            [NextBookmark] = new(EditorCommands.NextBookmark, EditorCommandCategory.Bookmark, "Jump to next bookmark"),
            [PreviousBookmark] = new(EditorCommands.PreviousBookmark, EditorCommandCategory.Bookmark, "Jump to previous bookmark"),

            [AddSecondaryCaret] = new(EditorCommands.AddSecondaryCaret, EditorCommandCategory.Editing, "Add secondary caret"),
            [ClearSecondaryCarets] = new(EditorCommands.ClearSecondaryCarets, EditorCommandCategory.Editing, "Clear secondary carets"),
            [MultiCursorSelectUp] = new(EditorCommands.MultiCursorSelectUp, EditorCommandCategory.Editing, "Multi-cursor select up"),
            [MultiCursorSelectDown] = new(EditorCommands.MultiCursorSelectDown, EditorCommandCategory.Editing, "Multi-cursor select down"),
            [ToggleMultiCaretMode] = new(EditorCommands.ToggleMultiCaretMode, EditorCommandCategory.Editing, "Toggle multi-caret mode"),

            [UndoLocation] = new(EditorCommands.UndoLocation, EditorCommandCategory.Editing, "Undo location jump"),
            [RedoLocation] = new(EditorCommands.RedoLocation, EditorCommandCategory.Editing, "Redo location jump"),

            [ToggleInsertOverwrite] = new(EditorCommands.ToggleInsertOverwrite, EditorCommandCategory.Accessibility, "Toggle overwrite mode"),
            [ToggleHighContrast] = new(EditorCommands.ToggleHighContrast, EditorCommandCategory.Accessibility, "Toggle high contrast colors"),
            [ToggleAccessibilityMode] = new(EditorCommands.ToggleAccessibilityMode, EditorCommandCategory.Accessibility, "Toggle accessibility mode")
        };

    public static bool TryGetMetadata(string commandId, out EditorCommandMetadata metadata)
    {
        return Metadata.TryGetValue(commandId, out metadata);
    }

    public static EditorCommandCategory GetCategory(string commandId)
    {
        return Metadata.TryGetValue(commandId, out var info) ? info.Category : EditorCommandCategory.Editing;
    }
}
