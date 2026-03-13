namespace SampleLib;

public class Cat : IAnimal
{
    public string Name => "Cat";

    public string Speak() => "Meow";

    public void Purr()
    {
        Console.WriteLine("Purring...");
    }
}
