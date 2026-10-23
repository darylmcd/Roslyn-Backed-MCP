using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeStructuredIdentitySessionTests
{
    [TestMethod]
    public void ReusedMetadata_DoesNotReuseValuesOrLoseExactStrings()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new NativeStructuredIdentity.Session(cancellation.Token);
        var key = "key" + new string((char)0xD800, 1);
        var values = new Dictionary<string, string> { [key] = "first" + new string((char)0xDC00, 1), ["fixed"] = "" };
        var first = session.Serialize(values);
        Assert.AreEqual(NativeStructuredIdentity.Serialize(values, cancellation.Token), first);
        values[key] = "second" + new string((char)0xDC01, 1);
        var second = session.Serialize(values);
        Assert.AreEqual(NativeStructuredIdentity.Serialize(values, cancellation.Token), second);
        Assert.AreNotEqual(first, second);
        var reordered = values.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.AreEqual(second, session.Serialize(reordered));
        cancellation.Cancel();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => session.Serialize(values));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
    }
}
