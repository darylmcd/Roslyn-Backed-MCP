using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public sealed partial class ChangeSignaturePreviewTests
{
    [TestMethod]
    [DataRow("primary")]
    [DataRow("ordinary")]
    [DataRow("readonly")]
    [DataRow("nested-generic")]
    [DataRow("name-collision")]
    public async Task ChangeSignaturePreview_StructConstruction_LastRemovalPreservesExternalZeroPaths(string shape)
    {
        var declaration = shape switch
        {
            "ordinary" => "public struct Construction { public int Value; public Construction(int unused) { Value = 7; } }",
            "readonly" => "public readonly struct Construction(int unused) { public readonly int Value = 7; }",
            "nested-generic" => "public class Outer<T> { public struct Construction<U>(int unused) { public int Value = 7; } }",
            "name-collision" => "public partial struct Construction(int unused) { public int Value = 7; } public partial struct Construction { public struct __SignatureConstructionMarker {} public int __signatureConstruction; }",
            _ => "public struct Construction(int unused) { public int Value = 7; }"
        };
        var type = shape == "nested-generic" ? "Outer<int>.Construction<string>" : "Construction";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new " + type + "(2).Value + \"|\" + ZeroOnly.Read(); }";
        var untouched = "namespace SampleLib; public static class ZeroOnly { public static int Read() => System.Text.Json.JsonSerializer.Deserialize<" + type + ">(\"{}\").Value + new " + type + "().Value + System.Activator.CreateInstance<" + type + ">().Value; }";
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var zeroPath = workspace.GetPath("SampleLib", "ZeroOnly.cs");
        await File.WriteAllTextAsync(path, source);
        await File.WriteAllTextAsync(zeroPath, untouched);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var constructorColumn = shape == "ordinary" ? declaration.IndexOf("Construction(int", StringComparison.Ordinal) + 1
            : declaration.IndexOf("Construction", StringComparison.Ordinal) + 1;
        var preview = await _changeSignatureService.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, constructorColumn), new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        async Task VerifyAsync(Solution solution)
        {
            Assert.AreEqual("7|0", await RunSignatureFixtureAsync(solution, "SampleLib.SignatureProof"));
            var zero = solution.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == zeroPath);
            Assert.AreEqual(untouched, (await zero.GetTextAsync()).ToString(), "Non-target construction must retain exact source text.");
        }
        await VerifyAsync(stored.Value.ModifiedSolution);
        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(untouched, await File.ReadAllTextAsync(zeroPath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_StructConstruction_AddToExplicitZeroRetainsSharedBody(bool chained)
    {
        var declaration = "public struct Construction { public int Value = 3; public Construction()"
            + (chained ? " : this(2)" : "")
            + " { Value += 4; SignatureProof.Calls++; }"
            + (chained ? " public Construction(int retained) { Value = retained; }" : "") + " }";
        var expected = chained ? "6|6|2" : "7|7|2";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static int Calls; public static string Run() { Calls = 0; var edited = new Construction(); var runtime = System.Activator.CreateInstance<Construction>(); return edited.Value + \"|\" + runtime.Value + \"|\" + Calls; } }";
        // A distinct parameter type avoids colliding with the retained int constructor in the chaining case.
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("add", Name: "added", ParameterType: "string", DefaultValue: "null"),
            2, declaration.IndexOf("Construction()", StringComparison.Ordinal) + 1, expected, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_StructConstruction_MovedBodyRetainsCallerLineNumber()
    {
        var declaration = "public struct Construction { public int Value;\npublic Construction() {\nValue = SignatureProof.Line();\n} }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; public static string Run() => new Construction().Value + \"|\" + System.Activator.CreateInstance<Construction>().Value; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("add", Name: "added", ParameterType: "int"),
            3, declaration.Split('\n')[1].IndexOf("Construction()", StringComparison.Ordinal) + 1, "4|4", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_StructConstruction_AddToJsonSelectedZeroPreservesSerializer()
    {
        var declaration = "public struct Construction { public int Value { get; } = 3; [System.Text.Json.Serialization.JsonConstructor] public Construction() { Value += 4; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new Construction().Value + \"|\" + System.Text.Json.JsonSerializer.Deserialize<Construction>(\"{}\").Value; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("add", Name: "added", ParameterType: "int"),
            2, declaration.IndexOf("Construction()", StringComparison.Ordinal) + 1, "7|7", 1);
    }

    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    public async Task ChangeSignaturePreview_StructConstruction_PartialConstructorPreservesBothEntryPoints(string operation)
    {
        var declaration = operation == "add"
            ? "public partial struct Construction { public int Value; public partial Construction(); public partial Construction() { Value = 7; } }"
            : "public partial struct Construction { public int Value; public partial Construction(int unused); public partial Construction(int unused) { Value = 7; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new Construction(" + (operation == "add" ? "" : "2")
            + ").Value + \"|\" + System.Activator.CreateInstance<Construction>().Value; }";
        var request = operation == "add" ? new ChangeSignatureRequest("add", Name: "added", ParameterType: "int")
            : new ChangeSignatureRequest("remove", Name: "unused");
        await AssertSignatureProofAsync(source, request, 2, declaration.IndexOf("Construction(", StringComparison.Ordinal) + 1,
            operation == "add" ? "7|7" : "7|0", 1);
    }

    [TestMethod]
    [DataRow("line")]
    [DataRow("expression")]
    public async Task ChangeSignaturePreview_StructConstruction_RetainsUnrelatedCallerInformation(string shape)
    {
        var declaration = "public struct Construction(\nint unused\n) { public int Value = 7; }";
        var proof = shape == "line"
            ? "public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; public static string Run() => new Construction(2).Value + \"|\" + Line();"
            : "public static string Describe(Construction value, [System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string text = \"\") => value.Value + \"|\" + text; public static string Run() => Describe(new Construction(2));";
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class SignatureProof { " + proof + " }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"),
            2, declaration.IndexOf("Construction", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_StructConstruction_JsonSelectedLastParameter_RefusesAtomically()
    {
        var declaration = "public struct Construction { public int Value { get; } [System.Text.Json.Serialization.JsonConstructor] public Construction(int value) { Value = 7; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new Construction(2).Value + \"|\" + System.Text.Json.JsonSerializer.Deserialize<Construction>(\"{\\\"Value\\\":2}\").Value; }";
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|7", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(PreviewStore);
        var service = new ChangeSignatureService(WorkspaceManager, probe, RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 2, declaration.IndexOf("Construction(int", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "value"), CancellationToken.None));
        StringAssert.Contains(error.Message, "JsonConstructor");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_StructConstruction_UnrelatedAttributeName_DoesNotRefuse()
    {
        var declaration = "[method: System.Text.Json.Serialization.JsonConstructor] public struct Construction(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib {\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new Construction(2).Value + \"|\" + new Construction().Value; } }"
            + "\nnamespace System.Text.Json.Serialization { [System.AttributeUsage(System.AttributeTargets.Constructor)] public sealed class JsonConstructorAttribute : System.Attribute { } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"),
            2, declaration.IndexOf("Construction(int", StringComparison.Ordinal) + 1, "7|0", 1);
    }
}
