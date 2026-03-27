using System.Threading;

namespace SampleLib;

public class AnimalService
{
    public List<IAnimal> GetAllAnimals()
    {
        return new List<IAnimal>
        {
            new Dog(),
            new Cat()
        };
    }

    public void MakeThemSpeak(    IEnumerable<IAnimal>     animals   )
    {
        foreach (var animal in animals)
        {
            var sound = animal.Speak();
            Console.WriteLine($"{animal.Name} says {sound}");
        }
    }

    public int CountAnimals(List<IAnimal> animals)
    {
        return animals.Count;
    }

    public int CountAnimals(IEnumerable<IAnimal> animals)
    {
        var count = 0;
        foreach (var _ in animals)
        {
            count++;
        }
        return count;
    }
}
