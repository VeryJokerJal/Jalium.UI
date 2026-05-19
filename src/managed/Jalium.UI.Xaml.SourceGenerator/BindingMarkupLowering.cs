using System.Collections.Generic;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Compile-time split of a <c>{Binding ...}</c> markup-extension string into its
/// positional path + <c>name=value</c> pairs. This is the build-time half of binding
/// lowering: it does exactly the string work the runtime
/// <c>MarkupExtensionParser.TryParse</c> envelope + <c>SplitParameters</c> +
/// <c>CreateBindingExtension</c> positional rule would do, and nothing more — the runtime
/// trampoline (<c>XamlBuilder.SetCompiledBinding</c> →
/// <c>MarkupExtensionParser.BuildBindingExtension</c>) reconstructs the
/// <c>BindingExtension</c> through the verbatim <c>SetBindingParameter</c> and applies it
/// via the same path a runtime-parsed binding uses. So every <c>{Binding}</c> — including
/// Converter / Source / RelativeSource / ConverterParameter / nested markup — is lowered
/// with byte-identical behaviour; only the per-element string parse moves to build time.
/// <para>
/// The split algorithm here is a deliberate line-for-line mirror of the runtime
/// <c>SplitParameters</c> (brace-depth + quote aware, comma at depth 0). Keeping the two
/// in lockstep is a hard correctness requirement — divergence would mean an SG-compiled
/// binding parses differently from a runtime-parsed one.
/// </para>
/// </summary>
internal static class BindingMarkupLowering
{
    /// <summary>
    /// Returns true and the structured split when <paramref name="rawValue"/> is a
    /// <c>{Binding ...}</c> / <c>{Binding}</c> markup extension. False for anything that is
    /// not a binding (the caller then keeps its existing handling — literal / other markup
    /// extension / runtime <c>SetProperty</c>).
    /// </summary>
    public static bool TryLower(
        string? rawValue,
        out string? positionalPath,
        out string[] names,
        out string[] values)
    {
        positionalPath = null;
        names = System.Array.Empty<string>();
        values = System.Array.Empty<string>();

        if (string.IsNullOrEmpty(rawValue))
            return false;

        // Mirror MarkupExtensionParser.TryParse envelope.
        var trimmed = rawValue!.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            return false;

        var content = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (content.Length == 0)
            return false;

        var spaceIndex = content.IndexOf(' ');
        var extensionName = spaceIndex >= 0 ? content.Substring(0, spaceIndex) : content;
        var parameters = spaceIndex >= 0 ? content.Substring(spaceIndex + 1).Trim() : string.Empty;

        // CreateMarkupExtension strips a leading "x:" then switches on the lowercased name.
        if (extensionName.StartsWith("x:", System.StringComparison.Ordinal))
            extensionName = extensionName.Substring(2);
        if (!string.Equals(extensionName.ToLowerInvariant(), "binding", System.StringComparison.Ordinal))
            return false;

        // {Binding} with no parameters — CreateBindingExtension early-returns an empty
        // BindingExtension; nothing to split.
        if (parameters.Length == 0)
            return true;

        var nameList = new List<string>();
        var valueList = new List<string>();
        foreach (var part in SplitParameters(parameters))
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex < 0)
            {
                // Positional parameter — first/only is Path (runtime: last assignment wins).
                positionalPath = part.Trim();
            }
            else
            {
                nameList.Add(part.Substring(0, equalsIndex).Trim());
                valueList.Add(part.Substring(equalsIndex + 1).Trim());
            }
        }

        names = nameList.ToArray();
        values = valueList.ToArray();
        return true;
    }

    /// <summary>
    /// Byte-for-byte mirror of <c>MarkupExtensionParser.SplitParameters</c>: split on
    /// commas at brace-depth 0 and outside quotes, so a nested
    /// <c>Converter={StaticResource X}</c> or <c>RelativeSource={RelativeSource ...}</c>
    /// stays a single part. MUST stay identical to the runtime implementation.
    /// </summary>
    private static List<string> SplitParameters(string parameters)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var braceDepth = 0;
        var inQuote = false;

        foreach (var c in parameters)
        {
            if (c == '\'' || c == '"')
            {
                inQuote = !inQuote;
                current.Append(c);
            }
            else if (!inQuote && c == '{')
            {
                braceDepth++;
                current.Append(c);
            }
            else if (!inQuote && c == '}')
            {
                braceDepth--;
                current.Append(c);
            }
            else if (!inQuote && braceDepth == 0 && c == ',')
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString().Trim());
        }

        return result;
    }
}
