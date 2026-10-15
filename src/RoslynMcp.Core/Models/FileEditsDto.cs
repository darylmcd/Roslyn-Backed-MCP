using System.Collections.Immutable;

namespace RoslynMcp.Core.Models;

/// <summary>
/// Groups a set of text edits to be applied to a single file.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Edits"/> stores an ordered read-only snapshot on construction and init assignment.
/// Mutating the caller's collection cannot change the DTO's value or hash, and the exposed
/// collection does not permit mutation through a mutable list interface.
/// </para>
/// <para>
/// The interface swap alone does NOT restore record value equality — the compiler-synthesized
/// <c>Equals</c>/<c>GetHashCode</c> route collection properties through
/// <c>EqualityComparer&lt;T&gt;.Default</c>, which for any of the concrete collection types that
/// back this interface (arrays, <c>List&lt;T&gt;</c>) falls back to reference equality. The
/// explicit <see cref="Equals(FileEditsDto)"/> / <see cref="GetHashCode"/> overrides below do the
/// structural element-wise comparison instead, so two instances holding equal edit sequences
/// compare equal regardless of which concrete collection carries them. The record-generated
/// <c>==</c>/<c>!=</c> operators call through to these overrides automatically.
/// </para>
/// </remarks>
/// <param name="FilePath">The absolute path to the file being edited.</param>
/// <param name="Edits">Edits may be supplied in any application order and are copied into a read-only snapshot. Element order participates in value equality and hashing.</param>
public sealed record FileEditsDto(string FilePath, IReadOnlyList<TextEditDto> Edits)
{
    private readonly ImmutableArray<TextEditDto> _edits = SnapshotEdits(Edits);

    /// <summary>Ordered edit snapshot; init assignment copies the supplied collection.</summary>
    public IReadOnlyList<TextEditDto> Edits
    {
        get => _edits;
        init => _edits = SnapshotEdits(value);
    }

    private static ImmutableArray<TextEditDto> SnapshotEdits(IReadOnlyList<TextEditDto> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return ImmutableArray.CreateRange(edits);
    }

    /// <summary>
    /// Structural equality: <see cref="FilePath"/> compared ordinally (matching the
    /// synthesized behaviour it replaces) and <see cref="Edits"/> compared element-wise in
    /// order. <see cref="TextEditDto"/> is an all-primitive record, so its own synthesized
    /// value equality makes the element comparison correct.
    /// </summary>
    public bool Equals(FileEditsDto? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
            && Edits.SequenceEqual(other.Edits);
    }

    /// <summary>
    /// Order-sensitive element-wise hash over <see cref="Edits"/> combined with
    /// <see cref="FilePath"/>, so any two instances that are <see cref="Equals(FileEditsDto)"/>
    /// -true necessarily produce the same hash code.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FilePath, StringComparer.Ordinal);
        foreach (var edit in Edits)
        {
            hash.Add(edit);
        }

        return hash.ToHashCode();
    }
}
