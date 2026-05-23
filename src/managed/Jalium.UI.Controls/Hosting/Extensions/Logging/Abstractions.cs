using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.Logging;

/// <summary>Severity ordinal for logger filtering.</summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6,
}

public readonly struct EventId : IEquatable<EventId>
{
    public EventId(int id, string? name = null) { Id = id; Name = name; }
    public int Id { get; }
    public string? Name { get; }
    public static implicit operator EventId(int i) => new(i);
    public bool Equals(EventId other) => Id == other.Id && Name == other.Name;
    public override bool Equals(object? obj) => obj is EventId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Name);
    public override string ToString() => Name ?? Id.ToString();
}

public interface ILogger
{
    void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter);
    bool IsEnabled(LogLevel logLevel);
    IDisposable? BeginScope<TState>(TState state) where TState : notnull;
}

public interface ILogger<out TCategoryName> : ILogger { }

public interface ILoggerProvider : IDisposable
{
    ILogger CreateLogger(string categoryName);
}

public interface ILoggerFactory : IDisposable
{
    ILogger CreateLogger(string categoryName);
    void AddProvider(ILoggerProvider provider);
}

public interface ILoggingBuilder
{
    Jalium.Extensions.DependencyInjection.IServiceCollection Services { get; }
}

public interface IExternalScopeProvider
{
    void ForEachScope<TState>(Action<object?, TState> callback, TState state);
    IDisposable Push(object? state);
}

public interface ISupportExternalScope
{
    void SetScopeProvider(IExternalScopeProvider scopeProvider);
}

public interface ILoggerFilterRule { }

public sealed class LoggerFilterOptions
{
    public LogLevel MinLevel { get; set; } = LogLevel.Information;
    public bool CaptureScopes { get; set; } = true;
    public IList<LoggerFilterRule> Rules { get; } = new List<LoggerFilterRule>();
}

public sealed class LoggerFilterRule : ILoggerFilterRule
{
    public string? ProviderName { get; }
    public string? CategoryName { get; }
    public LogLevel? LogLevel { get; }
    public Func<string?, string?, LogLevel, bool>? Filter { get; }
    public LoggerFilterRule(string? providerName, string? categoryName, LogLevel? logLevel, Func<string?, string?, LogLevel, bool>? filter)
    { ProviderName = providerName; CategoryName = categoryName; LogLevel = logLevel; Filter = filter; }
}
