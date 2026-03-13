namespace SampleLib;

public class Dog : IAnimal
{
    private int _unusedField = 42;

    public string Name => "Dog";

    public string Speak() => "Woof";

    public void Fetch(string item)
    {
        Console.WriteLine($"Fetching {item}");
    }
}
