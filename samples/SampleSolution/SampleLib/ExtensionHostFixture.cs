namespace SampleLib;

/// <summary>
/// Fixture for <c>find-references-static-extension-host-blind-spot</c>: a static
/// extension-host class whose members are consumed EXCLUSIVELY via extension-method
/// invocation syntax (e.g. <c>animal.LoudName()</c>). The type-name token never
/// appears at the call sites, so Roslyn's type-level reference index returns 0
/// locations on the class itself — exposing the blind spot that
/// <see cref="RoslynMcp.Roslyn.Services.ConsumerAnalysisService.FindConsumersAsync"/>
/// must paper over by aggregating per-member references.
///
/// Distinct from <see cref="AnimalExtensions"/>: that fixture's <c>Describe</c> is
/// also consumed via extension-method syntax, but it shares a file with other
/// fixtures whose call patterns vary; this one is purpose-built so the test can
/// assert on the metadata name of the host without conflating siblings.
/// </summary>
public static class IAnimalNamingExtensions
{
    public static string LoudName(this IAnimal animal) =>
        animal.Name.ToUpperInvariant();

    public static string QuietName(this IAnimal animal) =>
        animal.Name.ToLowerInvariant();
}

/// <summary>
/// Consumer of the static extension-host above via extension-method syntax — the
/// type-name token never appears in this class's source, only the receiver-style
/// call <c>animal.LoudName()</c>. This is the canonical pattern that the type-level
/// reference index is blind to; the member-union fallback in
/// <c>ConsumerAnalysisService</c> is what surfaces this consumer in
/// <c>find_consumers</c> output.
///
/// Intentionally avoids a <c>cref</c> to the host class so the regression fixture
/// keeps the type-name token strictly absent at the syntactic level — a stray
/// XML-doc cref would create a type-level reference and mask the blind spot the
/// test exists to assert against.
/// </summary>
public sealed class AnimalNameAnnouncer
{
    public string AnnounceLoud(IAnimal animal) => animal.LoudName();

    public string AnnounceQuiet(IAnimal animal) => animal.QuietName();
}
