using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// A single reference location resolved against its document, with the per-location artifacts
/// (preview text, syntax root, semantic model, containing symbol) already materialized.
/// </summary>
/// <remarks>
/// Returned by <see cref="ReferenceLocationMaterializer.MaterializeAsync"/>. Callers that only
/// want a flat list of <see cref="LocationDto"/>s should use
/// <see cref="ReferenceLocationMaterializer.MaterializeDtosAsync"/> instead.
/// </remarks>
internal sealed record MaterializedReference(
    ReferenceLocation Source,
    LocationDto Dto,
    ISymbol? ContainingSymbol,
    SyntaxNode? SyntaxRoot,
    SemanticModel? SemanticModel);

/// <summary>
/// Resolves a flat list of <see cref="ReferenceLocation"/>s into <see cref="MaterializedReference"/>
/// records in parallel under a bounded semaphore. Each location triggers async preview-text,
/// syntax-root, and semantic-model fetches; running them sequentially is the dominant cost for
/// high-reference symbols on large solutions.
/// </summary>
/// <remarks>
/// The bounded semaphore (<see cref="Parallelism"/>) prevents huge fan-outs from hammering the
/// Roslyn document cache. The same parallelism heuristic is used in
/// <c>ReferenceService.MaterializeReferenceLocationsAsync</c> after perf #74; this helper exists
/// so other services (<c>MutationAnalysisService</c>, <c>ConsumerAnalysisService</c>) can reuse
/// the pattern instead of re-implementing it per call site.
/// </remarks>
internal static class ReferenceLocationMaterializer
{
    /// <summary>
    /// Bounded parallelism for per-location work. Caps at 8 even on high-core machines so that a
    /// single tool call can't exhaust the global execution gate's slots.
    /// </summary>
    private static int Parallelism => Math.Min(Environment.ProcessorCount, 8);

    /// <summary>
    /// Materializes every reference location into a rich record with the DTO, containing symbol,
    /// syntax root, and semantic model. Use this overload when callers need the syntax/semantic
    /// data for downstream classification (e.g., property write detection, type-usage analysis).
    /// </summary>
    public static async Task<IReadOnlyList<MaterializedReference>> MaterializeAsync(
        IReadOnlyList<ReferenceLocation> locations, CancellationToken ct)
    {
        if (locations.Count == 0) return [];

        using var semaphore = new SemaphoreSlim(Parallelism, Parallelism);
        var tasks = new Task<MaterializedReference>[locations.Count];
        for (var i = 0; i < locations.Count; i++)
        {
            tasks[i] = ToRichLocationAsync(locations[i], semaphore, ct);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Materializes every reference location into a flat <see cref="LocationDto"/> list.
    /// Convenience overload for callers (e.g., <c>ReferenceService</c>) that don't need the
    /// surrounding syntax/semantic data.
    /// </summary>
    /// <param name="summary">
    /// When <c>true</c>, drops <see cref="LocationDto.PreviewText"/> to keep responses small
    /// for high-fan-out symbols. Container/classification fields stay populated.
    /// (find-references-preview-text-inflates-response — Jellyfin stress test 2026-04-15
    /// Phase 7: <c>find_references(IUserManager)</c> returned 154 KB on 233 refs because
    /// per-ref preview text dominated the payload.)
    /// </param>
    public static async Task<IReadOnlyList<LocationDto>> MaterializeDtosAsync(
        IReadOnlyList<ReferenceLocation> locations, CancellationToken ct, bool summary = false)
    {
        var rich = await MaterializeAsync(locations, ct).ConfigureAwait(false);
        if (rich.Count == 0) return [];

        var dtos = new LocationDto[rich.Count];
        for (var i = 0; i < rich.Count; i++)
        {
            dtos[i] = summary
                ? rich[i].Dto with { PreviewText = null }
                : rich[i].Dto;
        }
        return dtos;
    }

    private static async Task<MaterializedReference> ToRichLocationAsync(
        ReferenceLocation refLocation, SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = refLocation.Document;

            // Kick off the three independent fetches concurrently inside the semaphore slot.
            // Roslyn's per-document caches mean these usually hit warm state, but the rare cold
            // path benefits from the overlap.
            var rootTask = doc.GetSyntaxRootAsync(ct);
            var modelTask = doc.GetSemanticModelAsync(ct);
            var previewTask = SymbolResolver.GetPreviewTextAsync(doc, refLocation.Location, ct);
            await Task.WhenAll(rootTask, modelTask, previewTask).ConfigureAwait(false);

            var root = await rootTask.ConfigureAwait(false);
            var model = await modelTask.ConfigureAwait(false);
            var preview = await previewTask.ConfigureAwait(false);

            ISymbol? containingSymbol = null;
            if (root is not null && model is not null)
            {
                containingSymbol = SymbolServiceHelpers.GetContainingSymbolFromRoot(root, model, refLocation.Location, ct);
            }

            var classification = SymbolMapper.ClassifyReferenceLocation(refLocation);
            var dto = SymbolMapper.ToLocationDto(refLocation.Location, containingSymbol, preview, classification);

            return new MaterializedReference(refLocation, dto, containingSymbol, root, model);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
