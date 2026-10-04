using System.Text;

namespace Jalium.UI.Styling;

/// <summary>Filters CSS input code points before tokenization.</summary>
internal static class CssInputPreprocessor
{
    internal static string Filter(string text)
    {
        StringBuilder? result = null;
        int segmentStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            char replacement;
            if (current is '\r' or '\f') replacement = '\n';
            else if (current == '\0') replacement = '\uFFFD';
            else if (char.IsHighSurrogate(current))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }
                replacement = '\uFFFD';
            }
            else if (char.IsLowSurrogate(current)) replacement = '\uFFFD';
            else continue;

            result ??= new StringBuilder(text.Length);
            result.Append(text, segmentStart, i - segmentStart).Append(replacement);
            if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            segmentStart = i + 1;
        }

        if (result is null) return text;
        result.Append(text, segmentStart, text.Length - segmentStart);
        return result.ToString();
    }
}
