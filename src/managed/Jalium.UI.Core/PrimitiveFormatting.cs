using System.Globalization;

namespace Jalium.UI;

internal static class PrimitiveFormatting
{
    private static readonly CultureInfo InvariantEnglishUs = CultureInfo.GetCultureInfo("en-US");

    internal static (double First, double Second) ParsePair(string source, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(source, parameterName);

        var tokens = new List<string>(2);
        int index = 0;
        SkipWhitespace(source, ref index);

        while (index < source.Length)
        {
            if (source[index] == ',')
            {
                throw new FormatException($"'{source}' contains an empty numeric component.");
            }

            int start = index;
            while (index < source.Length && !char.IsWhiteSpace(source[index]) && source[index] != ',')
            {
                index++;
            }

            tokens.Add(source[start..index]);
            SkipWhitespace(source, ref index);

            if (index < source.Length && source[index] == ',')
            {
                index++;
                SkipWhitespace(source, ref index);
                if (index >= source.Length || source[index] == ',')
                {
                    throw new FormatException($"'{source}' contains an empty numeric component.");
                }
            }
        }

        if (tokens.Count != 2)
        {
            throw new FormatException($"'{source}' must contain exactly two numeric components.");
        }

        return (
            double.Parse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture),
            double.Parse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    internal static Size ParseSize(string source)
    {
        var tokenizer = new InvariantSizeTokenizer(source);
        string firstToken = tokenizer.NextTokenRequired();

        Size value;
        if (firstToken == "Empty")
        {
            value = Size.Empty;
        }
        else
        {
            value = new Size(
                Convert.ToDouble(firstToken, InvariantEnglishUs),
                Convert.ToDouble(tokenizer.NextTokenRequired(), InvariantEnglishUs));
        }

        tokenizer.LastTokenRequired();
        return value;
    }

    internal static string FormatPair(
        double first,
        double second,
        string? format,
        IFormatProvider? provider)
    {
        char separator = GetNumericListSeparator(provider);
        string componentFormat = format ?? string.Empty;
        return string.Format(
            provider,
            "{1:" + componentFormat + "}{0}{2:" + componentFormat + "}",
            separator,
            first,
            second);
    }

    internal static char GetNumericListSeparator(IFormatProvider? provider)
    {
        NumberFormatInfo numberFormat = NumberFormatInfo.GetInstance(provider);
        return numberFormat.NumberDecimalSeparator.Length > 0
            && numberFormat.NumberDecimalSeparator[0] == ','
            ? ';'
            : ',';
    }

    private static void SkipWhitespace(string source, ref int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }
    }

    private sealed class InvariantSizeTokenizer
    {
        private readonly string _source;
        private int _index;

        internal InvariantSizeTokenizer(string? source)
        {
            _source = source ?? string.Empty;
            SkipWhitespace();
        }

        internal string NextTokenRequired()
        {
            if (_index >= _source.Length)
            {
                throw new InvalidOperationException("A Size value must contain another token.");
            }

            int start = _index;
            while (_index < _source.Length
                && !char.IsWhiteSpace(_source[_index])
                && _source[_index] != ',')
            {
                _index++;
            }

            int length = _index - start;
            ScanToNextToken();

            if (length == 0)
            {
                throw new InvalidOperationException("Size text contains an empty component.");
            }

            return _source.Substring(start, length);
        }

        internal void LastTokenRequired()
        {
            if (_index != _source.Length)
            {
                throw new InvalidOperationException("Size text contains additional data.");
            }
        }

        private void ScanToNextToken()
        {
            if (_index >= _source.Length)
            {
                return;
            }

            int separatorCount = 0;
            while (_index < _source.Length)
            {
                char current = _source[_index];
                if (current == ',')
                {
                    separatorCount++;
                    _index++;
                    if (separatorCount > 1)
                    {
                        throw new InvalidOperationException("Size text contains an empty component.");
                    }
                }
                else if (char.IsWhiteSpace(current))
                {
                    _index++;
                }
                else
                {
                    break;
                }
            }

            if (separatorCount > 0 && _index >= _source.Length)
            {
                throw new InvalidOperationException("Size text contains an empty component.");
            }
        }

        private void SkipWhitespace()
        {
            while (_index < _source.Length && char.IsWhiteSpace(_source[_index]))
            {
                _index++;
            }
        }
    }
}
