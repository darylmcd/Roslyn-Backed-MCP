using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class ParameterObjectService
{
    internal static async Task<bool> HasViableParameterGroupAsync(
        Solution solution, IMethodSymbol method, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ParameterObjectTargetContract.GetRefusalReason(method) is not null) return false;
        var shaped = method.Parameters.Where(parameter =>
            ParameterObjectTargetContract.GetParameterShapeRefusal(method, parameter) is null).ToArray();
        if (shaped.Length < 2) return false;

        var project = solution.GetProject(method.ContainingAssembly, ct)
            ?? throw InvalidOperationErrors.Internal("The measured method does not belong to the supplied solution.");
        var visibility = GetSameProjectDtoVisibility(method);
        var unsupportedUses = await CollectUnsupportedParameterUsesAsync(solution, method, shaped, ct).ConfigureAwait(false);
        var written = unsupportedUses.Select(use => use.Ordinal).ToHashSet();
        var typeName = GetUnusedPreflightIdentifier(method, "ParameterObjectSuggestion", ct);
        var parameterName = GetUnusedPreflightIdentifier(method, "parameterObjectSuggestion", ct);
        var candidates = new List<IParameterSymbol>();
        foreach (var parameter in shaped.Where(parameter => !written.Contains(parameter.Ordinal)))
        {
            if (await IsParameterViableAsync(solution, method, project, visibility,
                parameter, typeName, parameterName, ct).ConfigureAwait(false))
                candidates.Add(parameter);
        }
        if (candidates.Count < 2) return false;

        // Discover callers once. Pair trials reuse their captured immutable models/roots;
        // they never mint previews, create a destination, or mutate the loaded solution.
        var contexts = new List<CallSiteContext>();
        try
        {
            await foreach (var context in CaptureCallSiteContextsAsync(solution, method, ct).ConfigureAwait(false))
                contexts.Add(context);
        }
        catch (Exception exception) when (ParameterObjectRefusals.IsExpected(exception))
        {
            return false;
        }

        return await HasViablePairAsync(solution, method, project, candidates,
            contexts, typeName, parameterName, ct).ConfigureAwait(false);
    }

    private static async Task<bool> IsParameterViableAsync(
        Solution solution, IMethodSymbol method, Project project, bool visibility,
        IParameterSymbol parameter, string typeName, string parameterName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            ValidateGeneratedMemberNames([parameter], typeName);
            await EnforceGroupedParameterTypesAreDtoCompatibleAsync(
                solution, project, project, [parameter], visibility, ct).ConfigureAwait(false);
            await AnalyzeDeclarationMetadataAsync(solution, method, [parameter], parameterName, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (ParameterObjectRefusals.IsExpected(exception))
        {
            return false;
        }
    }

    private static async Task<bool> HasViablePairAsync(
        Solution solution, IMethodSymbol method, Project project, IReadOnlyList<IParameterSymbol> candidates,
        IReadOnlyList<CallSiteContext> contexts, string typeName, string parameterName, CancellationToken ct)
    {
        for (var first = 0; first < candidates.Count - 1; first++)
        {
            for (var second = first + 1; second < candidates.Count; second++)
            {
                ct.ThrowIfCancellationRequested();
                if (await IsPairViableAsync(solution, method, project,
                    [candidates[first], candidates[second]], contexts, typeName, parameterName, ct).ConfigureAwait(false))
                    return true;
            }
        }
        return false;
    }

    private static async Task<bool> IsPairViableAsync(
        Solution solution, IMethodSymbol method, Project project, IReadOnlyList<IParameterSymbol> pair,
        IReadOnlyList<CallSiteContext> contexts, string typeName, string parameterName, CancellationToken ct)
    {
        try
        {
            ValidateGeneratedMemberNames(pair, typeName);
            await AnalyzeDeclarationMetadataAsync(solution, method, pair, parameterName, ct).ConfigureAwait(false);
            var bindings = BindCallSites(contexts, method, pair, ct);
            EnforceCrossProjectReferences(solution, project, project, bindings);
            return (await CollectDefaultValueWarningsAsync(solution, pair, bindings, ct).ConfigureAwait(false)).Count == 0;
        }
        catch (Exception exception) when (ParameterObjectRefusals.IsExpected(exception))
        {
            return false;
        }
    }

    private static string GetUnusedPreflightIdentifier(IMethodSymbol method, string seed, CancellationToken ct)
    {
        var occupied = method.Parameters.Select(parameter => Capitalize(parameter.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var declaration in method.DeclaringSyntaxReferences)
        {
            foreach (var token in declaration.GetSyntax(ct).DescendantTokens())
                occupied.Add(token.ValueText);
        }
        while (occupied.Contains(seed)) seed += "_";
        return seed;
    }
}
