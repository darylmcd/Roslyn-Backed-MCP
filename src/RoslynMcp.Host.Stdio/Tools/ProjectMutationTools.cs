using System.ComponentModel;
using RoslynMcp.Core.Services;
using RoslynMcp.Core.Models;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for project-file mutation (Package/Project references,
/// target frameworks, MSBuild properties, Directory.Packages.props entries).
/// WS1 phase 1.6 — each <c>Preview*</c> shim delegates to
/// <see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/>, and
/// <c>ApplyProjectMutation</c> uses the phase-1.6 delegate overload of
/// <see cref="ToolDispatch.ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, Func{string, string?}, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>
/// so it can reuse the shared dispatch body even though
/// <see cref="IProjectMutationPreviewStore"/> doesn't derive from
/// <c>IPreviewStore</c>. See <c>CodeActionTools</c> (canary, PR #305) and
/// <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c>.
/// </summary>
[McpServerToolType]
public static class ProjectMutationTools
{

    [McpServerTool(Name = "add_package_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview adding a PackageReference to a project file."),
     Description("Preview adding a PackageReference to a project file in the loaded workspace.")]
    public static Task<string> PreviewAddPackageReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("NuGet package id to add")] string packageId,
        [Description("NuGet package version to add")] string version,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewAddPackageReferenceAsync(
                workspaceId,
                new AddPackageReferenceDto(projectName, packageId, version),
                c),
            ct);

    [McpServerTool(Name = "remove_package_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview removing a PackageReference from a project file."),
     Description("Preview removing a PackageReference from a project file in the loaded workspace.")]
    public static Task<string> PreviewRemovePackageReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("NuGet package id to remove")] string packageId,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewRemovePackageReferenceAsync(
                workspaceId,
                new RemovePackageReferenceDto(projectName, packageId),
                c),
            ct);

    [McpServerTool(Name = "add_project_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview adding a ProjectReference to a project file."),
     Description("Preview adding a ProjectReference to a project file in the loaded workspace.")]
    public static Task<string> PreviewAddProjectReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Referenced project name or project file path")] string referencedProjectName,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewAddProjectReferenceAsync(
                workspaceId,
                new AddProjectReferenceDto(projectName, referencedProjectName),
                c),
            ct);

    [McpServerTool(Name = "remove_project_reference_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview removing a ProjectReference from a project file."),
     Description("Preview removing a ProjectReference from a project file in the loaded workspace.")]
    public static Task<string> PreviewRemoveProjectReference(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Referenced project name or project file path")] string referencedProjectName,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewRemoveProjectReferenceAsync(
                workspaceId,
                new RemoveProjectReferenceDto(projectName, referencedProjectName),
                c),
            ct);

    [McpServerTool(Name = "set_project_property_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview setting an allowlisted property in a project file."),
     Description("Preview setting an allowlisted property in a project file in the loaded workspace.")]
    public static Task<string> PreviewSetProjectProperty(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Allowlisted property name (Nullable, LangVersion, ImplicitUsings, TargetFramework)")] string propertyName,
        [Description("Property value to set")] string value,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewSetProjectPropertyAsync(
                workspaceId,
                new SetProjectPropertyDto(projectName, propertyName, value),
                c),
            ct);

    [McpServerTool(Name = "add_target_framework_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview adding a target framework to a project file."),
     Description("Preview adding a target framework to a project file in the loaded workspace.")]
    public static Task<string> PreviewAddTargetFramework(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Target framework moniker to add, for example net8.0")] string targetFramework,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewAddTargetFrameworkAsync(
                workspaceId,
                new AddTargetFrameworkDto(projectName, targetFramework),
                c),
            ct);

    [McpServerTool(Name = "remove_target_framework_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview removing a target framework from a project file."),
     Description("Preview removing a target framework from a project file in the loaded workspace.")]
    public static Task<string> PreviewRemoveTargetFramework(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Target framework moniker to remove, for example net8.0")] string targetFramework,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewRemoveTargetFrameworkAsync(
                workspaceId,
                new RemoveTargetFrameworkDto(projectName, targetFramework),
                c),
            ct);

    [McpServerTool(Name = "set_conditional_property_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview setting an allowlisted conditional project property."),
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
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewSetConditionalPropertyAsync(
                workspaceId,
                new SetConditionalPropertyDto(projectName, propertyName, value, condition),
                c),
            ct);

    [McpServerTool(Name = "add_central_package_version_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "experimental", true, false,
        "Preview adding a PackageVersion entry to Directory.Packages.props."),
     Description("Preview adding a PackageVersion entry to Directory.Packages.props for the loaded workspace.")]
    public static Task<string> PreviewAddCentralPackageVersion(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("NuGet package id to add to Directory.Packages.props")] string packageId,
        [Description("NuGet package version to set in Directory.Packages.props")] string version,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewAddCentralPackageVersionAsync(
                workspaceId,
                new AddCentralPackageVersionDto(packageId, version),
                c),
            ct);

    [McpServerTool(Name = "remove_central_package_version_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "stable", true, false,
        "Preview removing a PackageVersion entry from Directory.Packages.props."),
     Description("Preview removing a PackageVersion entry from Directory.Packages.props for the loaded workspace.")]
    public static Task<string> PreviewRemoveCentralPackageVersion(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("NuGet package id to remove from Directory.Packages.props")] string packageId,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => projectMutationService.PreviewRemoveCentralPackageVersionAsync(
                workspaceId,
                new RemoveCentralPackageVersionDto(packageId),
                c),
            ct);

    [McpServerTool(Name = "apply_project_mutation", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("project-mutation", "experimental", false, true,
        "Apply a previously previewed project file mutation."),
     Description("Apply a previously previewed project file mutation using its preview token.")]
    public static Task<string> ApplyProjectMutation(
        IWorkspaceExecutionGate gate,
        IProjectMutationService projectMutationService,
        IProjectMutationPreviewStore projectMutationPreviewStore,
        [Description("The preview token returned by one of the project mutation preview tools")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            projectMutationPreviewStore.PeekWorkspaceId,
            previewToken,
            c => projectMutationService.ApplyProjectMutationAsync(previewToken, c),
            ct);
}
