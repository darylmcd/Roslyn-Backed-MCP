using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

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
            OnSetOption = (_, _, key, _, _) =>
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
            OnApply = (_, _, editsToApply, _, _) =>
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

    private sealed class StubEditorConfig : IEditorConfigService
    {
        public Func<string, string, string, string, CancellationToken, Task<EditorConfigWriteResultDto>>? OnSetOption { get; init; }

        public Task<EditorConfigOptionsDto> GetOptionsAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<EditorConfigWriteResultDto> SetOptionAsync(
            string workspaceId, string sourceFilePath, string key, string value, CancellationToken ct) =>
            OnSetOption?.Invoke(workspaceId, sourceFilePath, key, value, ct)
            ?? Task.FromResult(new EditorConfigWriteResultDto("", key, value, false));
    }

    private sealed class StubEditService : IEditService
    {
        public Func<string, string, IReadOnlyList<TextEditDto>, CancellationToken, bool, Task<TextEditResultDto>>? OnApply { get; init; }

        public Task<TextEditResultDto> ApplyTextEditsAsync(
            string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits, CancellationToken ct, bool skipSyntaxCheck = false) =>
            OnApply?.Invoke(workspaceId, filePath, edits, ct, skipSyntaxCheck)
            ?? Task.FromResult(new TextEditResultDto(false, filePath, 0, []));

        public Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
            string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false) =>
            throw new NotSupportedException();

        public Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
            string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false) =>
            throw new NotSupportedException();
    }
}
