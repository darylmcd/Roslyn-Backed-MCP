using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Tests;

[TestClass]
public class CatalogTypeNameFormatterTests
{
    // catalog-formattypename-dedup: FormatTypeName was duplicated verbatim in
    // PromptParameterIndex and ToolParameterIndex; both now delegate to the shared
    // CatalogTypeNameFormatter. This pins the label contract both index builders rely on.

    [TestMethod]
    public void FormatTypeName_MapsPrimitivesToKeywords()
    {
        Assert.AreEqual("string", CatalogTypeNameFormatter.FormatTypeName(typeof(string)));
        Assert.AreEqual("int", CatalogTypeNameFormatter.FormatTypeName(typeof(int)));
        Assert.AreEqual("long", CatalogTypeNameFormatter.FormatTypeName(typeof(long)));
        Assert.AreEqual("bool", CatalogTypeNameFormatter.FormatTypeName(typeof(bool)));
        Assert.AreEqual("object", CatalogTypeNameFormatter.FormatTypeName(typeof(object)));
    }

    [TestMethod]
    public void FormatTypeName_UnwrapsNullableValueType()
    {
        Assert.AreEqual("int?", CatalogTypeNameFormatter.FormatTypeName(typeof(int?)));
    }

    [TestMethod]
    public void FormatTypeName_StripsGenericArityAndRecursesIntoArguments()
    {
        Assert.AreEqual("List<string>", CatalogTypeNameFormatter.FormatTypeName(typeof(List<string>)));
        Assert.AreEqual(
            "Dictionary<string, int>",
            CatalogTypeNameFormatter.FormatTypeName(typeof(Dictionary<string, int>)));
    }
}
