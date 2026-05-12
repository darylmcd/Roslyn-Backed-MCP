namespace SampleLib;

/// <summary>
/// Static helper for formatting animal descriptions.
/// Used by tests that assert static-class consumers are classified as
/// StaticMemberAccess / invocation rather than Other.
/// </summary>
public static class AnimalFormatter
{
    public static string Format(IAnimal animal) =>
        $"[{animal.Name}] says: {animal.Speak()}";

    public static string FormatAll(IEnumerable<IAnimal> animals) =>
        string.Join(", ", animals.Select(Format));
}
