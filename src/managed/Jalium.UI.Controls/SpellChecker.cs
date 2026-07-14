using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides spell checking functionality using the Windows Spell Checking API.
/// </summary>
/// <remarks>
/// NativeAOT ships with no built-in COM interop, so activating the classic <c>SpellCheckerFactory</c>
/// coclass through a runtime-callable wrapper throws <see cref="NotSupportedException"/> ("Built-in
/// COM has been disabled"). The broad <c>try</c>/<c>catch</c> here used to swallow that, silently
/// disabling spell-check in every PublishAot build. This type now activates the factory with
/// <c>CoCreateInstance</c> and dispatches every member through raw <c>delegate* unmanaged[Stdcall]</c>
/// vtable slots (see <see cref="SpellCheckComInterop"/>), exactly like the shell file dialogs
/// (<c>ShellComInterop</c>) and the OLE drag/drop interop. Every COM object is held as a raw
/// <see langword="nint"/> and released with <see cref="Marshal.Release(nint)"/> — never
/// <see cref="Marshal.ReleaseComObject"/>, which is a silent no-op under AOT.
/// </remarks>
internal sealed class SpellChecker : IDisposable
{
    // The native ISpellChecker held as a raw COM pointer (0 when spell-check is unavailable),
    // released exactly once with Marshal.Release in Dispose.
    private nint _spellChecker;
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

        if (!OperatingSystem.IsWindows())
        {
            // The Windows Spell Checking API is unavailable off-Windows; spell-check stays disabled.
            return;
        }

