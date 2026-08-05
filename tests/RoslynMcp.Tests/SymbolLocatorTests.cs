using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SymbolLocatorTests
{
    [TestMethod]
    public void Validate_RejectsLocatorWithoutCompleteStrategy()
    {
        var locator = new SymbolLocator("Sample.cs", Line: 1, Column: null, null, null);

        var exception = Assert.ThrowsExactly<ArgumentException>(locator.Validate);

        StringAssert.Contains(exception.Message, "file path with line/column");
    }

    [TestMethod]
    public void Validate_AcceptsCompleteSourceLocation()
    {
        var locator = SymbolLocator.BySource("Sample.cs", line: 1, column: 1);

        locator.Validate();

        Assert.IsTrue(locator.HasSourceLocation);
        Assert.IsFalse(locator.HasHandle);
        Assert.IsFalse(locator.HasMetadataName);
    }

    [TestMethod]
    public void Validate_AcceptsHandleWithoutSourceLocation()
    {
        var locator = SymbolLocator.ByHandle("symbol-handle");

        locator.Validate();

        Assert.IsFalse(locator.HasSourceLocation);
        Assert.IsTrue(locator.HasHandle);
        Assert.IsFalse(locator.HasMetadataName);
    }

    [TestMethod]
    public void Validate_AcceptsMetadataNameWithoutOtherStrategies()
    {
        var locator = SymbolLocator.ByMetadataName("Namespace.Type");

        locator.Validate();

        Assert.IsFalse(locator.HasSourceLocation);
        Assert.IsFalse(locator.HasHandle);
        Assert.IsTrue(locator.HasMetadataName);
    }
}
