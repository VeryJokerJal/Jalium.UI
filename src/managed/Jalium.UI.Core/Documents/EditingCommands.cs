using System.Collections.Concurrent;
using Jalium.UI.Input;

namespace Jalium.UI.Documents;

/// <summary>Provides the standard routed commands used by text editors.</summary>
public static class EditingCommands
{
    private static readonly ConcurrentDictionary<string, RoutedUICommand> Commands = new();

    private static RoutedUICommand Get(
        string name,
        string text,
        params KeyGesture[] gestures) =>
        Commands.GetOrAdd(name, _ => new RoutedUICommand(
            text,
            name,
            typeof(EditingCommands),
            new InputGestureCollection(gestures)));

    public static RoutedUICommand ToggleInsert => Get(nameof(ToggleInsert), "Toggle Insert", new KeyGesture(45, 0));
    public static RoutedUICommand Delete => Get(nameof(Delete), "Delete", new KeyGesture(46, 0));
    public static RoutedUICommand Backspace => Get(nameof(Backspace), "Backspace", new KeyGesture(8, 0));
    public static RoutedUICommand DeleteNextWord => Get(nameof(DeleteNextWord), "Delete Next Word", new KeyGesture(46, 2));
    public static RoutedUICommand DeletePreviousWord => Get(nameof(DeletePreviousWord), "Delete Previous Word", new KeyGesture(8, 2));
    public static RoutedUICommand EnterParagraphBreak => Get(nameof(EnterParagraphBreak), "Enter Paragraph Break", new KeyGesture(13, 0));
    public static RoutedUICommand EnterLineBreak => Get(nameof(EnterLineBreak), "Enter Line Break", new KeyGesture(13, 4));
    public static RoutedUICommand TabForward => Get(nameof(TabForward), "Tab Forward", new KeyGesture(9, 0));
    public static RoutedUICommand TabBackward => Get(nameof(TabBackward), "Tab Backward", new KeyGesture(9, 4));
    public static RoutedUICommand MoveUpByLine => Get(nameof(MoveUpByLine), "Move Up By Line", new KeyGesture(38, 0));
    public static RoutedUICommand MoveDownByLine => Get(nameof(MoveDownByLine), "Move Down By Line", new KeyGesture(40, 0));
    public static RoutedUICommand MoveLeftByCharacter => Get(nameof(MoveLeftByCharacter), "Move Left By Character", new KeyGesture(37, 0));
    public static RoutedUICommand MoveRightByCharacter => Get(nameof(MoveRightByCharacter), "Move Right By Character", new KeyGesture(39, 0));
    public static RoutedUICommand MoveLeftByWord => Get(nameof(MoveLeftByWord), "Move Left By Word", new KeyGesture(37, 2));
    public static RoutedUICommand MoveRightByWord => Get(nameof(MoveRightByWord), "Move Right By Word", new KeyGesture(39, 2));
    public static RoutedUICommand MoveToLineStart => Get(nameof(MoveToLineStart), "Move To Line Start", new KeyGesture(36, 0));
    public static RoutedUICommand MoveToLineEnd => Get(nameof(MoveToLineEnd), "Move To Line End", new KeyGesture(35, 0));
    public static RoutedUICommand MoveToDocumentStart => Get(nameof(MoveToDocumentStart), "Move To Document Start", new KeyGesture(36, 2));
    public static RoutedUICommand MoveToDocumentEnd => Get(nameof(MoveToDocumentEnd), "Move To Document End", new KeyGesture(35, 2));
    public static RoutedUICommand SelectUpByLine => Get(nameof(SelectUpByLine), "Select Up By Line", new KeyGesture(38, 4));
    public static RoutedUICommand SelectDownByLine => Get(nameof(SelectDownByLine), "Select Down By Line", new KeyGesture(40, 4));
    public static RoutedUICommand SelectLeftByCharacter => Get(nameof(SelectLeftByCharacter), "Select Left By Character", new KeyGesture(37, 4));
    public static RoutedUICommand SelectRightByCharacter => Get(nameof(SelectRightByCharacter), "Select Right By Character", new KeyGesture(39, 4));
    public static RoutedUICommand SelectLeftByWord => Get(nameof(SelectLeftByWord), "Select Left By Word", new KeyGesture(37, 6));
    public static RoutedUICommand SelectRightByWord => Get(nameof(SelectRightByWord), "Select Right By Word", new KeyGesture(39, 6));
    public static RoutedUICommand SelectToLineStart => Get(nameof(SelectToLineStart), "Select To Line Start", new KeyGesture(36, 4));
    public static RoutedUICommand SelectToLineEnd => Get(nameof(SelectToLineEnd), "Select To Line End", new KeyGesture(35, 4));
    public static RoutedUICommand SelectToDocumentStart => Get(nameof(SelectToDocumentStart), "Select To Document Start", new KeyGesture(36, 6));
    public static RoutedUICommand SelectToDocumentEnd => Get(nameof(SelectToDocumentEnd), "Select To Document End", new KeyGesture(35, 6));
    public static RoutedUICommand ToggleBold => Get(nameof(ToggleBold), "Toggle Bold", new KeyGesture(66, 2));
    public static RoutedUICommand ToggleItalic => Get(nameof(ToggleItalic), "Toggle Italic", new KeyGesture(73, 2));
    public static RoutedUICommand ToggleUnderline => Get(nameof(ToggleUnderline), "Toggle Underline", new KeyGesture(85, 2));

