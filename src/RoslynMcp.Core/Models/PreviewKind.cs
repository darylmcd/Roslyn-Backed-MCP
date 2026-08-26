namespace RoslynMcp.Core.Models;

/// <summary>
/// <b>preview-token-apply-route-provenance:</b> machine-checkable discriminator recording which
/// producer family created a preview token, so an apply route can verify a token's provenance
/// before mutating the workspace instead of redeeming any token that reaches the shared store.
/// </summary>
/// <remarks>
/// <para>
/// Semantics are permissive-by-default so producers can be tagged incrementally without a flag
/// day: <see cref="Unspecified"/> means "no provenance recorded" and MUST be accepted by every
/// apply route. A concrete member is enforceable — a route that knows which kind it consumes may
/// reject a token whose kind is concrete and different.
/// </para>
/// <para>
/// This type lives in <c>RoslynMcp.Core.Models</c> (dependency-free, already referenced by both
/// <c>RoslynMcp.Roslyn</c> and <c>RoslynMcp.Host.Stdio</c>) so host-side apply routes can consume
/// it without a new project reference.
/// </para>
/// </remarks>
public enum PreviewKind
{
    /// <summary>
    /// No provenance recorded. Permissive: every apply route accepts a token with this kind.
    /// This is the default for producers that have not yet been tagged.
    /// </summary>
    Unspecified = 0,

    /// <summary>A symbol rename preview.</summary>
    SymbolRename,

    /// <summary>A whole-document formatting preview.</summary>
    FormatDocument,

    /// <summary>A range-scoped formatting preview.</summary>
    FormatRange,

    /// <summary>An organize-usings / import-cleanup preview.</summary>
    OrganizeUsings,

    /// <summary>A diagnostic code-fix preview.</summary>
    CodeFix,

    /// <summary>A batched multi-file text-edit preview.</summary>
    MultiFileEdit,

    /// <summary>A source-file creation preview.</summary>
    FileCreate,

    /// <summary>A source-file deletion preview.</summary>
    FileDelete,

    /// <summary>A source-file move/rename preview.</summary>
    FileMove,

    /// <summary>A Roslyn code-action preview.</summary>
    CodeAction,

    /// <summary>A solution-wide fix-all preview.</summary>
    FixAll,
}
