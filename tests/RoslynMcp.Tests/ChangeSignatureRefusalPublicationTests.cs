using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Every caller refusal in <see cref="ChangeSignatureService"/> must be a server-authored
/// public exception naming the real wire parameter, and must never echo raw request text.
/// </summary>
[TestClass]
public sealed class ChangeSignatureRefusalPublicationTests : ChangeSignatureTestBase
{
    private const string Sentinel = "PRIVATE-CS-SENTINEL";

    [TestMethod]
    [DataRow("missing-op", "", null, null, null, null, null, "op", "'op' is required")]
    [DataRow("unsupported-op", Sentinel, null, null, null, null, null, "op", "'op' is not supported")]
    [DataRow("add-missing-name", "add", null, null, null, null, null, "name", "requires parameter 'name'")]
    [DataRow("add-missing-type", "add", Sentinel, null, null, null, null, "parameterType", "requires parameter 'parameterType'")]
    [DataRow("add-position", "add", Sentinel, "int", null, 99, null, "position", "0..2")]
    [DataRow("remove-identity", "remove", Sentinel, null, null, 99, null, "position", "requires 'position'")]
    [DataRow("rename-missing-name", "rename", null, null, "x", null, null, "name", "requires parameter 'name'")]
    [DataRow("rename-missing-new-name", "rename", "a", null, null, null, null, "newName", "requires parameter 'newName'")]
    [DataRow("rename-unknown-name", "rename", Sentinel, null, "x", null, null, "name", "does not match a parameter")]
    [DataRow("reorder-missing", "reorder", null, null, null, null, null, "newOrder", "requires parameter 'newOrder'")]
    [DataRow("reorder-arity", "reorder", null, null, null, null, Sentinel, "newOrder", "exactly once")]
    [DataRow("reorder-unknown-token", "reorder", null, null, null, null, "a," + Sentinel, "newOrder", "entry 2")]
    [DataRow("reorder-index", "reorder", null, null, null, null, "0,7", "newOrder", "index 7 is out of range")]
    [DataRow("reorder-duplicate", "reorder", null, null, null, null, "a,a", "newOrder", "more than once")]
    [DataRow("reorder-identity", "reorder", null, null, null, null, "a,b", "newOrder", "identity permutation")]
    public async Task CallerRefusals_ArePublicArgumentExceptions_WithRealParameterNames(
        string scenario, string op, string? name, string? parameterType, string? newName,
        int? position, string? newOrder, string parameterName, string correction)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureRefusalFixture.cs");
        await File.WriteAllTextAsync(path,
            "namespace SampleLib;\npublic static class SignatureRefusalFixture\n{\n    public static int Compute(int a, int b) => a + b;\n}\n");
        await workspace.LoadAsync(CancellationToken.None);
        var request = new ChangeSignatureRequest(op, name, newName, parameterType, null, position, newOrder);

        var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, SymbolLocator.BySource(path, 4, 24), request, CancellationToken.None),
            scenario);

        Assert.AreEqual(parameterName, error.ParamName, scenario);
        StringAssert.Contains(error.PublicMessage, correction, scenario);
        Assert.IsFalse(error.PublicMessage.Contains(Sentinel, StringComparison.Ordinal), error.PublicMessage);
        Assert.IsFalse(error.PublicMessage.Contains(path, StringComparison.Ordinal), error.PublicMessage);
    }

    [TestMethod]
    public async Task ParenthesizedMetadataName_IsPublicArgumentException_WithoutEcho()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        await workspace.LoadAsync(CancellationToken.None);

        var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId,
                SymbolLocator.ByMetadataName("Some.Type.Method(" + Sentinel + ")"),
                new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None));

        Assert.AreEqual("metadataName", error.ParamName);
        StringAssert.Contains(error.PublicMessage, "parenthesized");
        Assert.IsFalse(error.PublicMessage.Contains(Sentinel, StringComparison.Ordinal), error.PublicMessage);
    }

    [TestMethod]
    public async Task NonMethodTarget_IsPublicInvalidOperationException()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureRefusalFixture.cs");
        await File.WriteAllTextAsync(path,
            "namespace SampleLib;\npublic static class SignatureRefusalFixture\n{\n    public static int Field;\n}\n");
        await workspace.LoadAsync(CancellationToken.None);

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, SymbolLocator.BySource(path, 4, 23),
                new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None));

        StringAssert.Contains(error.PublicMessage, "symbolHandle");
        Assert.IsFalse(error.PublicMessage.Contains(path, StringComparison.Ordinal), error.PublicMessage);
    }

    [TestMethod]
    public async Task RenameOfMetadataOnlyMethod_IsPublicInvalidOperationException()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureRefusalFixture.cs");
        const string line = "    public static bool F(string s) => string.IsNullOrEmpty(s);";
        await File.WriteAllTextAsync(path,
            "namespace SampleLib;\npublic static class SignatureRefusalFixture\n{\n" + line + "\n}\n");
        await workspace.LoadAsync(CancellationToken.None);
        var column = line.IndexOf("IsNullOrEmpty", StringComparison.Ordinal) + 2;

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            ChangeSignature.PreviewChangeSignatureAsync(
                workspace.WorkspaceId, SymbolLocator.BySource(path, 4, column),
                new ChangeSignatureRequest("rename", Name: "value", NewName: Sentinel), CancellationToken.None));

        StringAssert.Contains(error.PublicMessage, "declared in source");
        Assert.IsFalse(error.PublicMessage.Contains(Sentinel, StringComparison.Ordinal), error.PublicMessage);
        Assert.IsFalse(error.PublicMessage.Contains(path, StringComparison.Ordinal), error.PublicMessage);
    }

    [TestMethod]
    public void PrimaryConstructorRefusal_IsPublicInvalidOperationException_ByDeclaredReturnType()
    {
        var method = typeof(ChangeSignatureService).GetMethod(
            "PrimaryConstructorRefusal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);
        Assert.AreEqual(typeof(PublicInvalidOperationException), method.ReturnType);
    }
}
