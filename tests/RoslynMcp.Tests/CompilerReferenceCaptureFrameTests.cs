using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerReferenceCaptureFrameTests
{
    [TestMethod]
    [DataRow("high")]
    [DataRow("low")]
    [DataRow("replacement")]
    [DataRow("pair")]
    [DataRow("ordinary")]
    public void ActualPrivateFrameRoundTrip_PreservesExactStringCodeUnits(string shape)
    {
        var value = shape switch
        {
            "high" => "\ud800",
            "low" => "\udc00",
            "replacement" => "\ufffd",
            "pair" => "\ud83d\ude00",
            _ => "ordinary:framed|value"
        };
        var expected = new CompilerReferenceCaptureFrame("controlled", value, [value, "fixed"]);
        var bytes = expected.Encode(CancellationToken.None);
        Assert.IsTrue(bytes.Length <= CompilerReferenceCaptureFrame.MaximumWireBytes);
        var actual = CompilerReferenceCaptureFrame.Decode(bytes, CancellationToken.None, out _);
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Kind, actual.Kind);
        Assert.IsTrue(expected.Context.AsSpan().SequenceEqual(actual.Context.AsSpan()), "Private frame context must preserve exact code units.");
        Assert.HasCount(expected.Fields.Length, actual.Fields);
        for (var index = 0; index < expected.Fields.Length; index++)
            Assert.IsTrue(expected.Fields[index].AsSpan().SequenceEqual(actual.Fields[index].AsSpan()), "Private frame fields must preserve exact code units.");
    }
}
