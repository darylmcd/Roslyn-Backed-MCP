using System.Xml.Linq;
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
        var project = ResolveProject(workspaceId, request.TestProjectName);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var testFilePath = Path.Combine(projectDirectory, $"{request.TargetTypeName}GeneratedTests.cs");
        var testNamespace = project.Name;

        var framework = ResolveTestFramework(request.TestFramework, project.FilePath);

        var typeInfo = await ResolveTargetTypeAndMethodAsync(
            workspaceId, request.TestProjectName, request.TargetTypeName, request.TargetMethodName, ct).ConfigureAwait(false);
        var content = BuildTestContent(testNamespace, request, typeInfo.targetNamespace, typeInfo.constructorArgs, framework, typeInfo.targetMethod);
        var preview = await _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, testFilePath, content), ct).ConfigureAwait(false);

        if (typeInfo.warnings is null || typeInfo.warnings.Count == 0)
            return preview;

        return preview with { Warnings = typeInfo.warnings };
    }

    private static string ResolveTestFramework(string? requested, string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(requested) ||
            string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return DetectTestFrameworkFromProjectFile(projectFilePath);
        }

        if (string.Equals(requested, "mstest", StringComparison.OrdinalIgnoreCase)) return "mstest";
        if (string.Equals(requested, "xunit", StringComparison.OrdinalIgnoreCase)) return "xunit";
        if (string.Equals(requested, "nunit", StringComparison.OrdinalIgnoreCase)) return "nunit";

        throw new InvalidOperationException(
            $"Unsupported testFramework '{requested}'. Use mstest, xunit, nunit, or auto.");
    }

    private static string DetectTestFrameworkFromProjectFile(string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
            return "mstest";

        try
        {
            var doc = XDocument.Load(projectFilePath, LoadOptions.None);
            var includes = doc.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i!.ToLowerInvariant())
                .ToList();

            if (includes.Any(i => i.Contains("xunit", StringComparison.Ordinal)))
                return "xunit";
            if (includes.Any(i => i.Contains("nunit", StringComparison.Ordinal)))
                return "nunit";
        }
        catch
        {
            // Fall through to default
        }

        return "mstest";
    }

    private async Task<(string targetNamespace, string constructorArgs, IMethodSymbol? targetMethod, List<string>? warnings)>
        ResolveTargetTypeAndMethodAsync(
            string workspaceId, string testProjectName, string targetTypeName, string? targetMethodName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, testProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, testProjectName, StringComparison.OrdinalIgnoreCase));

        if (testProject is null)
            return (string.Empty, string.Empty, null, null);

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
            return (string.Empty, string.Empty, null, null);

        var ns = matchedType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : matchedType.ContainingNamespace.ToDisplayString();

        var constructorArgs = BuildConstructorArgs(matchedType);

        IMethodSymbol? targetMethod = null;
        List<string>? warnings = null;
        if (!string.IsNullOrWhiteSpace(targetMethodName))
        {
            targetMethod = matchedType.GetMembers(targetMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation);

            if (targetMethod is null)
            {
                warnings ??= [];
                warnings.Add($"Target method '{targetMethodName}' was not found on type '{matchedType.Name}'.");
            }
            else if (targetMethod.DeclaredAccessibility == Accessibility.Private)
            {
                warnings ??= [];
                warnings.Add(
                    $"Target method '{targetMethodName}' is private — the scaffold uses reflection to invoke it; " +
                    "prefer InternalsVisibleTo or testing via public API when possible.");
            }
        }

        return (ns, constructorArgs, targetMethod, warnings);
    }

    private static string BuildConstructorArgs(INamedTypeSymbol type)
    {
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

    private static string BuildTestContent(
        string testNamespace,
        ScaffoldTestDto request,
        string targetNamespace,
        string constructorArgs,
        string framework,
        IMethodSymbol? targetMethod)
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

        var methodTargetBlock = BuildMethodTargetInvocationBlock(framework, request.TargetTypeName, request.TargetMethodName, targetMethod);

        return framework switch
        {
            "xunit" => BuildXUnitTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock),
            "nunit" => BuildNUnitTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock),
            _ => BuildMSTestTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock),
        };
    }

    private static string BuildMethodTargetInvocationBlock(
        string framework,
        string targetTypeName,
        string? targetMethodName,
        IMethodSymbol? targetMethod)
    {
        if (string.IsNullOrWhiteSpace(targetMethodName))
        {
            return "        // No target method specified.\n";
        }

        if (targetMethod is null)
        {
            return $"        // Target method '{targetMethodName}' was not resolved on {targetTypeName}.\n";
        }

        if (targetMethod.DeclaredAccessibility == Accessibility.Private)
        {
            var assertNotNull = framework switch
            {
                "xunit" => "Assert.NotNull(__method);",
                "nunit" => "Assert.That(__method, Is.Not.Null);",
                _ => "Assert.IsNotNull(__method);",
            };
            return
                "        // Private method — invoke via reflection (replace with InternalsVisibleTo or a public API test if preferred).\n" +
                $"        var __method = typeof({targetTypeName}).GetMethod(\n" +
                $"            \"{targetMethodName}\",\n" +
                "            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);\n" +
                "        " + assertNotNull + "\n" +
                "        __method!.Invoke(subject, null);\n";
        }

        if (targetMethod.Parameters.Length == 0 && !targetMethod.ReturnsVoid)
        {
            return $"        _ = subject.{targetMethodName}();\n";
        }

        if (targetMethod.Parameters.Length == 0 && targetMethod.ReturnsVoid)
        {
            return $"        subject.{targetMethodName}();\n";
        }

        return
            $"        // Target method '{targetMethodName}' has parameters — add arguments or use a wrapper.\n" +
            $"        // Example: subject.{targetMethodName}(/* args */);\n";
    }

    private static string BuildMSTestTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock)
    {
        return
            "using Microsoft.VisualStudio.TestTools.UnitTesting;\n" +
            usingDirective +
            "\nnamespace " + testNamespace + ";\n\n" +
            "[TestClass]\n" +
            "public class " + targetTypeName + "GeneratedTests\n" +
            "{\n" +
            "    [TestMethod]\n" +
            "    public void " + methodName + "()\n" +
            "    {\n" +
            "        var subject = " + ctorCall + ";\n\n" +
            methodBlock +
            "        Assert.IsNotNull(subject);\n" +
            "    }\n" +
            "}\n";
    }

    private static string BuildXUnitTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock)
    {
        return
            "using Xunit;\n" +
            usingDirective +
            "\nnamespace " + testNamespace + ";\n\n" +
            "public class " + targetTypeName + "GeneratedTests\n" +
            "{\n" +
            "    [Fact]\n" +
            "    public void " + methodName + "()\n" +
            "    {\n" +
            "        var subject = " + ctorCall + ";\n\n" +
            methodBlock +
            "        Assert.NotNull(subject);\n" +
            "    }\n" +
            "}\n";
    }

    private static string BuildNUnitTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock)
    {
        return
            "using NUnit.Framework;\n" +
            usingDirective +
            "\nnamespace " + testNamespace + ";\n\n" +
            "[TestFixture]\n" +
            "public class " + targetTypeName + "GeneratedTests\n" +
            "{\n" +
            "    [Test]\n" +
            "    public void " + methodName + "()\n" +
            "    {\n" +
            "        var subject = " + ctorCall + ";\n\n" +
            methodBlock +
            "        Assert.That(subject, Is.Not.Null);\n" +
            "    }\n" +
            "}\n";
    }
}
