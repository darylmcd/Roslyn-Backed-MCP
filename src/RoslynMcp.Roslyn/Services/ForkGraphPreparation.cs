using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Rejects evaluated file paths that would cross from the copied graph back into source.</summary>
internal static class ForkGraphPreparation
{
    // These are physical destinations consumed by restore, the SDK and compiler targets.
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
        "GeneratedMSBuildEditorConfigFile",
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

    internal static void Validate(string entryPath, string sourceRoot, string forkRoot,
        Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        MsBuildInitializer.EnsureInitialized();
        ValidateInitialized(entryPath, sourceRoot, forkRoot, remainingBudget, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateInitialized(string entryPath, string sourceRoot, string forkRoot,
        Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        try
        {
            var source = PhysicalPathResolver.Resolve(sourceRoot);
            var fork = PhysicalPathResolver.Resolve(forkRoot);
            RestoreInvocationPlanner.EvaluateGraph(entryPath, remainingBudget, ct, (graph, phaseToken) =>
            {
                foreach (var node in graph.ProjectNodes)
                {
                    phaseToken.ThrowIfCancellationRequested();
                    remainingBudget();
                    Inspect(node.ProjectInstance, source, fork, remainingBudget, phaseToken);
                }
                return true;
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
    private static void Inspect(ProjectInstance project, string source, string fork,
        Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        var projectDirectory = Path.GetDirectoryName(project.FullPath)
            ?? throw InvalidOperationErrors.Internal("An evaluated fork project has no directory.");
        // A project outside the copy is still the original graph project, even when it
        // lived outside the loaded solution directory. Restore must never run against it.
        if (!Contains(fork, PhysicalPathResolver.Resolve(project.FullPath)))
        {
            throw new PublicInvalidOperationException(
                "The copied project graph still references an original source workspace project outside the fork. Include that project in the copied workspace before retrying.",
                InvalidOperationErrors.Internal($"Evaluated project '{project.FullPath}' is outside the current fork."));
        }
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
            if (item.ItemType is "PotentialEditorConfigFiles" or "GlobalAnalyzerConfigFiles")
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
            ct.ThrowIfCancellationRequested();
            remainingBudget();
            if (string.IsNullOrWhiteSpace(value)) return;
            // Do not use GetFullPath here: a link before '..' must resolve first.
            var candidate = PhysicalPathResolver.Resolve(Path.IsPathRooted(value) ? value : Path.Join(projectDirectory, value));
            if (Contains(fork, candidate)) return;
            if (Contains(source, candidate))
            {
                throw new PublicInvalidOperationException(
                    "The copied project graph refers to files or outputs in the source workspace. Make those paths fork-relative or use an independent external location before retrying.",
                    InvalidOperationErrors.Internal($"Evaluated fork project '{project.FullPath}' role '{role}' reaches source path '{candidate}'."));
            }
        }
    }

    private static bool Contains(string root, string path) =>
        FileSystemPath.Comparer.Equals(root, path) || FileSystemPath.IsStrictDescendant(root, path);
}
