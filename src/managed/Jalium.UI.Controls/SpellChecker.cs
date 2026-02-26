using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides spell checking functionality using Windows Spell Checking API.
/// </summary>
public sealed class SpellChecker : IDisposable
{
    private ISpellChecker? _spellChecker;
    private readonly string _language;
    private bool _disposed;

    /// <summary>
    /// Gets the default spell checker for the system language.
    /// </summary>
    public static SpellChecker? Default { get; private set; }

    /// <summary>
    /// Gets or sets whether spell checking is enabled globally.
    /// </summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Initializes the default spell checker.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            Default = new SpellChecker("en-US");
        }
        catch
        {
            // Spell checking not available
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpellChecker"/> class.
    /// </summary>
    /// <param name="language">The language tag (e.g., "en-US").</param>
    public SpellChecker(string language)
    {
        _language = language;

        try
        {
            var factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass();
            int supported = 0;
            factory.IsSupported(language, out supported);

            if (supported != 0)
            {
                factory.CreateSpellChecker(language, out _spellChecker);
            }
        }
        catch
        {
            _spellChecker = null;
        }
    }

    /// <summary>
    /// Gets the language of this spell checker.
    /// </summary>
    public string Language => _language;

    /// <summary>
    /// Gets whether spell checking is available.
    /// </summary>
    public bool IsAvailable => _spellChecker != null;

    /// <summary>
    /// Checks the spelling of the given text and returns spelling errors.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>A list of spelling errors.</returns>
    public IReadOnlyList<SpellingError> Check(string text)
    {
        var errors = new List<SpellingError>();

        if (_spellChecker == null || string.IsNullOrEmpty(text))
            return errors;

        try
        {
            _spellChecker.Check(text, out var enumErrors);
            if (enumErrors != null)
            {
                while (true)
                {
                    enumErrors.Next(out var error);
                    if (error == null)
                        break;

                    error.get_StartIndex(out var startIndex);
                    error.get_Length(out var length);
                    error.get_CorrectiveAction(out var action);

                    var misspelledWord = text.Substring((int)startIndex, (int)length);
                    var suggestions = GetSuggestions(misspelledWord);

                    string? replacement = null;
                    if (action == CORRECTIVE_ACTION.CORRECTIVE_ACTION_REPLACE)
                    {
                        error.get_Replacement(out replacement);
                    }

                    errors.Add(new SpellingError(
                        (int)startIndex,
                        (int)length,
                        misspelledWord,
                        (SpellingErrorType)action,
                        replacement,
                        suggestions));
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return errors;
    }

    /// <summary>
    /// Gets suggestions for a misspelled word.
    /// </summary>
    /// <param name="word">The misspelled word.</param>
    /// <returns>A list of suggestions.</returns>
    public IReadOnlyList<string> GetSuggestions(string word)
    {
        var suggestions = new List<string>();

        if (_spellChecker == null || string.IsNullOrEmpty(word))
            return suggestions;

        try
        {
            _spellChecker.Suggest(word, out var enumSuggestions);
            if (enumSuggestions != null)
            {
                while (true)
                {
                    enumSuggestions.Next(out var suggestion);
                    if (suggestion == null)
                        break;

                    suggestions.Add(suggestion);
                    if (suggestions.Count >= 5) // Limit to 5 suggestions
                        break;
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return suggestions;
    }

    /// <summary>
    /// Adds a word to the ignore list for this session.
    /// </summary>
    /// <param name="word">The word to ignore.</param>
    public void IgnoreWord(string word)
    {
        _spellChecker?.Ignore(word);
    }

    /// <summary>
    /// Adds a word to the user dictionary.
    /// </summary>
    /// <param name="word">The word to add.</param>
    public void AddToDictionary(string word)
    {
        _spellChecker?.Add(word);
    }

    /// <summary>
    /// Sets an autocorrect pair.
    /// </summary>
    /// <param name="from">The word to replace.</param>
    /// <param name="to">The replacement word.</param>
    public void SetAutoCorrect(string from, string to)
    {
        _spellChecker?.AutoCorrect(from, to);
    }

    /// <summary>
    /// Checks if a single word is spelled correctly.
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <returns>True if spelled correctly; otherwise, false.</returns>
    public bool IsSpelledCorrectly(string word)
    {
        if (_spellChecker == null || string.IsNullOrEmpty(word))
            return true;

        var errors = Check(word);
        return errors.Count == 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_spellChecker != null)
            {
                Marshal.ReleaseComObject(_spellChecker);
                _spellChecker = null;
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents a spelling error.
/// </summary>
public sealed class SpellingError
{
    /// <summary>
    /// Gets the start index of the error in the text.
    /// </summary>
    public int StartIndex { get; }

    /// <summary>
    /// Gets the length of the misspelled word.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the misspelled word.
    /// </summary>
    public string Word { get; }

    /// <summary>
    /// Gets the type of error.
    /// </summary>
    public SpellingErrorType ErrorType { get; }

    /// <summary>
    /// Gets the automatic replacement if available.
    /// </summary>
    public string? Replacement { get; }

    /// <summary>
    /// Gets the suggested corrections.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpellingError"/> class.
    /// </summary>
    public SpellingError(int startIndex, int length, string word, SpellingErrorType errorType,
        string? replacement, IReadOnlyList<string> suggestions)
    {
        StartIndex = startIndex;
        Length = length;
        Word = word;
        ErrorType = errorType;
        Replacement = replacement;
        Suggestions = suggestions;
    }
}

/// <summary>
/// Specifies the type of spelling error.
/// </summary>
public enum SpellingErrorType
{
    /// <summary>
    /// No action needed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Get suggestions for the word.
    /// </summary>
    GetSuggestions = 1,

    /// <summary>
    /// Replace with the suggested replacement.
    /// </summary>
    Replace = 2,

    /// <summary>
    /// Delete the repeated word.
    /// </summary>
    Delete = 3
}

#region Windows Spell Checking API Interop

[ComImport]
[Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellCheckerFactory
{
    void get_SupportedLanguages(out IEnumString value);
    void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
    void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
}

[ComImport]
[Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
internal class SpellCheckerFactoryClass
{
}

[ComImport]
[Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellChecker
{
    void get_LanguageTag(out string value);
    void Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
    void Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString value);
    void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
    void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
    void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);
    void GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId, out byte value);
    void get_OptionIds(out IEnumString value);
    void get_Id(out string value);
    void get_LocalizedName(out string value);
    // Additional members omitted
}

[ComImport]
[Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumSpellingError
{
    void Next(out ISpellingError value);
}

[ComImport]
[Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISpellingError
{
    void get_StartIndex(out uint value);
    void get_Length(out uint value);
    void get_CorrectiveAction(out CORRECTIVE_ACTION value);
    void get_Replacement([MarshalAs(UnmanagedType.LPWStr)] out string value);
}

internal enum CORRECTIVE_ACTION
{
    CORRECTIVE_ACTION_NONE = 0,
    CORRECTIVE_ACTION_GET_SUGGESTIONS = 1,
    CORRECTIVE_ACTION_REPLACE = 2,
    CORRECTIVE_ACTION_DELETE = 3
}

[ComImport]
[Guid("00000101-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumString
{
    void Next(uint celt, [MarshalAs(UnmanagedType.LPWStr)] out string rgelt, out uint pceltFetched);
    void Skip(uint celt);
    void Reset();
    void Clone(out IEnumString ppenum);
}

internal static class IEnumStringExtensions
{
    public static void Next(this IEnumString enumString, out string? value)
    {
        try
        {
            enumString.Next(1, out var result, out var fetched);
            value = fetched > 0 ? result : null;
        }
        catch
        {
            value = null;
        }
    }
}

#endregion
