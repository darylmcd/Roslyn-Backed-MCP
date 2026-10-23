using System.Collections.Immutable;
using Microsoft.Build.Execution;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Evaluated declaration/default metadata used by SDK-added project-reference items.</summary>
internal sealed record CompilerProjectReferences(bool DisableTransitive,
    CompilerProjectReferenceMetadata Defaults, ImmutableArray<CompilerProjectReferenceDeclaration> Declared,
    ImmutableArray<string> NativeCompileTargets, string? MicrosoftNetBuildTasksAssembly)
{
    internal static CompilerProjectReferences Capture(ProjectInstance instance,
        IReadOnlyList<ImmutableArray<CompilerProjectReferenceTarget>> targets)
    {
        var directory = Path.GetDirectoryName(instance.FullPath)!;
        var buildTasks = instance.GetPropertyValue("MicrosoftNETBuildTasksAssembly");
        var defaults = instance.ItemDefinitions.TryGetValue("ProjectReference", out var definition)
            ? Read(name => definition.GetMetadata(name)?.EvaluatedValue ?? "") : Read(static _ => "");
        return new(instance.GetPropertyValue("DisableTransitiveProjectReferences").Equals("true", StringComparison.OrdinalIgnoreCase),
            defaults, instance.GetItems("ProjectReference").Select((item, index) => new CompilerProjectReferenceDeclaration(
                item.GetMetadataValue("FullPath"), Read(item.GetMetadataValue), targets[index])).ToImmutableArray(),
            instance.Targets.ContainsKey("DesignTimeMarkupCompilation") ? ["Compile", "CoreCompile", "DesignTimeMarkupCompilation"] : ["Compile", "CoreCompile"],
            string.IsNullOrWhiteSpace(buildTasks) ? null : Path.GetFullPath(buildTasks, directory));
    }

    private static CompilerProjectReferenceMetadata Read(Func<string, string> metadata) => new(
        !metadata("ReferenceOutputAssembly").Equals("false", StringComparison.OrdinalIgnoreCase)
            && !metadata("OutputItemType").Equals("Analyzer", StringComparison.OrdinalIgnoreCase),
        metadata("Aliases").Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToImmutableArray(),
        metadata("EmbedInteropTypes").Equals("true", StringComparison.OrdinalIgnoreCase));
}

internal sealed record CompilerProjectReferenceMetadata(bool ReferenceOutputAssembly, ImmutableArray<string> Aliases, bool EmbedInteropTypes);
internal sealed record CompilerProjectReferenceDeclaration(string Path, CompilerProjectReferenceMetadata Metadata,
    ImmutableArray<CompilerProjectReferenceTarget> Targets);
internal sealed record CompilerProjectReferenceTarget(string Path, string? Output, string Framework,
    ImmutableDictionary<string, string> Globals);
