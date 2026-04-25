using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class ProjectMutationService : IProjectMutationService
{
    private static readonly Regex SupportedConditionPattern = new(
        @"^\s*'\$\((Configuration|TargetFramework|Platform)\)'\s*==\s*'.+'\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "Nullable",
        "LangVersion",
        "ImplicitUsings",
        "TargetFramework"
    };

    private readonly IWorkspaceManager _workspace;
    private readonly IProjectMutationPreviewStore _previewStore;
    private readonly IMsBuildEvaluationService _msbuildEvaluation;
    private readonly ILogger<ProjectMutationService> _logger;
    private readonly IChangeTracker? _changeTracker;

    public ProjectMutationService(
        IWorkspaceManager workspace,
        IProjectMutationPreviewStore previewStore,
        IMsBuildEvaluationService msbuildEvaluation,
        ILogger<ProjectMutationService> logger,
        IChangeTracker? changeTracker = null)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _msbuildEvaluation = msbuildEvaluation;
        _logger = logger;
        _changeTracker = changeTracker;
    }

    public async Task<RefactoringPreviewDto> PreviewAddPackageReferenceAsync(string workspaceId, AddPackageReferenceDto request, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, request.ProjectName);
        var warnings = new List<string>();
        var packagesPropsPath = ResolveDirectoryPackagesPropsPath(workspaceId);
        var usesCentralPackageManagement = packagesPropsPath is not null && MsBuildMetadataHelper.IsCentralPackageManagementEnabled(packagesPropsPath);

        // add-package-reference-preview-cpm-duplicate-detection: Probe the evaluated
        // PackageReference item graph so we catch packages injected via
        // Directory.Build.props / Directory.Packages.props / SDK imports. The XDocument
        // check below only sees references declared in the .csproj itself, so an
        // implicit/transitive-via-imports reference would silently duplicate.
        var evaluatedPackages = await _msbuildEvaluation
            .EvaluateItemsAsync(workspaceId, request.ProjectName, "PackageReference", ct)
            .ConfigureAwait(false);
        if (evaluatedPackages.Items.Any(item =>
                string.Equals(item.Include, request.PackageId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Package reference '{request.PackageId}' is already present in the evaluated project graph " +
                "(declared in the .csproj or imported via Directory.Build.props / Directory.Packages.props / SDK imports). " +
                "No changes needed.");
        }

        return await PreviewProjectMutationAsync(workspaceId, project, document =>
        {
            if (document.Descendants("PackageReference").Any(element =>
                    string.Equals((string?)element.Attribute("Include"), request.PackageId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Package reference '{request.PackageId}' already exists.");
            }

            var packageReference = new XElement("PackageReference",
                new XAttribute("Include", request.PackageId));

            if (usesCentralPackageManagement)
            {
                if (packagesPropsPath is null ||
                    !MsBuildMetadataHelper.ContainsCentralPackageVersion(packagesPropsPath, request.PackageId))
                {
                    warnings.Add($"Central package management is enabled. Add '{request.PackageId}' to Directory.Packages.props before building or applying a central package version preview.");
                }
            }
            else
            {
                packageReference.Add(new XAttribute("Version", request.Version));
            }

            var itemGroup = OrchestrationMsBuildXml.GetOrCreateItemGroup(document, "PackageReference");
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(itemGroup, packageReference);
        }, $"Add package reference '{request.PackageId}'", ct, warnings).ConfigureAwait(false);
    }

    public Task<RefactoringPreviewDto> PreviewRemovePackageReferenceAsync(string workspaceId, RemovePackageReferenceDto request, CancellationToken ct)
    {
        return PreviewProjectMutationAsync(workspaceId, request.ProjectName, document =>
        {
            var element = document.Descendants("PackageReference").FirstOrDefault(candidate =>
                string.Equals((string?)candidate.Attribute("Include"), request.PackageId, StringComparison.OrdinalIgnoreCase));
            if (element is null)
            {
                throw new InvalidOperationException($"Package reference '{request.PackageId}' was not found.");
            }

            OrchestrationMsBuildXml.RemoveElementCleanly(element);
        }, $"Remove package reference '{request.PackageId}'", ct);
    }

    public Task<RefactoringPreviewDto> PreviewAddProjectReferenceAsync(string workspaceId, AddProjectReferenceDto request, CancellationToken ct)
    {
        return PreviewProjectMutationAsync(workspaceId, request.ProjectName, document =>
        {
            var project = ResolveProject(workspaceId, request.ProjectName);
            var referencedProject = ResolveProject(workspaceId, request.ReferencedProjectName);
            if (string.IsNullOrWhiteSpace(project.FilePath) || string.IsNullOrWhiteSpace(referencedProject.FilePath))
            {
                throw new InvalidOperationException("Both projects must have a file path on disk.");
            }

            var relativePath = Path.GetRelativePath(
                Path.GetDirectoryName(project.FilePath)!,
                referencedProject.FilePath);

            if (document.Descendants("ProjectReference").Any(element =>
                    string.Equals(NormalizeInclude((string?)element.Attribute("Include")), NormalizeInclude(relativePath), StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Project reference '{referencedProject.Name}' already exists.");
            }

            var itemGroup = OrchestrationMsBuildXml.GetOrCreateItemGroup(document, "ProjectReference");
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(
                itemGroup,
                new XElement("ProjectReference", new XAttribute("Include", relativePath)));
        }, $"Add project reference '{request.ReferencedProjectName}'", ct);
    }

    public Task<RefactoringPreviewDto> PreviewRemoveProjectReferenceAsync(string workspaceId, RemoveProjectReferenceDto request, CancellationToken ct)
    {
        return PreviewProjectMutationAsync(workspaceId, request.ProjectName, document =>
        {
            var referencedProject = ResolveProject(workspaceId, request.ReferencedProjectName);
            var targetFileName = Path.GetFileName(referencedProject.FilePath);

            var element = document.Descendants("ProjectReference").FirstOrDefault(candidate =>
            {
                var include = (string?)candidate.Attribute("Include");
                return !string.IsNullOrWhiteSpace(include) &&
                       string.Equals(Path.GetFileName(include), targetFileName, StringComparison.OrdinalIgnoreCase);
            });

            if (element is null)
            {
                throw new InvalidOperationException($"Project reference '{request.ReferencedProjectName}' was not found.");
            }

            OrchestrationMsBuildXml.RemoveElementCleanly(element);
        }, $"Remove project reference '{request.ReferencedProjectName}'", ct);
    }

    public Task<RefactoringPreviewDto> PreviewSetProjectPropertyAsync(string workspaceId, SetProjectPropertyDto request, CancellationToken ct)
    {
        return PreviewProjectMutationAsync(workspaceId, request.ProjectName, document =>
        {
            ValidateAllowedProperty(request.PropertyName);

            var propertyGroup = document.Root?.Elements("PropertyGroup").FirstOrDefault();
            if (propertyGroup is null)
            {
                // Create a new PropertyGroup if none exists (e.g., Directory.Build.props-based projects).
                // Insert with line breaks so the project file does not become a single-line malformed document.
                propertyGroup = new XElement("PropertyGroup");
                OrchestrationMsBuildXml.InsertFirstElementChildWithFormatting(document, propertyGroup);
            }

            // Detect no-op: check if property already has the target value
            var existingElement = propertyGroup.Element(request.PropertyName);
            if (string.Equals(existingElement?.Value, request.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"No changes needed — property '{request.PropertyName}' is already set to '{request.Value}'.");
            }

            // project-mutation-preview-xml-formatting: SetElementValue inserts a new XElement with
            // no surrounding text nodes, which causes the serializer to emit
            // `<TargetFramework>...</TargetFramework><LangVersion>preview</LangVersion></PropertyGroup>`
            // on a single line whenever the property is being added (not just updated). Route through
            // AddChildElementPreservingIndentation so the new child gets proper child-indent trivia.
            if (existingElement is not null)
            {
                existingElement.Value = request.Value;
            }
            else
            {
                OrchestrationMsBuildXml.AddChildElementPreservingIndentation(
                    propertyGroup,
                    new XElement(request.PropertyName, request.Value));
            }
        }, $"Set project property '{request.PropertyName}'", ct);
    }

    public async Task<RefactoringPreviewDto> PreviewAddTargetFrameworkAsync(string workspaceId, AddTargetFrameworkDto request, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, request.ProjectName);
        string? evalTf = null;
        string? evalTfs = null;
        var projectPath = project.FilePath!;
        var sniffText = await File.ReadAllTextAsync(projectPath, ct).ConfigureAwait(false);
        var sniffDoc = XDocument.Parse(sniffText, LoadOptions.PreserveWhitespace);
        if (!sniffDoc.Descendants("TargetFramework").Any() && !sniffDoc.Descendants("TargetFrameworks").Any())
        {
            evalTf = (await _msbuildEvaluation.EvaluatePropertyAsync(workspaceId, request.ProjectName, "TargetFramework", ct).ConfigureAwait(false)).EvaluatedValue;
            evalTfs = (await _msbuildEvaluation.EvaluatePropertyAsync(workspaceId, request.ProjectName, "TargetFrameworks", ct).ConfigureAwait(false)).EvaluatedValue;
        }

        return await PreviewProjectMutationAsync(workspaceId, project, document =>
        {
            var frameworksElement = document.Descendants("TargetFrameworks").FirstOrDefault();
            if (frameworksElement is not null)
            {
                var frameworks = ParseTargetFrameworks(frameworksElement.Value);
                if (frameworks.Contains(request.TargetFramework, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Target framework '{request.TargetFramework}' already exists.");
                }

                frameworks.Add(request.TargetFramework);
                frameworksElement.Value = string.Join(";", frameworks);
                return;
            }

            var targetFrameworkElement = document.Descendants("TargetFramework").FirstOrDefault();
            if (targetFrameworkElement is not null)
            {
                if (string.Equals(targetFrameworkElement.Value, request.TargetFramework, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Target framework '{request.TargetFramework}' already exists.");
                }

                targetFrameworkElement.Name = "TargetFrameworks";
                targetFrameworkElement.Value = string.Join(";", new[] { targetFrameworkElement.Value, request.TargetFramework });
                return;
            }

            // No explicit TF in the .csproj — resolve via MSBuild (Directory.Build.props / SDK imports).
            var baseline = !string.IsNullOrWhiteSpace(evalTfs) ? evalTfs : evalTf;
            if (string.IsNullOrWhiteSpace(baseline))
            {
                throw new InvalidOperationException(
                    "Project file does not declare <TargetFramework> or <TargetFrameworks>, and MSBuild evaluation did not return a value. " +
                    "Ensure the project loads in the workspace and that imports define TargetFramework, or add an explicit element to the .csproj.");
            }

            var list = ParseTargetFrameworks(baseline);
            if (list.Contains(request.TargetFramework, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Target framework '{request.TargetFramework}' already exists.");
            }

            list.Add(request.TargetFramework);
            var propertyGroup = new XElement("PropertyGroup");
            // project-mutation-preview-xml-formatting: insert the empty PropertyGroup first so
            // AddChildElementPreservingIndentation can detect parent indentation from its
            // previous-sibling text node (without insertion first the new element has no
            // surrounding text, so child indentation defaults to "  " instead of the project's
            // "    " for nested elements).
            OrchestrationMsBuildXml.InsertFirstElementChildWithFormatting(document, propertyGroup);
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(
                propertyGroup,
                new XElement("TargetFrameworks", string.Join(";", list)));
        }, $"Add target framework '{request.TargetFramework}'", ct).ConfigureAwait(false);
    }

    public async Task<RefactoringPreviewDto> PreviewRemoveTargetFrameworkAsync(string workspaceId, RemoveTargetFrameworkDto request, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, request.ProjectName);
        string? evalTf = null;
        string? evalTfs = null;
        var projectPath = project.FilePath!;
        var sniffText = await File.ReadAllTextAsync(projectPath, ct).ConfigureAwait(false);
        var sniffDoc = XDocument.Parse(sniffText, LoadOptions.PreserveWhitespace);
        if (!sniffDoc.Descendants("TargetFramework").Any() && !sniffDoc.Descendants("TargetFrameworks").Any())
        {
            evalTf = (await _msbuildEvaluation.EvaluatePropertyAsync(workspaceId, request.ProjectName, "TargetFramework", ct).ConfigureAwait(false)).EvaluatedValue;
            evalTfs = (await _msbuildEvaluation.EvaluatePropertyAsync(workspaceId, request.ProjectName, "TargetFrameworks", ct).ConfigureAwait(false)).EvaluatedValue;
        }

        return await PreviewProjectMutationAsync(workspaceId, project, document =>
        {
            var frameworksElement = document.Descendants("TargetFrameworks").FirstOrDefault();
            if (frameworksElement is not null)
            {
                var frameworks = ParseTargetFrameworks(frameworksElement.Value);
                var removed = frameworks.RemoveAll(value => string.Equals(value, request.TargetFramework, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    throw new InvalidOperationException($"Target framework '{request.TargetFramework}' was not found.");
                }

                if (frameworks.Count == 0)
                {
                    throw new InvalidOperationException("A project must keep at least one target framework.");
                }

                if (frameworks.Count == 1)
                {
                    frameworksElement.Name = "TargetFramework";
                    frameworksElement.Value = frameworks[0];
                    return;
                }

                frameworksElement.Value = string.Join(";", frameworks);
                return;
            }

            var targetFrameworkElement = document.Descendants("TargetFramework").FirstOrDefault();
            if (targetFrameworkElement is not null)
            {
                if (!string.Equals(targetFrameworkElement.Value, request.TargetFramework, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Target framework '{request.TargetFramework}' was not found.");
                }

                throw new InvalidOperationException("Cannot remove the only target framework from a project.");
            }

            var baseline = !string.IsNullOrWhiteSpace(evalTfs) ? evalTfs : evalTf;
            if (string.IsNullOrWhiteSpace(baseline))
            {
                throw new InvalidOperationException(
                    "Project file does not declare <TargetFramework> or <TargetFrameworks>, and MSBuild evaluation did not return a value.");
            }

            var list = ParseTargetFrameworks(baseline);
            var removedImplicit = list.RemoveAll(value => string.Equals(value, request.TargetFramework, StringComparison.OrdinalIgnoreCase));
            if (removedImplicit == 0)
            {
                throw new InvalidOperationException($"Target framework '{request.TargetFramework}' was not found.");
            }

            if (list.Count == 0)
            {
                throw new InvalidOperationException(
                    "Removing this target framework would leave the project with none. The value currently comes from MSBuild imports (e.g. Directory.Build.props). " +
                    "Edit that file or add an explicit <TargetFramework> override in this .csproj.");
            }

            var propertyGroup = new XElement("PropertyGroup");
            // project-mutation-preview-xml-formatting: insert empty PropertyGroup first, then add
            // the TargetFramework(s) child via the trivia-aware helper. See note in
            // PreviewAddTargetFrameworkAsync for why this two-step ordering matters.
            OrchestrationMsBuildXml.InsertFirstElementChildWithFormatting(document, propertyGroup);
            var tfElement = list.Count == 1
                ? new XElement("TargetFramework", list[0])
                : new XElement("TargetFrameworks", string.Join(";", list));
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(propertyGroup, tfElement);
        }, $"Remove target framework '{request.TargetFramework}'", ct).ConfigureAwait(false);
    }

    public Task<RefactoringPreviewDto> PreviewSetConditionalPropertyAsync(string workspaceId, SetConditionalPropertyDto request, CancellationToken ct)
    {
        return PreviewProjectMutationAsync(workspaceId, request.ProjectName, document =>
        {
            ValidateAllowedProperty(request.PropertyName);
            ValidateSupportedCondition(request.Condition);

            var propertyGroup = document.Root?.Elements("PropertyGroup")
                .FirstOrDefault(candidate => string.Equals((string?)candidate.Attribute("Condition"), request.Condition, StringComparison.Ordinal))
                ?? AddConditionalPropertyGroup(document, request.Condition);

            // project-mutation-preview-xml-formatting: same trivia-collapse defect as
            // PreviewSetProjectPropertyAsync — see note there. SetElementValue produces collapsed
            // single-line PropertyGroup output for newly-inserted children.
            var existingElement = propertyGroup.Element(request.PropertyName);
            if (existingElement is not null)
            {
                existingElement.Value = request.Value;
            }
            else
            {
                OrchestrationMsBuildXml.AddChildElementPreservingIndentation(
                    propertyGroup,
                    new XElement(request.PropertyName, request.Value));
            }
        }, $"Set project property '{request.PropertyName}' when {request.Condition}", ct);
    }

    public Task<RefactoringPreviewDto> PreviewAddCentralPackageVersionAsync(string workspaceId, AddCentralPackageVersionDto request, CancellationToken ct)
    {
        var packagesPropsPath = ResolveDirectoryPackagesPropsPath(workspaceId)
            ?? throw new FileNotFoundException(
                $"Directory.Packages.props was not found for the loaded workspace " +
                $"(searched from '{_workspace.GetStatus(workspaceId).LoadedPath}'). " +
                "To use central package management, create a Directory.Packages.props file at the solution root " +
                "with <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>.");

        return PreviewXmlFileMutationAsync(workspaceId, packagesPropsPath, document =>
        {
            EnsureCentralPackageManagementEnabled(packagesPropsPath);

            if (document.Descendants("PackageVersion").Any(element =>
                    string.Equals((string?)element.Attribute("Include"), request.PackageId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Central package version '{request.PackageId}' already exists.");
            }

            var itemGroup = OrchestrationMsBuildXml.GetOrCreateItemGroup(document, "PackageVersion");
            var packageVersion = new XElement("PackageVersion",
                new XAttribute("Include", request.PackageId),
                new XAttribute("Version", request.Version));
            OrchestrationMsBuildXml.AddChildElementPreservingIndentation(itemGroup, packageVersion);
        }, $"Add central package version '{request.PackageId}'", ct);
    }

    public Task<RefactoringPreviewDto> PreviewRemoveCentralPackageVersionAsync(string workspaceId, RemoveCentralPackageVersionDto request, CancellationToken ct)
    {
        var packagesPropsPath = ResolveDirectoryPackagesPropsPath(workspaceId)
            ?? throw new FileNotFoundException(
                $"Directory.Packages.props was not found for the loaded workspace " +
                $"(searched from '{_workspace.GetStatus(workspaceId).LoadedPath}'). " +
                "To use central package management, create a Directory.Packages.props file at the solution root " +
                "with <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>.");

        return PreviewXmlFileMutationAsync(workspaceId, packagesPropsPath, document =>
        {
            EnsureCentralPackageManagementEnabled(packagesPropsPath);

            var element = document.Descendants("PackageVersion").FirstOrDefault(candidate =>
                string.Equals((string?)candidate.Attribute("Include"), request.PackageId, StringComparison.OrdinalIgnoreCase));
            if (element is null)
            {
                throw new InvalidOperationException($"Central package version '{request.PackageId}' was not found.");
            }

            OrchestrationMsBuildXml.RemoveElementCleanly(element);
        }, $"Remove central package version '{request.PackageId}'", ct);
    }

    public async Task<ApplyResultDto> ApplyProjectMutationAsync(string previewToken, CancellationToken ct)
    {
        var entry = _previewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return new ApplyResultDto(false, [], "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");
        }

        var (workspaceId, projectFilePath, updatedContent, _, _) = entry.Value;
        // preview-token-cross-coupling-bundle (BREAKING): version-equality check removed.
        // Project-mutation previews hold the complete post-edit csproj text and a target
        // path — a full per-token snapshot. A sibling `*_apply` that mutated unrelated
        // projects does not invalidate this token. If a sibling edited the SAME csproj,
        // last-apply wins (documented semantic). If the workspace was reloaded or closed,
        // the store's lifecycle hook has already dropped the entry and Retrieve above
        // returned null.

        try
        {
            await File.WriteAllTextAsync(projectFilePath, updatedContent, ct).ConfigureAwait(false);
            await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
            _previewStore.Invalidate(previewToken);
            _changeTracker?.RecordChange(workspaceId, $"Project mutation: {Path.GetFileName(projectFilePath)}", [projectFilePath], "apply_project_mutation");
            return new ApplyResultDto(true, [projectFilePath], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to apply project mutation for {ProjectFilePath}", projectFilePath);
            return new ApplyResultDto(false, [], "Failed to apply project mutation.");
        }
    }

    private async Task<RefactoringPreviewDto> PreviewProjectMutationAsync(
        string workspaceId,
        string projectName,
        Action<XDocument> mutator,
        string description,
        CancellationToken ct,
        IReadOnlyList<string>? warnings = null)
    {
        return await PreviewProjectMutationAsync(
            workspaceId,
            ResolveProject(workspaceId, projectName),
            mutator,
            description,
            ct,
            warnings).ConfigureAwait(false);
    }

    private Task<RefactoringPreviewDto> PreviewProjectMutationAsync(
        string workspaceId,
        ProjectStatusDto project,
        Action<XDocument> mutator,
        string description,
        CancellationToken ct,
        IReadOnlyList<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
        {
            throw new InvalidOperationException($"Project file was not found for '{project.Name}'.");
        }

        return PreviewXmlFileMutationAsync(workspaceId, project.FilePath, mutator, description, ct, warnings);
    }

    private async Task<RefactoringPreviewDto> PreviewXmlFileMutationAsync(
        string workspaceId,
        string filePath,
        Action<XDocument> mutator,
        string description,
        CancellationToken ct,
        IReadOnlyList<string>? warnings = null)
    {
        var originalContent = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var document = XDocument.Parse(originalContent, LoadOptions.PreserveWhitespace);
        mutator(document);

        // BUG-N14 + project-mutation-preview-xml-formatting: emit readable multi-line XML instead
        // of a single-line <Project> blob. Delegates to OrchestrationMsBuildXml.FormatProjectXml
        // which uses MemoryStream + XmlWriter (preserves UTF-8 encoding declaration when present).
        var updatedContent = OrchestrationMsBuildXml.FormatProjectXml(document, originalContent);
        var diff = DiffGenerator.GenerateUnifiedDiff(originalContent, updatedContent, filePath);
        var token = _previewStore.Store(workspaceId, filePath, updatedContent, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, [new FileChangeDto(filePath, diff)], warnings);
    }

    private ProjectStatusDto ResolveProject(string workspaceId, string projectName)
    {
        return _workspace.GetStatus(workspaceId).Projects.FirstOrDefault(project =>
                   string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(project.FilePath, projectName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Project not found: {projectName}");
    }

    private static string NormalizeInclude(string? include)
    {
        return (include ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static void ValidateAllowedProperty(string propertyName)
    {
        if (!AllowedProperties.Contains(propertyName))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is not supported. Allowed properties: {string.Join(", ", AllowedProperties.OrderBy(value => value, StringComparer.Ordinal))}.");
        }
    }

    private static void ValidateSupportedCondition(string condition)
    {
        if (!SupportedConditionPattern.IsMatch(condition))
        {
            throw new InvalidOperationException(
                "Conditional property updates only support equality conditions on $(Configuration), $(TargetFramework), or $(Platform).");
        }
    }

    private string? ResolveDirectoryPackagesPropsPath(string workspaceId)
    {
        var loadedPath = _workspace.GetStatus(workspaceId).LoadedPath;
        return MsBuildMetadataHelper.FindDirectoryPackagesProps(loadedPath);
    }

    private static void EnsureCentralPackageManagementEnabled(string packagesPropsPath)
    {
        if (!MsBuildMetadataHelper.IsCentralPackageManagementEnabled(packagesPropsPath))
        {
            throw new InvalidOperationException("Central package management is not enabled for the loaded workspace.");
        }
    }

    private static XElement AddConditionalPropertyGroup(XDocument document, string condition)
    {
        // project-mutation-preview-xml-formatting: previously this called document.Root?.Add(propertyGroup)
        // which produced `</PreviousElement><PropertyGroup Condition="...">...</PropertyGroup></Project>`
        // on a single line (no trivia, no line breaks). Route through the trivia-aware splice helper
        // shared with GetOrCreateItemGroup so the new conditional group lands on its own line and
        // </Project> stays on the next line.
        var propertyGroup = new XElement("PropertyGroup", new XAttribute("Condition", condition));
        OrchestrationMsBuildXml.AppendRootChildWithFormatting(document, propertyGroup);
        return propertyGroup;
    }

    private static List<string> ParseTargetFrameworks(string value)
    {
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
