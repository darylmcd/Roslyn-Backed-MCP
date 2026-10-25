using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Rejects evaluated file paths that would cross from the copied graph back into source.</summary>
internal static class ForkGraphPreparation
{
    // These are physical destinations and explicit inputs consumed by restore, the SDK and compiler targets.
    // Evaluation identities such as MSBuildProjectDirectory are deliberately not destinations.
    private static readonly string[] OutputProperties =
    [
        "BaseIntermediateOutputPath", "IntermediateOutputPath", "MSBuildProjectExtensionsPath",
        "ProjectAssetsFile", "RestoreOutputPath", "BaseOutputPath", "OutputPath", "OutDir",
        "TargetDir", "TargetPath", "DocumentationFile", "GeneratedAssemblyInfoFile",
        "GeneratedGlobalUsingsFile", "CompilerGeneratedFilesOutputPath", "ErrorLog", "PdbFile",
        "PackageOutputPath", "RestorePackagesPath", "NuGetLockFilePath", "PackagesLockFilePath",
        "PublishDir", "PublishUrl", "ArtifactsPath", "AssemblyOriginatorKeyFile", "ApplicationIcon",
        "ApplicationManifest", "Win32Resource", "CodeAnalysisRuleSet", "SourceLink",
        "WinMDOutputDocumentationFile", "WinMDExpOutputPdb", "_IntermediateWindowsMetadataPath",
        "GeneratedMSBuildEditorConfigFile", "AppConfig",
    ];

    private static readonly HashSet<string> FileItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProjectReference", "Compile", "AdditionalFiles", "AdditionalDocuments", "Analyzer",
        "EmbeddedResource", "Content", "None", "ReferencePath", "ReferenceCopyLocalPaths",
        "IntermediateAssembly", "IntermediateRefAssembly", "DocFileItem", "FileWrites",
        "EditorConfigFiles", "AnalyzerConfigFiles",
    };

    private static readonly HashSet<string> LogicalItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "PackageReference", "PackageVersion", "FrameworkReference", "KnownFrameworkReference",
        "KnownRuntimePack", "KnownAppHostPack", "KnownCrossgen2Pack", "KnownILCompilerPack",
        "KnownILLinkPack", "KnownWebAssemblySdkPack", "SdkReference", "ProjectCapability",
        "Using", "AssemblyAttribute", "SupportedPlatform", "RuntimeHostConfigurationOption",
        "CompilerVisibleProperty", "CompilerVisibleItemMetadata", "InternalsVisibleTo",
        "AvailableItemName", "PropertyPageSchema", "AppDesigner", "Reference", "SourceRoot", "SourceControlRoot",
        "_AllDirectoriesAbove",
    };

    internal static string[] Validate(string entryPath, string sourceRoot, string forkRoot,
        Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        MsBuildInitializer.EnsureInitialized();
        return ValidateInitialized(entryPath, sourceRoot, forkRoot, remainingBudget, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] ValidateInitialized(string entryPath, string sourceRoot, string forkRoot,
        Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        try
        {
            var source = PhysicalPathResolver.Resolve(sourceRoot);
            var fork = PhysicalPathResolver.Resolve(forkRoot);
            return RestoreInvocationPlanner.EvaluateGraph(entryPath, remainingBudget, ct,
                RestoreInvocationPlanner.CreateOrdinaryInstance, (graph, phaseToken) =>
            {
                foreach (var node in graph.ProjectNodes)
                {
                    phaseToken.ThrowIfCancellationRequested();
                    remainingBudget();
                    Inspect(node.ProjectInstance, source, fork, remainingBudget, phaseToken);
                }
                return graph.ProjectNodes.Select(node => node.ProjectInstance.FullPath).Distinct(FileSystemPath.Comparer).ToArray();
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException and not PublicInvalidOperationException)
        {
            throw new PublicInvalidOperationException(
                "The copied project paths could not be verified for source isolation. Correct the project paths and retry.",
                InvalidOperationErrors.Internal("Could not resolve an evaluated fork path.", ex));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Inspect(ProjectInstance project, string source, string fork,
        Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        var projectDirectory = Path.GetDirectoryName(project.FullPath)
            ?? throw InvalidOperationErrors.Internal("An evaluated fork project has no directory.");
        var admission = new ForkPathAdmission(source, fork, project.FullPath, () =>
        {
            ct.ThrowIfCancellationRequested();
            remainingBudget();
        });
        Check(project.FullPath, "project");
        foreach (var import in project.ImportPaths) Check(import, "import");
        foreach (var name in OutputProperties)
        {
            var value = project.GetProperty(name);
            if (value is not null) CheckList(ProjectInstance.GetPropertyValueEscaped(value), name);
        }
        foreach (var item in project.Items)
        {
            ct.ThrowIfCancellationRequested();
            remainingBudget();
            var include = ProjectInstance.GetEvaluatedItemIncludeEscaped(item);
            // The SDK enumerates candidate ancestor configs at evaluation, then feeds only
            // existing files to the compiler through EditorConfigFiles (Managed.Core.targets).
            if (item.ItemType.Equals("PotentialEditorConfigFiles", StringComparison.OrdinalIgnoreCase) ||
                item.ItemType.Equals("GlobalAnalyzerConfigFiles", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = PhysicalPathResolver.Resolve(Path.IsPathRooted(item.EvaluatedInclude)
                    ? item.EvaluatedInclude : Path.Join(projectDirectory, item.EvaluatedInclude));
                if (File.Exists(candidate)) Check(item.EvaluatedInclude, item.ItemType);
            }
            else if (FileItems.Contains(item.ItemType)) CheckList(include, item.ItemType);
            else if (!LogicalItems.Contains(item.ItemType) && IsPhysical(item.EvaluatedInclude)) CheckList(include, item.ItemType);

            // Reference Include can be an assembly identity. HintPath is always a file;
            // a path-valued Include is also physical, unlike a simple assembly name.
            if (item.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase))
            {
                if (IsPhysical(item.EvaluatedInclude)) CheckList(include, "Reference");
                var hint = item.GetMetadata("HintPath");
                if (hint is not null) CheckList(ProjectInstance.GetMetadataValueEscaped(hint), "Reference.HintPath");
            }
        }

        bool IsPhysical(string value) => value.IndexOfAny(['/', '\\']) >= 0 ||
            File.Exists(Path.Join(projectDirectory, value)) || Path.IsPathRooted(value);

        void CheckList(string escaped, string role)
        {
            // Split before unescaping: %3B is part of one file name, not a list separator.
            foreach (var component in escaped.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Check(ProjectCollection.Unescape(component), role);
        }

        void Check(string value, string role)
        {
            // Restore's existing path contract resolves a link before a following parent.
            admission.Check(value, role, normalizeNativeOperation: false);
        }
    }
}
