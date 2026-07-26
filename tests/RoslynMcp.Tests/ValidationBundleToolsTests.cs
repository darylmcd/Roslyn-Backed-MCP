using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>validate-recent-git-changes-bare-error-envelope</c> (P3-UX): external
/// audits (self, TradeWise, NetworkDoc, FirewallAnalyzer) observed
/// <c>validate_recent_git_changes</c> returning the bare SDK string
/// <c>"An error occurred invoking 'validate_recent_git_changes'."</c> instead of the
/// <c>{category, tool, message, exceptionType, _meta}</c> structured envelope that
/// <c>validate_workspace</c> produces. The handler is now wrapped in an in-body
/// try/catch that routes failures through <see cref="ToolErrorHandler.ClassifyAndFormat"/> +
/// <see cref="ToolErrorHandler.InjectMetaIfPossible"/>, guaranteeing the structured
/// envelope regardless of whether the <c>StructuredCallToolFilter</c> catch path fires.
/// </summary>
[TestClass]
public sealed class ValidationBundleToolsTests
{
    [TestMethod]
    public async Task ValidateRecentGitChanges_ServiceThrowsFileNotFound_ReturnsStructuredEnvelope()
    {
        // Simulates the "git not on PATH" class of failure: the subprocess launch is expected
        // to be caught inside the service and surfaced as a Warnings entry — but if for any
        // reason the exception escapes (e.g. a downstream Path.GetFullPath throw on malformed
        // output), the handler must still produce the structured envelope.
        var gate = new PassThroughGate();
        var service = new ThrowingValidationService(new FileNotFoundException("git: No such file or directory"));

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = await ValidationBundleTools.ValidateRecentGitChanges(
            gate, service, workspaceId: "ws-1", runTests: false, summary: false, ct: default);

        AssertStructuredEnvelope(result, expectedTool: "validate_recent_git_changes");
        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("FileNotFound", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("FileNotFoundException", doc.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(doc.RootElement.GetProperty("message").GetString(), "git",
            "Envelope message should carry the inner-exception detail so the caller can correct the problem.");
    }

    [TestMethod]
    public async Task ValidateRecentGitChanges_ServiceThrowsInvalidOperation_ReturnsStructuredEnvelope()
    {
        // Simulates the "solution outside a git repo" failure mode where the workspace
        // status has no LoadedPath and ResolveSolutionDirectory throws InvalidOperationException.
        var gate = new PassThroughGate();
        var service = new ThrowingValidationService(
            new InvalidOperationException("Workspace 'ws-1' is not loaded."));

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = await ValidationBundleTools.ValidateRecentGitChanges(
            gate, service, workspaceId: "ws-1", runTests: false, summary: false, ct: default);

        AssertStructuredEnvelope(result, expectedTool: "validate_recent_git_changes");
        var doc = JsonDocument.Parse(result);
        Assert.AreEqual("InvalidOperation", doc.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", doc.RootElement.GetProperty("exceptionType").GetString());
    }

    [TestMethod]
    public async Task ValidateRecentGitChanges_SuccessPath_ReturnsDtoJson_NotErrorEnvelope()
    {
        // The envelope-wrapper must pass through success results byte-for-byte (modulo _meta
        // injection). This test pins the contract that we didn't accidentally wrap every
        // response as an error envelope.
        var gate = new PassThroughGate();
        var dto = new WorkspaceValidationDto(
            OverallStatus: "clean",
            ChangedFilePaths: Array.Empty<string>(),
            UnknownFilePaths: Array.Empty<string>(),
            CompileResult: new CompileCheckDto(
                Success: true,
                ErrorCount: 0,
                WarningCount: 0,
                TotalDiagnostics: 0,
                ReturnedDiagnostics: 0,
                Offset: 0,
                Limit: 200,
                HasMore: false,
                Diagnostics: Array.Empty<DiagnosticDto>(),
                ElapsedMs: 0),
            ErrorDiagnostics: Array.Empty<DiagnosticDto>(),
            ErrorCount: 0,
            WarningCount: 0,
            DiscoveredTests: Array.Empty<RelatedTestCaseDto>(),
            DotnetTestFilter: null,
            TestRunResult: null,
            Warnings: Array.Empty<string>());
        var service = new SuccessfulValidationService(dto);

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = await ValidationBundleTools.ValidateRecentGitChanges(
            gate, service, workspaceId: "ws-1", runTests: false, summary: false, ct: default);

        var doc = JsonDocument.Parse(result);
        Assert.IsFalse(doc.RootElement.TryGetProperty("error", out _),
            "Success path must not emit the {error:true,...} envelope.");
        Assert.IsTrue(doc.RootElement.TryGetProperty("overallStatus", out var status),
            "Success response should carry the DTO's overallStatus field.");
        Assert.AreEqual("clean", status.GetString());
    }

    [TestMethod]
    public async Task ValidateRecentGitChanges_OperationCanceled_PropagatesWithoutWrapping()
    {
        // Cooperative cancellation is a protocol-level signal — it must propagate out of the
        // tool body unwrapped so the SDK can emit the JSON-RPC cancellation envelope. Wrapping
        // it into an error envelope would lose that contract and make the client think the
        // tool itself failed.
        var gate = new PassThroughGate();
        var service = new ThrowingValidationService(new OperationCanceledException("caller cancelled"));

        using var scope = AmbientGateMetrics.BeginRequest();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ValidationBundleTools.ValidateRecentGitChanges(
                gate, service, workspaceId: "ws-1", runTests: false, summary: false, ct: default));
    }

    // ── workspace-fork-apply-security-hardening ─────────────────────────────
    // Secret-file exclusion, retained-fork TTL sweep, and env-configurable restore path/timeout.

    [TestMethod]
    [DataRow(".env")]
    [DataRow(".ENV")]
    [DataRow(".env.local")]
    [DataRow(".env.Production")]
    [DataRow("secrets.json")]
    [DataRow("Secrets.JSON")]
    [DataRow("app.pubxml")]
    [DataRow("app.pubxml.user")]
    [DataRow("cert.pfx")]
    [DataRow("private.key")]
    [DataRow("appsettings.Production.json")]
    [DataRow("appsettings.Staging.json")]
    [DataRow("APPSETTINGS.PRODUCTION.JSON")]
    public void IsSecretBearingFile_SecretShapes_ReturnsTrue(string fileName)
        => Assert.IsTrue(ValidationBundleTools.IsSecretBearingFile(fileName),
            $"'{fileName}' is a secret-bearing shape and must be excluded from forks.");

    [TestMethod]
    [DataRow("appsettings.json")]
    [DataRow("APPSETTINGS.JSON")]
    [DataRow("appsettings.Development.json")]
    [DataRow("appsettings.development.json")]
    [DataRow("Program.cs")]
    [DataRow("README.md")]
    [DataRow("keychain.txt")]
    [DataRow("envelope.cs")]
    [DataRow("")]
    public void IsSecretBearingFile_SafeShapes_ReturnsFalse(string fileName)
        => Assert.IsFalse(ValidationBundleTools.IsSecretBearingFile(fileName),
            $"'{fileName}' is not a secret-bearing shape and must be copied into forks.");

    [TestMethod]
    public void CopyDirectory_ExcludesSecretFiles_KeepsSafeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn-mcp-fork-copy-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "src");
        var dest = Path.Combine(root, "fork");
        try
        {
            Directory.CreateDirectory(source);
            // Secret-bearing — must NOT land in the fork.
            File.WriteAllText(Path.Combine(source, ".env"), "SECRET=1");
            File.WriteAllText(Path.Combine(source, "appsettings.Production.json"), "{}");
            File.WriteAllText(Path.Combine(source, "cert.pfx"), "binary");
            // Safe — must land in the fork.
            File.WriteAllText(Path.Combine(source, "appsettings.json"), "{}");
            File.WriteAllText(Path.Combine(source, "appsettings.Development.json"), "{}");
            File.WriteAllText(Path.Combine(source, "Program.cs"), "class C {}");

            ValidationBundleTools.CopyDirectory(source, dest, CancellationToken.None);

            Assert.IsFalse(File.Exists(Path.Combine(dest, ".env")), ".env must be excluded.");
            Assert.IsFalse(File.Exists(Path.Combine(dest, "appsettings.Production.json")),
                "appsettings.Production.json must be excluded.");
            Assert.IsFalse(File.Exists(Path.Combine(dest, "cert.pfx")), "*.pfx must be excluded.");
            Assert.IsTrue(File.Exists(Path.Combine(dest, "appsettings.json")),
                "base appsettings.json must be copied.");
            Assert.IsTrue(File.Exists(Path.Combine(dest, "appsettings.Development.json")),
                "appsettings.Development.json must be copied.");
            Assert.IsTrue(File.Exists(Path.Combine(dest, "Program.cs")), "Program.cs must be copied.");
        }
        finally
        {
            TryDeleteTree(root);
        }
    }

