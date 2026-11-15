using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jalium.UI.Controls;

internal static class CssHyphenator
{
    internal readonly record struct SpellingChange(
        int TailDeleteRunes,
        string TailReplace,
        int LeadDeleteRunes,
        string LeadReplace)
    {
        public static readonly SpellingChange None = new(0, string.Empty, 0, string.Empty);

        public bool IsEmpty =>
            TailDeleteRunes == 0 && string.IsNullOrEmpty(TailReplace) &&
            LeadDeleteRunes == 0 && string.IsNullOrEmpty(LeadReplace);
    }

    internal readonly record struct BreakOpportunity(
        int RuneBoundaryIndex,
        SpellingChange Change);

    private sealed record PatternRule(
        Rune[] MatchRunes,
        int[] Levels,
        int RuleOffset,
        int RuleLevel,
        int ReplaceStartOffset,
        int ReplaceCutRunes,
        string BeforeHyphen,
        string AfterHyphen);

    private sealed record ExceptionEntry(
        int[] BreakPositions,
        Dictionary<int, SpellingChange>? Changes);

    internal sealed class CompiledDictionary
    {
        private readonly Dictionary<Rune, List<PatternRule>> _patternsByFirstRune = new();
        private readonly Dictionary<string, ExceptionEntry> _exceptions = new(StringComparer.Ordinal);

        public string Key { get; }
        public int LeftHyphenMin { get; }
        public int RightHyphenMin { get; }

        private CompiledDictionary(string key, int leftMin, int rightMin)
        {
            Key = key;
            LeftHyphenMin = Math.Max(1, leftMin);
            RightHyphenMin = Math.Max(1, rightMin);
        }

        public static CompiledDictionary? TryCompile(CssHyphenationData.RawDictionary raw)
        {
            if (raw is null || string.IsNullOrWhiteSpace(raw.Key) || string.IsNullOrWhiteSpace(raw.Sha256))
                return null;
            if (!VerifyPayloadSha256(raw))
                return null;

            var compiled = new CompiledDictionary(raw.Key, raw.LeftHyphenMin, raw.RightHyphenMin);
            foreach (var rawPattern in raw.Patterns)
            {
                if (string.IsNullOrWhiteSpace(rawPattern)) continue;
                var rule = ParsePattern(rawPattern.Trim());
                if (rule is null || rule.MatchRunes.Length == 0) return null;
                var first = rule.MatchRunes[0];
                if (!compiled._patternsByFirstRune.TryGetValue(first, out var list))
                {
                    list = [];
                    compiled._patternsByFirstRune[first] = list;
                }
                list.Add(rule);
            }

            foreach (var rawException in raw.Exceptions)
            {
                if (string.IsNullOrWhiteSpace(rawException)) continue;
                var (wordKey, entry) = ParseException(rawException.Trim());
                if (wordKey.Length == 0) continue;
                compiled._exceptions[wordKey] = entry;
            }

            return compiled;
        }

        public List<BreakOpportunity> FindBreaks(ReadOnlySpan<Rune> wordRunes)
        {
            var count = wordRunes.Length;
            if (count < LeftHyphenMin + RightHyphenMin)
                return [];

            var lowered = new Rune[count];
            var wordBuilder = new StringBuilder(count);
            for (var i = 0; i < count; i++)
            {
                var lower = Rune.ToLowerInvariant(wordRunes[i]);
                lowered[i] = lower;
                wordBuilder.Append(lower.ToString());
            }
            var loweredWord = wordBuilder.ToString();

            var minPos = LeftHyphenMin;
            var maxPos = count - RightHyphenMin;
            if (minPos > maxPos) return [];

            if (_exceptions.TryGetValue(loweredWord, out var exception))
            {
                var exceptionResults = new List<BreakOpportunity>(exception.BreakPositions.Length);
                foreach (var pos in exception.BreakPositions)
                {
                    if (pos < minPos || pos > maxPos) continue;
                    var change = exception.Changes is not null && exception.Changes.TryGetValue(pos, out var c)
                        ? NormalizeSpellingChange(wordRunes, pos, c)
                        : SpellingChange.None;
                    exceptionResults.Add(new BreakOpportunity(pos, change));
                }
                return exceptionResults;
            }

            var extLen = count + 2;
            var extended = new Rune[extLen];
            extended[0] = new Rune('.');
            for (var i = 0; i < count; i++) extended[i + 1] = lowered[i];
            extended[count + 1] = new Rune('.');

            var levels = new int[count + 3];
            Dictionary<int, (int Level, SpellingChange Change)>? nonStandard = null;

            for (var start = 0; start < extLen; start++)
            {
                if (!_patternsByFirstRune.TryGetValue(extended[start], out var rules))
                    continue;
                foreach (var rule in rules)
                {
                    if (start + rule.MatchRunes.Length > extLen) continue;
                    var matched = true;
                    for (var idx = 1; idx < rule.MatchRunes.Length; idx++)
                    {
                        if (extended[start + idx] != rule.MatchRunes[idx])
                        {
                            matched = false;
                            break;
                        }
                    }
                    if (!matched) continue;

                    for (var idx = 0; idx < rule.Levels.Length; idx++)
                    {
                        var level = rule.Levels[idx];
                        if (level > levels[start + idx])
                            levels[start + idx] = level;
                    }

                    if (rule.ReplaceCutRunes > 0 && rule.RuleOffset >= 0)
                    {
                        var breakPos = start + rule.RuleOffset - 1;
                        var wordMatchStart = start - 1;
                        var replaceStart = wordMatchStart + rule.ReplaceStartOffset;
                        var tailDelete = breakPos - replaceStart;
                        var leadDelete = rule.ReplaceCutRunes - tailDelete;
                        if (breakPos >= 1 && breakPos < count &&
                            tailDelete >= 0 && leadDelete >= 0 &&
                            replaceStart >= 0 && replaceStart + rule.ReplaceCutRunes <= count)
                        {
                            nonStandard ??= new();
                            if (!nonStandard.TryGetValue(breakPos, out var prev) || rule.RuleLevel >= prev.Level)
                            {
                                nonStandard[breakPos] = (
                                    rule.RuleLevel,
                                    new SpellingChange(tailDelete, rule.BeforeHyphen, leadDelete, rule.AfterHyphen));
                            }
                        }
                    }
                }
            }

            var results = new List<BreakOpportunity>();
            for (var pos = minPos; pos <= maxPos; pos++)
            {
                var level = levels[pos + 1];
                if ((level & 1) == 0) continue;

                var change = nonStandard is not null &&
                    nonStandard.TryGetValue(pos, out var ns) &&
                    ns.Level == level
                    ? NormalizeSpellingChange(wordRunes, pos, ns.Change)
                    : SpellingChange.None;
                results.Add(new BreakOpportunity(pos, change));
            }

            return results;
        }
    }

