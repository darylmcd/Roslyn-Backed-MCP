using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for <c>build-test-services-swallowed-exceptions-no-logging</c>:
/// <see cref="MsBuildEvaluationService"/> previously had no <see cref="ILogger{T}"/> and
/// surfaced a project-not-found only through the thrown <see cref="InvalidOperationException"/>
/// message. It now emits a <see cref="LogLevel.Warning"/> naming the missing project, the
/// workspace, and the loaded-project list before throwing, so operators get a diagnostic
/// trail even when the exception message is not routed to logs.
/// </summary>
[TestClass]
public sealed class MsBuildEvaluationServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task EvaluateProperty_ProjectNotFound_LogsWarning_AndThrows()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        var logger = new CaptureLogger<MsBuildEvaluationService>();
        var service = new MsBuildEvaluationService(WorkspaceManager, logger);

        const string missingProject = "ZZZ_DoesNotExistProject";

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.EvaluatePropertyAsync(workspaceId, missingProject, "TargetFramework", CancellationToken.None));

        var warning = logger.Entries.SingleOrDefault(e => e.Level == LogLevel.Warning);
        Assert.IsNotNull(warning,
            "MSBuild project-not-found must emit a Warning log (previously silent). " +
            $"Captured entries: {string.Join("; ", logger.Entries.Select(e => $"{e.Level}:{e.Message}"))}");
        StringAssert.Contains(warning!.Message, missingProject);
        StringAssert.Contains(warning.Message, workspaceId);
    }
}

/// <summary>
/// Minimal in-memory <see cref="ILogger{T}"/> double that records every emitted log entry.
/// The test project has no capturing logger and every fixture builds services with
/// <c>NullLogger&lt;T&gt;.Instance</c>, so observability assertions must supply their own.
/// Shared by the swallowed-exception logging tests (Scaffolding / EditorConfig / MsBuild).
/// </summary>
internal sealed class CaptureLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    public ConcurrentQueue<Entry> Entries { get; } = new();

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Enqueue(new Entry(logLevel, formatter(state, exception), exception));
    }
}
