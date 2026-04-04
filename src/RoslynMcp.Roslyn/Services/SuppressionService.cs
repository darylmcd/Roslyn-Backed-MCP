using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed class SuppressionService : ISuppressionService
{
    private readonly IEditorConfigService _editorConfig;
    private readonly IEditService _editService;

    public SuppressionService(IEditorConfigService editorConfig, IEditService editService)
    {
        _editorConfig = editorConfig;
        _editService = editService;
    }

    public Task<EditorConfigWriteResultDto> SetDiagnosticSeverityAsync(
        string workspaceId, string diagnosticId, string severity, string sourceFilePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            throw new ArgumentException("Diagnostic id is required.", nameof(diagnosticId));
        }

        var key = $"dotnet_diagnostic.{diagnosticId.Trim()}.severity";
        return _editorConfig.SetOptionAsync(workspaceId, sourceFilePath, key, severity.Trim(), ct);
    }

    public Task<TextEditResultDto> AddPragmaWarningDisableAsync(
        string workspaceId, string filePath, int line, string diagnosticId, CancellationToken ct)
    {
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Line must be 1-based and positive.");
        }

        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            throw new ArgumentException("Diagnostic id is required.", nameof(diagnosticId));
        }

        var pragma = $"#pragma warning disable {diagnosticId.Trim()}{Environment.NewLine}";
        var edit = new TextEditDto(line, 1, line, 1, pragma);
        return _editService.ApplyTextEditsAsync(workspaceId, filePath, [edit], ct);
    }
}
