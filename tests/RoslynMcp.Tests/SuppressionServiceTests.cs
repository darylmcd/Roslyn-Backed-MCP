using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SuppressionServiceTests
{
    [TestMethod]
    public async Task SetDiagnosticSeverityAsync_Throws_WhenDiagnosticId_Missing()
    {
        var sut = new SuppressionService(new StubEditorConfig(), new StubEditService());
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            sut.SetDiagnosticSeverityAsync("ws", " ", "warning", "/tmp/a.cs", CancellationToken.None));
    }

    [TestMethod]
    public async Task AddPragmaWarningDisableAsync_Throws_WhenLine_NotPositive()
    {
        var sut = new SuppressionService(new StubEditorConfig(), new StubEditService());
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            sut.AddPragmaWarningDisableAsync("ws", "/tmp/a.cs", 0, "CS0168", CancellationToken.None));
    }

    [TestMethod]
    public async Task AddPragmaWarningDisableAsync_Throws_WhenDiagnosticId_Missing()
    {
        var sut = new SuppressionService(new StubEditorConfig(), new StubEditService());
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            sut.AddPragmaWarningDisableAsync("ws", "/tmp/a.cs", 1, " ", CancellationToken.None));
    }

    [TestMethod]
    public async Task SetDiagnosticSeverityAsync_Delegates_ToEditorConfig_WithNormalizedKey()
    {
        string? capturedKey = null;
        var editor = new StubEditorConfig
        {
            OnSetOption = (_, _, key, _, _, _) =>
            {
                capturedKey = key;
                return Task.FromResult(new EditorConfigWriteResultDto("/x/.editorconfig", key, "warning", false));
            }
        };
        var sut = new SuppressionService(editor, new StubEditService());
        await sut.SetDiagnosticSeverityAsync("ws", " CS0168 ", " warning ", "/src/Program.cs", CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual("dotnet_diagnostic.CS0168.severity", capturedKey);
    }

    [TestMethod]
    public async Task AddPragmaWarningDisableAsync_Delegates_ToEditService()
    {
        TextEditDto? captured = null;
        var edits = new StubEditService
        {
            OnApply = (_, _, editsToApply, _, _, _) =>
            {
                captured = editsToApply.Single();
                return Task.FromResult(new TextEditResultDto(true, "/src/Program.cs", 1, []));
            }
        };
        var sut = new SuppressionService(new StubEditorConfig(), edits);
        await sut.AddPragmaWarningDisableAsync("ws", "/src/Program.cs", 3, "CS0219", CancellationToken.None).ConfigureAwait(false);
        Assert.IsNotNull(captured);
        Assert.AreEqual(3, captured.StartLine);
        StringAssert.StartsWith(captured.NewText, "#pragma warning disable CS0219");
    }

    [TestMethod]
    public async Task AddPragmaWarningDisableAsync_UsesLf_WhenTargetFileUsesLf()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "suppression-lf-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(filePath, "class C\n{\n}\n", CancellationToken.None).ConfigureAwait(false);

        try
        {
            TextEditDto? captured = null;
            var edits = new StubEditService
            {
                OnApply = (_, _, editsToApply, _, _, _) =>
                {
                    captured = editsToApply.Single();
                    return Task.FromResult(new TextEditResultDto(true, filePath, 1, []));
                }
            };
            var sut = new SuppressionService(new StubEditorConfig(), edits);

            await sut.AddPragmaWarningDisableAsync("ws", filePath, 1, "CA1305", CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(captured);
            Assert.IsTrue(captured.NewText.EndsWith("\n", StringComparison.Ordinal));
            Assert.IsFalse(captured.NewText.Contains("\r\n", StringComparison.Ordinal),
                $"LF file must receive an LF pragma edit, not CRLF. NewText: {captured.NewText.Replace("\r", "\\r").Replace("\n", "\\n")}");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task AddPragmaWarningDisableAsync_UsesCrLf_WhenTargetFileUsesCrLf()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "suppression-crlf-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(filePath, "class C\r\n{\r\n}\r\n", CancellationToken.None).ConfigureAwait(false);

        try
        {
            TextEditDto? captured = null;
            var edits = new StubEditService
            {
                OnApply = (_, _, editsToApply, _, _, _) =>
                {
                    captured = editsToApply.Single();
                    return Task.FromResult(new TextEditResultDto(true, filePath, 1, []));
                }
            };
            var sut = new SuppressionService(new StubEditorConfig(), edits);

            await sut.AddPragmaWarningDisableAsync("ws", filePath, 1, "CA1305", CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(captured);
            Assert.IsTrue(captured.NewText.EndsWith("\r\n", StringComparison.Ordinal),
                $"CRLF file must receive a CRLF pragma edit. NewText: {captured.NewText.Replace("\r", "\\r").Replace("\n", "\\n")}");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task AddPragmaWarningDisableAsync_IsIdempotent_OnDuplicateRetry()
    {
        // Repro for add-pragma-suppression-duplicate-on-retry: calling the service twice for
        // the same (file, line, id) — as happens after an auto-reload-triggered retry passing
        // the SAME args — must NOT accumulate a second identical pragma. The first insert pushes
        // the target line down by one, so on retry `line` points at the freshly-inserted pragma
        // line; the dangling disable above it already suppresses `line`, so the second call
        // no-ops (EditsApplied == 0).
        var filePath = Path.Combine(Path.GetTempPath(), "suppression-idem-" + Guid.NewGuid().ToString("N") + ".cs");
        // Target the method line (line 3) which CA1822 would flag.
        await File.WriteAllTextAsync(filePath, "class C\n{\n    public void M() { }\n}\n", CancellationToken.None).ConfigureAwait(false);

        try
        {
            // The stub APPLIES the edit to disk so the next call's disk-read sees the inserted
            // pragma — mirroring the real EditService's write-through behaviour.
            var edits = new ApplyToDiskEditService();
            var sut = new SuppressionService(new StubEditorConfig(), edits);

            var first = await sut.AddPragmaWarningDisableAsync("ws", filePath, 3, "CA1822", CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(first.Success);
            Assert.IsTrue(first.EditsApplied >= 1, "First call should insert the pragma.");
            Assert.AreEqual(1, CountPragmas(await File.ReadAllTextAsync(filePath).ConfigureAwait(false)),
                "Exactly one pragma after the first call.");

            // Retry with the SAME args (simulating the auto-reload retry).
            var second = await sut.AddPragmaWarningDisableAsync("ws", filePath, 3, "CA1822", CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(second.Success);
            Assert.AreEqual(0, second.EditsApplied, "Retry must be a no-op (already suppressed).");
            Assert.AreEqual(1, CountPragmas(await File.ReadAllTextAsync(filePath).ConfigureAwait(false)),
                "Still exactly one pragma after the duplicate retry — no accumulation.");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task AddPragmaWarningDisableAsync_Inserts_WhenLineNotYetSuppressed()
    {
        // Happy path: a genuinely-uncovered line DOES get a pragma inserted.
        var filePath = Path.Combine(Path.GetTempPath(), "suppression-insert-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(filePath, "class C\n{\n    public void M() { }\n}\n", CancellationToken.None).ConfigureAwait(false);

        try
        {
            var edits = new ApplyToDiskEditService();
            var sut = new SuppressionService(new StubEditorConfig(), edits);

            var result = await sut.AddPragmaWarningDisableAsync("ws", filePath, 3, "CA1822", CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.EditsApplied >= 1);
            Assert.AreEqual(1, CountPragmas(await File.ReadAllTextAsync(filePath).ConfigureAwait(false)));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task VerifyPragmaSuppressesAsync_WhenWorkspaceMissing_ReturnsBoundedConfirmationReason()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "suppression-missing-workspace-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(
            filePath,
            """
            class C
            {
                void M()
                {
                    #pragma warning disable CS0219
                    int x = 1;
                    #pragma warning restore CS0219
                }
            }
            """,
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var compileCheck = new StubCompileCheckService
            {
                OnCheck = (_, _, _) => throw new AssertFailedException("compile check should not run for a missing workspace")
            };
            var sut = new SuppressionService(
                new StubEditorConfig(),
                new StubEditService(),
                new StubWorkspaceManager(containsWorkspace: false),
                compileCheck);

            var result = await sut.VerifyPragmaSuppressesAsync("missing", filePath, 6, "CS0219", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsTrue(result.Suppresses);
            Assert.IsNull(result.DiagnosticFiresAtLine);
            Assert.AreEqual("workspace-not-loaded", result.DiagnosticFiresAtLineUnavailableReason);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task VerifyPragmaSuppressesAsync_WhenConfirmationServicesUnavailable_ReturnsBoundedConfirmationReason()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "suppression-no-confirmation-services-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(
            filePath,
            """
            class C
            {
                void M()
                {
                    #pragma warning disable CS0219
                    int x = 1;
                    #pragma warning restore CS0219
                }
            }
            """,
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var sut = new SuppressionService(new StubEditorConfig(), new StubEditService());

            var result = await sut.VerifyPragmaSuppressesAsync("ws", filePath, 6, "CS0219", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsTrue(result.Suppresses);
            Assert.IsNull(result.DiagnosticFiresAtLine);
            Assert.AreEqual("confirmation-service-unavailable", result.DiagnosticFiresAtLineUnavailableReason);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task VerifyPragmaSuppressesAsync_WhenCompileCheckThrows_ReturnsBoundedConfirmationReason()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "suppression-compile-failure-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(
            filePath,
            """
            class C
            {
                void M()
                {
                    #pragma warning disable CS0219
                    int x = 1;
                    #pragma warning restore CS0219
                }
            }
            """,
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            var compileCheck = new StubCompileCheckService
            {
                OnCheck = (_, _, _) => throw new InvalidOperationException(
                    $"secret-ish details from {filePath} must not leave the process")
            };
            var sut = new SuppressionService(
                new StubEditorConfig(),
                new StubEditService(),
                new StubWorkspaceManager(containsWorkspace: true),
                compileCheck);

            var result = await sut.VerifyPragmaSuppressesAsync("ws", filePath, 6, "CS0219", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.IsTrue(result.Suppresses);
            Assert.IsNull(result.DiagnosticFiresAtLine);
            var reason = result.DiagnosticFiresAtLineUnavailableReason;
            Assert.AreEqual("confirmation-failed", reason);
            Assert.AreNotEqual(filePath, reason);
            Assert.IsFalse(
                reason!.Contains("secret-ish", StringComparison.Ordinal),
                "The confirmation reason must be a stable code, not exception text.");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static int CountPragmas(string text)
    {
        int count = 0, idx = 0;
        const string needle = "#pragma warning disable CA1822";
        while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    private sealed class StubEditorConfig : IEditorConfigService
    {
        public Func<string, string, string, string, string, CancellationToken, Task<EditorConfigWriteResultDto>>? OnSetOption { get; init; }

        public Task<EditorConfigOptionsDto> GetOptionsAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<EditorConfigWriteResultDto> SetOptionAsync(
            string workspaceId, string sourceFilePath, string key, string value, string toolName, CancellationToken ct) =>
            OnSetOption?.Invoke(workspaceId, sourceFilePath, key, value, toolName, ct)
            ?? Task.FromResult(new EditorConfigWriteResultDto("", key, value, false));
    }

    /// <summary>
    /// Edit service stub that actually writes the edits through to the file on disk, so a
    /// subsequent disk-read (the idempotency guard's source of truth) observes the inserted
    /// pragma. Uses Roslyn's SourceText to apply the 1-based (line, col) TextEditDto spans —
    /// matching how the production EditService maps coordinates.
    /// </summary>
    private sealed class ApplyToDiskEditService : IEditService
    {
        public Task<TextEditResultDto> ApplyTextEditsAsync(
            string workspaceId,
            string filePath,
            IReadOnlyList<TextEditDto> edits,
            string toolName,
            CancellationToken ct,
            bool skipSyntaxCheck = false,
            bool verify = false,
            bool autoRevertOnError = false)
        {
            var original = File.ReadAllText(filePath);
            var sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(original);
            var changes = new List<Microsoft.CodeAnalysis.Text.TextChange>();
            foreach (var e in edits)
            {
                var startLine = sourceText.Lines[e.StartLine - 1];
                var endLine = sourceText.Lines[e.EndLine - 1];
                var startPos = startLine.Start + (e.StartColumn - 1);
                var endPos = endLine.Start + (e.EndColumn - 1);
                changes.Add(new Microsoft.CodeAnalysis.Text.TextChange(
                    Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(startPos, endPos), e.NewText));
            }
            var updated = sourceText.WithChanges(changes).ToString();
            File.WriteAllText(filePath, updated);
            return Task.FromResult(new TextEditResultDto(true, filePath, edits.Count, []));
        }

        public Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
            string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, string toolName, CancellationToken ct,
            bool skipSyntaxCheck = false, bool verify = false, bool autoRevertOnError = false) =>
            throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
            string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false) =>
            throw new NotSupportedException();
    }

    private sealed class StubEditService : IEditService
    {
        public Func<string, string, IReadOnlyList<TextEditDto>, string, CancellationToken, bool, Task<TextEditResultDto>>? OnApply { get; init; }

        // SuppressionService only forwards the default (verify=false, autoRevertOnError=false)
        // parameter values to apply_text_edit. The stub ignores them — it verifies that the
        // pragma-injection edit is wired up, not the verify pathway.
        public Task<TextEditResultDto> ApplyTextEditsAsync(
            string workspaceId,
            string filePath,
            IReadOnlyList<TextEditDto> edits,
            string toolName,
            CancellationToken ct,
            bool skipSyntaxCheck = false,
            bool verify = false,
            bool autoRevertOnError = false) =>
            OnApply?.Invoke(workspaceId, filePath, edits, toolName, ct, skipSyntaxCheck)
            ?? Task.FromResult(new TextEditResultDto(false, filePath, 0, []));

        public Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
            string workspaceId,
            IReadOnlyList<FileEditsDto> fileEdits,
            string toolName,
            CancellationToken ct,
            bool skipSyntaxCheck = false,
            bool verify = false,
            bool autoRevertOnError = false) =>
            throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
            string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false) =>
            throw new NotSupportedException();
    }

    private sealed class StubCompileCheckService : ICompileCheckService
    {
        public Func<string, CompileCheckOptions, CancellationToken, Task<CompileCheckDto>>? OnCheck { get; init; }

        public Task<CompileCheckDto> CheckAsync(
            string workspaceId,
            CompileCheckOptions options,
            CancellationToken ct) =>
            OnCheck?.Invoke(workspaceId, options, ct)
            ?? Task.FromResult(new CompileCheckDto(true, 0, 0, 0, 0, 0, options.Limit, false, [], 0));
    }

    private sealed class StubWorkspaceManager(bool containsWorkspace) : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed;

        public event Action<string>? WorkspaceReloaded;

        public bool ContainsWorkspace(string workspaceId) => containsWorkspace;

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool IsStale(string workspaceId) => false;

        public bool Close(string workspaceId)
        {
            WorkspaceClosed?.Invoke(workspaceId);
            return false;
        }

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Array.Empty<WorkspaceStatusDto>();

        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();

        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
            string workspaceId,
            string? projectName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();

        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public Project? GetProject(string workspaceId, string projectNameOrPath) => throw new NotSupportedException();

        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();

        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();

        public void RaiseReloadedForTest(string workspaceId) => WorkspaceReloaded?.Invoke(workspaceId);
    }
}
