using Microsoft.Extensions.Logging;

namespace RoslynMcp.Tests;

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> structuredState
            ? structuredState.ToArray()
            : [];
        Entries.Add((logLevel, formatter(state, exception), exception, properties));
    }
}
