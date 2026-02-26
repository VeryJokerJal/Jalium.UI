using System.Text.RegularExpressions;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides automatic text formatting services.
/// </summary>
public class TextFormatter
{
    private static readonly Dictionary<string, string> _autoCorrections = new(StringComparer.OrdinalIgnoreCase)
    {
        { "teh", "the" },
        { "adn", "and" },
        { "taht", "that" },
        { "hte", "the" },
        { "youre", "you're" },
        { "dont", "don't" },
        { "cant", "can't" },
        { "wont", "won't" },
        { "didnt", "didn't" },
        { "isnt", "isn't" },
        { "wasnt", "wasn't" },
        { "shouldnt", "shouldn't" },
        { "couldnt", "couldn't" },
        { "wouldnt", "wouldn't" },
        { "im", "I'm" },
        { "ive", "I've" },
        { "ill", "I'll" },
        { "id", "I'd" },
        { "thats", "that's" },
        { "whats", "what's" },
        { "lets", "let's" },
        { "heres", "here's" },
        { "theres", "there's" },
        { "itll", "it'll" },
        { "theyre", "they're" },
        { "weve", "we've" },
        { "recieve", "receive" },
        { "beleive", "believe" },
        { "occured", "occurred" },
        { "seperate", "separate" },
        { "definately", "definitely" },
        { "accomodate", "accommodate" },
        { "occurence", "occurrence" },
        { "wierd", "weird" },
        { "untill", "until" },
        { "becuase", "because" },
        { "alot", "a lot" },
    };

    private static readonly Regex UrlPattern = new(
        @"(?<url>https?://[^\s<>""]+|www\.[^\s<>""]+\.[^\s<>""]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailPattern = new(
        @"(?<email>[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhonePattern = new(
        @"(?<phone>(?:\+?1[-.\s]?)?(?:\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4})",
        RegexOptions.Compiled);

    private static readonly Regex DatePattern = new(
        @"(?<date>\d{1,2}/\d{1,2}/\d{2,4}|\d{4}-\d{2}-\d{2})",
        RegexOptions.Compiled);

    /// <summary>
    /// Gets the default text formatter instance.
    /// </summary>
    public static TextFormatter Default { get; } = new TextFormatter();

    /// <summary>
    /// Gets or sets whether auto-capitalization is enabled.
    /// </summary>
    public bool AutoCapitalize { get; set; } = true;

    /// <summary>
    /// Gets or sets whether auto-correction is enabled.
    /// </summary>
    public bool AutoCorrect { get; set; } = true;

    /// <summary>
    /// Gets or sets whether URL detection is enabled.
    /// </summary>
    public bool DetectUrls { get; set; } = true;

    /// <summary>
    /// Gets or sets whether email detection is enabled.
    /// </summary>
    public bool DetectEmails { get; set; } = true;

    /// <summary>
    /// Gets or sets whether phone number detection is enabled.
    /// </summary>
    public bool DetectPhoneNumbers { get; set; } = true;

    /// <summary>
    /// Gets or sets whether date detection is enabled.
    /// </summary>
    public bool DetectDates { get; set; } = true;

    /// <summary>
    /// Adds a custom auto-correction rule.
    /// </summary>
    /// <param name="from">The text to replace.</param>
    /// <param name="to">The replacement text.</param>
    public void AddAutoCorrection(string from, string to)
    {
        _autoCorrections[from] = to;
    }

    /// <summary>
    /// Removes an auto-correction rule.
    /// </summary>
    /// <param name="from">The text to remove from auto-correction.</param>
    public void RemoveAutoCorrection(string from)
    {
        _autoCorrections.Remove(from);
    }

    /// <summary>
    /// Gets the auto-correction for a word, if any.
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <returns>The corrected word, or null if no correction needed.</returns>
    public string? GetAutoCorrection(string word)
    {
        if (!AutoCorrect || string.IsNullOrEmpty(word))
            return null;

        if (_autoCorrections.TryGetValue(word, out var correction))
        {
            // Preserve case
            if (char.IsUpper(word[0]))
            {
                if (word.All(char.IsUpper))
                {
                    return correction.ToUpperInvariant();
                }
                return char.ToUpper(correction[0]) + correction.Substring(1);
            }
            return correction;
        }

        return null;
    }

    /// <summary>
    /// Determines if the text at the given position should be capitalized.
    /// </summary>
    /// <param name="text">The full text.</param>
    /// <param name="position">The position to check.</param>
    /// <returns>True if the character at position should be capitalized.</returns>
    public bool ShouldCapitalize(string text, int position)
    {
        if (!AutoCapitalize || string.IsNullOrEmpty(text) || position < 0 || position >= text.Length)
            return false;

        // Don't capitalize if already uppercase
        char c = text[position];
        if (!char.IsLetter(c) || char.IsUpper(c))
            return false;

        // Capitalize at start of text
        if (position == 0)
            return true;

        // Look backwards for sentence boundary
        for (int i = position - 1; i >= 0; i--)
        {
            char prev = text[i];

            // Skip whitespace
            if (char.IsWhiteSpace(prev))
                continue;

            // Capitalize after sentence-ending punctuation
            if (prev == '.' || prev == '!' || prev == '?')
                return true;

            // Don't capitalize after other characters
            return false;
        }

        return false;
    }

    /// <summary>
    /// Applies auto-capitalization to the text at the given position.
    /// </summary>
    /// <param name="text">The text to modify.</param>
    /// <param name="position">The position to check and capitalize.</param>
    /// <returns>The modified text, or the original if no change needed.</returns>
    public string ApplyCapitalization(string text, int position)
    {
        if (ShouldCapitalize(text, position))
        {
            char[] chars = text.ToCharArray();
            chars[position] = char.ToUpper(chars[position]);
            return new string(chars);
        }
        return text;
    }

