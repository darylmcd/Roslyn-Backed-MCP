namespace RoslynMcp.Core.Models;

/// <summary>
/// Groups a set of text edits to be applied to a single file.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Edits"/> is deliberately typed as <see cref="IReadOnlyList{T}"/> rather than
/// <c>TextEditDto[]</c>: an array-typed record property hands every holder of the DTO a live
/// mutable reference, so one caller doing <c>dto.Edits[0] = …</c> silently corrupts the edit
/// sequence observed by every other holder of the same instance.
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
/// <param name="Edits">The text edits to apply, in any order.</param>
public sealed record FileEditsDto(string FilePath, IReadOnlyList<TextEditDto> Edits)
{
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