    [TestMethod]
    public void SweepExpiredForks_DeletesExpired_KeepsFreshAndUnparseable()
    {
        var forkRoot = Path.Combine(Path.GetTempPath(), "roslyn-mcp-fork-ttl-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(forkRoot);
            var now = DateTimeOffset.UtcNow;

            // Expired: stamped 48h ago (TTL below is 24h).
            var expired = MakeForkDir(forkRoot, now.AddHours(-48), "old");
            // Fresh: stamped 1h ago.
            var fresh = MakeForkDir(forkRoot, now.AddHours(-1), "new");
            // Unparseable prefix — must be kept (fail-safe).
            var unparseable = Path.Combine(forkRoot, "not-a-timestamp-abc");
            Directory.CreateDirectory(unparseable);

            ValidationBundleTools.SweepExpiredForks(forkRoot, ttlHours: 24, now: now);

            Assert.IsFalse(Directory.Exists(expired), "Fork older than the TTL must be swept.");
            Assert.IsTrue(Directory.Exists(fresh), "Fork newer than the TTL must be kept.");
            Assert.IsTrue(Directory.Exists(unparseable),
                "Fork with an unparseable timestamp prefix must be kept (fail-safe).");
        }
        finally
        {
            TryDeleteTree(forkRoot);
        }
    }

