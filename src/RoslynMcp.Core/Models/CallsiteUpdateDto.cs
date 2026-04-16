namespace RoslynMcp.Core.Models;

/// <summary>
/// Compact summary of an apply path's callsite work for one file. Surfaces in
/// <see cref="RefactoringPreviewDto.CallsiteUpdates"/> so callers can audit total
/// reach (file + invocation count) even when the per-file unified diff would
/// inflate the payload past the MCP cap. Populated by tools whose apply path may
/// rewrite invocations across many files (e.g., <c>change_signature_preview</c>
/// op=remove on an interface method that dispatches across N implementer call sites).
/// </summary>
/// <param name="FilePath">Absolute path to the file containing one or more rewritten invocations.</param>
/// <param name="CallsiteCount">Number of invocations rewritten in <paramref name="FilePath"/> for this preview.</param>
public sealed record CallsiteUpdateDto(string FilePath, int CallsiteCount);
