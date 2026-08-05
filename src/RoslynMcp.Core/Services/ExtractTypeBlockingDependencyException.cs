using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Refusal error for <c>extract_type_preview</c> that carries the structured blocking-member
/// data the refusal was computed from, on top of the existing prose message.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> — mirroring
/// <see cref="SymbolNotFoundException"/>'s relationship to <see cref="KeyNotFoundException"/> —
/// so every existing <c>catch (InvalidOperationException)</c> and the <c>ToolErrorHandler</c>
/// classification table keep matching unchanged: the error <c>category</c> stays
/// <c>InvalidOperation</c> and the prose <c>message</c> is untouched. Only the JSON envelope
/// gains an extra <c>blockingDependencies</c> field.
/// </remarks>
public sealed class ExtractTypeBlockingDependencyException : InvalidOperationException
{
    public ExtractTypeBlockingDependencyException(string message, IReadOnlyList<BlockingDependencyDto> blockingDependencies)
        : base(message)
    {
        BlockingDependencies = blockingDependencies;
    }

    public IReadOnlyList<BlockingDependencyDto> BlockingDependencies { get; }
}
