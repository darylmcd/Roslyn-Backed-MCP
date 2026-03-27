using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Core.Models;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ProjectMutationTools
{

    [McpServerTool(Name = "add_package_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview adding a PackageReference to a project file in the loaded workspace.")]
    public static Task<string> PreviewAddPackageReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("NuGet package id to add")] string packageId,
        [Description("NuGet package version to add")] string version,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewAddPackageReferenceAsync(
                    workspaceId,
                    new AddPackageReferenceDto(projectName, packageId, version),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "remove_package_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview removing a PackageReference from a project file in the loaded workspace.")]
    public static Task<string> PreviewRemovePackageReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("NuGet package id to remove")] string packageId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewRemovePackageReferenceAsync(
                    workspaceId,
                    new RemovePackageReferenceDto(projectName, packageId),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "add_project_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview adding a ProjectReference to a project file in the loaded workspace.")]
    public static Task<string> PreviewAddProjectReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Referenced project name or project file path")] string referencedProjectName,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewAddProjectReferenceAsync(
                    workspaceId,
                    new AddProjectReferenceDto(projectName, referencedProjectName),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "remove_project_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview removing a ProjectReference from a project file in the loaded workspace.")]
    public static Task<string> PreviewRemoveProjectReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Referenced project name or project file path")] string referencedProjectName,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewRemoveProjectReferenceAsync(
                    workspaceId,
                    new RemoveProjectReferenceDto(projectName, referencedProjectName),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "set_project_property_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview setting an allowlisted property in a project file in the loaded workspace.")]
    public static Task<string> PreviewSetProjectProperty(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Allowlisted property name (Nullable, LangVersion, ImplicitUsings, TargetFramework)")] string propertyName,
        [Description("Property value to set")] string value,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewSetProjectPropertyAsync(
                    workspaceId,
                    new SetProjectPropertyDto(projectName, propertyName, value),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "add_target_framework_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview adding a target framework to a project file in the loaded workspace.")]
    public static Task<string> PreviewAddTargetFramework(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Target framework moniker to add, for example net8.0")] string targetFramework,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewAddTargetFrameworkAsync(
                    workspaceId,
                    new AddTargetFrameworkDto(projectName, targetFramework),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "remove_target_framework_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview removing a target framework from a project file in the loaded workspace.")]
    public static Task<string> PreviewRemoveTargetFramework(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Target framework moniker to remove, for example net8.0")] string targetFramework,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewRemoveTargetFrameworkAsync(
                    workspaceId,
                    new RemoveTargetFrameworkDto(projectName, targetFramework),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "set_conditional_property_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview setting an allowlisted property in a conditional PropertyGroup within a project file.")]
    public static Task<string> PreviewSetConditionalProperty(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Allowlisted property name (Nullable, LangVersion, ImplicitUsings, TargetFramework)")] string propertyName,
        [Description("Property value to set")] string value,
        [Description("Condition expression using $(Configuration), $(TargetFramework), or $(Platform)")] string condition,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewSetConditionalPropertyAsync(
                    workspaceId,
                    new SetConditionalPropertyDto(projectName, propertyName, value, condition),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "add_central_package_version_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview adding a PackageVersion entry to Directory.Packages.props for the loaded workspace.")]
    public static Task<string> PreviewAddCentralPackageVersion(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("NuGet package id to add to Directory.Packages.props")] string packageId,
        [Description("NuGet package version to set in Directory.Packages.props")] string version,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewAddCentralPackageVersionAsync(
                    workspaceId,
                    new AddCentralPackageVersionDto(packageId, version),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "remove_central_package_version_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview removing a PackageVersion entry from Directory.Packages.props for the loaded workspace.")]
    public static Task<string> PreviewRemoveCentralPackageVersion(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("NuGet package id to remove from Directory.Packages.props")] string packageId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await projectMutationService.PreviewRemoveCentralPackageVersionAsync(
                    workspaceId,
                    new RemoveCentralPackageVersionDto(packageId),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "apply_project_mutation", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed project file mutation using its preview token.")]
    public static Task<string> ApplyProjectMutation(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        IProjectMutationPreviewStore projectMutationPreviewStore,
        [Description("The preview token returned by one of the project mutation preview tools")] string previewToken,
        CancellationToken ct = default)
    {
        var wsId = projectMutationPreviewStore.PeekWorkspaceId(previewToken);
        var gateKey = wsId != null ? $"__apply__:{wsId}" : "__apply__";
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await projectMutationService.ApplyProjectMutationAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}