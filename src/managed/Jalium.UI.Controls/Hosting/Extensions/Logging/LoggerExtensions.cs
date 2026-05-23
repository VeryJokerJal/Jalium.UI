using System.Diagnostics;

namespace Jalium.Extensions.Logging;

/// <summary>
/// Strongly-typed structured-logging helpers. Mirror the MS <c>LoggerExtensions</c> surface
/// (LogTrace / LogDebug / LogInformation / LogWarning / LogError / LogCritical) with the
/// same overload set so call sites need no change.
/// </summary>
public static class LoggerExtensions
{
    // ── LogTrace ─────────────────────────────────────────────────────────────
    public static void LogTrace(this ILogger logger, string? message, params object?[] args) => logger.Log(LogLevel.Trace, 0, null, message, args);
    public static void LogTrace(this ILogger logger, EventId eventId, string? message, params object?[] args) => logger.Log(LogLevel.Trace, eventId, null, message, args);
    public static void LogTrace(this ILogger logger, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Trace, 0, exception, message, args);
    public static void LogTrace(this ILogger logger, EventId eventId, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Trace, eventId, exception, message, args);

    public static void LogDebug(this ILogger logger, string? message, params object?[] args) => logger.Log(LogLevel.Debug, 0, null, message, args);
    public static void LogDebug(this ILogger logger, EventId eventId, string? message, params object?[] args) => logger.Log(LogLevel.Debug, eventId, null, message, args);
    public static void LogDebug(this ILogger logger, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Debug, 0, exception, message, args);
    public static void LogDebug(this ILogger logger, EventId eventId, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Debug, eventId, exception, message, args);

    public static void LogInformation(this ILogger logger, string? message, params object?[] args) => logger.Log(LogLevel.Information, 0, null, message, args);
    public static void LogInformation(this ILogger logger, EventId eventId, string? message, params object?[] args) => logger.Log(LogLevel.Information, eventId, null, message, args);
    public static void LogInformation(this ILogger logger, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Information, 0, exception, message, args);
    public static void LogInformation(this ILogger logger, EventId eventId, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Information, eventId, exception, message, args);

    public static void LogWarning(this ILogger logger, string? message, params object?[] args) => logger.Log(LogLevel.Warning, 0, null, message, args);
    public static void LogWarning(this ILogger logger, EventId eventId, string? message, params object?[] args) => logger.Log(LogLevel.Warning, eventId, null, message, args);
    public static void LogWarning(this ILogger logger, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Warning, 0, exception, message, args);
    public static void LogWarning(this ILogger logger, EventId eventId, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Warning, eventId, exception, message, args);

    public static void LogError(this ILogger logger, string? message, params object?[] args) => logger.Log(LogLevel.Error, 0, null, message, args);
    public static void LogError(this ILogger logger, EventId eventId, string? message, params object?[] args) => logger.Log(LogLevel.Error, eventId, null, message, args);
    public static void LogError(this ILogger logger, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Error, 0, exception, message, args);
    public static void LogError(this ILogger logger, EventId eventId, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Error, eventId, exception, message, args);

    public static void LogCritical(this ILogger logger, string? message, params object?[] args) => logger.Log(LogLevel.Critical, 0, null, message, args);
    public static void LogCritical(this ILogger logger, EventId eventId, string? message, params object?[] args) => logger.Log(LogLevel.Critical, eventId, null, message, args);
    public static void LogCritical(this ILogger logger, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Critical, 0, exception, message, args);
    public static void LogCritical(this ILogger logger, EventId eventId, Exception? exception, string? message, params object?[] args) => logger.Log(LogLevel.Critical, eventId, exception, message, args);

    public static void Log(this ILogger logger, LogLevel logLevel, string? message, params object?[] args) => logger.Log(logLevel, 0, null, message, args);
    public static void Log(this ILogger logger, LogLevel logLevel, EventId eventId, string? message, params object?[] args) => logger.Log(logLevel, eventId, null, message, args);
    public static void Log(this ILogger logger, LogLevel logLevel, Exception? exception, string? message, params object?[] args) => logger.Log(logLevel, 0, exception, message, args);

    public static void Log(this ILogger logger, LogLevel logLevel, EventId eventId, Exception? exception, string? message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (!logger.IsEnabled(logLevel)) return;
        var values = new FormattedLogValues(message, args);
        logger.Log(logLevel, eventId, values, exception, FormattedLogValues.Formatter);
    }

    public static IDisposable? BeginScope(this ILogger logger, string messageFormat, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.BeginScope(new FormattedLogValues(messageFormat, args));
    }
}

/// <summary>
/// Captures a message template + positional args as a structured payload. Iterating it
/// yields the parsed name/value pairs, which providers can pick up for structured sinks
/// (OTel / Serilog / etc.). <see cref="ToString"/> renders the formatted message.
/// </summary>
public readonly struct FormattedLogValues : IReadOnlyList<KeyValuePair<string, object?>>
{
    public static readonly Func<FormattedLogValues, Exception?, string> Formatter = (v, _) => v.ToString();
    private readonly string? _format;
    private readonly object?[] _args;
    private readonly string[] _names;

    public FormattedLogValues(string? format, params object?[] args)
    {
        _format = format ?? string.Empty;
        _args = args ?? Array.Empty<object?>();
        _names = ParseNames(_format, _args.Length);
    }

    public int Count => _args.Length + 1;
    public KeyValuePair<string, object?> this[int index]
    {
        get
        {
            if (index == _args.Length) return new("{OriginalFormat}", _format);
            return new(_names.Length > index ? _names[index] : index.ToString(), _args[index]);
        }
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        if (string.IsNullOrEmpty(_format)) return string.Empty;
        if (_args.Length == 0) return _format!;
        // Replace each {name}/{0}/etc. with the positional arg.
        var sb = new System.Text.StringBuilder(_format!.Length + 32);
        int arg = 0, i = 0;
        while (i < _format.Length)
        {
            var c = _format[i];
            if (c == '{')
            {
                if (i + 1 < _format.Length && _format[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                var end = _format.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(c); i++; continue; }
                if (arg < _args.Length) sb.Append(_args[arg]?.ToString() ?? "(null)");
                arg++; i = end + 1;
            }
            else if (c == '}' && i + 1 < _format.Length && _format[i + 1] == '}') { sb.Append('}'); i += 2; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    private static string[] ParseNames(string? format, int max)
    {
        if (string.IsNullOrEmpty(format)) return Array.Empty<string>();
        var names = new List<string>();
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
        return names.ToArray();
    }
}

/// <summary>Marker attribute used by external source-generators — kept for API parity, no behavior.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LoggerMessageAttribute : Attribute
{
    public int EventId { get; set; }
    public string? EventName { get; set; }
    public LogLevel Level { get; set; } = LogLevel.Information;
    public string? Message { get; set; }
    public bool SkipEnabledCheck { get; set; }
}
