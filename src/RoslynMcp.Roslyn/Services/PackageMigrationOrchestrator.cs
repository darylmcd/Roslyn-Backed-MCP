using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class PackageMigrationOrchestrator : IPackageMigrationOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;

    public PackageMigrationOrchestrator(IWorkspaceManager workspace, ICompositePreviewStore compositePreviewStore)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewMigratePackageAsync(
        string workspaceId,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        CancellationToken ct)
    {
        var status = await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        var workspaceVersion = _workspace.GetCurrentVersion(workspaceId);
        var packagesPropsPath = MsBuildMetadataHelper.FindDirectoryPackagesProps(status.LoadedPath);
        var usesCentralPackageManagement = packagesPropsPath is not null && MsBuildMetadataHelper.IsCentralPackageManagementEnabled(packagesPropsPath);
        var mutations = new List<CompositeFileMutation>();
        var changes = new List<FileChangeDto>();
        var warnings = new List<string>();

        foreach (var project in status.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
            {
                continue;
            }

            await BuildPackageReferenceEditAsync(
                project.FilePath, project.Name, oldPackageId, newPackageId, newVersion,
                usesCentralPackageManagement, mutations, changes, warnings, ct).ConfigureAwait(false);
        }

        if (usesCentralPackageManagement && packagesPropsPath is not null && File.Exists(packagesPropsPath))
        {
            await BuildCentralVersionEditAsync(
                packagesPropsPath, oldPackageId, newPackageId, newVersion, mutations, changes, ct).ConfigureAwait(false);
        }

        if (mutations.Count == 0)
        {
            throw new InvalidOperationException($"No project references to '{oldPackageId}' were found in the loaded workspace.");
        }

        var description = $"Migrate package '{oldPackageId}' to '{newPackageId}'";
        var token = _compositePreviewStore.Store(workspaceId, workspaceVersion, description, mutations);
        return new RefactoringPreviewDto(token, description, changes, warnings.Count == 0 ? null : warnings);
    }

    private static async Task BuildPackageReferenceEditAsync(
        string projectFilePath,
        string projectName,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        bool usesCentralPackageManagement,
        List<CompositeFileMutation> mutations,
        List<FileChangeDto> changes,
        List<string> warnings,
        CancellationToken ct)
    {
        var originalContent = await File.ReadAllTextAsync(projectFilePath, ct).ConfigureAwait(false);
        var document = XDocument.Parse(originalContent, LoadOptions.PreserveWhitespace);
        var packageReferences = document.Descendants("PackageReference")
            .Where(element => string.Equals((string?)element.Attribute("Include"), oldPackageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (packageReferences.Length == 0)
        {
            return;
        }

        foreach (var packageReference in packageReferences)
        {
            packageReference.Remove();
        }

        if (document.Descendants("PackageReference").Any(element =>
                string.Equals((string?)element.Attribute("Include"), newPackageId, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add($"Project '{projectName}' already referenced '{newPackageId}'. Removed '{oldPackageId}' only.");
        }
        else
        {
            var replacement = new XElement("PackageReference", new XAttribute("Include", newPackageId));
            if (!usesCentralPackageManagement)
            {
                replacement.Add(new XAttribute("Version", newVersion));
            }

            OrchestrationMsBuildXml.GetOrCreateItemGroup(document, "PackageReference").Add(replacement);
        }

        var updatedContent = document.ToString(SaveOptions.DisableFormatting);
        if (!string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
        {
            mutations.Add(new CompositeFileMutation(projectFilePath, updatedContent));
            changes.Add(new FileChangeDto(projectFilePath, DiffGenerator.GenerateUnifiedDiff(originalContent, updatedContent, projectFilePath)));
        }
    }

    private static async Task BuildCentralVersionEditAsync(
        string packagesPropsPath,
        string oldPackageId,
        string newPackageId,
        string newVersion,
        List<CompositeFileMutation> mutations,
        List<FileChangeDto> changes,
        CancellationToken ct)
    {
        var originalPropsContent = await File.ReadAllTextAsync(packagesPropsPath, ct).ConfigureAwait(false);
        var propsDocument = XDocument.Parse(originalPropsContent, LoadOptions.PreserveWhitespace);

        var oldCentralVersion = propsDocument.Descendants("PackageVersion")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Include"), oldPackageId, StringComparison.OrdinalIgnoreCase));
        oldCentralVersion?.Remove();

        UpsertCentralPackageVersion(propsDocument, newPackageId, newVersion);

        var updatedPropsContent = propsDocument.ToString(SaveOptions.DisableFormatting);
        if (!string.Equals(originalPropsContent, updatedPropsContent, StringComparison.Ordinal))
        {
            mutations.Add(new CompositeFileMutation(packagesPropsPath, updatedPropsContent));
            changes.Add(new FileChangeDto(packagesPropsPath, DiffGenerator.GenerateUnifiedDiff(originalPropsContent, updatedPropsContent, packagesPropsPath)));
        }
    }

    private static void UpsertCentralPackageVersion(XDocument propsDocument, string packageId, string newVersion)
    {
        var existing = propsDocument.Descendants("PackageVersion")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            OrchestrationMsBuildXml.GetOrCreateItemGroup(propsDocument, "PackageVersion")
                .Add(new XElement("PackageVersion",
                    new XAttribute("Include", packageId),
                    new XAttribute("Version", newVersion)));
        }
        else
        {
            existing.SetAttributeValue("Version", newVersion);
        }
    }
}
