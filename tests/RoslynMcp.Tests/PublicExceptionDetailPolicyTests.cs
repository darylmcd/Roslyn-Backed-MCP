using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PublicExceptionDetailPolicyTests
{
    private const string SecretSentinel = "secret-sentinel-47";

    [TestMethod]
    [DataRow("0123456789ab", "0123456789ab")]
    [DataRow("request_17-A", "request_17-A")]
    [DataRow("bad/path/secret-sentinel-47", "unavailable")]
    public void ProjectUnexpected_SeparatesStablePublicAndSecretSafeServerDetails(
        string correlationId,
        string expectedCorrelationId)
    {
        var exception = CaptureExceptionWithStack(
            new InvalidOperationException(
                $"Failed at C:/private/{SecretSentinel}/payload.cs",
                new IOException($"inner {SecretSentinel}")));

        var first = PublicExceptionDetailPolicy.ProjectUnexpected(exception, correlationId);
        var second = PublicExceptionDetailPolicy.ProjectUnexpected(exception, correlationId);

        Assert.AreEqual(first.Public, second.Public, "Public projection must be deterministic.");
        Assert.AreEqual(first.Server.CorrelationId, second.Server.CorrelationId);
        Assert.AreEqual(first.Server.StackFrameCount, second.Server.StackFrameCount);
        CollectionAssert.AreEqual(
            first.Server.ExceptionTypes.ToArray(),
            second.Server.ExceptionTypes.ToArray(),
            "Server projection must be deterministic.");
        Assert.AreEqual("InternalError", first.Public.Category);
        Assert.AreEqual(expectedCorrelationId, first.Public.CorrelationId);
        Assert.AreEqual(expectedCorrelationId, first.Server.CorrelationId);
        CollectionAssert.AreEqual(
            new[] { typeof(InvalidOperationException).FullName!, typeof(IOException).FullName! },
            first.Server.ExceptionTypes.ToArray());
        Assert.IsGreaterThan(0, first.Server.StackFrameCount);

        var combined = string.Join('|',
            first.Public.Category,
            first.Public.Summary,
            first.Public.Remediation,
            first.Public.CorrelationId,
            first.Server.CorrelationId,
            string.Join(',', first.Server.ExceptionTypes),
            first.Server.StackFrameCount);
        Assert.IsFalse(combined.Contains(SecretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("C:/private", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("payload.cs", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("request_17-A", "correlationId=request_17-A")]
    [DataRow("bad/path/secret-sentinel-47", "correlationId=unavailable")]
    [DataRow(null, "correlationId=unavailable")]
    public void FormatCorrelationIdSuffix_UsesThePublicNormalizationBoundary(
        string? correlationId,
        string expected)
    {
        Assert.AreEqual(expected, PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(correlationId));
    }

    private static Exception CaptureExceptionWithStack(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception captured)
        {
            return captured;
        }
    }
}
