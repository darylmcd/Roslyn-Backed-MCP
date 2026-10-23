using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerContextEncodingTests
{
    [TestMethod]
    [DataRow("unpaired")]
    [DataRow("replacement")]
    [DataRow("framing")]
    public void AnalyzerCatalogIdentity_BindsExactFramedCodeUnits(string shape)
    {
        var leftPath = shape == "framing" ? "AB" : "owner/\ud800.dll";
        var rightPath = shape switch { "framing" => "A", "replacement" => "owner/\ufffd.dll", _ => "owner/\ud801.dll" };
        var leftHash = shape == "framing" ? "C" : "same-controlled-content";
        var rightHash = shape == "framing" ? "BC" : "same-controlled-content";
        var before = AnalyzerInputCatalog.ContentIdentity([new(leftPath, null, [], leftHash)], CancellationToken.None);
        var after = AnalyzerInputCatalog.ContentIdentity([new(rightPath, null, [], rightHash)], CancellationToken.None);
        Assert.AreNotEqual(before, after, "Catalog identity must preserve exact string code units independently of file-system admissibility.");
    }
}
