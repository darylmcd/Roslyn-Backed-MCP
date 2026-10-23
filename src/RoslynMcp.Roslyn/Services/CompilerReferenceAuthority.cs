using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Independent evaluated authored and assets-certified implicit compiler edges.</summary>
internal sealed class CompilerReferenceAuthority
{
    internal sealed record ProjectEdges(ImmutableHashSet<string> Authored, ImmutableHashSet<string> Implicit);
    private readonly IReadOnlyDictionary<string, ProjectEdges> _projects;
    internal CompilerReferenceAuthority(IReadOnlyDictionary<string, ProjectEdges> projects) => _projects = projects;

    internal static CompilerReferenceAuthority ForNonRenderingDelta(Solution original, Solution modified)
    {
        if (modified.GetChanges(original).GetProjectChanges().Any(SolutionFileMutationPlanner.RequiresPhysicalProjectRender))
            throw new PublicInvalidOperationException("Physical project rendering requires evaluated compiler-reference authority.");
        return new(new Dictionary<string, ProjectEdges>());
    }

    internal void AssertCurrent(Project project)
    {
        var proof = Require(project);
        var actual = proof.Authored.Union(proof.Implicit);
        var expected = project.ProjectReferences.Select(reference => Key(project.Solution, reference)).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual))
            throw new PublicInvalidOperationException("The source project graph has external edits; reload the workspace and recreate the preview.");
    }

    internal void AssertCovers(Project project) => _ = Require(project);
    internal bool IsImplicit(Project originalOwner, ProjectReference reference) => originalOwner.Solution.GetProject(reference.ProjectId) is not null
        && Require(originalOwner).Implicit.Contains(Key(originalOwner.Solution, reference));

    private ProjectEdges Require(Project project) => _projects.TryGetValue(PhysicalProjectIdentity.Key(project, static path => path), out var proof)
        ? proof : throw new PublicInvalidOperationException("The physical project has no captured compiler-reference authority.");

    internal static string Key(Solution solution, ProjectReference reference)
    {
        var target = solution.GetProject(reference.ProjectId)
            ?? throw new PublicInvalidOperationException("A compiler reference has no target project.");
        return PhysicalProjectIdentity.Key(target, static path => path)
            + SolutionFileMutationPlanner.ReferenceMetadataKey(reference.Aliases, reference.EmbedInteropTypes);
    }

    internal static string Key(string path, string? output, IEnumerable<string> aliases, bool embed) =>
        PhysicalProjectIdentity.Key(path, output, static value => value) + SolutionFileMutationPlanner.ReferenceMetadataKey(aliases, embed);
}
