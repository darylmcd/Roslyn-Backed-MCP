namespace RoslynMcp.Core.Models;

/// <summary>
/// File-granularity rollup of consumers for a given type — one entry per source file that
/// references the type, with a deduped list of usage <see cref="Kinds"/> and the total number
/// of reference sites within the file.
/// </summary>
/// <remarks>
/// find-type-consumers-file-granularity-rollup: produced by <c>find_type_consumers</c>. Replaces
/// the per-site Grep fallback for "which files touch this type" workflows. Kinds are classified
/// via syntax-node walk against each reference site; unrecognized contexts surface as
/// <c>"other"</c> rather than failing.
/// </remarks>
/// <param name="FilePath">Absolute path of the source file containing the references.</param>
/// <param name="Kinds">
/// Deduped, sorted list of the kinds of references found in the file. Values are drawn from
/// <c>using</c>, <c>ctor</c>, <c>inherit</c>, <c>field</c>, <c>local</c>, and <c>other</c>.
/// </param>
/// <param name="Count">Total number of reference sites in the file (sum across all kinds).</param>
public sealed record TypeConsumerFileRollupDto(
    string FilePath,
    IReadOnlyList<string> Kinds,
    int Count);
