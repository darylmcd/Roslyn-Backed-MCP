using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureStructConstructionTests : ChangeSignatureTestBase
{
    [TestMethod]
    [DataRow("plain")]
    [DataRow("json")]
    [DataRow("partial")]
    public async Task ChangeSignaturePreview_StructConstruction_ZeroPrimaryRetainsInitialization(string shape)
    {
        var declaration = (shape == "json" ? "[method: System.Text.Json.Serialization.JsonConstructor] " : "")
            + "public partial struct Construction() { public int Value = SignatureProof.Next(); }";
        var source = "namespace SampleLib;\n" + declaration
            + (shape == "partial" ? "\npublic partial struct Construction { public int Other = SignatureProof.Next(); }" : "")
            + "\npublic static class SignatureProof { public static int Calls; public static int Next() => ++Calls; public static string Run() { Calls = 0; var edited = new Construction(); var runtime = System.Activator.CreateInstance<Construction>(); var json = System.Text.Json.JsonSerializer.Deserialize<Construction>(\"{}\"); return edited.Value + \"|\" + runtime.Value + \"|\" + json.Value + \"|\" + Calls; } }";
        var expected = shape == "partial" ? "1|3|5|6" : "1|2|3|3";
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        if (shape == "json")
        {
            var originalCompilation = await original.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
            Assert.IsNotNull(originalCompilation);
            Assert.AreEqual(1, originalCompilation.GetTypeByMetadataName("SampleLib.Construction")!.InstanceConstructors
                .Count(constructor => constructor.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
                    == "System.Text.Json.Serialization.JsonConstructorAttribute")));
        }
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("Construction()", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("add", Name: "added", ParameterType: "int"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        if (shape == "json")
        {
            var compilation = await stored.Value.ModifiedSolution.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
            Assert.IsNotNull(compilation);
            Assert.AreEqual(1, compilation.GetTypeByMetadataName("SampleLib.Construction")!.InstanceConstructors
                .Count(constructor => constructor.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
                    == "System.Text.Json.Serialization.JsonConstructorAttribute")));
        }
        Assert.AreEqual(1, preview.CallsiteUpdates?.Sum(call => call.CallsiteCount));
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_StructConstruction_ExternZeroRetainsOriginalAbi()
    {
        var declaration = "public struct Construction { public int Value; [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)] public extern Construction(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static Construction Run() => new Construction(); }";
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var originalCompilation = await original.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(originalCompilation);
        using var originalImage = new MemoryStream();
        Assert.IsTrue(originalCompilation.Emit(originalImage).Success, "Original external ABI declaration must compile.");
        var originalConstructor = originalCompilation.GetTypeByMetadataName("SampleLib.Construction")!.InstanceConstructors.Single();
        Assert.IsTrue(originalConstructor.IsExtern);
        var attributes = originalConstructor.GetAttributes().Select(attribute => attribute.ToString()).ToArray();
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("Construction()", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("add", Name: "added", ParameterType: "int"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var compilation = await stored.Value.ModifiedSolution.Projects.Single(project => project.Name == "SampleLib").GetCompilationAsync();
        Assert.IsNotNull(compilation);
        using var candidateImage = new MemoryStream();
        var emission = compilation.Emit(candidateImage);
        Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics));
        var bridge = compilation.GetTypeByMetadataName("SampleLib.Construction")!.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 1);
        Assert.IsFalse(bridge.IsExtern, "Adding a managed entry point must not invent a new native ABI.");
        var syntax = (Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax)await bridge.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
        Assert.IsNotNull(syntax.Initializer);
        var target = (IMethodSymbol?)compilation.GetSemanticModel(syntax.SyntaxTree).GetSymbolInfo(syntax.Initializer).Symbol;
        Assert.IsNotNull(target);
        Assert.IsTrue(target.IsExtern);
        Assert.AreEqual(0, target.Parameters.Length);
        CollectionAssert.AreEqual(attributes, target.GetAttributes().Select(attribute => attribute.ToString()).ToArray());
    }

    [TestMethod]
    [DataRow("add", "dependency-injection")]
    [DataRow("add", "custom")]
    [DataRow("add", "required")]
    [DataRow("add", "obsolete")]
    [DataRow("remove", "dependency-injection")]
    public async Task ChangeSignaturePreview_StructConstruction_RuntimeConstructorSelection(string operation, string selector)
    {
        var dependencyInjection = selector == "dependency-injection";
        var parameters = operation == "add" ? "" : "Dependency unused";
        var attributes = dependencyInjection ? "[Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]"
            : "[Selected]" + (selector == "required" ? "[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]"
                : selector == "obsolete" ? "[System.Obsolete(\"Retain the call contract.\")]" : "");
        var declaration = "public struct Construction { " + (selector == "required" ? "public required int Value { get; init; } " : "public int Value; ") + attributes + " public Construction("
            + parameters + ") { Value = 7; } }";
        var factory = dependencyInjection ? "Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<Construction>(new Provider())"
            : "((Construction)System.Linq.Enumerable.Single(typeof(Construction).GetConstructors(), constructor => constructor.IsDefined(typeof(SelectedAttribute), false)).Invoke(System.Array.Empty<object>())!)";
        var source = "namespace SampleLib;\n" + declaration
            + "\n[System.AttributeUsage(System.AttributeTargets.Constructor)] public sealed class SelectedAttribute : System.Attribute { }"
            + "\npublic sealed class Dependency { } public sealed class Provider : System.IServiceProvider { public object? GetService(System.Type type) => type == typeof(Dependency) ? new Dependency() : null; }"
            + "\npublic static class SignatureProof { public static string Run() => new Construction(" + (operation == "add" ? "" : "new Dependency()")
            + ").Value + \"|\" + " + factory + ".Value; }";
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        if (dependencyInjection)
        {
            var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
            var projectXml = System.Xml.Linq.XDocument.Load(projectPath);
            projectXml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup",
                new System.Xml.Linq.XElement("Reference",
                    new System.Xml.Linq.XAttribute("Include", "Microsoft.Extensions.DependencyInjection.Abstractions"),
                    new System.Xml.Linq.XElement("HintPath", typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilities).Assembly.Location))));
            projectXml.Save(projectPath);
        }
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|7", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var locator = SymbolLocator.BySource(path, 2, declaration.IndexOf("Construction(", StringComparison.Ordinal) + 1);
        if (operation == "remove")
        {
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
            {
                var accepted = await service.PreviewChangeSignatureAsync(
                    workspace.WorkspaceId, locator, new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
                var stored = Services.PreviewStore.Retrieve(accepted.PreviewToken);
                Assert.IsNotNull(stored);
                // A premature acceptance must exercise the actual factory, exposing its broken binding.
                _ = await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof");
            });
            StringAssert.Contains(error.Message, "ActivatorUtilitiesConstructor");
            Assert.AreEqual(0, probe.StoreCalls);
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator,
                new ChangeSignatureRequest("add", Name: "added", ParameterType: "int"), CancellationToken.None);
            var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
            Assert.IsNotNull(stored);
            Assert.AreEqual("7|7", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
            Assert.AreEqual(1, probe.StoreCalls);
            Assert.AreEqual(1, preview.CallsiteUpdates?.Sum(call => call.CallsiteCount));
            var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            Assert.AreEqual("7|7", await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
            return;
        }
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

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
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var zeroPath = workspace.GetPath("SampleLib", "ZeroOnly.cs");
        await File.WriteAllTextAsync(path, source);
        await File.WriteAllTextAsync(zeroPath, untouched);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var constructorColumn = shape == "ordinary" ? declaration.IndexOf("Construction(int", StringComparison.Ordinal) + 1
            : declaration.IndexOf("Construction", StringComparison.Ordinal) + 1;
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, constructorColumn), new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        async Task VerifyAsync(Solution solution)
        {
            Assert.AreEqual("7|0", await RunSignatureFixtureAsync(solution, "SampleLib.SignatureProof"));
            var zero = solution.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == zeroPath);
            Assert.AreEqual(untouched, (await zero.GetTextAsync()).ToString(), "Non-target construction must retain exact source text.");
        }
        await VerifyAsync(stored.Value.ModifiedSolution);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
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
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|7", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 2, declaration.IndexOf("Construction(int", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "value"), CancellationToken.None));
        StringAssert.Contains(error.Message, "JsonConstructor");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
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