    public static RoutedUICommand MoveUpByParagraph => Get(nameof(MoveUpByParagraph), "Move Up By Paragraph", new KeyGesture(38, 2));
    public static RoutedUICommand MoveDownByParagraph => Get(nameof(MoveDownByParagraph), "Move Down By Paragraph", new KeyGesture(40, 2));
    public static RoutedUICommand MoveUpByPage => Get(nameof(MoveUpByPage), "Move Up By Page", new KeyGesture(33, 0));
    public static RoutedUICommand MoveDownByPage => Get(nameof(MoveDownByPage), "Move Down By Page", new KeyGesture(34, 0));
    public static RoutedUICommand SelectUpByParagraph => Get(nameof(SelectUpByParagraph), "Select Up By Paragraph", new KeyGesture(38, 6));
    public static RoutedUICommand SelectDownByParagraph => Get(nameof(SelectDownByParagraph), "Select Down By Paragraph", new KeyGesture(40, 6));
    public static RoutedUICommand SelectUpByPage => Get(nameof(SelectUpByPage), "Select Up By Page", new KeyGesture(33, 4));
    public static RoutedUICommand SelectDownByPage => Get(nameof(SelectDownByPage), "Select Down By Page", new KeyGesture(34, 4));
    public static RoutedUICommand DecreaseFontSize => Get(nameof(DecreaseFontSize), "Decrease Font Size");
    public static RoutedUICommand IncreaseFontSize => Get(nameof(IncreaseFontSize), "Increase Font Size");
    public static RoutedUICommand AlignLeft => Get(nameof(AlignLeft), "Align Left", new KeyGesture(76, 2));
    public static RoutedUICommand AlignCenter => Get(nameof(AlignCenter), "Align Center", new KeyGesture(69, 2));
    public static RoutedUICommand AlignRight => Get(nameof(AlignRight), "Align Right", new KeyGesture(82, 2));
    public static RoutedUICommand AlignJustify => Get(nameof(AlignJustify), "Align Justify", new KeyGesture(74, 2));
    public static RoutedUICommand ToggleBullets => Get(nameof(ToggleBullets), "Toggle Bullets");
    public static RoutedUICommand ToggleNumbering => Get(nameof(ToggleNumbering), "Toggle Numbering");
    public static RoutedUICommand IncreaseIndentation => Get(nameof(IncreaseIndentation), "Increase Indentation");
    public static RoutedUICommand DecreaseIndentation => Get(nameof(DecreaseIndentation), "Decrease Indentation");
    public static RoutedUICommand CorrectSpellingError => Get(nameof(CorrectSpellingError), "Correct Spelling Error");
    public static RoutedUICommand IgnoreSpellingError => Get(nameof(IgnoreSpellingError), "Ignore Spelling Error");
    public static RoutedUICommand ToggleSubscript => Get(nameof(ToggleSubscript), "Toggle Subscript");
    public static RoutedUICommand ToggleSuperscript => Get(nameof(ToggleSuperscript), "Toggle Superscript");
}
