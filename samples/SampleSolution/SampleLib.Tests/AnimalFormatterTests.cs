using Microsoft.VisualStudio.TestTools.UnitTesting;
using SampleLib;

namespace SampleLib.Tests;

// Consumer of AnimalFormatter via explicit static-class invocation syntax.
// Used by RoslynMcp.Tests to assert that static-class consumers are classified as
// StaticMemberAccess / invocation rather than Other.
[TestClass]
public class AnimalFormatterTests
{
    [TestMethod]
    public void Format_ReturnsDogLabel()
    {
        var service = new AnimalService();
        var animals = service.GetAllAnimals();
        var dog = animals.First(a => a.Name == "Dog");

        // Explicit static-class invocation — this is what the classification fix targets.
        var result = AnimalFormatter.Format(dog);

        StringAssert.Contains(result, "Dog");
    }

    [TestMethod]
    public void FormatAll_ContainsAllAnimals()
    {
        var service = new AnimalService();
        var animals = service.GetAllAnimals();

        // Second explicit static-class invocation on the same class.
        var result = AnimalFormatter.FormatAll(animals);

        StringAssert.Contains(result, "Dog");
        StringAssert.Contains(result, "Cat");
    }
}
