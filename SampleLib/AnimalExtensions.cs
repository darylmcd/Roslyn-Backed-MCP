namespace SampleLib;

public static class AnimalExtensions
{
    public static string Describe(this IAnimal animal)
    {
        return $"{animal.Name}: {animal.Speak()}";
    }
}
