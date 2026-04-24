namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a source location where a property is written.
/// </summary>
/// <param name="WriteKind">
/// Classifies the syntactic shape of the write. One of:
/// <list type="bullet">
///   <item><c>"Assignment"</c> — ordinary <c>obj.Prop = value</c> (post-construction).</item>
///   <item><c>"ObjectInitializer"</c> — <c>new T { Prop = value }</c> (safe for <c>init</c>).</item>
///   <item><c>"OutRef"</c> — property passed by <c>out</c>/<c>ref</c> to a method call.</item>
///   <item>
///     <c>"PrimaryConstructorBind"</c> — the property is a positional-record
///     primary-constructor parameter and the site is a <c>new T(value)</c> call that binds
///     to this positional slot. Emitted by <c>find-property-writes-positional-record-silent-zero</c>
///     so callers see the ctor-call write sites that <c>SymbolFinder.FindReferencesAsync</c>
///     attributes to the primary-ctor parameter rather than the synthesized property.
///   </item>
/// </list>
/// </param>
public sealed record PropertyWriteDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? ContainingMember,
    string? PreviewText,
    string WriteKind);
