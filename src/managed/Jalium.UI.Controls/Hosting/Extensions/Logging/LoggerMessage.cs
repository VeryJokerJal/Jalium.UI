namespace Jalium.Extensions.Logging;

/// <summary>
/// Static factory that produces strongly-typed delegates for zero-allocation logging.
/// Mirrors <c>Microsoft.Extensions.Logging.LoggerMessage</c>. The generated delegate parses
/// the message template once and reuses the parser on every call — the only allocations
/// per call are the structured-state object's boxing of value types (use <see cref="LoggerMessageAttribute"/>
/// + the source generator for fully alloc-free hot paths).
/// </summary>
public static class LoggerMessage
{
    // ── No parameters ───────────────────────────────────────────────────────
    public static Action<ILogger, Exception?> Define(LogLevel level, EventId id, string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 0);
        return (logger, ex) =>
        {
            if (!logger.IsEnabled(level)) return;
            logger.Log(level, id, new LogValues(template), ex, LogValues.Formatter);
        };
    }

    // ── 1 parameter ─────────────────────────────────────────────────────────
    public static Action<ILogger, T1, Exception?> Define<T1>(LogLevel level, EventId id, string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 1);
        return (logger, a1, ex) =>
        {
            if (!logger.IsEnabled(level)) return;
            logger.Log(level, id, new LogValues<T1>(template, a1), ex, LogValues<T1>.Formatter);
        };
    }

    // ── 2 parameters ────────────────────────────────────────────────────────
    public static Action<ILogger, T1, T2, Exception?> Define<T1, T2>(LogLevel level, EventId id, string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 2);
        return (logger, a1, a2, ex) =>
        {
            if (!logger.IsEnabled(level)) return;
            logger.Log(level, id, new LogValues<T1, T2>(template, a1, a2), ex, LogValues<T1, T2>.Formatter);
        };
    }

    // ── 3 parameters ────────────────────────────────────────────────────────
    public static Action<ILogger, T1, T2, T3, Exception?> Define<T1, T2, T3>(LogLevel level, EventId id, string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 3);
        return (logger, a1, a2, a3, ex) =>
        {
            if (!logger.IsEnabled(level)) return;
            logger.Log(level, id, new LogValues<T1, T2, T3>(template, a1, a2, a3), ex, LogValues<T1, T2, T3>.Formatter);
        };
    }

    // ── 4 parameters ────────────────────────────────────────────────────────
    public static Action<ILogger, T1, T2, T3, T4, Exception?> Define<T1, T2, T3, T4>(LogLevel level, EventId id, string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 4);
        return (logger, a1, a2, a3, a4, ex) =>
        {
            if (!logger.IsEnabled(level)) return;
            logger.Log(level, id, new LogValues<T1, T2, T3, T4>(template, a1, a2, a3, a4), ex, LogValues<T1, T2, T3, T4>.Formatter);
        };
    }

    // ── 5 parameters ────────────────────────────────────────────────────────
    public static Action<ILogger, T1, T2, T3, T4, T5, Exception?> Define<T1, T2, T3, T4, T5>(LogLevel level, EventId id, string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 5);
        return (logger, a1, a2, a3, a4, a5, ex) =>
        {
            if (!logger.IsEnabled(level)) return;
            logger.Log(level, id, new LogValues<T1, T2, T3, T4, T5>(template, a1, a2, a3, a4, a5), ex, LogValues<T1, T2, T3, T4, T5>.Formatter);
        };
    }

    // ── 6 parameters ────────────────────────────────────────────────────────
    public static Action<ILogger, T1, T2, T3, T4, T5, T6, Exception?> Define<T1, T2, T3, T4, T5, T6>(LogLevel level, EventId id, string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 6);
        return (logger, a1, a2, a3, a4, a5, a6, ex) =>
        {
            if (!logger.IsEnabled(level)) return;
            logger.Log(level, id, new LogValues<T1, T2, T3, T4, T5, T6>(template, a1, a2, a3, a4, a5, a6), ex, LogValues<T1, T2, T3, T4, T5, T6>.Formatter);
        };
    }

    // ── BeginScope (one variant per arity) ──────────────────────────────────
    public static Func<ILogger, IDisposable?> DefineScope(string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 0);
        return logger => logger.BeginScope(new LogValues(template));
    }

    public static Func<ILogger, T1, IDisposable?> DefineScope<T1>(string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 1);
        return (logger, a1) => logger.BeginScope(new LogValues<T1>(template, a1));
    }

    public static Func<ILogger, T1, T2, IDisposable?> DefineScope<T1, T2>(string formatString)
    {
        var template = new LogTemplate(formatString, parameterCount: 2);
        return (logger, a1, a2) => logger.BeginScope(new LogValues<T1, T2>(template, a1, a2));
    }
}

