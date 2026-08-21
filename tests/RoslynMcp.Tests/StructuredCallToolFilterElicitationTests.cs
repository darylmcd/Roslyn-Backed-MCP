using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

/// <summary>
/// Pins the <see cref="StructuredCallToolFilter"/> fallback envelope used when request-scoped
/// recovery cannot complete. Allowlist decisions belong to <c>ElicitationAllowlistPolicyTests</c>;
/// recovery sequencing belongs to <c>StructuredCallElicitationCoordinatorTests</c>; transport-era
/// behavior belongs to <c>WorkspacePathMrtrWireTests</c>.
/// </summary>
[TestClass]
public sealed class StructuredCallToolFilterElicitationTests
{
    [TestMethod]
    public void BuildErrorResult_WhenElicitFallbackTaken_StillProducesSchemaHintEnvelope()
    {
        var binderException = new ArgumentException(
            "The arguments dictionary is missing a value for the required parameter 'path'.",
            paramName: "path");

        using var scope = AmbientGateMetrics.BeginRequest();
        var result = StructuredCallToolFilter.BuildErrorResult("workspace_load", binderException);

        Assert.IsTrue(result.IsError,
            "The fallback envelope retains IsError=true so the caller can self-correct on retry.");
        var text = ((TextContentBlock)result.Content![0]).Text;
        var payload = JsonDocument.Parse(text).RootElement;

        Assert.AreEqual("InvalidArgument", payload.GetProperty("category").GetString(),
            "Unsupported or declined recovery must preserve the established error category.");
        Assert.AreEqual("workspace_load", payload.GetProperty("tool").GetString());
        StringAssert.Contains(payload.GetProperty("message").GetString(), "path");
    }
}
