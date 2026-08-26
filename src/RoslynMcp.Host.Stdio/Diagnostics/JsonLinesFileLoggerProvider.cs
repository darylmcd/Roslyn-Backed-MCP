using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// Writes the enabled <see cref="ILogger"/> stream to a bounded, process-owned JSON-lines file.
/// The provider deliberately omits exception messages and stack text; callers must put only
/// operator-safe information in their formatted log message.
/// </summary>
internal sealed class JsonLinesFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private const long _defaultMaxFileBytes = 5 * 1024 * 1024;
    private static readonly UTF8Encoding _utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _writeLock = new();
    private readonly long _maxFileBytes;
    private readonly Func<DateTimeOffset> _utcNow;
    private IExternalScopeProvider? _scopeProvider;
    private bool _disposed;
    private bool _failureReported;

    public JsonLinesFileLoggerProvider(
        string logDirectory,
        long maxFileBytes = _defaultMaxFileBytes,
        int? processId = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);

        var resolvedProcessId = processId ?? Environment.ProcessId;
        var absoluteDirectory = Path.GetFullPath(logDirectory);
        FilePath = Path.Combine(absoluteDirectory, $"roslyn-mcp-{resolvedProcessId}.jsonl");
        RotatedFilePath = Path.Combine(absoluteDirectory, $"roslyn-mcp-{resolvedProcessId}.1.jsonl");
        _maxFileBytes = maxFileBytes;
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }

    internal string FilePath { get; }

    internal string RotatedFilePath { get; }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        return new JsonLinesFileLogger(this, categoryName);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _disposed = true;
        }
    }

    private void Write<TState>(
        string category,
        LogLevel level,
        EventId eventId,
        TState state,
        Func<TState, Exception?, string> formatter)
    {
        var correlationId = FindCorrelationId(state);
        var record = new JsonLinesLogRecord(
            _utcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            level.ToString(),
            category,
            eventId.Id,
            formatter(state, null),
            correlationId);
        var line = JsonSerializer.Serialize(record, _jsonOptions) + Environment.NewLine;
        var lineByteCount = _utf8WithoutBom.GetByteCount(line);

        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(FilePath)
                    ?? throw new InvalidOperationException("The observability log path has no directory.");
                Directory.CreateDirectory(directory);
                RotateIfNeeded(lineByteCount);
                File.AppendAllText(FilePath, line, _utf8WithoutBom);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportFailureOnce();
            }
        }
    }

    private string? FindCorrelationId<TState>(TState state)
    {
        var fromState = FindCorrelationIdInScope(state);
        if (fromState is not null || _scopeProvider is null)
        {
            return fromState;
        }

        var capture = new CorrelationCapture();
        _scopeProvider.ForEachScope(
            static (scope, current) =>
            {
                current.Value ??= FindCorrelationIdInScope(scope);
            },
            capture);
        return capture.Value;
    }

    private static string? FindCorrelationIdInScope(object? scope)
    {
        if (scope is not IEnumerable<KeyValuePair<string, object?>> properties)
        {
            return null;
        }

        foreach (var property in properties)
        {
            if (string.Equals(property.Key, "CorrelationId", StringComparison.OrdinalIgnoreCase) &&
                property.Value is not null)
            {
                return Convert.ToString(property.Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private void RotateIfNeeded(int pendingByteCount)
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        var currentLength = new FileInfo(FilePath).Length;
        if (currentLength == 0 || currentLength + pendingByteCount <= _maxFileBytes)
        {
            return;
        }

        File.Delete(RotatedFilePath);
        File.Move(FilePath, RotatedFilePath);
    }

    private void ReportFailureOnce()
    {
        if (_failureReported)
        {
            return;
        }

        _failureReported = true;
        try
        {
            Console.Error.WriteLine("[roslyn-mcp] JSON-lines observability file sink failed.");
        }
        catch
        {
            // A closed stderr stream leaves no secondary protocol-safe destination.
        }
    }

    private sealed class JsonLinesFileLogger(
        JsonLinesFileLoggerProvider provider,
        string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopeProvider?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(categoryName, logLevel, eventId, state, formatter);
            }
        }
    }

    private sealed class CorrelationCapture
    {
        public string? Value { get; set; }
    }

    private sealed record JsonLinesLogRecord(
        string Ts,
        string Level,
        string Category,
        int EventId,
        string Message,
        string? CorrelationId);
}