/// <summary>Parsed message template — extracted once, reused across calls.</summary>
internal sealed class LogTemplate
{
    public string Format { get; }
    public string[] Names { get; }
    public int ParameterCount { get; }

    public LogTemplate(string format, int parameterCount)
    {
        Format = format;
        ParameterCount = parameterCount;
        Names = ExtractNames(format, parameterCount);
    }

    private static string[] ExtractNames(string format, int max)
    {
        if (string.IsNullOrEmpty(format) || max == 0) return Array.Empty<string>();
        var names = new List<string>(max);
        int i = 0;
        while (i < format.Length && names.Count < max)
        {
            if (format[i] == '{')
            {
                if (i + 1 < format.Length && format[i + 1] == '{') { i += 2; continue; }
                var end = format.IndexOf('}', i + 1);
                if (end < 0) break;
                var name = format.Substring(i + 1, end - i - 1);
                var colon = name.IndexOf(':');
                if (colon >= 0) name = name.Substring(0, colon);
                names.Add(name);
                i = end + 1;
            }
            else i++;
        }
        while (names.Count < max) names.Add(names.Count.ToString());
        return names.ToArray();
    }

    public string Render(ReadOnlySpan<object?> args)
    {
        if (string.IsNullOrEmpty(Format)) return string.Empty;
        if (args.Length == 0) return Format;
        var sb = new System.Text.StringBuilder(Format.Length + 32);
        int arg = 0, i = 0;
        while (i < Format.Length)
        {
            var c = Format[i];
            if (c == '{')
            {
                if (i + 1 < Format.Length && Format[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                var end = Format.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(c); i++; continue; }
                if (arg < args.Length) sb.Append(args[arg]?.ToString() ?? "(null)");
                arg++; i = end + 1;
            }
            else if (c == '}' && i + 1 < Format.Length && Format[i + 1] == '}') { sb.Append('}'); i += 2; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }
}

internal readonly struct LogValues : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<LogValues, Exception?, string> Formatter = (v, _) => v._template.Render(ReadOnlySpan<object?>.Empty);
    private readonly LogTemplate _template;
    public LogValues(LogTemplate template) { _template = template; }
    public int Count => 1;
    public KeyValuePair<string, object?> this[int index] => index == 0 ? new("{OriginalFormat}", _template.Format) : throw new IndexOutOfRangeException();
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() { yield return this[0]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => _template.Format;
}

internal readonly struct LogValues<T1> : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<LogValues<T1>, Exception?, string> Formatter = (v, _) => v.Render();
    private readonly LogTemplate _template;
    private readonly T1 _a1;
    public LogValues(LogTemplate t, T1 a1) { _template = t; _a1 = a1; }
    private string Render() => _template.Render(new object?[] { _a1 });
    public int Count => 2;
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(_template.Names[0], _a1),
        1 => new("{OriginalFormat}", _template.Format),
        _ => throw new IndexOutOfRangeException(),
    };
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => Render();
}

internal readonly struct LogValues<T1, T2> : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<LogValues<T1, T2>, Exception?, string> Formatter = (v, _) => v.Render();
    private readonly LogTemplate _t;
    private readonly T1 _a1; private readonly T2 _a2;
    public LogValues(LogTemplate t, T1 a1, T2 a2) { _t = t; _a1 = a1; _a2 = a2; }
    private string Render() => _t.Render(new object?[] { _a1, _a2 });
    public int Count => 3;
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(_t.Names[0], _a1),
        1 => new(_t.Names[1], _a2),
        2 => new("{OriginalFormat}", _t.Format),
        _ => throw new IndexOutOfRangeException(),
    };
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => Render();
}