    private static readonly Lock SyncRoot = new();
    private static Dictionary<string, CssHyphenationData.RawDictionary>? _rawByTag;
    private static readonly ConcurrentDictionary<string, CompiledDictionary?> CompiledByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CssHyphenationData.RawDictionary> TestOverrides = new(StringComparer.OrdinalIgnoreCase);

    internal static void RegisterTestDictionary(CssHyphenationData.RawDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        lock (SyncRoot)
        {
            foreach (var tag in dictionary.Tags)
                TestOverrides[NormalizeTag(tag)] = dictionary;
            CompiledByKey.Clear();
        }
    }

    internal static void ResetTestDictionaries()
    {
        lock (SyncRoot)
        {
            TestOverrides.Clear();
            CompiledByKey.Clear();
        }
    }

    internal static CompiledDictionary? ResolveDictionary(string? languageTag)
    {
        var normalized = NormalizeTag(languageTag);
        if (normalized.Length == 0) return null;

        lock (SyncRoot)
        {
            EnsureRawLookupInitialized();

            var candidate = normalized;
            while (candidate.Length > 0)
            {
                if (TestOverrides.TryGetValue(candidate, out var testRaw))
                    return CompileCached(testRaw);
                if (_rawByTag!.TryGetValue(candidate, out var raw))
                    return CompileCached(raw);

                var dash = candidate.LastIndexOf('-');
                if (dash <= 0) break;
                candidate = candidate[..dash];
            }
        }

        return null;
    }

    private static CompiledDictionary? CompileCached(CssHyphenationData.RawDictionary raw)
    {
        var cacheKey = $"{raw.Key}:{raw.Sha256}";
        return CompiledByKey.GetOrAdd(cacheKey, _ => CompiledDictionary.TryCompile(raw));
    }

