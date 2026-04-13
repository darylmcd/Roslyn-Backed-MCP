using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

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
        IdentifierValidation.ThrowIfInvalidIdentifier(request.TypeName, "type name");
        var project = ResolveProject(workspaceId, request.ProjectName);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var typeNamespace = string.IsNullOrWhiteSpace(request.Namespace) ? project.Name : request.Namespace!;
        var folderSegments = ResolveFolderSegmentsForNamespace(typeNamespace, project.Name);
        var filePath = Path.Combine([projectDirectory, .. folderSegments, $"{request.TypeName}.cs"]);
        var content = BuildTypeContent(typeNamespace, request);
        return _fileOperationService.PreviewCreateFileAsync(workspaceId, new CreateFileDto(project.Name, filePath, content), ct);
    }

    /// <summary>
    /// Picks the folder segments under the project root for a scaffolded file. When the
    /// namespace starts with the project name (the conventional case), strip that prefix and
    /// use the rest as folder names. Otherwise, use the full namespace path so that an
    /// explicit \"SomeOther.Sub\" namespace lands in \"SomeOther/Sub/\" instead of the project
    /// root. Previously the namespace-doesn't-start-with-project-name case fell through to
    /// the project root, which mismatched the expectation that scaffolded files live under
    /// a folder matching their namespace.
    /// </summary>
    private static IReadOnlyList<string> ResolveFolderSegmentsForNamespace(string typeNamespace, string projectName)
    {
        if (string.IsNullOrWhiteSpace(typeNamespace) || string.Equals(typeNamespace, projectName, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        var workingNamespace = typeNamespace.StartsWith(projectName + ".", StringComparison.Ordinal)
            ? typeNamespace[(projectName.Length + 1)..]
            : typeNamespace;

        return workingNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<RefactoringPreviewDto> PreviewScaffoldTestAsync(string workspaceId, ScaffoldTestDto request, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, request.TestProjectName);
        ValidateIsTestProject(project);
        var projectDirectory = Path.GetDirectoryName(project.FilePath)
            ?? throw new InvalidOperationException($"Project directory could not be resolved for '{project.FilePath}'.");
        var testFilePath = Path.Combine(projectDirectory, $"{request.TargetTypeName}GeneratedTests.cs");
        var testNamespace = project.Name;

        var framework = ResolveTestFramework(request.TestFramework, project.FilePath);

        var typeInfo = await ResolveTargetTypeAndMethodAsync(
            workspaceId, request.TestProjectName, request.TargetTypeName, request.TargetMethodName, ct).ConfigureAwait(false);
        var content = BuildTestContent(
            testNamespace, request, typeInfo.targetNamespace, typeInfo.constructorArgs, framework, typeInfo.targetMethod, typeInfo.matchedType);
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

    private async Task<(string targetNamespace, string constructorArgs, IMethodSymbol? targetMethod, List<string>? warnings, INamedTypeSymbol? matchedType)>
        ResolveTargetTypeAndMethodAsync(
            string workspaceId, string testProjectName, string targetTypeName, string? targetMethodName, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var testProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, testProjectName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.FilePath, testProjectName, StringComparison.OrdinalIgnoreCase));

        if (testProject is null)
            return (string.Empty, string.Empty, null, null, null);

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
            return (string.Empty, string.Empty, null, null, null);

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

        return (ns, constructorArgs, targetMethod, warnings, matchedType);
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

        var args = bestCtor.Parameters.Select(p => $"{BuildArgExpression(p.Type)} /* {p.Name} */");
        return string.Join(", ", args);
    }

    /// <summary>
    /// Builds a default-constructible expression for a constructor parameter type. Empty
    /// collection interfaces (<c>IEnumerable&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, etc.) get
    /// <c>Array.Empty&lt;T&gt;()</c>, dictionaries get <c>new Dictionary&lt;K,V&gt;()</c>,
    /// and <c>string</c> gets <c>string.Empty</c>. Everything else falls back to
    /// <c>default(T)</c>. Previously every parameter was emitted as <c>default(T)</c>, which
    /// throws <c>NullReferenceException</c> on the first call when the parameter is a non-null
    /// collection interface — observed in the 2026-04-07 ITChatBot legacy-mutex audit.
    /// </summary>
    private static string BuildArgExpression(ITypeSymbol parameterType)
    {
        var displayName = parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            return "string.Empty";
        }

        if (parameterType is INamedTypeSymbol named && named.IsGenericType)
        {
            var openGenericName = named.ConstructedFrom.ToDisplayString();

            if (openGenericName is "System.Collections.Generic.IEnumerable<T>"
                or "System.Collections.Generic.ICollection<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>"
                or "System.Collections.Generic.IList<T>"
                or "System.Collections.Generic.IReadOnlyList<T>")
            {
                var elementType = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return $"System.Array.Empty<{elementType}>()";
            }

            if (openGenericName is "System.Collections.Generic.IDictionary<TKey, TValue>"
                or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                var keyType = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                var valueType = named.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return $"new System.Collections.Generic.Dictionary<{keyType}, {valueType}>()";
            }
        }

        return $"default({displayName})";
    }

    private ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        return _workspace.GetStatus(workspaceId).Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, projectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Project not found: {projectName}");
    }

    private static void ValidateIsTestProject(ProjectStatusDto project)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
            return; // Can't validate — allow and let framework detection handle it

        try
        {
            var doc = XDocument.Load(project.FilePath, LoadOptions.None);

            // Check <IsTestProject>true</IsTestProject>
            var isTestProject = doc.Descendants("IsTestProject")
                .Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
            if (isTestProject) return;

            // Check for test framework PackageReferences
            var includes = doc.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value?.ToLowerInvariant())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            var hasTestFramework = includes.Any(i =>
                i!.Contains("mstest", StringComparison.Ordinal) ||
                i!.Contains("xunit", StringComparison.Ordinal) ||
                i!.Contains("nunit", StringComparison.Ordinal) ||
                i!.Contains("microsoft.net.test.sdk", StringComparison.Ordinal));
            if (hasTestFramework) return;

            throw new InvalidOperationException(
                $"Project '{project.Name}' does not appear to be a test project. " +
                "It has no <IsTestProject>true</IsTestProject> property and no test framework package references (MSTest, xUnit, NUnit). " +
                "Please specify a test project instead.");
        }
        catch (InvalidOperationException) { throw; }
        catch
        {
            // If we can't parse the project file, allow and let downstream handle it
        }
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
        var normalizedKind = request.TypeKind.ToLowerInvariant();
        var typeKeyword = normalizedKind switch
        {
            "interface" => "interface",
            "record" => "record",
            "enum" => "enum",
            _ => "class"
        };

        // Modern .NET convention: default scaffolded classes to `internal sealed class` so
        // they don't expand the public API surface and aren't subclassable by accident.
        // Records/interfaces/enums stay `public` (interface and enum cannot be sealed; records
        // are typically intended as DTOs that get used widely).
        var modifier = normalizedKind == "interface" || normalizedKind == "record" || normalizedKind == "enum"
            ? "public"
            : "internal sealed";

        return $"namespace {typeNamespace};\n\n{modifier} {typeKeyword} {request.TypeName}{inheritanceClause}\n{{\n}}\n";
    }

    private static string BuildTestContent(
        string testNamespace,
        ScaffoldTestDto request,
        string targetNamespace,
        string constructorArgs,
        string framework,
        IMethodSymbol? targetMethod,
        INamedTypeSymbol? matchedType)
    {
        var methodName = string.IsNullOrWhiteSpace(request.TargetMethodName)
            ? "Generated_Test"
            : $"{request.TargetMethodName}_Needs_Test";

        var usingDirective = string.IsNullOrWhiteSpace(targetNamespace)
            ? string.Empty
            : $"using {targetNamespace};\n";

        var useStaticScaffold = ShouldUseStaticTestScaffold(matchedType);
        var ctorCall = useStaticScaffold
            ? string.Empty
            : string.IsNullOrWhiteSpace(constructorArgs)
                ? $"new {request.TargetTypeName}()"
                : $"new {request.TargetTypeName}({constructorArgs})";

        var methodTargetBlock = BuildMethodTargetInvocationBlock(
            framework, request.TargetTypeName, request.TargetMethodName, targetMethod, useStaticScaffold);

        return framework switch
        {
            "xunit" => BuildXUnitTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock, useStaticScaffold),
            "nunit" => BuildNUnitTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock, useStaticScaffold),
            _ => BuildMSTestTestContent(testNamespace, usingDirective, request.TargetTypeName, methodName, ctorCall, methodTargetBlock, useStaticScaffold),
        };
    }

    /// <summary>
    /// BUG-N10: static classes, or instance classes whose only public API is static methods (utility types),
    /// should not scaffold <c>new T()</c> + instance assertions.
    /// </summary>
    private static bool ShouldUseStaticTestScaffold(INamedTypeSymbol? matchedType)
    {
        if (matchedType is null)
            return false;
        if (matchedType.IsStatic)
            return true;

        var ordinaryMethods = matchedType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation)
            .ToList();

        var hasVisibleInstance = ordinaryMethods.Any(m =>
            !m.IsStatic &&
            m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.Protected);

        var hasVisibleStatic = ordinaryMethods.Any(m =>
            m.IsStatic &&
            m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);

        return !hasVisibleInstance && hasVisibleStatic;
    }

    private static string BuildMethodTargetInvocationBlock(
        string framework,
        string targetTypeName,
        string? targetMethodName,
        IMethodSymbol? targetMethod,
        bool useStaticScaffold)
    {
        if (string.IsNullOrWhiteSpace(targetMethodName))
        {
            return "        // No target method specified.\n";
        }

        if (targetMethod is null)
        {
            return $"        // Target method '{targetMethodName}' was not resolved on {targetTypeName}.\n";
        }

        if (useStaticScaffold && targetMethod.IsStatic)
        {
            if (targetMethod.Parameters.Length == 0 && !targetMethod.ReturnsVoid)
                return $"        _ = {targetTypeName}.{targetMethodName}();\n";
            if (targetMethod.Parameters.Length == 0 && targetMethod.ReturnsVoid)
                return $"        {targetTypeName}.{targetMethodName}();\n";
            return $"        // Add arguments for static method '{targetMethodName}'.\n";
        }

        if (targetMethod.DeclaredAccessibility == Accessibility.Private)
        {
            if (useStaticScaffold && !targetMethod.IsStatic)
            {
                return "        // Private instance method — not reachable from a static-only scaffold; test via public API or InternalsVisibleTo.\n";
            }

            var assertNotNull = framework switch
            {
                "xunit" => "Assert.NotNull(__method);",
                "nunit" => "Assert.That(__method, Is.Not.Null);",
                _ => "Assert.IsNotNull(__method);",
            };
            var flags = targetMethod.IsStatic
                ? "System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic"
                : "System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic";
            var invokeTarget = targetMethod.IsStatic ? "null" : "subject";
            return
                "        // Private method — invoke via reflection (replace with InternalsVisibleTo or a public API test if preferred).\n" +
                $"        var __method = typeof({targetTypeName}).GetMethod(\n" +
                $"            \"{targetMethodName}\",\n" +
                $"            {flags});\n" +
                "        " + assertNotNull + "\n" +
                $"        __method!.Invoke({invokeTarget}, null);\n";
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
        string methodBlock,
        bool isStaticType)
    {
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.IsTrue(true);\n"
            : "        Assert.IsNotNull(subject);\n";
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
            instanceSetup +
            methodBlock +
            tailAssert +
            "    }\n" +
            "}\n";
    }

    private static string BuildXUnitTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType)
    {
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.True(true);\n"
            : "        Assert.NotNull(subject);\n";
        return
            "using Xunit;\n" +
            usingDirective +
            "\nnamespace " + testNamespace + ";\n\n" +
            "public class " + targetTypeName + "GeneratedTests\n" +
            "{\n" +
            "    [Fact]\n" +
            "    public void " + methodName + "()\n" +
            "    {\n" +
            instanceSetup +
            methodBlock +
            tailAssert +
            "    }\n" +
            "}\n";
    }

    private static string BuildNUnitTestContent(
        string testNamespace,
        string usingDirective,
        string targetTypeName,
        string methodName,
        string ctorCall,
        string methodBlock,
        bool isStaticType)
    {
        var instanceSetup = isStaticType
            ? string.Empty
            : "        var subject = " + ctorCall + ";\n\n";
        var tailAssert = isStaticType
            ? "        Assert.That(true, Is.True);\n"
            : "        Assert.That(subject, Is.Not.Null);\n";
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
            instanceSetup +
            methodBlock +
            tailAssert +
            "    }\n" +
            "}\n";
    }
}
