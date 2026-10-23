using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Captures persisted MSBuild item authority independently of a reviewed snapshot.</summary>
internal static class EvaluatedProjectGraph
{
    private sealed record Evaluation(IReadOnlyList<Reference> References, string? GeneratedEditorConfigPath, bool IncludedByCompileGlob, BuildInputs Inputs);
    internal sealed record BuildInputs(IReadOnlyList<string> Definitions, IReadOnlyList<string> ImmutableRoots);
    internal sealed record Reference(string TargetPath, string DefiningPath, string Include, bool EmbedInteropTypes, IReadOnlyList<string> Aliases, int Line);

    internal static IReadOnlyDictionary<string, string> Properties(Solution solution) =>
        solution.Workspace is MSBuildWorkspace workspace ? workspace.Properties
            : new Dictionary<string, string>();

    internal static IReadOnlyList<Reference> References(Project project, IReadOnlyDictionary<string, string> properties)
    {
        MsBuildInitializer.EnsureInitialized();
        return ReadCore(project.FilePath ?? throw new PublicInvalidOperationException("A project has no physical path."), properties, definition: null, compilePath: null).References;
    }

    internal static IReadOnlyList<Reference> References(Project project, IReadOnlyDictionary<string, string> properties, byte[] definition)
    {
        MsBuildInitializer.EnsureInitialized();
        return ReadCore(project.FilePath ?? throw new PublicInvalidOperationException("A project has no physical path."),
            properties, definition, compilePath: null).References;
    }

    internal static string? GeneratedEditorConfigPath(string path, IReadOnlyDictionary<string, string> properties)
    {
        MsBuildInitializer.EnsureInitialized();
        return ReadCore(path, properties, definition: null, compilePath: null).GeneratedEditorConfigPath;
    }

    internal static bool IncludedByCompileGlob(Project project, IReadOnlyDictionary<string, string> properties, string candidatePath)
    {
        MsBuildInitializer.EnsureInitialized();
        return ReadCore(project.FilePath ?? throw new PublicInvalidOperationException("A project has no physical path."),
            properties, definition: null, candidatePath).IncludedByCompileGlob;
    }

    internal static BuildInputs DefinitionInputs(EvaluatedBuildGraph.Node node) => node.BuildInputs;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string? CaptureGeneratedEditorConfigPath(Microsoft.Build.Evaluation.Project evaluated)
    {
        string? generatedConfig = null;
        if (evaluated.Targets.TryGetValue("GenerateMSBuildEditorConfigFileShouldRun", out var generator)
            && generator.FullPath.EndsWith("Microsoft.Managed.Core.targets", StringComparison.OrdinalIgnoreCase))
        {
            var configured = EscapedProperty("GeneratedMSBuildEditorConfigFile");
            if (string.IsNullOrEmpty(configured))
                configured = EscapedProperty("IntermediateOutputPath")
                    + EscapedProperty("MSBuildProjectName") + ".GeneratedMSBuildEditorConfig.editorconfig";
            // The SDK passes this property to an ITaskItem parameter: normalize native
            // separators before unescaping, preserving an escaped literal separator.
            var taskPath = new Microsoft.Build.Utilities.TaskItem(configured).ItemSpec;
            generatedConfig = Path.GetFullPath(taskPath, Path.GetDirectoryName(evaluated.FullPath)!);
        }
        return generatedConfig;

        string EscapedProperty(string name) => evaluated.GetProperty(name) is { } property
            ? Microsoft.Build.Evaluation.Project.GetPropertyValueEscaped(property) : "";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evaluation ReadCore(string path, IReadOnlyDictionary<string, string> properties, byte[]? definition, string? compilePath)
    {
        using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        try
        {
            var globalProperties = properties.ToDictionary(pair => pair.Key, pair => pair.Value);
            Microsoft.Build.Evaluation.Project evaluated;
            if (definition is null) evaluated = collection.LoadProject(path, globalProperties, toolsVersion: null);
            else
            {
                using var text = new StringReader(CsprojSemanticEquality.CreateSnapshot(definition).Content);
                using var reader = System.Xml.XmlReader.Create(text);
                var root = Microsoft.Build.Construction.ProjectRootElement.Create(reader, collection);
                root.FullPath = path;
                evaluated = new Microsoft.Build.Evaluation.Project(root, globalProperties, toolsVersion: null, collection);
            }
            var references = evaluated.GetItems("ProjectReference")
                .Where(item => !string.Equals(item.GetMetadataValue("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(item.GetMetadataValue("OutputItemType"), "Analyzer", StringComparison.OrdinalIgnoreCase))
                .Select(item => new Reference(
                item.GetMetadataValue("FullPath"),
                item.Xml.ContainingProject.FullPath, item.UnevaluatedInclude,
                string.Equals(item.GetMetadataValue("EmbedInteropTypes"), "true", StringComparison.OrdinalIgnoreCase),
                item.GetMetadataValue("Aliases").Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                item.Xml.Location.Line)).ToArray();
            var generatedConfig = CaptureGeneratedEditorConfigPath(evaluated);
            var immutableRoots = BuildInputProvenance.ToolRoots();
            var definitions = evaluated.Imports.Select(import => import.ImportedProject.FullPath).Append(path)
                .Distinct(FileSystemPath.Comparer).ToArray();
            return new Evaluation(references, generatedConfig, compilePath is not null
                && evaluated.GetAllGlobs("Compile").Any(glob => glob.MsBuildGlob.IsMatch(compilePath)),
                new BuildInputs(definitions, immutableRoots));
        }
        catch (Microsoft.Build.Exceptions.InvalidProjectFileException ex)
        {
            throw InvalidOperationErrors.Internal("The physical project graph could not be evaluated (" + ex.ErrorCode + ").");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception failure)
        {
            throw InvalidOperationErrors.Internal("The physical project graph could not be evaluated (" + failure.GetType().Name + ").");
        }
        finally { collection.UnloadAllProjects(); }
    }

    // Normalize only an independently owned native load. The context issues its own
    // actual Csc-input proof before correcting metadata omitted by MSBuildWorkspace.
    internal static Task<UnresolvedAnalyzerReferenceStripper.Projection> WithPersistedReferenceMetadataAsync(
        string workspaceId, NativeCompilerContextCertificate context, IWorkspacePathPolicy pathPolicy, CancellationToken ct)
        => context.NormalizeAsync(workspaceId, pathPolicy, ct);

}

internal interface IWorkspaceEvaluationLoader
{
    void AssertForkPathPolicy(ForkInvocationPathPolicy policy);
    SupplementalPackageReadAuthority CapturePackageReadAuthority(string workspaceId, Solution reviewedSource);
    ForkInvocationAuthority CaptureStageInvocationAuthority(string workspaceId, Solution reviewedSource);
    ForkInvocationAuthority CaptureForkInvocationAuthority(string workspaceId, Solution reviewedSource,
        string destinationPath, IReadOnlyDictionary<string, string> properties,
        Helpers.PhysicalDirectoryScope.OwnedRoot destination, PhysicalGraphLayout layout);
    Task<RoslynMcp.Core.Models.WorkspaceStatusDto> LoadWithPropertiesAsync(string path,
        IDictionary<string, string> properties, SupplementalPackageReadAuthority supplementalPackages,
        ForkInvocationAuthority? invocation, CancellationToken ct);
    Task<RoslynMcp.Core.Models.WorkspaceStatusDto> ReloadWithInvocationAuthorityAsync(string workspaceId,
        ForkInvocationAuthority invocation, CancellationToken ct);
}
