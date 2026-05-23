namespace Jalium.Extensions.Logging.Abstractions;

public sealed class NullLogger : ILogger
{
    public static NullLogger Instance { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

public sealed class NullLogger<T> : ILogger<T>
{
    public static NullLogger<T> Instance { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

public sealed class NullLoggerFactory : ILoggerFactory
{
    public static NullLoggerFactory Instance { get; } = new();
    public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
}

public sealed class NullLoggerProvider : ILoggerProvider
{
    public static NullLoggerProvider Instance { get; } = new();
    public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
    public void Dispose() { }
}

internal sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();
    public void Dispose() { }
}
