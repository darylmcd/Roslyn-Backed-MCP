using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

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

    public async Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(string workspaceId, ScaffoldTestDto request, CancellationToken ct)
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

        var typeInfo = await ResolveTargetTypeAsync(workspaceId, request.TestProjectName, request.TargetTypeName, ct).ConfigureAwait(false);
        var content = BuildTestContent(testNamespace, request, typeInfo.targetNamespace, typeInfo.constructorArgs);
        return await _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, testFilePath, content), ct).ConfigureAwait(false);
    }

    private async Task<(string targetNamespace, string constructorArgs)> ResolveTargetTypeAsync(
        string workspaceId, string testProjectName, string targetTypeName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, testProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, testProjectName, StringComparison.OrdinalIgnoreCase));

        if (testProject is null)
            return (string.Empty, string.Empty);

        // Search the test project and all its referenced projects for the target type
        var projectsToSearch = new List<Project> { testProject };
        foreach (var projectRef in testProject.ProjectReferences)
        {
            var referencedProject = solution.GetProject(projectRef.ProjectId);
            if (referencedProject is not null)
                projectsToSearch.Add(referencedProject);
        }

        INamedTypeSymbol? matchedType = null;
        foreach (var project in projectsToSearch)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            var candidates = compilation.GetSymbolsWithName(targetTypeName, SymbolFilter.Type, ct)
                .OfType<INamedTypeSymbol>()
                .Where(t => t.TypeKind is TypeKind.Class or TypeKind.Struct &&
                            string.Equals(t.Name, targetTypeName, StringComparison.Ordinal))
                .ToList();

            if (candidates.Count == 1)
            {
                matchedType = candidates[0];
                break;
            }

            if (candidates.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Ambiguous type name '{targetTypeName}' — found in multiple namespaces: " +
                    string.Join(", ", candidates.Select(c => c.ToDisplayString())) +
                    ". Use the fully qualified type name.");
            }
        }

        if (matchedType is null)
            return (string.Empty, string.Empty);

        var ns = matchedType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : matchedType.ContainingNamespace.ToDisplayString();

        var constructorArgs = BuildConstructorArgs(matchedType);
        return (ns, constructorArgs);
    }

    private static string BuildConstructorArgs(INamedTypeSymbol type)
    {
        // Find the most accessible constructor, preferring parameterless
        var constructors = type.Constructors
            .Where(c => !c.IsImplicitlyDeclared || c.Parameters.Length == 0)
            .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .OrderBy(c => c.Parameters.Length)
            .ToList();

        if (constructors.Count == 0)
            return string.Empty;

        var bestCtor = constructors[0];
        if (bestCtor.Parameters.Length == 0)
            return string.Empty;

        var args = bestCtor.Parameters.Select(p =>
            $"default({p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}) /* {p.Name} */");
        return string.Join(", ", args);
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

    private static string BuildTestContent(string testNamespace, ScaffoldTestDto request, string targetNamespace, string constructorArgs)
    {
        var methodName = string.IsNullOrWhiteSpace(request.TargetMethodName)
            ? "Generated_Test"
            : $"{request.TargetMethodName}_Needs_Test";

        var usingDirective = string.IsNullOrWhiteSpace(targetNamespace)
            ? string.Empty
            : $"using {targetNamespace};\n";

        var ctorCall = string.IsNullOrWhiteSpace(constructorArgs)
            ? $"new {request.TargetTypeName}()"
            : $"new {request.TargetTypeName}({constructorArgs})";

        return $"using Microsoft.VisualStudio.TestTools.UnitTesting;\n{usingDirective}\nnamespace {testNamespace};\n\n[TestClass]\npublic class {request.TargetTypeName}GeneratedTests\n{{\n    [TestMethod]\n    public void {methodName}()\n    {{\n        var subject = {ctorCall};\n\n        Assert.IsNotNull(subject);\n        Assert.Fail(\"Add real assertions.\");\n    }}\n}}\n";
    }
}
