namespace RoslynMcp.Core.Models;

/// <summary>
/// A single structured reason why a refactoring refused to proceed, naming the member that
/// blocks the operation and why. Emitted alongside the prose error message so callers can
/// programmatically adjust their request (e.g. widen an <c>extract_type_preview</c>
/// <c>memberNames</c> set) instead of string-matching the message.
/// </summary>
/// <param name="Member">The member name the refusal is attributed to.</param>
/// <param name="Reason">Human-readable explanation of why this member blocks the operation.</param>
public sealed record BlockingDependencyDto(
    string Member,
    string Reason);
