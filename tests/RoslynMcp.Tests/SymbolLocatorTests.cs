using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SymbolLocatorTests
{
    [TestMethod]
    [DataRow(null, null, null, "filePath")]
    [DataRow("  ", 1, 1, "filePath")]
    [DataRow("Sample.cs", null, 1, "line")]
    [DataRow("Sample.cs", 1, null, "column")]
    public void Validate_IncompleteStrategy_NamesMissingParameter(string? filePath, int? line, int? column, string parameter)
    {
        var exception = Assert.ThrowsExactly<PublicArgumentException>(() =>
            new SymbolLocator(filePath, line, column, null, null).Validate());
        Assert.AreEqual(parameter, exception.ParamName);
        Assert.AreEqual("ArgumentException", exception.WireExceptionType);
        Assert.IsFalse(exception.PublicMessage.Contains("Sample.cs", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_CompleteHandleOrMetadata_TakesPrecedenceOverIncompleteSource()
    {
        new SymbolLocator("Sample.cs", null, null, "handle", null).Validate();
        new SymbolLocator("Sample.cs", null, null, null, "Namespace.Type").Validate();
    }

    [TestMethod]
    public void Validate_RejectsLocatorWithoutCompleteStrategy()
    {
        var locator = new SymbolLocator("Sample.cs", Line: 1, Column: null, null, null);

        var exception = Assert.ThrowsExactly<PublicArgumentException>(locator.Validate);

        StringAssert.Contains(exception.PublicMessage, "file path with line/column");
        Assert.AreEqual("column", exception.ParamName);
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
