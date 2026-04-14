using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// dead-interface-member-removal-guided: composes
/// <see cref="SymbolFinder.FindImplementationsAsync(ISymbol, Solution, System.Threading.CancellationToken)"/>
/// + <see cref="SymbolFinder.FindReferencesAsync(ISymbol, Solution, System.Threading.CancellationToken)"/>
/// + <see cref="IDeadCodeService.PreviewRemoveDeadCodeAsync"/> so callers can remove an
/// interface member and every concrete implementation in one tool call.
/// </summary>
public interface IInterfaceMemberRemovalOrchestrator
{
    Task<InterfaceMemberRemovalResultDto> PreviewRemoveAsync(
        string workspaceId, string interfaceMemberHandle, bool removeEmptyFiles, CancellationToken ct);
}

/// <summary>
/// Either <see cref="PreviewToken"/> is non-null (status "previewed", apply via
/// <c>remove_dead_code_apply</c>) or <see cref="ExternalCallers"/> is non-empty
/// (status "refused", caller must update those sites first). The tool layer renders this
/// to JSON for the agent.
/// </summary>
public sealed record InterfaceMemberRemovalResultDto(
    string? PreviewToken,
    string Status,
    string? Reason,
    string InterfaceMember,
    int ImplementationCount,
    IReadOnlyList<string> Implementations,
    IReadOnlyList<ExternalCallerLocationDto> ExternalCallers,
    IReadOnlyList<FileChangeDto>? Changes,
    IReadOnlyList<string>? Warnings,
    string? Note);

public sealed record ExternalCallerLocationDto(string FilePath, int Line, int Column);

public sealed class InterfaceMemberRemovalOrchestrator : IInterfaceMemberRemovalOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly IDeadCodeService _deadCodeService;

    public InterfaceMemberRemovalOrchestrator(
        IWorkspaceManager workspace,
        IDeadCodeService deadCodeService)
    {
        _workspace = workspace;
        _deadCodeService = deadCodeService;
    }

    public async Task<InterfaceMemberRemovalResultDto> PreviewRemoveAsync(
        string workspaceId, string interfaceMemberHandle, bool removeEmptyFiles, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var locator = SymbolLocator.ByHandle(interfaceMemberHandle);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Symbol handle '{interfaceMemberHandle}' could not be resolved. The handle may be stale (workspace reloaded since it was issued) or invalid.");

        if (symbol.ContainingType?.TypeKind != TypeKind.Interface)
        {
            throw new InvalidOperationException(
                $"Symbol '{symbol.ToDisplayString()}' is not a member of an interface (kind: {symbol.ContainingType?.TypeKind}). " +
                "remove_interface_member_preview requires an interface method/property/event handle.");
        }

        if (symbol is not IMethodSymbol and not IPropertySymbol and not IEventSymbol)
        {
            throw new InvalidOperationException(
                $"Symbol kind '{symbol.Kind}' is not supported. Pass an interface method, property, or event.");
        }

        var implementations = await SymbolFinder
            .FindImplementationsAsync(symbol, solution, cancellationToken: ct)
            .ConfigureAwait(false);
        var implList = implementations.ToList();

        var implLocations = new HashSet<Location>(
            implList.SelectMany(i => i.Locations.Where(l => l.IsInSource)));
        var memberLocations = new HashSet<Location>(symbol.Locations.Where(l => l.IsInSource));

        var refs = await SymbolFinder
            .FindReferencesAsync(symbol, solution, cancellationToken: ct)
            .ConfigureAwait(false);

        var externalCallers = refs
            .SelectMany(r => r.Locations)
            .Select(rl => rl.Location)
            .Where(l => l.IsInSource && !implLocations.Contains(l) && !memberLocations.Contains(l))
            .Select(l => new ExternalCallerLocationDto(
                l.GetLineSpan().Path,
                l.GetLineSpan().StartLinePosition.Line + 1,
                l.GetLineSpan().StartLinePosition.Character + 1))
            .ToList();

        if (externalCallers.Count > 0)
        {
            return new InterfaceMemberRemovalResultDto(
                PreviewToken: null,
                Status: "refused",
                Reason: $"Interface member '{symbol.ToDisplayString()}' has {externalCallers.Count} external caller(s); refusing to remove. Update the callers first, then re-run.",
                InterfaceMember: symbol.ToDisplayString(),
                ImplementationCount: implList.Count,
                Implementations: implList.Select(i => i.ToDisplayString()).ToList(),
                ExternalCallers: externalCallers,
                Changes: null,
                Warnings: null,
                Note: null);
        }

        var allHandles = new List<string> { SymbolHandleSerializer.CreateHandle(symbol) };
        foreach (var impl in implList)
        {
            allHandles.Add(SymbolHandleSerializer.CreateHandle(impl));
        }

        var preview = await _deadCodeService.PreviewRemoveDeadCodeAsync(
            workspaceId,
            new DeadCodeRemovalDto(allHandles, removeEmptyFiles),
            ct).ConfigureAwait(false);

        return new InterfaceMemberRemovalResultDto(
            PreviewToken: preview.PreviewToken,
            Status: "previewed",
            Reason: null,
            InterfaceMember: symbol.ToDisplayString(),
            ImplementationCount: implList.Count,
            Implementations: implList.Select(i => i.ToDisplayString()).ToList(),
            ExternalCallers: [],
            Changes: preview.Changes,
            Warnings: preview.Warnings,
            Note: "Apply via remove_dead_code_apply with this previewToken.");
    }
}