internal readonly struct LogValues<T1, T2, T3> : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<LogValues<T1, T2, T3>, Exception?, string> Formatter = (v, _) => v.Render();
    private readonly LogTemplate _t;
    private readonly T1 _a1; private readonly T2 _a2; private readonly T3 _a3;
    public LogValues(LogTemplate t, T1 a1, T2 a2, T3 a3) { _t = t; _a1 = a1; _a2 = a2; _a3 = a3; }
    private string Render() => _t.Render(new object?[] { _a1, _a2, _a3 });
    public int Count => 4;
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(_t.Names[0], _a1),
        1 => new(_t.Names[1], _a2),
        2 => new(_t.Names[2], _a3),
        3 => new("{OriginalFormat}", _t.Format),
        _ => throw new IndexOutOfRangeException(),
    };
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => Render();
}

internal readonly struct LogValues<T1, T2, T3, T4> : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<LogValues<T1, T2, T3, T4>, Exception?, string> Formatter = (v, _) => v.Render();
    private readonly LogTemplate _t;
    private readonly T1 _a1; private readonly T2 _a2; private readonly T3 _a3; private readonly T4 _a4;
    public LogValues(LogTemplate t, T1 a1, T2 a2, T3 a3, T4 a4) { _t = t; _a1 = a1; _a2 = a2; _a3 = a3; _a4 = a4; }
    private string Render() => _t.Render(new object?[] { _a1, _a2, _a3, _a4 });
    public int Count => 5;
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(_t.Names[0], _a1),
        1 => new(_t.Names[1], _a2),
        2 => new(_t.Names[2], _a3),
        3 => new(_t.Names[3], _a4),
        4 => new("{OriginalFormat}", _t.Format),
        _ => throw new IndexOutOfRangeException(),
    };
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => Render();
}

internal readonly struct LogValues<T1, T2, T3, T4, T5> : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<LogValues<T1, T2, T3, T4, T5>, Exception?, string> Formatter = (v, _) => v.Render();
    private readonly LogTemplate _t;
    private readonly T1 _a1; private readonly T2 _a2; private readonly T3 _a3; private readonly T4 _a4; private readonly T5 _a5;
    public LogValues(LogTemplate t, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5) { _t = t; _a1 = a1; _a2 = a2; _a3 = a3; _a4 = a4; _a5 = a5; }
    private string Render() => _t.Render(new object?[] { _a1, _a2, _a3, _a4, _a5 });
    public int Count => 6;
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(_t.Names[0], _a1),
        1 => new(_t.Names[1], _a2),
        2 => new(_t.Names[2], _a3),
        3 => new(_t.Names[3], _a4),
        4 => new(_t.Names[4], _a5),
        5 => new("{OriginalFormat}", _t.Format),
        _ => throw new IndexOutOfRangeException(),
    };
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => Render();
}

internal readonly struct LogValues<T1, T2, T3, T4, T5, T6> : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<LogValues<T1, T2, T3, T4, T5, T6>, Exception?, string> Formatter = (v, _) => v.Render();
    private readonly LogTemplate _t;
    private readonly T1 _a1; private readonly T2 _a2; private readonly T3 _a3; private readonly T4 _a4; private readonly T5 _a5; private readonly T6 _a6;
    public LogValues(LogTemplate t, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6) { _t = t; _a1 = a1; _a2 = a2; _a3 = a3; _a4 = a4; _a5 = a5; _a6 = a6; }
    private string Render() => _t.Render(new object?[] { _a1, _a2, _a3, _a4, _a5, _a6 });
    public int Count => 7;
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(_t.Names[0], _a1),
        1 => new(_t.Names[1], _a2),
        2 => new(_t.Names[2], _a3),
        3 => new(_t.Names[3], _a4),
        4 => new(_t.Names[4], _a5),
        5 => new(_t.Names[5], _a6),
        6 => new("{OriginalFormat}", _t.Format),
        _ => throw new IndexOutOfRangeException(),
    };
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => Render();
}
