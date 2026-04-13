using Microsoft.VisualStudio.TestTools.UnitTesting;
using SampleLib;

namespace SampleLib.Tests;

[TestClass]
public class AnimalServiceTests
{
    [TestMethod]
    public void CountAnimals_Returns_Total_Count()
    {
        var service = new AnimalService();
        var animals = service.GetAllAnimals();

        var count = service.CountAnimals(animals);

        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public void GetAllAnimals_Returns_Dog_And_Cat()
    {
        var service = new AnimalService();

        var animals = service.GetAllAnimals();

        CollectionAssert.AreEquivalent(new[] { "Dog", "Cat" }, animals.Select(a => a.Name).ToList());
    }
}
