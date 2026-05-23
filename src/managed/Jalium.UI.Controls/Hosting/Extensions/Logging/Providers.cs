using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.Logging.Debug;

/// <summary>
/// <see cref="ILoggerProvider"/> that forwards messages to <see cref="System.Diagnostics.Debug"/>.
/// Visible in the IDE Debug Output window — not a console writer. Always shipped because it has
/// no native console dependency.
/// </summary>
public sealed class DebugLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DebugLogger(categoryName);
    public void Dispose() { }
}

internal sealed class DebugLogger : ILogger
{
    private readonly string _name;
    public DebugLogger(string name) { _name = name; }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => Debugger.IsAttached && logLevel != LogLevel.None;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null) return;
        System.Diagnostics.Debug.WriteLine($"{logLevel}: {_name}[{eventId.Id}] {message}{(exception != null ? Environment.NewLine + exception : string.Empty)}");
    }
}

public static class DebugLoggerFactoryExtensions
{
    public static ILoggingBuilder AddDebug(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddLoggerProvider<DebugLoggerProvider>();
        return builder;
    }
}