    [TestMethod]
    public void SweepExpiredForks_NonPositiveTtl_DisablesSweep()
    {
        var forkRoot = Path.Combine(Path.GetTempPath(), "roslyn-mcp-fork-ttl-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(forkRoot);
            var now = DateTimeOffset.UtcNow;
            var ancient = MakeForkDir(forkRoot, now.AddHours(-1000), "ancient");

            ValidationBundleTools.SweepExpiredForks(forkRoot, ttlHours: 0, now: now);

            Assert.IsTrue(Directory.Exists(ancient), "A non-positive TTL must disable the sweep entirely.");
        }
        finally
        {
            TryDeleteTree(forkRoot);
        }
    }

    [TestMethod]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NaN)]
    public void SweepExpiredForks_NonFiniteTtl_DisablesSweepWithoutOverflow(double ttlHours)
    {
        var forkRoot = Path.Combine(Path.GetTempPath(), "roslyn-mcp-fork-ttl-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(forkRoot);
            var now = DateTimeOffset.UtcNow;
            var ancient = MakeForkDir(forkRoot, now.AddHours(-1000), "ancient");

            ValidationBundleTools.SweepExpiredForks(forkRoot, ttlHours, now);

            Assert.IsTrue(Directory.Exists(ancient));
        }
        finally
        {
            TryDeleteTree(forkRoot);
        }
    }

    [TestMethod]
    public void ResolveForkTtlHours_HonorsEnvOverrideAndFallsBack()
    {
        const string var = "ROSLYNMCP_FORK_TTL_HOURS";
        var previous = Environment.GetEnvironmentVariable(var);
        try
        {
            Environment.SetEnvironmentVariable(var, null);
            Assert.AreEqual(24, ValidationBundleTools.ResolveForkTtlHours(), "Unset must fall back to 24h.");

            Environment.SetEnvironmentVariable(var, "6");
            Assert.AreEqual(6, ValidationBundleTools.ResolveForkTtlHours(), "Override must be honored.");

            Environment.SetEnvironmentVariable(var, "0");
            Assert.AreEqual(0, ValidationBundleTools.ResolveForkTtlHours(), "Zero is a valid disable value for TTL.");

            Environment.SetEnvironmentVariable(var, "not-a-number");
            Assert.AreEqual(24, ValidationBundleTools.ResolveForkTtlHours(), "Invalid must fall back to the default.");

            Environment.SetEnvironmentVariable(var, "-5");
            Assert.AreEqual(24, ValidationBundleTools.ResolveForkTtlHours(), "Negative must fall back to the default.");

            Environment.SetEnvironmentVariable(var, "Infinity");
            Assert.AreEqual(24, ValidationBundleTools.ResolveForkTtlHours(), "Infinity must fall back to the default.");

            Environment.SetEnvironmentVariable(var, "NaN");
            Assert.AreEqual(24, ValidationBundleTools.ResolveForkTtlHours(), "NaN must fall back to the default.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(var, previous);
        }
    }

    [TestMethod]
    public void ResolveForkRestoreTimeoutMinutes_HonorsEnvOverrideAndFallsBack()
    {
        const string var = "ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES";
        var previous = Environment.GetEnvironmentVariable(var);
        try
        {
            Environment.SetEnvironmentVariable(var, null);
            Assert.AreEqual(2, ValidationBundleTools.ResolveForkRestoreTimeoutMinutes(), "Unset must fall back to 2 min.");

            Environment.SetEnvironmentVariable(var, "10");
            Assert.AreEqual(10, ValidationBundleTools.ResolveForkRestoreTimeoutMinutes(), "Override must be honored.");

            Environment.SetEnvironmentVariable(var, "0");
            Assert.AreEqual(2, ValidationBundleTools.ResolveForkRestoreTimeoutMinutes(),
                "Zero is not a usable timeout — must fall back to the default.");

            Environment.SetEnvironmentVariable(var, "garbage");
            Assert.AreEqual(2, ValidationBundleTools.ResolveForkRestoreTimeoutMinutes(), "Invalid must fall back.");

            Environment.SetEnvironmentVariable(var, "Infinity");
            Assert.AreEqual(2, ValidationBundleTools.ResolveForkRestoreTimeoutMinutes(),
                "Infinity is not a bounded timeout and must fall back.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(var, previous);
        }
    }

    [TestMethod]
    public void ResolveForkDotnetPath_HonorsEnvOverrideAndFallsBack()
    {
        const string var = "ROSLYNMCP_FORK_DOTNET_PATH";
        var previous = Environment.GetEnvironmentVariable(var);
        try
        {
            Environment.SetEnvironmentVariable(var, null);
            Assert.AreEqual("dotnet", ValidationBundleTools.ResolveForkDotnetPath(), "Unset must fall back to 'dotnet'.");

            Environment.SetEnvironmentVariable(var, "  ");
            Assert.AreEqual("dotnet", ValidationBundleTools.ResolveForkDotnetPath(), "Whitespace must fall back to 'dotnet'.");

            Environment.SetEnvironmentVariable(var, @"C:\sdk\dotnet.exe");
            Assert.AreEqual(@"C:\sdk\dotnet.exe", ValidationBundleTools.ResolveForkDotnetPath(),
                "A configured dotnet path must be honored.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(var, previous);
        }
    }

    private static string MakeForkDir(string forkRoot, DateTimeOffset stamp, string slug)
    {
        var name = $"{stamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture)}-{slug}";
        var path = Path.Combine(forkRoot, name);
        Directory.CreateDirectory(path);
        // Drop a file so we prove recursive deletion, not just empty-dir removal.
        File.WriteAllText(Path.Combine(path, "marker.txt"), "x");
        return path;
    }

    private static void TryDeleteTree(string path)
        => TestFixtureFileSystem.DeleteDirectoryIfExists(path);

    private static void AssertStructuredEnvelope(string result, string expectedTool)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(result), "Handler must always return a non-empty JSON string.");
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("error", out var errorFlag) && errorFlag.GetBoolean(),
            "Structured error envelope must carry error:true.");
        Assert.IsTrue(root.TryGetProperty("category", out _),
            "Structured error envelope must carry a 'category' field.");
        Assert.IsTrue(root.TryGetProperty("tool", out var tool),
            "Structured error envelope must carry a 'tool' field.");
        Assert.AreEqual(expectedTool, tool.GetString());
        Assert.IsTrue(root.TryGetProperty("message", out _),
            "Structured error envelope must carry a 'message' field.");
        Assert.IsTrue(root.TryGetProperty("exceptionType", out _),
            "Structured error envelope must carry an 'exceptionType' field.");
        Assert.IsTrue(root.TryGetProperty("_meta", out _),
            "Structured error envelope must carry a '_meta' block (gate metrics injected by ToolErrorHandler.InjectMetaIfPossible).");
    }

    /// <summary>
    /// Trivial gate stub that runs the action synchronously without contention. Keeps the
    /// tests focused on the tool-level envelope-wrapping contract, not the gate's own
    /// serialization behavior (that's covered by <see cref="ToolDispatchTests"/>).
    /// </summary>
    private sealed class PassThroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true)
            => action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
            => throw new NotSupportedException();

        public void RemoveGate(string workspaceId)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Stand-in for <see cref="IWorkspaceValidationService"/> that throws the supplied
    /// exception on the <c>ValidateRecentGitChangesAsync</c> entry point. Used to exercise
    /// the tool's envelope-wrapping path without wiring a real Roslyn workspace / git subprocess.
    /// </summary>
    private sealed class ThrowingValidationService : IWorkspaceValidationService
    {
        private readonly Exception _toThrow;
        public ThrowingValidationService(Exception toThrow) => _toThrow = toThrow;

        public Task<WorkspaceValidationDto> ValidateAsync(
            string workspaceId, IReadOnlyList<string>? changedFilePaths, bool runTests,
            CancellationToken ct, bool summary = false)
            => Task.FromException<WorkspaceValidationDto>(_toThrow);

        public Task<WorkspaceValidationDto> ValidateRecentGitChangesAsync(
            string workspaceId, bool runTests, CancellationToken ct, bool summary = false)
            => Task.FromException<WorkspaceValidationDto>(_toThrow);
    }

    /// <summary>
    /// Stand-in that returns a pre-built DTO on the <c>ValidateRecentGitChangesAsync</c>
    /// entry point, so the success-path pass-through contract can be asserted.
    /// </summary>
    private sealed class SuccessfulValidationService : IWorkspaceValidationService
    {
        private readonly WorkspaceValidationDto _dto;
        public SuccessfulValidationService(WorkspaceValidationDto dto) => _dto = dto;

        public Task<WorkspaceValidationDto> ValidateAsync(
            string workspaceId, IReadOnlyList<string>? changedFilePaths, bool runTests,
            CancellationToken ct, bool summary = false)
            => Task.FromResult(_dto);

        public Task<WorkspaceValidationDto> ValidateRecentGitChangesAsync(
            string workspaceId, bool runTests, CancellationToken ct, bool summary = false)
            => Task.FromResult(_dto);
    }
}
