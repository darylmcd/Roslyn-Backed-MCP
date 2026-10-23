using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using RoslynMcp.Roslyn.Services;
using RoslynProject = Microsoft.CodeAnalysis.Project;

namespace RoslynMcp.Roslyn.Helpers;

internal static class ProjectMetadataParser
{
    /// <summary>Reads the resolved package version for every active evaluated target. Missing or ambiguous targets remain unknown.</summary>
    public static IReadOnlyList<NuGet.Versioning.NuGetVersion>? TryGetResolvedPackageVersions(string? projectFilePath, string packageId,
        IReadOnlyDictionary<string, string> globals, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath)) return null;
        try
        {
            var versions = new List<NuGet.Versioning.NuGetVersion>();
            foreach (var context in MsBuildMetadataHelper.EvaluateProjectContexts(projectFilePath, globals))
            {
                if (context.AssetsPath is null || !File.Exists(context.AssetsPath)) return null;
                using var document = JsonDocument.Parse(File.ReadAllText(context.AssetsPath));
                var version = ReadResolvedPackageVersion(context, document.RootElement, packageId);
                if (version is null) return null;
                versions.Add(version);
            }
            return versions.Count == 0 ? null : versions;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            logger?.LogDebug("Failed to read evaluated resolved package versions ({FailureType})", exception.GetType().Name);
            return null;
        }
    }

    internal static NuGet.Versioning.NuGetVersion? ReadResolvedPackageVersion(MsBuildMetadataHelper.RestoreInputs context,
        JsonElement root, string packageId)
    {
        if (!TryGetAssetsTarget(root, context, includeRuntimeIdentifier: true, out var target)) return null;
        var matches = target.EnumerateObject().Where(item => item.Name.StartsWith(packageId + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Object
            || !matches[0].Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase)
            || !NuGet.Versioning.NuGetVersion.TryParse(matches[0].Name[(packageId.Length + 1)..], out var version)) return null;
        return version;
    }

    internal static bool TryGetAssetsTarget(JsonElement root, MsBuildMetadataHelper.RestoreInputs context,
        bool includeRuntimeIdentifier, out JsonElement target)
    {
        target = default;
        var selected = string.IsNullOrWhiteSpace(context.TargetFramework) ? context.TargetFrameworkMoniker : context.TargetFramework;
        if (string.IsNullOrWhiteSpace(selected) || root.ValueKind != JsonValueKind.Object
            || !TryGetAssetsFramework(root, selected, out var frameworkName, out _)
            || !root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object) return false;
        var suffix = includeRuntimeIdentifier && !string.IsNullOrWhiteSpace(context.RuntimeIdentifier) ? "/" + context.RuntimeIdentifier : "";
        var names = new[] { frameworkName, context.TargetFramework, context.TargetFrameworkMoniker }
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name + suffix).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = targets.EnumerateObject().Where(item => names.Contains(item.Name)).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Object) return false;
        target = matches[0].Value;
        return true;
    }

    internal static bool TryGetAssetsFramework(JsonElement root, string selected, out string targetName, out JsonElement framework)
    {
        targetName = selected;
        framework = default;
        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object) return false;
        var matches = frameworks.EnumerateObject().Where(item => item.Name.Equals(selected, StringComparison.OrdinalIgnoreCase) ||
            item.Value.ValueKind == JsonValueKind.Object && item.Value.TryGetProperty("targetAlias", out var alias) &&
            alias.ValueKind == JsonValueKind.String && string.Equals(alias.GetString(), selected, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Object) return false;
        targetName = matches[0].Name;
        framework = matches[0].Value;
        return true;
    }

    public static IReadOnlyList<string> GetTargetFrameworks(XDocument? document)
    {
        if (document is null)
        {
            return ["unknown"];
        }

        var targetFramework = document.Descendants("TargetFramework").Select(element => element.Value.Trim());
        var targetFrameworks = document.Descendants("TargetFrameworks")
            .SelectMany(element => element.Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var allFrameworks = targetFramework.Concat(targetFrameworks).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return allFrameworks.Count > 0 ? allFrameworks : ["unknown"];
    }

    internal static IReadOnlyList<string> GetTargetFrameworks(MsBuildMetadataHelper.RestoreInputs context, XDocument? document)
    {
        var frameworks = ParseTargetFrameworkValues(context.TargetFramework, context.TargetFrameworks);
        if (frameworks.Count > 0) return frameworks;
        return !string.IsNullOrWhiteSpace(context.TargetFrameworkMoniker) ? [context.TargetFrameworkMoniker] : GetTargetFrameworks(document);
    }

    private static IReadOnlyList<string> ParseTargetFrameworkValues(string? targetFramework, string? targetFrameworks)
    {
        var singleFrameworks = string.IsNullOrWhiteSpace(targetFramework)
            ? Array.Empty<string>()
            : new[] { targetFramework.Trim() };
        var multiFrameworks = string.IsNullOrWhiteSpace(targetFrameworks)
            ? Array.Empty<string>()
            : targetFrameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return (singleFrameworks.Length > 0 ? singleFrameworks : multiFrameworks)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly HashSet<string> XunitV3Packages = new(StringComparer.OrdinalIgnoreCase)
    {
        "xunit.v3", "xunit.v3.mtp-v1", "xunit.v3.mtp-v2", "xunit.v3.core", "xunit.v3.core.mtp-v1", "xunit.v3.core.mtp-v2"
    };
    internal static bool IsXunitV3Package(string name) => XunitV3Packages.Contains(name);

    private static readonly HashSet<string> TestPackageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "xunit", "xunit.core", "xunit.v3", "xunit.v3.core",
        "NUnit", "nunit", "NUnit3TestAdapter",
        "MSTest.TestFramework", "Microsoft.NET.Test.Sdk",
        // TUnit ships its own Microsoft.Testing.Platform host instead of the classic VSTest
        // adapter, so it never references Microsoft.NET.Test.Sdk and doesn't stamp
        // <IsTestProject> itself — without this entry these projects were invisible to
        // test_discover/test_run (0 test projects found).
        "TUnit",
    };

    public static bool IsTestProject(XDocument? document)
    {
        if (document is null)
        {
            return false;
        }

        // Explicit <IsTestProject>true</IsTestProject> (MSTest SDK sets this automatically)
        var isTestProject = document.Descendants("IsTestProject").FirstOrDefault()?.Value;
        if (bool.TryParse(isTestProject, out var parsed) && parsed)
        {
            return true;
        }

        // Heuristic: check for test framework package references (xUnit, NUnit, MSTest)
        var packageReferences = document.Descendants("PackageReference");
        foreach (var pkgRef in packageReferences)
        {
            var include = pkgRef.Attribute("Include")?.Value;
            if (include is not null && (TestPackageNames.Contains(include) || IsXunitV3Package(include)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the Roslyn project is a test project (<c>IsTestProject</c> or test framework packages).
    /// </summary>
    public static bool IsTestProject(RoslynProject project, CancellationToken ct)
    {
        if (project.FilePath is null) return false;
        var contexts = EvaluatedBuildGraph.MatchingContexts(project, EvaluatedBuildGraph.Source(project.Solution, ct));
        if (contexts.Length == 0) throw new RoslynMcp.Core.Services.PublicInvalidOperationException("The loaded semantic project has no matching evaluated context.");
        return IsTestProject(contexts.Select(node => node.Inputs));
    }

    internal static bool IsTestProject(IEnumerable<MsBuildMetadataHelper.RestoreInputs> contexts) => contexts.Any(IsTestProject);

    internal static bool IsTestProject(MsBuildMetadataHelper.RestoreInputs context) =>
        context.IsTestProject || context.IsTestingPlatformApplication || context.Packages.Any(package => TestPackageNames.Contains(package.Id) || IsXunitV3Package(package.Id));

    public static bool RequiresMtpNativeExecution(RoslynProject project, CancellationToken ct) =>
        EvaluatedBuildGraph.Resolve(project, ct).Inputs.Packages.Any(package => MtpOnlyTestFrameworkPackageNames.Contains(package.Id));

    public static bool IsTestProject(string? path, IReadOnlyDictionary<string, string> globals) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) && MsBuildMetadataHelper.EvaluateProjectContexts(path, globals)
            .Any(context => context.IsTestProject || context.IsTestingPlatformApplication || context.Packages.Any(package => TestPackageNames.Contains(package.Id) || IsXunitV3Package(package.Id)));

    public static bool RequiresMtpNativeExecution(string? path, IReadOnlyDictionary<string, string> globals)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        var modes = MsBuildMetadataHelper.EvaluateProjectContexts(path, globals)
            .Where(context => context.IsTestProject || context.IsTestingPlatformApplication || context.Packages.Any(package => TestPackageNames.Contains(package.Id) || IsXunitV3Package(package.Id)))
            .Select(context => context.Packages.Any(package => MtpOnlyTestFrameworkPackageNames.Contains(package.Id))).Distinct().ToArray();
        if (modes.Length > 1) throw new RoslynMcp.Core.Services.PublicInvalidOperationException(
            "The active target frameworks require different test execution modes. Configure an explicit TargetFramework in the project or the application loading the workspace, reload, and run each framework separately.");
        return modes.Length == 1 && modes[0];
    }

    /// <summary>
    /// Test frameworks that are built entirely on Microsoft.Testing.Platform (MTP) and never
    /// register with the classic VSTest adapter — currently just TUnit
    /// (https://learn.microsoft.com/dotnet/core/testing/#testing-tools: "TUnit is entirely
    /// built on top of MTP and doesn't support VSTest"). Unlike MSTest/NUnit/xUnit, which stay
    /// VSTest-compatible via Microsoft.Testing.Extensions.VSTestBridge, a project referencing
    /// one of these packages requires the MTP-native <c>dotnet test</c> argument shape —
    /// <c>--logger</c>/<c>--filter</c> are silently ignored for it.
    /// </summary>
    private static readonly HashSet<string> MtpOnlyTestFrameworkPackageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "TUnit",
    };

    /// <summary>
    /// True when <paramref name="document"/> references a test framework that only supports
    /// Microsoft.Testing.Platform (MTP), never the classic VSTest adapter. Callers that shell
    /// out to <c>dotnet test</c> must use the MTP-native argument shape for such a project.
    /// </summary>
    public static bool RequiresMtpNativeExecution(XDocument? document)
    {
        if (document is null)
        {
            return false;
        }

        foreach (var pkgRef in document.Descendants("PackageReference"))
        {
            var include = pkgRef.Attribute("Include")?.Value;
            if (include is not null && MtpOnlyTestFrameworkPackageNames.Contains(include))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetOutputType(XDocument? document)
    {
        return document?.Descendants("OutputType").FirstOrDefault()?.Value.Trim() ?? "Library";
    }

    internal static string GetOutputType(MsBuildMetadataHelper.RestoreInputs context, XDocument? document) =>
        !string.IsNullOrWhiteSpace(context.OutputType) ? context.OutputType.Trim() : GetOutputType(document);

    internal static string? GetEvaluatedOutputType(string? projectFilePath, IReadOnlyDictionary<string, string> globals, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return null;
        }

        try
        {
            var outputType = MsBuildMetadataHelper.EvaluateProperties(projectFilePath, globals, "OutputType")[0];
            return string.IsNullOrWhiteSpace(outputType) ? null : outputType.Trim();
        }
        catch (Exception ex)
        {
            var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], globals));
            logger?.LogDebug(redactor.Failure(ex), "Failed to evaluate output type for project {Path}", redactor.Text(projectFilePath));
            return null;
        }
    }

    public static string GetAssemblyName(RoslynProject project)
    {
        if (project.CompilationOptions?.AssemblyIdentityComparer is not null && !string.IsNullOrWhiteSpace(project.AssemblyName))
        {
            return project.AssemblyName;
        }

        return project.Name;
    }

    /// <summary>
    /// project-graph-missing-metadata-fields: defensive name resolver. Roslyn's
    /// <see cref="RoslynProject.Name"/> is declared non-nullable but can surface as an empty
    /// string when a project is loaded without a populated <c>&lt;AssemblyName&gt;</c> or when
    /// the underlying MSBuild evaluation raced with a reload. Fall back through (1) the
    /// populated AssemblyName, (2) a filename-derived stem, (3) the literal "unknown"
    /// sentinel — never emit an empty string into the project graph or status DTO since
    /// downstream tools (scaffold_test_preview, DI resolution) rely on a non-empty name for
    /// lookup.
    /// </summary>
    public static string ResolveProjectName(RoslynProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.Name))
            return project.Name;

        if (!string.IsNullOrWhiteSpace(project.AssemblyName))
            return project.AssemblyName;

        if (!string.IsNullOrWhiteSpace(project.FilePath))
            return Path.GetFileNameWithoutExtension(project.FilePath);

        return "unknown";
    }

    /// <summary>
    /// project-graph-missing-metadata-fields: defensive path resolver. Matches the
    /// <see cref="ResolveProjectName"/> contract — never emits empty / whitespace strings
    /// into the project graph.
    /// </summary>
    public static string ResolveProjectPath(RoslynProject project) =>
        string.IsNullOrWhiteSpace(project.FilePath) ? "unknown" : project.FilePath;

    public static XDocument? LoadProjectDocument(string? projectFilePath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return null;
        }

        try
        {
            return XmlFileLoader.Load(projectFilePath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse project file {Path}", projectFilePath);
            return null;
        }
    }

    /// <summary>
    /// Item #1 / severity-critical-fail-preview-diff-does-not-match-t. Computes the relative
    /// folder segments from a project file to a target file, for use as the <c>folders</c>
    /// argument to <see cref="Microsoft.CodeAnalysis.Project.AddDocument(string, Microsoft.CodeAnalysis.Text.SourceText, System.Collections.Generic.IEnumerable{string}?, string?, System.Collections.Generic.IReadOnlyList{string}?, bool)"/>.
    /// Omitting folders makes MSBuildWorkspace treat the added document as living at project
    /// root, which — when our own explicit disk write uses a deeper path — produces TWO files
    /// on disk (the deep path plus a rogue project-root copy). All AddDocument callers that
    /// target a file outside the project root MUST pass folders to keep Roslyn's path
    /// resolution consistent with the explicit write.
    /// </summary>
    /// <param name="projectFilePath">The absolute path to the .csproj.</param>
    /// <param name="filePath">The absolute path to the file being added.</param>
    /// <returns>
    /// The relative folder segments from the project directory to the file directory,
    /// or an empty list when the file lives at project root or when paths are not resolvable.
    /// </returns>
    public static IReadOnlyList<string> ComputeDocumentFolders(string? projectFilePath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            return [];
        }

        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        var fileDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(fileDirectory))
        {
            return [];
        }

        var relativeDirectory = Path.GetRelativePath(projectDirectory, fileDirectory);
        if (string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
        {
            return [];
        }

        return relativeDirectory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && folder != ".")
            .ToArray();
    }

    /// <summary>
    /// Item #5 — <c>severity-medium-breaks-msbuild-until-csproj-is-hand</c>.
    /// Returns <see langword="true"/> when the project file is SDK-style
    /// (has an <c>Sdk=</c> attribute on the root <c>&lt;Project&gt;</c> element
    /// or a top-level <c>&lt;Sdk&gt;</c> import) AND does not explicitly disable
    /// default compile items with <c>&lt;EnableDefaultCompileItems&gt;false&lt;/EnableDefaultCompileItems&gt;</c>.
    /// </summary>
    /// <remarks>
    /// When this returns true, the server MUST NOT let
    /// <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.TryApplyChanges(Microsoft.CodeAnalysis.Solution)"/>
    /// inject an explicit <c>&lt;Compile Include="…"/&gt;</c> for an added document —
    /// the default glob picks up the file automatically on the next workspace load, and
    /// an explicit include produces the <c>Duplicate 'Compile' items were included</c>
    /// MSBuild error reported in the firewall-analyzer audit §9.6 (BUG-COMPILE-INCLUDE)
    /// and IT-Chat-Bot audit §9.1.
    /// </remarks>
    public static bool IsSdkStyleWithDefaultCompileItems(string? projectFilePath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        var document = LoadProjectDocument(projectFilePath, logger);
        return IsSdkStyleWithDefaultCompileItems(document);
    }

    /// <summary>
    /// XML-shape overload — avoids re-reading the file when the caller already has a
    /// parsed document. Pure: no I/O, no MSBuild evaluation.
    /// </summary>
    public static bool IsSdkStyleWithDefaultCompileItems(XDocument? document)
    {
        if (document?.Root is null)
        {
            return false;
        }

        var root = document.Root;

        // SDK-style detection. Three shapes in the wild:
        //   1. Attribute form: <Project Sdk="Microsoft.NET.Sdk">…</Project>    (most common)
        //   2. Element form:   <Project><Sdk Name="Microsoft.NET.Sdk"/>…</Project>
        //   3. Import form:    <Project><Import Sdk="…"/>…</Project>
        // Non-SDK legacy csprojs use <Project ToolsVersion=…> without Sdk attributes.
        var hasSdkAttribute = root.Attribute("Sdk") is not null;
        var hasSdkElement = root.Elements().Any(e => string.Equals(e.Name.LocalName, "Sdk", StringComparison.Ordinal));
        var hasSdkImport = root.Descendants().Any(e =>
            string.Equals(e.Name.LocalName, "Import", StringComparison.Ordinal) &&
            e.Attribute("Sdk") is not null);

        var isSdkStyle = hasSdkAttribute || hasSdkElement || hasSdkImport;
        if (!isSdkStyle)
        {
            return false;
        }

        // Default is true for SDK-style; only opted-out projects carry
        // <EnableDefaultCompileItems>false</EnableDefaultCompileItems> explicitly.
        // We intentionally only look at XML; if a user overrides the property via an
        // imported .props file they're taking responsibility for their build graph.
        foreach (var element in root.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "EnableDefaultCompileItems", StringComparison.Ordinal))
            {
                continue;
            }

            if (bool.TryParse(element.Value.Trim(), out var enabled) && !enabled)
            {
                return false;
            }
        }

        return true;
    }
}
