namespace SampleLib;

public class Dog : IAnimal
{
    public string Name => "Dog";

    public string Speak() => "Woof";

    public void Fetch(string item)
    {
        Console.WriteLine($"Fetching {item}");
    }
}
