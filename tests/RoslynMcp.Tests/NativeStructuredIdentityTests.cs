using System.Text.Json;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeStructuredIdentityTests
{
    [TestMethod]
    public void SharedStringCodec_UsesCanonicalPaddedJsonSafeAlphabet()
    {
        var value = new string(new[] { (char)0x3E00, (char)0xFFFF });
        var token = NativeStructuredIdentity.StringToken(value, CancellationToken.None);
        Assert.IsTrue(token.EndsWith('=') && token.Contains('-') && token.Contains('_'));
        var options = NativeStructuredIdentity.CreateStringOptions(CancellationToken.None);
        var encoded = JsonSerializer.Serialize(value, options);
        Assert.AreEqual("\"" + token + "\"", encoded, "The private token must need no extra JSON character escaping.");
        Assert.AreEqual(value, JsonSerializer.Deserialize<string>(encoded, options));
        var standardAlphabet = token.Replace('-', '+').Replace('_', '/');
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(standardAlphabet), options));
    }

    [TestMethod]
    public void SharedStringCodec_RoundTripsExactValuesAndDictionaryKeys()
    {
        var key = "key" + new string((char)0xD800, 1);
        var value = "value" + new string((char)0xDC00, 1);
        var original = new Dictionary<string, string> { [key] = value, ["empty"] = "" };
        var options = NativeStructuredIdentity.CreateStringOptions(CancellationToken.None);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(original, options);
        var actual = JsonSerializer.Deserialize<Dictionary<string, string>>(encoded, options)!;
        Assert.AreEqual(value, actual[key]);
        Assert.AreEqual("", actual["empty"]);
        Assert.AreEqual(2, actual.Count);
    }

    [TestMethod]
    [DataRow("base64")]
    [DataRow("noncanonical")]
    [DataRow("negative")]
    [DataRow("truncated")]
    [DataRow("length")]
    [DataRow("trailing")]
    [DataRow("kind")]
    public void SharedStringCodec_RefusesMalformedExactFrames(string shape)
    {
        var token = shape switch
        {
            "base64" => "!",
            "noncanonical" => Convert.ToBase64String(new byte[4]) + " ",
            "negative" => Convert.ToBase64String(new byte[] { 255, 255, 255, 255 }),
            "truncated" => Convert.ToBase64String(new byte[] { 0, 0, 0 }),
            "length" => Convert.ToBase64String(new byte[] { 1, 0, 0, 0 }),
            "trailing" => Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 1 }),
            "kind" => "unused",
            _ => throw new AssertFailedException("Unknown controlled frame shape.")
        };
        token = token.Replace('+', '-').Replace('/', '_');
        var json = shape == "kind" ? "42" : JsonSerializer.Serialize(token);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<string>(json,
            NativeStructuredIdentity.CreateStringOptions(CancellationToken.None)));
    }

    [TestMethod]
    [DataRow("key")]
    [DataRow("value")]
    [DataRow("order")]
    public void DictionaryIdentity_PreservesCodeUnitsAndCanonicalizesOnlyEntryOrder(string change)
    {
        var first = new string((char)0xD800, 1);
        var second = new string((char)0xD801, 1);
        var left = new Dictionary<string, string> { [first] = first, ["fixed"] = "fixed" };
        var right = change switch
        {
            "key" => new Dictionary<string, string> { [second] = first, ["fixed"] = "fixed" },
            "value" => new Dictionary<string, string> { [first] = second, ["fixed"] = "fixed" },
            "order" => new Dictionary<string, string> { ["fixed"] = "fixed", [first] = first },
            _ => throw new AssertFailedException("Unknown controlled identity shape.")
        };
        Assert.AreEqual(change == "order", NativeStructuredIdentity.Serialize(left, CancellationToken.None)
            == NativeStructuredIdentity.Serialize(right, CancellationToken.None));
    }

    [TestMethod]
    public void StructuralIdentity_CancellationPreservesOwningToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            NativeStructuredIdentity.Serialize(new { Value = "owned" }, cancellation.Token));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PhysicalProjectKey_PreservesDistinctPathCodeUnits(bool output)
    {
        var first = Path.Combine(TestTempRoot.Current, "identity-" + new string((char)0xD800, 1));
        var second = Path.Combine(TestTempRoot.Current, "identity-" + new string((char)0xD801, 1));
        Assert.IsFalse(string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.Ordinal),
            "The platform path normalizer must preserve the distinct controlled code units before key capture.");
        var fixedPath = Path.Combine(TestTempRoot.Current, "identity-fixed");
        var before = PhysicalProjectIdentity.Key(output ? fixedPath : first, output ? first : fixedPath, static path => path);
        var after = PhysicalProjectIdentity.Key(output ? fixedPath : second, output ? second : fixedPath, static path => path);
        Assert.IsFalse(string.Equals(before, after, StringComparison.Ordinal),
            "A project/output identity must not replace distinct path code units with the same text.");
    }
}
