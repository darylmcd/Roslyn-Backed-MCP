using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class ReferenceService : IReferenceService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<ReferenceService> _logger;

    public ReferenceService(IWorkspaceManager workspace, ILogger<ReferenceService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var refLocations = references.SelectMany(r => r.Locations).ToList();
        return await MaterializeReferenceLocationsAsync(refLocations, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a flat list of <see cref="ReferenceLocation"/>s to <see cref="LocationDto"/>s in parallel.
    /// Each location requires an async preview-text fetch and a containing-symbol lookup; doing them
    /// sequentially is the dominant cost for high-reference symbols. Uses a bounded semaphore so we
    /// don't blow up document caches on large fan-outs.
    /// </summary>
    private static async Task<IReadOnlyList<LocationDto>> MaterializeReferenceLocationsAsync(
        IReadOnlyList<ReferenceLocation> refLocations, CancellationToken ct)
    {
        if (refLocations.Count == 0) return [];

        var parallelism = Math.Min(Environment.ProcessorCount, 8);
        using var semaphore = new SemaphoreSlim(parallelism, parallelism);

        var tasks = new Task<LocationDto>[refLocations.Count];
        for (var i = 0; i < refLocations.Count; i++)
        {
            var refLocation = refLocations[i];
            tasks[i] = ToLocationDtoAsync(refLocation, semaphore, ct);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<LocationDto> ToLocationDtoAsync(
        ReferenceLocation refLocation, SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = refLocation.Document;
            var preview = await SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
            var containingSymbol = await SymbolServiceHelpers.GetContainingSymbolAsync(doc, refLocation.Location, ct).ConfigureAwait(false);
            var classification = SymbolMapper.ClassifyReferenceLocation(refLocation);
            return SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview, classification);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        var implementations = await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false);
        var results = new List<LocationDto>();

        foreach (var impl in implementations)
        {
            foreach (var location in impl.Locations.Where(l => l.IsInSource))
            {
                var doc = solution.GetDocument(location.SourceTree!);
                var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
                results.Add(SymbolMapper.ToLocationDto(location, impl, preview));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<LocationDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return [];
        }

        var overrides = await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false);
        return await SymbolServiceHelpers.SymbolsToLocationsAsync(overrides, solution, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocationDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null)
        {
            return [];
        }

        return await SymbolServiceHelpers.SymbolsToLocationsAsync(SymbolServiceHelpers.GetBaseMembers(symbol), solution, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct)
    {
        if (symbols.Count > 50)
            throw new ArgumentException("Maximum of 50 symbols per bulk request.");

        var solution = _workspace.GetCurrentSolution(workspaceId);
        // Outer fan-out: how many bulk symbols we resolve concurrently. Each task may itself
        // spawn parallel preview fetches inside MaterializeReferenceLocationsAsync, so keep
        // the outer cap modest on big boxes to avoid runaway document-cache pressure.
        var bulkParallelism = Math.Clamp(Environment.ProcessorCount / 2, 4, 8);
        using var semaphore = new SemaphoreSlim(bulkParallelism, bulkParallelism);

        async Task<BulkReferenceResultDto> ProcessOneAsync(BulkSymbolLocator bulk, int index)
        {
            var key = bulk.SymbolHandle ?? bulk.MetadataName
                ?? (bulk.FilePath is not null && bulk.Line.HasValue && bulk.Column.HasValue
                    ? $"{bulk.FilePath}:{bulk.Line}:{bulk.Column}"
                    : $"symbol[{index}]");

            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var locator = ToSymbolLocator(bulk);
                var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
                if (symbol is null)
                    return new BulkReferenceResultDto(key, null, 0, [], "Symbol could not be resolved.");

                var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
                var locations = new List<LocationDto>();

                if (includeDefinition)
                {
                    foreach (var loc in symbol.Locations.Where(l => l.IsInSource))
                    {
                        var doc = solution.GetDocument(loc.SourceTree!);
                        var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, loc, ct).ConfigureAwait(false) : null;
                        locations.Add(SymbolMapper.ToLocationDto(loc, symbol, preview, "Definition"));
                    }
                }

                var refLocations = references.SelectMany(r => r.Locations).ToList();
                var refDtos = await MaterializeReferenceLocationsAsync(refLocations, ct).ConfigureAwait(false);
                locations.AddRange(refDtos);

                return new BulkReferenceResultDto(key, symbol.ToDisplayString(), locations.Count, locations, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new BulkReferenceResultDto(key, null, 0, [], ex.Message);
            }
            finally
            {
                semaphore.Release();
            }
        }

        var tasks = symbols.Select((s, i) => ProcessOneAsync(s, i)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static SymbolLocator ToSymbolLocator(BulkSymbolLocator bulk)
    {
        if (!string.IsNullOrWhiteSpace(bulk.SymbolHandle))
            return SymbolLocator.ByHandle(bulk.SymbolHandle);
        if (!string.IsNullOrWhiteSpace(bulk.MetadataName))
            return SymbolLocator.ByMetadataName(bulk.MetadataName);
        if (!string.IsNullOrWhiteSpace(bulk.FilePath) && bulk.Line.HasValue && bulk.Column.HasValue)
            return SymbolLocator.BySource(bulk.FilePath, bulk.Line.Value, bulk.Column.Value);
        throw new ArgumentException("BulkSymbolLocator requires symbolHandle, metadataName, or filePath/line/column.");
    }
}