        nint factory = 0;
        try
        {
            factory = SpellCheckComInterop.CreateFactory();
            if (SpellCheckComInterop.IsSupported(factory, language))
            {
                _spellChecker = SpellCheckComInterop.CreateSpellChecker(factory, language);
            }
        }
        catch
        {
            // Spell checking not available (service missing/unregistered, unsupported language, or
            // activation failure). Fail soft to disabled — IsAvailable stays false.
            if (_spellChecker != 0)
            {
                SpellCheckComInterop.Release(_spellChecker);
                _spellChecker = 0;
            }
        }
        finally
        {
            SpellCheckComInterop.Release(factory);
        }
    }

    /// <summary>
    /// Gets the language of this spell checker.
    /// </summary>
    public string Language => _language;

    /// <summary>
    /// Gets whether spell checking is available.
    /// </summary>
    public bool IsAvailable => _spellChecker != 0;

    /// <summary>
    /// Checks the spelling of the given text and returns spelling errors.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>A list of spelling errors.</returns>
    public IReadOnlyList<SpellingError> Check(string text)
    {
        var errors = new List<SpellingError>();

        if (_spellChecker == 0 || string.IsNullOrEmpty(text))
            return errors;

        nint enumErrors = 0;
        try
        {
            enumErrors = SpellCheckComInterop.Check(_spellChecker, text);
            if (enumErrors != 0)
            {
                while (true)
                {
                    nint error = SpellCheckComInterop.EnumSpellingErrorNext(enumErrors);
                    if (error == 0)
                        break;

                    try
                    {
                        var startIndex = SpellCheckComInterop.SpellingErrorStartIndex(error);
                        var length = SpellCheckComInterop.SpellingErrorLength(error);
                        var action = SpellCheckComInterop.SpellingErrorCorrectiveAction(error);

                        var misspelledWord = text.Substring((int)startIndex, (int)length);
                        var suggestions = GetSuggestions(misspelledWord);

                        string? replacement = null;
                        if (action == (int)CORRECTIVE_ACTION.CORRECTIVE_ACTION_REPLACE)
                        {
                            replacement = SpellCheckComInterop.SpellingErrorReplacement(error);
                        }

                        errors.Add(new SpellingError(
                            (int)startIndex,
                            (int)length,
                            misspelledWord,
                            (SpellingErrorType)action,
                            replacement,
                            suggestions));
                    }
                    finally
                    {
                        SpellCheckComInterop.Release(error);
                    }
                }
            }
        }
        catch
        {
            // Ignore errors — fail soft, returning whatever was collected before the fault.
        }
        finally
        {
            SpellCheckComInterop.Release(enumErrors);
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

        if (_spellChecker == 0 || string.IsNullOrEmpty(word))
            return suggestions;

        nint enumSuggestions = 0;
        try
        {
            enumSuggestions = SpellCheckComInterop.Suggest(_spellChecker, word);
            if (enumSuggestions != 0)
            {
                while (true)
                {
                    var suggestion = SpellCheckComInterop.EnumStringNext(enumSuggestions);
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
        finally
        {
            SpellCheckComInterop.Release(enumSuggestions);
        }

        return suggestions;
    }

    /// <summary>
    /// Adds a word to the ignore list for this session.
    /// </summary>
    /// <param name="word">The word to ignore.</param>
    public void IgnoreWord(string word)
    {
        if (_spellChecker != 0)
            SpellCheckComInterop.Ignore(_spellChecker, word);
    }

    /// <summary>
    /// Adds a word to the user dictionary.
    /// </summary>
    /// <param name="word">The word to add.</param>
    public void AddToDictionary(string word)
    {
        if (_spellChecker != 0)
            SpellCheckComInterop.Add(_spellChecker, word);
    }

    /// <summary>
    /// Sets an autocorrect pair.
    /// </summary>
    /// <param name="from">The word to replace.</param>
    /// <param name="to">The replacement word.</param>
    public void SetAutoCorrect(string from, string to)
    {
        if (_spellChecker != 0)
            SpellCheckComInterop.AutoCorrect(_spellChecker, from, to);
    }

    /// <summary>
    /// Checks if a single word is spelled correctly.
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <returns>True if spelled correctly; otherwise, false.</returns>
    public bool IsSpelledCorrectly(string word)
    {
        if (_spellChecker == 0 || string.IsNullOrEmpty(word))
            return true;

        var errors = Check(word);
        return errors.Count == 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_spellChecker != 0)
            {
                SpellCheckComInterop.Release(_spellChecker);
                _spellChecker = 0;
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
    private readonly Action<SpellingError, string>? _correct;
    private readonly Action<SpellingError>? _ignoreAll;
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
    public IEnumerable<string> Suggestions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpellingError"/> class.
    /// </summary>
    public SpellingError(int startIndex, int length, string word, SpellingErrorType errorType,
        string? replacement, IReadOnlyList<string> suggestions)
        : this(startIndex, length, word, errorType, replacement, suggestions, null, null)
    {
    }

    private SpellingError(
        int startIndex,
        int length,
        string word,
        SpellingErrorType errorType,
        string? replacement,
        IReadOnlyList<string> suggestions,
        Action<SpellingError, string>? correct,
        Action<SpellingError>? ignoreAll)
    {
        StartIndex = startIndex;
        Length = length;
        Word = word;
        ErrorType = errorType;
        Replacement = replacement;
        Suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
        _correct = correct;
        _ignoreAll = ignoreAll;
    }

    /// <summary>Replaces this misspelling with the supplied text.</summary>
    public void Correct(string correctedText)
    {
        ArgumentNullException.ThrowIfNull(correctedText);
        _correct?.Invoke(this, correctedText);
    }

    /// <summary>Ignores every occurrence of this misspelled word for the current session.</summary>
    public void IgnoreAll()
    {
        if (_ignoreAll != null)
        {
            _ignoreAll(this);
        }
        else
        {
            SpellChecker.Default?.IgnoreWord(Word);
        }
    }

    internal SpellingError WithHandlers(
        Action<SpellingError, string> correct,
        Action<SpellingError> ignoreAll) =>
        new(
            StartIndex,
            Length,
            Word,
            ErrorType,
            Replacement,
            Suggestions.ToArray(),
            correct,
            ignoreAll);
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

/// <summary>
/// Maps to the native <c>CORRECTIVE_ACTION</c> enum (spellcheck.h). The values match
/// <see cref="SpellingErrorType"/> one-for-one, so an <see cref="ISpellingError"/> corrective action
/// casts straight across.
/// </summary>
internal enum CORRECTIVE_ACTION
{
    CORRECTIVE_ACTION_NONE = 0,
    CORRECTIVE_ACTION_GET_SUGGESTIONS = 1,
    CORRECTIVE_ACTION_REPLACE = 2,
    CORRECTIVE_ACTION_DELETE = 3
}

/// <summary>
/// NativeAOT-safe interop for the Windows Spell Checking API
/// (ISpellCheckerFactory / ISpellChecker / IEnumSpellingError / ISpellingError / IEnumString).
/// </summary>
/// <remarks>
/// <para>
/// NativeAOT has no built-in COM interop: instantiating the classic <c>SpellCheckerFactory</c>
/// coclass and dispatching through a runtime-synthesised RCW throws
/// <see cref="NotSupportedException"/> ("Built-in COM has been disabled") — the same wall the file
/// dialogs hit. This helper follows the pattern already shipping in <c>ShellComInterop</c>,
/// <see cref="OleDropTarget"/>, and the notification backend: <c>CoCreateInstance</c> to a raw
/// <see langword="nint"/> followed by <c>delegate* unmanaged[Stdcall]</c> vtable-slot calls. Every
/// COM reference is released with <see cref="Marshal.Release(nint)"/> (never
/// <see cref="Marshal.ReleaseComObject"/>, a silent no-op under AOT because
/// <see cref="Marshal.IsComObject"/> is always false there).
/// </para>
/// <para>
/// Unlike the file dialogs' <c>IFileDialog</c> (whose members are <c>PreserveSig</c> in the IDL), the
/// spell-check interfaces declare their members returning <c>HRESULT</c> with <c>[out, retval]</c>
/// results — under a classic RCW the runtime auto-throws on a failing HRESULT. Hand-rolled slots lose
/// that, so every call here inspects the returned <c>int</c> HRESULT and routes it through
/// <see cref="CheckHResult"/>.
/// </para>
/// <para>
/// Vtable slots are zero-based from IUnknown (QueryInterface=0, AddRef=1, Release=2); each interface
/// derives directly from IUnknown, so its own members begin at slot 3. The orders below match the
/// declared member order in the shipping JIT interfaces this file replaced (which — because that code
/// ran under a real RCW — equals the native spellcheck.h vtable order).
/// </para>
/// </remarks>
internal static unsafe class SpellCheckComInterop
{
    #region Constants

    private const uint CLSCTX_INPROC_SERVER = 1;

    private static readonly Guid CLSID_SpellCheckerFactory = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
    private static readonly Guid IID_ISpellCheckerFactory = new("8E018A9D-2415-4677-BF08-794EA61F94BB");

    // ISpellCheckerFactory vtable slots.
    private const int VT_Factory_IsSupported = 4;         // [3]=get_SupportedLanguages (unused)
    private const int VT_Factory_CreateSpellChecker = 5;

    // ISpellChecker vtable slots ([3]=get_LanguageTag, unused).
    private const int VT_SpellChecker_Check = 4;
    private const int VT_SpellChecker_Suggest = 5;
    private const int VT_SpellChecker_Add = 6;
    private const int VT_SpellChecker_Ignore = 7;
    private const int VT_SpellChecker_AutoCorrect = 8;

    // IEnumSpellingError vtable slots.
    private const int VT_EnumSpellingError_Next = 3;

    // ISpellingError vtable slots.
    private const int VT_SpellingError_get_StartIndex = 3;
    private const int VT_SpellingError_get_Length = 4;
    private const int VT_SpellingError_get_CorrectiveAction = 5;
    private const int VT_SpellingError_get_Replacement = 6;

    // IEnumString vtable slots.
    private const int VT_EnumString_Next = 3;

    #endregion

    #region Activation

    /// <summary>Creates an ISpellCheckerFactory; the returned pointer is owned and must be released.</summary>
    internal static nint CreateFactory()
    {
        Guid clsid = CLSID_SpellCheckerFactory;
        Guid iid = IID_ISpellCheckerFactory;
        CheckHResult(CoCreateInstance(in clsid, 0, CLSCTX_INPROC_SERVER, in iid, out var factory));
        return factory;
    }

    #endregion

    #region ISpellCheckerFactory members

    /// <summary>ISpellCheckerFactory::IsSupported([in] LPCWSTR languageTag, [out] BOOL* value).</summary>
    internal static bool IsSupported(nint factory, string languageTag)
    {
        nint pLang = Marshal.StringToCoTaskMemUni(languageTag);
        try
        {
            int supported = 0;
            CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint, int*, int>)Slot(factory, VT_Factory_IsSupported))(factory, pLang, &supported));
            return supported != 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pLang);
        }
    }

    /// <summary>
    /// ISpellCheckerFactory::CreateSpellChecker([in] LPCWSTR languageTag, [out] ISpellChecker** value).
    /// Returns an owned ISpellChecker pointer.
    /// </summary>
    internal static nint CreateSpellChecker(nint factory, string languageTag)
    {
        nint pLang = Marshal.StringToCoTaskMemUni(languageTag);
        try
        {
            nint checker = 0;
            CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(factory, VT_Factory_CreateSpellChecker))(factory, pLang, &checker));
            return checker;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pLang);
        }
    }

    #endregion

    #region ISpellChecker members

    /// <summary>
    /// ISpellChecker::Check([in] LPCWSTR text, [out] IEnumSpellingError** value).
    /// Returns an owned IEnumSpellingError pointer.
    /// </summary>
    internal static nint Check(nint spellChecker, string text)
    {
        nint pText = Marshal.StringToCoTaskMemUni(text);
        try
        {
            nint enumErrors = 0;
            CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(spellChecker, VT_SpellChecker_Check))(spellChecker, pText, &enumErrors));
            return enumErrors;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pText);
        }
    }

    /// <summary>
    /// ISpellChecker::Suggest([in] LPCWSTR word, [out] IEnumString** value).
    /// Returns an owned IEnumString pointer (S_FALSE — a correctly spelled word — is still success).
    /// </summary>
    internal static nint Suggest(nint spellChecker, string word)
    {
        nint pWord = Marshal.StringToCoTaskMemUni(word);
        try
        {
            nint enumSuggestions = 0;
            CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(spellChecker, VT_SpellChecker_Suggest))(spellChecker, pWord, &enumSuggestions));
            return enumSuggestions;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pWord);
        }
    }

    /// <summary>ISpellChecker::Add([in] LPCWSTR word).</summary>
    internal static void Add(nint spellChecker, string word) => InvokeInStr(spellChecker, VT_SpellChecker_Add, word);

    /// <summary>ISpellChecker::Ignore([in] LPCWSTR word).</summary>
    internal static void Ignore(nint spellChecker, string word) => InvokeInStr(spellChecker, VT_SpellChecker_Ignore, word);

    /// <summary>ISpellChecker::AutoCorrect([in] LPCWSTR from, [in] LPCWSTR to).</summary>
    internal static void AutoCorrect(nint spellChecker, string from, string to)
    {
        nint pFrom = Marshal.StringToCoTaskMemUni(from);
        nint pTo = Marshal.StringToCoTaskMemUni(to);
        try
        {
            CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(spellChecker, VT_SpellChecker_AutoCorrect))(spellChecker, pFrom, pTo));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pFrom);
            Marshal.FreeCoTaskMem(pTo);
        }
    }

    #endregion

    #region Enumerator members

    /// <summary>
    /// IEnumSpellingError::Next([out] ISpellingError** value). Returns an owned ISpellingError
    /// pointer, or 0 at the end of the enumeration (S_FALSE, a non-failing HRESULT with a null out).
    /// </summary>
    internal static nint EnumSpellingErrorNext(nint enumErrors)
    {
        nint error = 0;
        CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(enumErrors, VT_EnumSpellingError_Next))(enumErrors, &error));
        return error;
    }

    /// <summary>
    /// IEnumString::Next(1, [out] LPWSTR* rgelt, [out] ULONG* pceltFetched). Returns the next string
    /// (CoTaskMem-allocated by the callee, freed here), or null when the enumeration is exhausted.
    /// </summary>
    internal static string? EnumStringNext(nint enumString)
    {
        nint pStr = 0;
        uint fetched = 0;
        CheckHResult(((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)Slot(enumString, VT_EnumString_Next))(enumString, 1, &pStr, &fetched));
        if (fetched == 0)
        {
            // On S_FALSE the callee should not have written a string, but free defensively if it did.
            if (pStr != 0)
                Marshal.FreeCoTaskMem(pStr);
            return null;
        }

        return PtrToStringAndFree(pStr);
    }

    #endregion

    #region ISpellingError members

    /// <summary>ISpellingError::get_StartIndex([out] ULONG* value).</summary>
    internal static uint SpellingErrorStartIndex(nint error)
    {
        uint value = 0;
        CheckHResult(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(error, VT_SpellingError_get_StartIndex))(error, &value));
        return value;
    }

    /// <summary>ISpellingError::get_Length([out] ULONG* value).</summary>
    internal static uint SpellingErrorLength(nint error)
    {
        uint value = 0;
        CheckHResult(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(error, VT_SpellingError_get_Length))(error, &value));
        return value;
    }

    /// <summary>ISpellingError::get_CorrectiveAction([out] CORRECTIVE_ACTION* value) — returned as its raw int.</summary>
    internal static int SpellingErrorCorrectiveAction(nint error)
    {
        int value = 0;
        CheckHResult(((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(error, VT_SpellingError_get_CorrectiveAction))(error, &value));
        return value;
    }

    /// <summary>
    /// ISpellingError::get_Replacement([out] LPWSTR* value). The out string is CoTaskMem-allocated by
    /// the callee and freed here; returns null when there is no replacement.
    /// </summary>
    internal static string? SpellingErrorReplacement(nint error)
    {
        nint pStr = 0;
        CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(error, VT_SpellingError_get_Replacement))(error, &pStr));
        return PtrToStringAndFree(pStr);
    }

    #endregion

    #region Vtable dispatch primitives

    /// <summary>Releases a COM interface pointer via IUnknown::Release (slot 2).</summary>
    internal static void Release(nint pUnk)
    {
        if (pUnk != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(pUnk, 2))(pUnk);
        }
    }

    internal static void CheckHResult(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static nint Slot(nint pThis, int index) => ((nint*)*(nint*)pThis)[index];

    /// <summary>Invokes a slot taking a single <c>[in] LPCWSTR</c> and returning an HRESULT.</summary>
    private static void InvokeInStr(nint pThis, int slot, string value)
    {
        nint pStr = Marshal.StringToCoTaskMemUni(value);
        try
        {
            CheckHResult(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(pThis, slot))(pThis, pStr));
        }
        finally
        {
            Marshal.FreeCoTaskMem(pStr);
        }
    }

    /// <summary>Copies a CoTaskMem-allocated LPWSTR into a managed string and frees the native buffer.</summary>
    private static string? PtrToStringAndFree(nint pStr)
    {
        if (pStr == 0)
            return null;

        try
        {
            return Marshal.PtrToStringUni(pStr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pStr);
        }
    }

    #endregion

    #region Native methods

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    #endregion
}

#endregion
