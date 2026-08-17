using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class ScaffoldingService
{
    /// <summary>
    /// Delegates <c>scaffold_test</c> preview to the <see cref="SingleTestScaffolder"/> collaborator.
    /// The facade resolves and validates the project and resolves the test framework (the
    /// logger-bound paths that stay on this facade), then hands the resolved project + framework
    /// to the collaborator. The <see cref="IScaffoldingService"/> facade contract and DI lifetime
    /// are unchanged.
    /// </summary>
    public async Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(
        string workspaceId,
        ScaffoldTestDto request,
        CancellationToken ct,
        ITestNameSuggestionProvider? testNameSuggestionProvider = null)
    {
        var project = ResolveProject(workspaceId, request.TestProjectName);
        ValidateIsTestProject(project);
        var framework = ResolveTestFramework(request.TestFramework, project.FilePath);

        var scaffolder = new SingleTestScaffolder(
            _workspace,
            _fileOperationService,
            _exceptionReporter);
        return await scaffolder
            .PreviewAsync(project, framework, workspaceId, request, ct, testNameSuggestionProvider)
            .ConfigureAwait(false);
    }
}
