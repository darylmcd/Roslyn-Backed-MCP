using SampleLib;
using SampleLib.Hierarchy;

var service = new AnimalService();
var animals = service.GetAllAnimals();
service.MakeThemSpeak(animals);

var count = service.CountAnimals(animals);
Console.WriteLine($"Total animals: {count}");

// Calls extension method via obj.Method() syntax (reduced extension method)
var description = animals.First().Describe();
Console.WriteLine(description);

// Calls CountAnimals(IEnumerable<IAnimal>) via implicit conversion from IAnimal[] -> IEnumerable<IAnimal>
IAnimal[] animalArray = animals.ToArray();
var arrayCount = service.CountAnimals(animalArray);
Console.WriteLine($"Array count: {arrayCount}");

var shapes = new List<Shape>
{
    new Circle(5.0),
    new Rectangle(3.0, 4.0)
};

foreach (var shape in shapes)
{
    Console.WriteLine(shape.Describe());
}
