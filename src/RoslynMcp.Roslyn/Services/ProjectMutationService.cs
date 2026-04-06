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
    private readonly ILogger<ProjectMutationService> _logger;

    public ProjectMutationService(
        IWorkspaceManager workspace,
        IProjectMutationPreviewStore previewStore,
        ILogger<ProjectMutationService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public Task<RefactoringPreviewDto> PreviewAddPackageReferenceAsync(string workspaceId, AddPackageReferenceDto request, CancellationToken ct)
    {
        var project = ResolveProject(workspaceId, request.ProjectName);
        var warnings = new List<string>();
        var packagesPropsPath = ResolveDirectoryPackagesPropsPath(workspaceId);
        var usesCentralPackageManagement = packagesPropsPath is not null && MsBuildMetadataHelper.IsCentralPackageManagementEnabled(packagesPropsPath);

        return PreviewProjectMutationAsync(workspaceId, project, document =>
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

            var itemGroup = GetOrCreateItemGroup(document, "PackageReference");
            AddChildElementPreservingIndentation(itemGroup, packageReference);
        }, $"Add package reference '{request.PackageId}'", ct, warnings);
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

            element.Remove();
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

            GetOrCreateItemGroup(document, "ProjectReference")
                .Add(new XElement("ProjectReference", new XAttribute("Include", relativePath)));
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

            element.Remove();
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
                InsertFirstElementChildWithFormatting(document, propertyGroup);
            }

            // Detect no-op: check if property already has the target value
            var existingValue = propertyGroup.Element(request.PropertyName)?.Value;
            if (string.Equals(existingValue, request.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"No changes needed — property '{request.PropertyName}' is already set to '{request.Value}'.");
            }

            propertyGroup.SetElementValue(request.PropertyName, request.Value);
        }, $"Set project property '{request.PropertyName}'", ct);
    }

    public Task<RefactoringPreviewDto> PreviewAddTargetFrameworkAsync(string workspaceId, AddTargetFrameworkDto request, CancellationToken ct)
    {
        return PreviewProjectMutationAsync(workspaceId, request.ProjectName, document =>
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

            var targetFrameworkElement = document.Descendants("TargetFramework").FirstOrDefault()
                ?? throw new InvalidOperationException("Project file does not declare TargetFramework or TargetFrameworks.");

            if (string.Equals(targetFrameworkElement.Value, request.TargetFramework, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Target framework '{request.TargetFramework}' already exists.");
            }

            targetFrameworkElement.Name = "TargetFrameworks";
            targetFrameworkElement.Value = string.Join(";", new[] { targetFrameworkElement.Value, request.TargetFramework });
        }, $"Add target framework '{request.TargetFramework}'", ct);
    }

    public Task<RefactoringPreviewDto> PreviewRemoveTargetFrameworkAsync(string workspaceId, RemoveTargetFrameworkDto request, CancellationToken ct)
    {
        return PreviewProjectMutationAsync(workspaceId, request.ProjectName, document =>
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

            var targetFrameworkElement = document.Descendants("TargetFramework").FirstOrDefault()
                ?? throw new InvalidOperationException("Project file does not declare TargetFramework or TargetFrameworks.");

            if (!string.Equals(targetFrameworkElement.Value, request.TargetFramework, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Target framework '{request.TargetFramework}' was not found.");
            }

            throw new InvalidOperationException("Cannot remove the only target framework from a project.");
        }, $"Remove target framework '{request.TargetFramework}'", ct);
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

            propertyGroup.SetElementValue(request.PropertyName, request.Value);
        }, $"Set project property '{request.PropertyName}' when {request.Condition}", ct);
    }

    public Task<RefactoringPreviewDto> PreviewAddCentralPackageVersionAsync(string workspaceId, AddCentralPackageVersionDto request, CancellationToken ct)
    {
        var packagesPropsPath = ResolveDirectoryPackagesPropsPath(workspaceId)
            ?? throw new InvalidOperationException("Directory.Packages.props was not found for the loaded workspace.");

        return PreviewXmlFileMutationAsync(workspaceId, packagesPropsPath, document =>
        {
            EnsureCentralPackageManagementEnabled(packagesPropsPath);

            if (document.Descendants("PackageVersion").Any(element =>
                    string.Equals((string?)element.Attribute("Include"), request.PackageId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Central package version '{request.PackageId}' already exists.");
            }

            GetOrCreateItemGroup(document, "PackageVersion")
                .Add(new XElement("PackageVersion",
                    new XAttribute("Include", request.PackageId),
                    new XAttribute("Version", request.Version)));
        }, $"Add central package version '{request.PackageId}'", ct);
    }

    public Task<RefactoringPreviewDto> PreviewRemoveCentralPackageVersionAsync(string workspaceId, RemoveCentralPackageVersionDto request, CancellationToken ct)
    {
        var packagesPropsPath = ResolveDirectoryPackagesPropsPath(workspaceId)
            ?? throw new InvalidOperationException("Directory.Packages.props was not found for the loaded workspace.");

        return PreviewXmlFileMutationAsync(workspaceId, packagesPropsPath, document =>
        {
            EnsureCentralPackageManagementEnabled(packagesPropsPath);

            var element = document.Descendants("PackageVersion").FirstOrDefault(candidate =>
                string.Equals((string?)candidate.Attribute("Include"), request.PackageId, StringComparison.OrdinalIgnoreCase));
            if (element is null)
            {
                throw new InvalidOperationException($"Central package version '{request.PackageId}' was not found.");
            }

            element.Remove();
        }, $"Remove central package version '{request.PackageId}'", ct);
    }

    public async Task<ApplyResultDto> ApplyProjectMutationAsync(string previewToken, CancellationToken ct)
    {
        var entry = _previewStore.Retrieve(previewToken);
        if (entry is null)
        {
            return new ApplyResultDto(false, [], "Preview token is invalid, expired, or stale because the workspace changed since the preview was generated. Please create a new preview.");
        }

        var (workspaceId, projectFilePath, updatedContent, workspaceVersion, _) = entry.Value;
        if (_workspace.GetCurrentVersion(workspaceId) != workspaceVersion)
        {
            _previewStore.Invalidate(previewToken);
            return new ApplyResultDto(false, [], "Preview token is stale because the target workspace changed. Please create a new preview.");
        }

        try
        {
            await File.WriteAllTextAsync(projectFilePath, updatedContent, ct).ConfigureAwait(false);
            await _workspace.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
            _previewStore.Invalidate(previewToken);
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

        var updatedContent = document.ToString(SaveOptions.DisableFormatting);
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

    private static XElement GetOrCreateItemGroup(XDocument document, string itemName)
    {
        var existingGroup = document.Root?.Elements("ItemGroup")
            .FirstOrDefault(group => group.Elements(itemName).Any());
        if (existingGroup is not null)
        {
            return existingGroup;
        }

        var itemGroup = new XElement("ItemGroup");
        var root = document.Root;
        if (root is null)
        {
            return itemGroup;
        }

        var lineEnding = DetectLineEnding(document);
        const string indent = "  ";
        if (root.Elements().Any())
        {
            var lastElement = root.Elements().Last();
            lastElement.AddAfterSelf(new XText(lineEnding + indent));
            lastElement.AddAfterSelf(itemGroup);
        }
        else
        {
            root.Add(new XText(lineEnding + "  "));
            root.Add(itemGroup);
            root.Add(new XText(lineEnding));
        }

        return itemGroup;
    }

    /// <summary>
    /// Inserts an element as the first child of the document root with a leading newline and indent,
    /// avoiding inline concatenation on the same line as the opening <c>&lt;Project&gt;</c> tag.
    /// </summary>
    private static void InsertFirstElementChildWithFormatting(XDocument document, XElement element)
    {
        var root = document.Root;
        if (root is null)
        {
            return;
        }

        var lineEnding = DetectLineEnding(document);
        const string indent = "  ";
        var firstElement = root.Elements().FirstOrDefault();
        if (firstElement is not null)
        {
            firstElement.AddBeforeSelf(element);
            firstElement.AddBeforeSelf(new XText(lineEnding + indent));
            return;
        }

        root.Add(new XText(lineEnding + indent));
        root.Add(element);
        root.Add(new XText(lineEnding));
    }

    private static void AddChildElementPreservingIndentation(XElement parent, XElement child)
    {
        var trailingWhitespace = parent.Nodes().OfType<XText>().LastOrDefault(node =>
            node.NextNode is null && string.IsNullOrWhiteSpace(node.Value));

        var childIndentation = DetectChildIndentation(parent);
        var lineEnding = DetectLineEnding(parent.Document);

        if (trailingWhitespace is not null)
        {
            trailingWhitespace.AddBeforeSelf(new XText(lineEnding + childIndentation));
            trailingWhitespace.AddBeforeSelf(child);
            return;
        }

        if (!parent.HasElements)
        {
            var parentIndentation = DetectParentIndentation(parent);
            parent.Add(new XText(lineEnding + childIndentation));
            parent.Add(child);
            parent.Add(new XText(lineEnding + parentIndentation));
            return;
        }

        // Existing siblings: insert newline + indent before the new element (matches first-child formatting)
        var lastElement = parent.Elements().LastOrDefault();
        if (lastElement is not null)
        {
            lastElement.AddAfterSelf(new XText(lineEnding + childIndentation), child);
            return;
        }

        parent.Add(child);
    }

    private static string DetectChildIndentation(XElement parent)
    {
        var firstChild = parent.Elements().FirstOrDefault();
        if (firstChild?.PreviousNode is XText previousText)
        {
            var indentation = GetTrailingIndentation(previousText.Value);
            if (indentation is not null)
            {
                return indentation;
            }
        }

        return DetectParentIndentation(parent) + "  ";
    }

    private static string DetectParentIndentation(XElement element)
    {
        if (element.PreviousNode is XText previousText)
        {
            return GetTrailingIndentation(previousText.Value) ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? GetTrailingIndentation(string whitespace)
    {
        if (string.IsNullOrWhiteSpace(whitespace))
        {
            var newlineIndex = whitespace.LastIndexOf('\n');
            if (newlineIndex >= 0)
            {
                return whitespace[(newlineIndex + 1)..];
            }
        }

        return null;
    }

    private static string DetectLineEnding(XDocument? document)
    {
        if (document is null)
        {
            return Environment.NewLine;
        }

        foreach (var textNode in document.DescendantNodes().OfType<XText>())
        {
            if (textNode.Value.Contains("\r\n", StringComparison.Ordinal))
            {
                return "\r\n";
            }

            if (textNode.Value.Contains("\n", StringComparison.Ordinal))
            {
                return "\n";
            }
        }

        return Environment.NewLine;
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
        var propertyGroup = new XElement("PropertyGroup", new XAttribute("Condition", condition));
        document.Root?.Add(propertyGroup);
        return propertyGroup;
    }

    private static List<string> ParseTargetFrameworks(string value)
    {
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
