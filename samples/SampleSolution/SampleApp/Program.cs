using SampleLib;
using SampleLib.Hierarchy;

var service = new AnimalService();
var animals = service.GetAllAnimals();
service.MakeThemSpeak(animals);

var count = service.CountAnimals(animals);
Console.WriteLine($"Total animals: {count}");

var shapes = new List<Shape>
{
    new Circle(5.0),
    new Rectangle(3.0, 4.0)
};

foreach (var shape in shapes)
{
    Console.WriteLine(shape.Describe());
}