    /// <summary>
    /// Detects formatted regions in the text.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <returns>A list of detected formatted regions.</returns>
    public IReadOnlyList<FormattedRegion> DetectFormattedRegions(string text)
    {
        var regions = new List<FormattedRegion>();

        if (string.IsNullOrEmpty(text))
            return regions;

        if (DetectUrls)
        {
            foreach (Match match in UrlPattern.Matches(text))
            {
                regions.Add(new FormattedRegion(
                    match.Index, match.Length, FormattedRegionType.Url, match.Value));
            }
        }

        if (DetectEmails)
        {
            foreach (Match match in EmailPattern.Matches(text))
            {
                regions.Add(new FormattedRegion(
                    match.Index, match.Length, FormattedRegionType.Email, match.Value));
            }
        }

        if (DetectPhoneNumbers)
        {
            foreach (Match match in PhonePattern.Matches(text))
            {
                regions.Add(new FormattedRegion(
                    match.Index, match.Length, FormattedRegionType.PhoneNumber, match.Value));
            }
        }

        if (DetectDates)
        {
            foreach (Match match in DatePattern.Matches(text))
            {
                regions.Add(new FormattedRegion(
                    match.Index, match.Length, FormattedRegionType.Date, match.Value));
            }
        }

        // Sort by start index
        regions.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));

        return regions;
    }

    /// <summary>
    /// Formats newly typed text, applying auto-correction and capitalization.
    /// </summary>
    /// <param name="text">The current text.</param>
    /// <param name="insertPosition">The position where text was inserted.</param>
    /// <param name="insertedText">The inserted text.</param>
    /// <returns>A formatting result with any corrections to apply.</returns>
    public FormattingResult FormatInput(string text, int insertPosition, string insertedText)
    {
        var result = new FormattingResult();

        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(insertedText))
            return result;

        // Check for word boundary (space, punctuation)
        if (insertedText.Length == 1)
        {
            char c = insertedText[0];
            bool isWordBoundary = char.IsWhiteSpace(c) || char.IsPunctuation(c);

            if (isWordBoundary && AutoCorrect)
            {
                // Find the word before the cursor
                var wordStart = FindWordStart(text, insertPosition - 1);
                var wordEnd = insertPosition - 1;

                if (wordEnd > wordStart)
                {
                    var word = text.Substring(wordStart, wordEnd - wordStart);
                    var correction = GetAutoCorrection(word);

                    if (correction != null && correction != word)
                    {
                        result.CorrectionStart = wordStart;
                        result.CorrectionEnd = wordEnd;
                        result.CorrectionText = correction;
                    }
                }
            }

            // Check for auto-capitalization after sentence boundary
            if (AutoCapitalize && insertPosition < text.Length)
            {
                var nextCharPos = insertPosition;
                while (nextCharPos < text.Length && char.IsWhiteSpace(text[nextCharPos]))
                    nextCharPos++;

                if (nextCharPos < text.Length && ShouldCapitalize(text, nextCharPos))
                {
                    result.CapitalizePosition = nextCharPos;
                }
            }
        }

        return result;
    }

    private int FindWordStart(string text, int position)
    {
        if (position < 0 || position >= text.Length)
            return position;

        int start = position;
        while (start > 0 && char.IsLetterOrDigit(text[start - 1]))
            start--;

        return start;
    }
}

/// <summary>
/// Represents a formatted region in text.
/// </summary>
public sealed class FormattedRegion
{
    /// <summary>
    /// Gets the start index of the region.
    /// </summary>
    public int StartIndex { get; }

    /// <summary>
    /// Gets the length of the region.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the type of formatted region.
    /// </summary>
    public FormattedRegionType Type { get; }

    /// <summary>
    /// Gets the text content of the region.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedRegion"/> class.
    /// </summary>
    public FormattedRegion(int startIndex, int length, FormattedRegionType type, string text)
    {
        StartIndex = startIndex;
        Length = length;
        Type = type;
        Text = text;
    }
}

/// <summary>
/// Specifies the type of formatted region.
/// </summary>
public enum FormattedRegionType
{
    /// <summary>
    /// A URL.
    /// </summary>
    Url,

    /// <summary>
    /// An email address.
    /// </summary>
    Email,

    /// <summary>
    /// A phone number.
    /// </summary>
    PhoneNumber,

    /// <summary>
    /// A date.
    /// </summary>
    Date
}

/// <summary>
/// Represents the result of formatting input.
/// </summary>
public sealed class FormattingResult
{
    /// <summary>
    /// Gets or sets the start position of text to correct.
    /// </summary>
    public int CorrectionStart { get; set; } = -1;

    /// <summary>
    /// Gets or sets the end position of text to correct.
    /// </summary>
    public int CorrectionEnd { get; set; } = -1;

    /// <summary>
    /// Gets or sets the correction text.
    /// </summary>
    public string? CorrectionText { get; set; }

    /// <summary>
    /// Gets or sets the position to capitalize.
    /// </summary>
    public int CapitalizePosition { get; set; } = -1;

    /// <summary>
    /// Gets whether any correction is needed.
    /// </summary>
    public bool HasCorrection => CorrectionText != null;

    /// <summary>
    /// Gets whether capitalization is needed.
    /// </summary>
    public bool HasCapitalization => CapitalizePosition >= 0;
}
