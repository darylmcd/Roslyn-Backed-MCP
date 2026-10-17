using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureRefusalsTests : ChangeSignatureTestBase
{

    [TestMethod]
    [DataRow("capture")]
    [DataRow("base-reference")]
    [DataRow("optional-placement")]
    [DataRow("duplicate-name")]
    [DataRow("unknown-type")]
    [DataRow("invalid-syntax")]
    [DataRow("params-placement")]
    [DataRow("struct-initializer")]
    [DataRow("overload-drift")]
    [DataRow("valid-warning")]
    public async Task ChangeSignaturePreview_ConcreteInvalidCandidate_IsAtomic(string shape)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var declaration = "public class AtomicTarget(int removed) { }";
        var request = new ChangeSignatureRequest("remove", Position: 0);
        var extra = "";
        switch (shape)
        {
            case "capture":
                declaration = "public class AtomicTarget(int removed) { public int Value => removed; }";
                break;
            case "base-reference":
                declaration = "public class AtomicTarget(int removed) : System.Exception(removed.ToString()) { }";
                break;
            case "optional-placement":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "0", Position: 0);
                break;
            case "duplicate-name":
                request = new ChangeSignatureRequest("add", Name: "removed", ParameterType: "int");
                break;
            case "unknown-type":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "UndefinedParameterType");
                break;
            case "invalid-syntax":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int) { } class Injected(");
                break;
            case "params-placement":
                declaration = "public class AtomicTarget(params int[] removed) { }";
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int");
                break;
            case "struct-initializer":
                declaration = "public struct AtomicTarget(int removed) { public int Value = removed; }";
                break;
            case "overload-drift":
                declaration = "public class AtomicTarget(int removed, object retained) { public AtomicTarget(string retained) : this(0, (object)retained) { } }";
                extra = "public static class AtomicCaller { public static AtomicTarget Make() => new AtomicTarget(1, \"value\"); }";
                break;
            case "valid-warning":
                request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int");
                break;
        }
        var path = workspace.GetPath("SampleLib", "AtomicTarget.cs");
        var source = "namespace SampleLib;\n" + declaration + "\n" + extra;
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("AtomicTarget", StringComparison.Ordinal) + 1);
        if (shape == "valid-warning")
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator, request, CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            Assert.IsNotNull(preview.Warnings);
            Assert.IsTrue(preview.Warnings.Any(w => w.Contains("CS9113", StringComparison.Ordinal)));
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator, request, CancellationToken.None));
            Assert.IsFalse(error.Message.Contains(path, StringComparison.Ordinal), "authored evidence must be path-free");
            Assert.AreEqual(0, probe.StoreCalls, "invalid candidates must never reach token storage");
        }
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_EmptyCandidate_StoresNoToken()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        // Reach the defensive empty-candidate boundary directly: valid non-identity signature
        // operations always edit a source declaration before this boundary.
        var builder = typeof(ChangeSignatureService).GetMethod("BuildPreviewDto",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(builder);
        var error = Assert.ThrowsExactly<System.Reflection.TargetInvocationException>(() =>
            builder.Invoke(service, [
                workspace.WorkspaceId, original, new List<FileChangeDto>(),
                new List<CallsiteUpdateDto>(), new List<string>(), "empty candidate",
            ]));
        Assert.AreEqual(typeof(PublicInvalidOperationException), error.InnerException?.GetType());
        StringAssert.Contains(error.InnerException!.Message, "no editable declaration changes");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_DiagnosticExchange_IsAtomic(bool boundConditional)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "DiagnosticExchange.cs");
        var declaration = "public class DiagnosticTarget { public static int M(int missing) => missing; public static int Run() => M(" + (boundConditional ? "missing ? 1 : 2" : "missing") + "); }";
        var source = "namespace SampleLib;\n" + declaration;
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var diagnostic = (await project.GetCompilationAsync())!.GetDiagnostics().Single(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.AreEqual("CS0103", diagnostic.Id);
        Assert.IsTrue(diagnostic.Location.SourceSpan.Start > source.IndexOf("Run()", StringComparison.Ordinal));
        var document = project.Documents.Single(d => d.FilePath == path);
        var root = (await document.GetSyntaxRootAsync())!;
        var model = (await document.GetSemanticModelAsync())!;
        var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
        if (boundConditional) Assert.IsNotNull(model.GetSymbolInfo(call).Symbol, "typed conditional argument must retain an actual original method binding");
        else
        {
            Assert.IsNull(model.GetSymbolInfo(call).Symbol);
            Assert.IsTrue(model.GetSymbolInfo(call).CandidateSymbols.OfType<Microsoft.CodeAnalysis.IMethodSymbol>()
                .Any(m => m.Name == "M"));
        }
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None));
        if (boundConditional) StringAssert.Contains(error.Message, "CS0103");
        else Assert.AreEqual("An unresolved invocation may bind to the edited member. Resolve its binding errors before changing this signature.", error.Message);
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("syntax")]
    [DataRow("entrypoint")]
    public async Task ChangeSignaturePreview_UnrelatedExistingDiagnostic_RemainsAccepted(string shape)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "ExistingDiagnostic.cs");
        var broken = shape == "missing" ? "public static int Broken() => missing;" : shape == "syntax" ? "public static int Broken() =>;" : "";
        var declaration = "public class DiagnosticTarget { public static int M(int unused, int retained) => retained; public static int Run() => M(1, 2); " + broken + " }";
        if (shape == "entrypoint")
        {
            var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
            var projectText = await File.ReadAllTextAsync(projectPath);
            await File.WriteAllTextAsync(projectPath, projectText.Replace("<PropertyGroup>", "<PropertyGroup><OutputType>Exe</OutputType>", StringComparison.Ordinal));
        }
        await File.WriteAllTextAsync(path, "namespace SampleLib;\n" + declaration);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var before = (await original.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        Assert.IsTrue(before.Length > 0);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        Assert.AreEqual(1, probe.StoreCalls);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var after = (await stored.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
        CollectionAssert.AreEqual(before.Select(d => d.Id + ":" + d.GetMessage()).Order(StringComparer.Ordinal).ToArray(), after.Select(d => d.Id + ":" + d.GetMessage()).Order(StringComparer.Ordinal).ToArray());
        if (shape != "entrypoint") Assert.AreNotEqual(before[0].Location.SourceSpan.Start, after[0].Location.SourceSpan.Start, "the surviving original diagnostic moves with retained syntax");
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ChangeSignaturePreview_DynamicInvocation_RefusesOnlyPossibleEditedMember(bool unrelated, bool knownReceiver)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "DynamicSignature.cs");
        var declaration = "public class DynamicTarget { public int M(int x, int unused) => x; public int Other(int x, int unused) => x; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic class DynamicOther { public int M(int x, int unused) => x; }"
            + "\npublic static class SignatureProof { public static int Run() { "
            + (knownReceiver ? "dynamic arg = 1; var d = new " + (unrelated ? "DynamicOther" : "DynamicTarget") + "(); return d.M(arg, 2);"
                : "dynamic d = new DynamicTarget(); return d." + (unrelated ? "Other" : "M") + "(1, 2);") + " } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1);
        if (!unrelated)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None));
            StringAssert.Contains(error.Message, "dynamic");
            Assert.AreEqual(0, probe.StoreCalls);
            Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
            var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_DynamicPrimaryConstruction_IsAtomic(bool unrelated)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "DynamicConstructor.cs");
        var declaration = "public class DynamicConstructed(int x, int unused) { public int Value => x; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic class DynamicOther(int x, int unused) { public int Value => x; }"
            + "\npublic static class SignatureProof { public static int Run() { dynamic arg = 1; return new "
            + (unrelated ? "DynamicOther" : "DynamicConstructed") + "(arg, 2).Value; } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("DynamicConstructed", StringComparison.Ordinal) + 1);
        if (!unrelated)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None));
            StringAssert.Contains(error.Message, "dynamic");
            Assert.AreEqual(0, probe.StoreCalls);
            Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_DynamicSimpleName_RespectsLocalDelegateIdentity(bool localDelegate)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "DynamicSimpleName.cs");
        var declaration = "public static class SignatureProof { public static int M(int x, int unused) => x;";
        var source = "namespace SampleLib;\n" + declaration + " public static int Run() { dynamic arg = 1; "
            + (localDelegate ? "System.Func<int, int, int> M = (x, y) => x;" : "")
            + " return M(arg, 2); } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("1", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("M(", StringComparison.Ordinal) + 1);
        if (!localDelegate)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None));
            StringAssert.Contains(error.Message, "dynamic");
            Assert.AreEqual(0, probe.StoreCalls);
            Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Position: 1), CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            Assert.AreEqual("1", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        }
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_UnnameableUninferableExtensionType_IsAtomic()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "UnnameableExtension.cs");
        var declaration = "public static class SignatureExtensions { public static int Compute<T>(this T unused, int value) => value; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static int Run() => new { Value = 1 }.Compute(2); }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("2", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None));
        StringAssert.Contains(error.Message, "CS0411");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ChangeSignaturePreview_UnboundErrorTypeCall_ProtectsOnlyEditedCandidates(bool related)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "ErrorTypeSubstitution.cs");
        var declaration = "public static class SignatureProof { public static int Compute<T>(T unused, int value) => value;";
        var source = "namespace SampleLib;\n" + declaration
            + " public static int Other<T>(T value, int retained) => retained; public static int Run() { MissingType value = default; return "
            + (related ? "Compute(value, 2)" : "Other(value, Compute(\"x\", 2))") + "; } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var document = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var model = (await document.GetSemanticModelAsync())!;
        var call = (await document.GetSyntaxRootAsync())!.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().First();
        Assert.IsNull(model.GetSymbolInfo(call).Symbol);
        Assert.IsTrue(model.GetSymbolInfo(call).CandidateSymbols.OfType<Microsoft.CodeAnalysis.IMethodSymbol>()
            .Any(m => m.Name == (related ? "Compute" : "Other")));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1);
        if (related)
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                    new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None));
            StringAssert.Contains(error.Message, "unresolved invocation");
            Assert.AreEqual(0, probe.StoreCalls);
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
            var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            var updated = stored.Value.ModifiedSolution.GetDocument(document.Id)!;
            var updatedModel = (await updated.GetSemanticModelAsync())!;
            var updatedCall = (await updated.GetSyntaxRootAsync())!.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => n.Expression.ToString().Contains("Compute", StringComparison.Ordinal));
            var target = (Microsoft.CodeAnalysis.IMethodSymbol)updatedModel.GetSymbolInfo(updatedCall).Symbol!;
            Assert.AreEqual(Microsoft.CodeAnalysis.SpecialType.System_String, target.TypeArguments.Single().SpecialType);
            Assert.AreEqual("value", target.Parameters.Single().Name);
            Assert.AreEqual(1, probe.StoreCalls);
        }
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }
}
