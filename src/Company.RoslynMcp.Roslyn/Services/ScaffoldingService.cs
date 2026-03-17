using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class ScaffoldingService : IScaffoldingService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IFileOperationService _fileOperationService;

    public ScaffoldingService(IWorkspaceManager workspace, IFileOperationService fileOperationService)
    {
        _workspace = workspace;
        _fileOperationService = fileOperationService;
    }

    public Task<RefactoringPreviewDto> PreviewScaffoldTypeAsync(string workspaceId, ScaffoldTypeDto request, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, request.ProjectName);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var typeNamespace = string.IsNullOrWhiteSpace(request.Namespace) ? project.Name : request.Namespace!;
        var namespaceSuffix = typeNamespace.StartsWith(project.Name + ".", StringComparison.Ordinal)
            ? typeNamespace[(project.Name.Length + 1)..]
            : string.Empty;
        var folderSegments = string.IsNullOrWhiteSpace(namespaceSuffix)
            ? Array.Empty<string>()
            : namespaceSuffix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filePath = Path.Combine([projectDirectory, .. folderSegments, $"{request.TypeName}.cs"]);
        var content = BuildTypeContent(typeNamespace, request);
        return _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, filePath, content), ct);
    }

    public Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(string workspaceId, ScaffoldTestDto request, CancellationToken ct)
    {
        if (!string.Equals(request.TestFramework, "mstest", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only MSTest scaffolding is currently supported.");
        }

        var project = ResolveProject(workspaceId, request.TestProjectName);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var testFilePath = Path.Combine(projectDirectory, $"{request.TargetTypeName}GeneratedTests.cs");
        var testNamespace = project.Name;
        var content = BuildTestContent(testNamespace, request);
        return _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, testFilePath, content), ct);
    }

    private ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        return _workspace.GetStatus(workspaceId).Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, projectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Project not found: {projectName}");
    }

    private static string BuildTypeContent(string typeNamespace, ScaffoldTypeDto request)
    {
        var inheritance = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.BaseType))
        {
            inheritance.Add(request.BaseType);
        }

        if (request.Interfaces is not null)
        {
            inheritance.AddRange(request.Interfaces.Where(@interface => !string.IsNullOrWhiteSpace(@interface)));
        }

        var inheritanceClause = inheritance.Count > 0 ? $" : {string.Join(", ", inheritance)}" : string.Empty;
        var typeKeyword = request.TypeKind.ToLowerInvariant() switch
        {
            "interface" => "interface",
            "record" => "record",
            "enum" => "enum",
            _ => "class"
        };

        return $"namespace {typeNamespace};\n\npublic {typeKeyword} {request.TypeName}{inheritanceClause}\n{{\n}}\n";
    }

    private static string BuildTestContent(string testNamespace, ScaffoldTestDto request)
    {
        var methodName = string.IsNullOrWhiteSpace(request.TargetMethodName)
            ? "Generated_Test"
            : $"{request.TargetMethodName}_Needs_Test";

        return $"using Microsoft.VisualStudio.TestTools.UnitTesting;\nusing SampleLib;\n\nnamespace {testNamespace};\n\n[TestClass]\npublic class {request.TargetTypeName}GeneratedTests\n{{\n    [TestMethod]\n    public void {methodName}()\n    {{\n        var subject = new {request.TargetTypeName}();\n\n        Assert.IsNotNull(subject);\n        Assert.Fail(\"Add real assertions.\");\n    }}\n}}\n";
    }
}