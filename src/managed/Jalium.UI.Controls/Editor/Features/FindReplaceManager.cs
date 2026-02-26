using System.Text.RegularExpressions;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Result of a find operation.
/// </summary>
public readonly record struct FindResult(int Offset, int Length);

/// <summary>
/// Manages find and replace operations on a TextDocument.
/// </summary>
public sealed class FindReplaceManager
{
    private TextDocument? _document;
    private string _searchText = string.Empty;
    private bool _caseSensitive;
    private bool _wholeWord;
    private bool _useRegex;
    private List<FindResult> _results = [];
    private int _currentIndex = -1;

    /// <summary>
    /// Gets or sets the document to search.
    /// </summary>
    public TextDocument? Document
    {
        get => _document;
        set
        {
            if (_document == value)
                return;

            if (_document != null)
                _document.Changed -= OnDocumentChanged;

            _document = value;

            if (_document != null)
                _document.Changed += OnDocumentChanged;

            _results.Clear();
            _currentIndex = -1;
        }
    }

    /// <summary>
    /// Gets or sets the search text.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; _results.Clear(); _currentIndex = -1; }
    }

    /// <summary>
    /// Gets or sets whether the search is case-sensitive.
    /// </summary>
    public bool CaseSensitive
    {
        get => _caseSensitive;
        set { _caseSensitive = value; _results.Clear(); _currentIndex = -1; }
    }

    /// <summary>
    /// Gets or sets whether to match whole words only.
    /// </summary>
    public bool WholeWord
    {
        get => _wholeWord;
        set { _wholeWord = value; _results.Clear(); _currentIndex = -1; }
    }

    /// <summary>
    /// Gets or sets whether to use regex search.
    /// </summary>
    public bool UseRegex
    {
        get => _useRegex;
        set { _useRegex = value; _results.Clear(); _currentIndex = -1; }
    }

    /// <summary>
    /// Gets the current search results.
    /// </summary>
    public IReadOnlyList<FindResult> Results => _results;

    /// <summary>
    /// Gets the index of the current result.
    /// </summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>
    /// Gets the current result, or null if no match.
    /// </summary>
    public FindResult? CurrentResult =>
        _currentIndex >= 0 && _currentIndex < _results.Count ? _results[_currentIndex] : null;

    /// <summary>
    /// Performs the search and populates results.
    /// </summary>
    public void FindAll()
    {
        _results.Clear();
        _currentIndex = -1;

        if (_document == null || string.IsNullOrEmpty(_searchText))
            return;

        var text = _document.Text;

        if (_useRegex)
        {
            try
            {
                var options = _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                var regex = new Regex(_searchText, options);
                foreach (Match match in regex.Matches(text))
                {
                    if (match.Length > 0)
                        _results.Add(new FindResult(match.Index, match.Length));
                }
            }
            catch (RegexParseException)
            {
                // Invalid regex; return no results
            }
        }
        else
        {
            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int pos = 0;
            while (pos < text.Length)
            {
                int found = text.IndexOf(_searchText, pos, comparison);
                if (found < 0) break;

                if (_wholeWord && !IsWholeWord(text, found, _searchText.Length))
                {
                    pos = found + 1;
                    continue;
                }

                _results.Add(new FindResult(found, _searchText.Length));
                pos = found + _searchText.Length;
            }
        }

        if (_results.Count > 0)
            _currentIndex = 0;
    }

    /// <summary>
    /// Finds the next occurrence from the given offset.
    /// </summary>
    public FindResult? FindNext(int fromOffset)
    {
        if (_results.Count == 0)
            FindAll();

        if (_results.Count == 0)
            return null;

        // Find the first result at or after fromOffset
        for (int i = 0; i < _results.Count; i++)
        {
            if (_results[i].Offset >= fromOffset)
            {
                _currentIndex = i;
                return _results[i];
            }
        }

        // Wrap around
        _currentIndex = 0;
        return _results[0];
    }

    /// <summary>
    /// Finds the previous occurrence from the given offset.
    /// </summary>
    public FindResult? FindPrevious(int fromOffset)
    {
        if (_results.Count == 0)
            FindAll();

        if (_results.Count == 0)
            return null;

        for (int i = _results.Count - 1; i >= 0; i--)
        {
            if (_results[i].Offset < fromOffset)
            {
                _currentIndex = i;
                return _results[i];
            }
        }

        // Wrap around
        _currentIndex = _results.Count - 1;
        return _results[_currentIndex];
    }

    /// <summary>
    /// Replaces the current match with the specified text.
    /// </summary>
    public bool ReplaceCurrent(string replacement)
    {
        if (_document == null || _currentIndex < 0 || _currentIndex >= _results.Count)
            return false;

        var result = _results[_currentIndex];
        _document.Replace(result.Offset, result.Length, replacement);

        // Refresh results
        FindAll();
        return true;
    }

    /// <summary>
    /// Replaces all matches with the specified text.
    /// </summary>
    public int ReplaceAll(string replacement)
    {
        if (_document == null || _results.Count == 0)
        {
            FindAll();
            if (_results.Count == 0) return 0;
        }

        int count = _results.Count;

        // Replace from end to start to preserve offsets
        _document.BeginUpdate();
        for (int i = _results.Count - 1; i >= 0; i--)
        {
            var result = _results[i];
            _document.Replace(result.Offset, result.Length, replacement);
        }
        _document.EndUpdate();

        _results.Clear();
        _currentIndex = -1;
        return count;
    }

    private static bool IsWholeWord(string text, int offset, int length)
    {
        if (offset > 0 && IsWordChar(text[offset - 1]))
            return false;
        int end = offset + length;
        if (end < text.Length && IsWordChar(text[end]))
            return false;
        return true;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void OnDocumentChanged(object? sender, TextChangeEventArgs e)
    {
        _results.Clear();
        _currentIndex = -1;
    }
}