    private static void EnsureRawLookupInitialized()
    {
        if (_rawByTag is not null) return;
        var map = new Dictionary<string, CssHyphenationData.RawDictionary>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in CssHyphenationData.Dictionaries)
        {
            foreach (var tag in entry.Tags)
            {
                var norm = NormalizeTag(tag);
                if (norm.Length > 0) map[norm] = entry;
            }
        }
        _rawByTag = map;
    }

    internal static string NormalizeTag(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return string.Empty;
        return languageTag.Trim().Replace('_', '-').ToLowerInvariant();
    }

    internal static bool VerifyPayloadSha256(CssHyphenationData.RawDictionary raw)
    {
        var parts = new List<string>(2 + raw.Patterns.Length + raw.Exceptions.Length)
        {
            $"{raw.LeftHyphenMin}:{raw.RightHyphenMin}"
        };
        parts.AddRange(raw.Patterns);
        parts.Add("---");
        parts.AddRange(raw.Exceptions);
        var canonical = string.Join('\n', parts);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexStringLower(hash);
        return string.Equals(hex, raw.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static PatternRule? ParsePattern(string rawPattern)
    {
        var patternPart = rawPattern;
        var replaceOffset = 0;
        var replaceCut = 0;
        var beforeHyphen = string.Empty;
        var afterHyphen = string.Empty;

        var slash = rawPattern.IndexOf('/');
        if (slash >= 0)
        {
            patternPart = rawPattern[..slash];
            var spec = rawPattern[(slash + 1)..];
            var parts = spec.Split(',');
            if (parts.Length != 3) return null;
            var repParts = parts[0].Split('=');
            if (repParts.Length != 2) return null;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos1Based) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out replaceCut) ||
                pos1Based < 1 || replaceCut < 1)
                return null;
            beforeHyphen = repParts[0];
            afterHyphen = repParts[1];
            replaceOffset = pos1Based - 1;
        }

        var runes = new List<Rune>(patternPart.Length);
        var levels = new List<int>(patternPart.Length + 1) { 0 };
        var oddRuleOffset = -1;
        var oddRuleLevel = 0;

        foreach (var rune in patternPart.EnumerateRunes())
        {
            if (rune.Value is >= '0' and <= '9')
            {
                var digit = rune.Value - '0';
                levels[^1] = digit;
                if ((digit & 1) == 1 && digit >= oddRuleLevel)
                {
                    oddRuleLevel = digit;
                    oddRuleOffset = runes.Count;
                }
            }
            else
            {
                runes.Add(Rune.ToLowerInvariant(rune));
                levels.Add(0);
            }
        }

        return new PatternRule(
            runes.ToArray(),
            levels.ToArray(),
            oddRuleOffset,
            oddRuleLevel,
            replaceOffset,
            replaceCut,
            beforeHyphen,
            afterHyphen);
    }

    private static (string WordKey, ExceptionEntry Entry) ParseException(string rawException)
    {
        var wordBuilder = new StringBuilder(rawException.Length);
        var breaks = new List<int>();
        var runeCount = 0;
        foreach (var rune in rawException.EnumerateRunes())
        {
            if (rune.Value == '-')
            {
                if (runeCount > 0) breaks.Add(runeCount);
            }
            else
            {
                wordBuilder.Append(Rune.ToLowerInvariant(rune).ToString());
                runeCount++;
            }
        }
        return (wordBuilder.ToString(), new ExceptionEntry(breaks.ToArray(), null));
    }

    private static SpellingChange NormalizeSpellingChange(
        ReadOnlySpan<Rune> wordRunes,
        int breakPos,
        SpellingChange raw)
    {
        if (raw.IsEmpty) return SpellingChange.None;

        var tailStart = breakPos - raw.TailDeleteRunes;
        var leadEnd = breakPos + raw.LeadDeleteRunes;
        if (tailStart < 0 || leadEnd > wordRunes.Length) return SpellingChange.None;

        var tailDeleted = wordRunes.Slice(tailStart, raw.TailDeleteRunes);
        var leadDeleted = wordRunes.Slice(breakPos, raw.LeadDeleteRunes);

        var tailReplace = MatchCase(raw.TailReplace, tailDeleted, wordRunes);
        var leadReplace = MatchCase(raw.LeadReplace, leadDeleted, wordRunes);

        var tailDelCount = raw.TailDeleteRunes;
        if (SpanEqualsString(tailDeleted, tailReplace))
        {
            tailDelCount = 0;
            tailReplace = string.Empty;
        }

        var leadDelCount = raw.LeadDeleteRunes;
        if (SpanEqualsString(leadDeleted, leadReplace))
        {
            leadDelCount = 0;
            leadReplace = string.Empty;
        }

        return new SpellingChange(tailDelCount, tailReplace, leadDelCount, leadReplace);
    }

    private static bool SpanEqualsString(ReadOnlySpan<Rune> runes, string text)
    {
        var idx = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (idx >= runes.Length || runes[idx] != rune) return false;
            idx++;
        }
        return idx == runes.Length;
    }

    private static string MatchCase(
        string replacement,
        ReadOnlySpan<Rune> replacedRunes,
        ReadOnlySpan<Rune> wordRunes)
    {
        if (replacement.Length == 0) return replacement;

        var allUpper = true;
        var hasLetter = false;
        foreach (var rune in replacedRunes)
        {
            if (!Rune.IsLetter(rune)) continue;
            hasLetter = true;
            if (!Rune.IsUpper(rune))
            {
                allUpper = false;
                break;
            }
        }

        if (!hasLetter)
        {
            foreach (var rune in wordRunes)
            {
                if (!Rune.IsLetter(rune)) continue;
                hasLetter = true;
                if (!Rune.IsUpper(rune))
                {
                    allUpper = false;
                    break;
                }
            }
        }

        if (hasLetter && allUpper)
            return replacement.ToUpperInvariant();

        if (replacedRunes.Length > 0 && Rune.IsUpper(replacedRunes[0]))
        {
            var sb = new StringBuilder(replacement.Length);
            var first = true;
            foreach (var rune in replacement.EnumerateRunes())
            {
                sb.Append((first ? Rune.ToUpperInvariant(rune) : rune).ToString());
                first = false;
            }
            return sb.ToString();
        }

        return replacement;
    }
}
