using System.Text.Json;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerReferenceCaptureQuotaTests
{
    [TestMethod]
    [DataRow("field")]
    [DataRow("cardinality")]
    [DataRow("combined")]
    public void ImpossibleLogicalFrames_RejectBeforeTokenSerialization(string shape)
    {
        var fields = shape switch
        {
            "field" => new[] { new string('x', CompilerReferenceCaptureFrame.MaximumBytes + 1) },
            "cardinality" => Enumerable.Repeat("", CompilerReferenceCaptureFrame.MaximumBytes / 2 + 1).ToArray(),
            "combined" => new[] { new string('x', CompilerReferenceCaptureFrame.MaximumBytes / 2), new string('y', CompilerReferenceCaptureFrame.MaximumBytes / 2) },
            _ => throw new InvalidOperationException("Unknown controlled quota shape.")
        };
        var frame = new CompilerReferenceCaptureFrame("controlled", "", fields);
        Assert.ThrowsExactly<InvalidDataException>(() => frame.LogicalSize(CancellationToken.None));
        Assert.ThrowsExactly<InvalidDataException>(() => frame.Encode(CancellationToken.None));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    public void ExactWireCodec_PreservesTheOldLogicalFrameBoundary(int offset)
    {
        var empty = new CompilerReferenceCaptureFrame("controlled", "", [""]);
        var overhead = checked((int)empty.LogicalSize(CancellationToken.None));
        var frame = empty with { Fields = [new string('a', CompilerReferenceCaptureFrame.MaximumBytes - overhead + offset)] };
        if (offset > 0)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => frame.Encode(CancellationToken.None));
            var oversizedWire = JsonSerializer.SerializeToUtf8Bytes(frame, NativeStructuredIdentity.CreateStringOptions(CancellationToken.None));
            Assert.IsTrue(oversizedWire.Length < CompilerReferenceCaptureFrame.MaximumWireBytes);
            Assert.ThrowsExactly<InvalidDataException>(() => CompilerReferenceCaptureFrame.Decode(oversizedWire, CancellationToken.None, out _));
            return;
        }
        var wire = frame.Encode(CancellationToken.None);
        Assert.IsTrue(wire.Length > CompilerReferenceCaptureFrame.MaximumBytes,
            "A previously accepted large ASCII frame must survive exact-codec wire expansion.");
        var decoded = CompilerReferenceCaptureFrame.Decode(wire, CancellationToken.None, out var logicalSize);
        Assert.AreEqual((long)CompilerReferenceCaptureFrame.MaximumBytes + offset, logicalSize);
        Assert.IsNotNull(decoded);
        Assert.IsTrue(frame.Fields[0].AsSpan().SequenceEqual(decoded.Fields[0].AsSpan()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FramedUrlTokens_RespectDerivedWireBoundAndFixedSchema(bool alphabet)
    {
        var value = alphabet ? "\u3e00\uffff" : "";
        var frame = new CompilerReferenceCaptureFrame("controlled", value, alphabet ? [value] : Enumerable.Repeat("", 1000).ToArray());
        var logical = frame.LogicalSize(CancellationToken.None);
        var wire = frame.Encode(CancellationToken.None);
        Assert.IsTrue(wire.Length <= 5 * logical, "Every exact string token must respect the derived representation bound.");
        using var json = JsonDocument.Parse(wire);
        CollectionAssert.AreEqual(new[] { "Kind", "Context", "Fields" }, json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var tokenElement = json.RootElement.GetProperty("Context");
        var token = tokenElement.GetString();
        Assert.IsNotNull(token);
        Assert.AreEqual(token.Length + 2, tokenElement.GetRawText().Length, "The canonical URL alphabet must need no JSON escaping.");
        if (alphabet) Assert.IsTrue(token.Contains('-') && token.Contains('_') && token.EndsWith('='));
        var decoded = CompilerReferenceCaptureFrame.Decode(wire, CancellationToken.None, out var decodedLogical);
        Assert.AreEqual(logical, decodedLogical);
        Assert.IsNotNull(decoded);
        Assert.IsTrue(value.AsSpan().SequenceEqual(decoded.Context.AsSpan()));
    }

    [TestMethod]
    [DataRow("logical")]
    [DataRow("wire")]
    [DataRow("count")]
    [DataRow("cancelled")]
    public void IndependentCaptureBudgets_RetainStreamAndCardinalityLimits(string shape)
    {
        using var cancellation = new CancellationTokenSource();
        var budget = new CompilerReferenceCaptureBudget(cancellation.Token);
        switch (shape)
        {
            case "logical":
                for (var index = 0; index < CompilerReferenceCaptureFrame.MaximumTotalBytes / CompilerReferenceCaptureFrame.MaximumBytes; index++)
                    budget.AcceptLogicalLength(CompilerReferenceCaptureFrame.MaximumBytes);
                Assert.ThrowsExactly<InvalidDataException>(() => budget.AcceptLogicalLength(1));
                break;
            case "wire":
                for (var index = 0; index < CompilerReferenceCaptureFrame.MaximumWireTotalBytes / CompilerReferenceCaptureFrame.MaximumWireBytes; index++)
                    budget.AcceptWireLength(CompilerReferenceCaptureFrame.MaximumWireBytes);
                Assert.ThrowsExactly<InvalidDataException>(() => budget.AcceptWireLength(1));
                break;
            case "count":
                for (var index = 0; index < CompilerReferenceCaptureBudget.MaximumFrames; index++) budget.AcceptWireLength(1);
                Assert.ThrowsExactly<InvalidDataException>(() => budget.AcceptWireLength(1));
                break;
            case "cancelled":
                cancellation.Cancel();
                Assert.ThrowsExactly<OperationCanceledException>(() => budget.AcceptWireLength(1));
                Assert.ThrowsExactly<OperationCanceledException>(() => budget.AcceptLogicalLength(1));
                Assert.ThrowsExactly<OperationCanceledException>(() => new CompilerReferenceCaptureFrame("controlled", "", []).Encode(cancellation.Token));
                break;
            default: Assert.Fail("Unknown controlled quota shape."); break;
        }
    }
}
