using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public sealed partial class InterfaceParameterSemanticsIntegrationTests
{
    [TestMethod]
    [DataRow("standalone", "ref")]
    [DataRow("cross-project", "ref")]
    [DataRow("dependency-inversion", "ref")]
    [DataRow("extract-and-wire", "ref")]
    [DataRow("standalone", "unsafe")]
    [DataRow("cross-project", "unsafe")]
    [DataRow("dependency-inversion", "unsafe")]
    [DataRow("extract-and-wire", "unsafe")]
    [DataRow("standalone", "attributes")]
    [DataRow("cross-project", "attributes")]
    [DataRow("dependency-inversion", "attributes")]
    [DataRow("extract-and-wire", "attributes")]
    [DataRow("standalone", "names")]
    [DataRow("cross-project", "names")]
    [DataRow("dependency-inversion", "names")]
    [DataRow("extract-and-wire", "names")]
    [DataRow("standalone", "struct-ref")]
    [DataRow("cross-project", "struct-ref")]
    [DataRow("dependency-inversion", "struct-ref")]
    [DataRow("extract-and-wire", "struct-ref")]
    [DataRow("standalone", "ref-struct")]
    [DataRow("cross-project", "ref-struct")]
    [DataRow("dependency-inversion", "ref-struct")]
    [DataRow("extract-and-wire", "ref-struct")]
    [DataRow("standalone", "accessor-ref")]
    [DataRow("cross-project", "accessor-ref")]
    [DataRow("dependency-inversion", "accessor-ref")]
    [DataRow("extract-and-wire", "accessor-ref")]
    public async Task Declaration_Fidelity_Compiles_And_Executes_Through_All_Routes(string route, string shape)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var source = shape switch
        {
            "ref" => RefSource,
            "struct-ref" => StructRefSource,
            "ref-struct" => StructRefSource.Replace("public struct", "public ref struct", StringComparison.Ordinal),
            "accessor-ref" => StructRefSource.Replace("[System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int Mutable => ref _value;",
                    "public ref int Mutable { [System.Diagnostics.CodeAnalysis.UnscopedRef] get => ref _value; }", StringComparison.Ordinal)
                .Replace("[System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int this[int key] => ref _value;",
                    "public ref int this[int key] { [System.Diagnostics.CodeAnalysis.UnscopedRef] get => ref _value; }", StringComparison.Ordinal),
            "unsafe" => UnsafeSource,
            "names" => EscapedSource,
            _ => AttributeSource
        };
        var calls = shape switch
        {
            "ref" => RefCalls,
            "struct-ref" or "ref-struct" or "accessor-ref" => RefGenericCalls,
            "unsafe" => UnsafeCalls,
            "names" => EscapedCalls,
            _ => AttributeCalls
        };
        var path = workspace.GetPath("SampleLib", "ParameterService.cs");
        await File.WriteAllTextAsync(path, source);
        if (shape == "attributes") await AddHandlersAsync(workspace);
        if (shape == "unsafe")
        {
            SetAllowUnsafe(workspace.GetPath("SampleLib", "SampleLib.csproj"));
            SetAllowUnsafe(workspace.GetPath("Contracts", "Contracts.csproj"));
        }
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var original = (await before.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!
            .GetTypeByMetadataName("Contracts.ParameterService")!;
        var references = (await before.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.References.OfType<PortableExecutableReference>().ToArray();
        var concreteCalls = shape is "struct-ref" or "ref-struct" or "accessor-ref" ? RefCalls : calls;
        var concreteBehavior = ExecuteContractCalls(source, null, concreteCalls.Replace("IParameterService", "ParameterService", StringComparison.Ordinal), references,
            shape == "unsafe", shape == "attributes" ? SharedHandlers : null);
        var expected = shape switch { "ref" or "struct-ref" or "ref-struct" or "accessor-ref" => "9|9|9|9", "unsafe" => "5|5|6|5", "names" => "11", _ => "5|7hello3|object8world4|7index5:assigned:2" };
        Assert.AreEqual(expected, concreteBehavior);
        var store = new CompositePreviewStore();
        var preview = await FidelityPreviewAsync(workspace, path, route, store);
        var pending = FidelityPreviewSolution(before, preview.PreviewToken, route, store);
        await AssertFidelityAsync(pending, original, calls, route, shape, references, expected);
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        var apply = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, store, undoService: UndoService).ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "fidelity_test", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertFidelityAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), original, calls, route, shape, references, expected);
    }

    [TestMethod]
    [DataRow("standalone", "handler")]
    [DataRow("cross-project", "handler")]
    [DataRow("dependency-inversion", "handler")]
    [DataRow("extract-and-wire", "handler")]
    [DataRow("standalone", "handler-alternate")]
    [DataRow("cross-project", "handler-alternate")]
    [DataRow("dependency-inversion", "handler-alternate")]
    [DataRow("extract-and-wire", "handler-alternate")]
    [DataRow("standalone", "handler-dynamic")]
    [DataRow("cross-project", "handler-dynamic")]
    [DataRow("dependency-inversion", "handler-dynamic")]
    [DataRow("extract-and-wire", "handler-dynamic")]
    [DataRow("standalone", "handler-overload")]
    [DataRow("cross-project", "handler-overload")]
    [DataRow("dependency-inversion", "handler-overload")]
    [DataRow("extract-and-wire", "handler-overload")]
    [DataRow("cross-project", "unsafe")]
    [DataRow("dependency-inversion", "unsafe")]
    [DataRow("extract-and-wire", "unsafe")]
    public async Task Incompatible_Projected_Contract_Refuses_Before_Any_Publication(string route, string shape)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var targetProjectName = shape == "unsafe" ? "SensitiveTargetProject" : "Contracts";
        AddProjectToCopiedSolution(workspace.RootPath, targetProjectName, "net10.0");
        var path = workspace.GetPath("SampleLib", "ParameterService.cs");
        await File.WriteAllTextAsync(path, shape switch
        {
            "unsafe" => UnsafeSource,
            "handler-alternate" => IncompatibleAlternateSource,
            "handler-overload" => OverloadedHandlerSource,
            "handler-dynamic" => IncompatibleHandlerSource.Replace("Log(", "Log(dynamic ignored, ", StringComparison.Ordinal)
                .Replace("ProviderHandler", "SensitiveTenantHandler", StringComparison.Ordinal),
            _ => IncompatibleHandlerSource
        });
        if (shape == "unsafe") SetAllowUnsafe(workspace.GetPath("SampleLib", "SampleLib.csproj"));
        else await AddHandlersAsync(workspace);
        if (shape == "handler-dynamic")
            await File.WriteAllTextAsync(workspace.GetPath("Contracts", "SharedHandler.cs"),
                SharedHandlers.Replace("ProviderHandler", "SensitiveTenantHandler", StringComparison.Ordinal));
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        if (shape != "unsafe")
        {
            var sourceProject = before.Projects.Single(p => p.Name == "SampleLib");
            var arguments = shape == "handler-overload" ? "baseMessage: $\"literal\"" :
                shape is "handler-alternate" or "handler-dynamic" ? "7, $\"literal\"" : "$\"literal\"";
            await AssertCompilesAsync(before.AddDocument(DocumentId.CreateNewId(sourceProject.Id), "ConcreteHandlerControl.cs", SourceText.From(
                "namespace Contracts; public static class ConcreteHandlerControl { public static string Run() => new ParameterService().Log(" + arguments + "); }")));
        }
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var files = SnapshotSources(workspace.RootPath);
        var store = new RejectPublicationStore();
        var composite = new RejectCompositePublicationStore();
        var cache = new CompilationCache(WorkspaceManager);
        var cross = new CrossProjectRefactoringService(WorkspaceManager, store, cache);
        var standalone = new InterfaceExtractionService(WorkspaceManager, store, cache, NullLogger<InterfaceExtractionService>.Instance);
        var exception = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => route switch
        {
            "standalone" => standalone.PreviewExtractInterfaceAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", null, false, CancellationToken.None),
            "dependency-inversion" => cross.PreviewDependencyInversionAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", targetProjectName, CancellationToken.None),
            "extract-and-wire" => new ExtractAndWireOrchestrator(WorkspaceManager, composite, store, cross, DiRegistrationService)
                .PreviewExtractAndWireInterfaceAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", targetProjectName, true, CancellationToken.None),
            _ => cross.PreviewExtractInterfaceAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", targetProjectName, CancellationToken.None),
        });
        StringAssert.Contains(exception.Message, shape == "unsafe" ? "unsafe" : "handler");
        Assert.IsFalse(exception.Message.Contains("SensitiveTargetProject", StringComparison.Ordinal), exception.Message);
        if (shape != "unsafe")
        {
            Assert.IsFalse(exception.Message.Contains("SensitiveTenantHandler", StringComparison.Ordinal), exception.Message);
            Assert.IsFalse(exception.Message.Contains("IFormatProvider", StringComparison.Ordinal), exception.Message);
            Assert.IsFalse(exception.Message.Contains("IParameterService", StringComparison.Ordinal), exception.Message);
        }
        Assert.AreEqual(0, store.Publications);
        Assert.AreEqual(0, composite.Publications);
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        var after = SnapshotSources(workspace.RootPath);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), after.Keys.ToArray());
        foreach (var (file, bytes) in files) CollectionAssert.AreEqual(bytes, after[file], file);
    }

    [TestMethod]
    public async Task Standalone_Escapes_Event_And_Supported_Method_Type_Parameter_Names()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var path = workspace.GetPath("SampleLib", "EscapedService.cs");
        await File.WriteAllTextAsync(path, "namespace SampleLib; public class EscapedService { public event System.Action @event { add { } remove { } } public @class Echo<@class>(@class @value) => @value; }");
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId, path, "EscapedService", "IEscapedService", null, false, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var calls = "namespace SampleLib; public static class Consumer { public static int Run(IEscapedService service) { service.@event += () => { }; return service.Echo(13); } }";
        var project = pending.Projects.Single(p => p.Name == "SampleLib");
        await AssertCompilesAsync(pending.AddDocument(DocumentId.CreateNewId(project.Id), "Consumer.cs", SourceText.From(calls)));
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "escaped_test", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    [DataRow("standalone")]
    [DataRow("cross-project")]
    [DataRow("dependency-inversion")]
    [DataRow("extract-and-wire")]
    public async Task Vararg_Method_Retains_Calling_Convention(string route)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        var path = workspace.GetPath("SampleLib", "ParameterService.cs");
        await File.WriteAllTextAsync(path, "namespace Contracts; public class ParameterService { public void Write(int value, __arglist) { } }");
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var store = new CompositePreviewStore();
        var preview = await FidelityPreviewAsync(workspace, path, route, store);
        var pending = FidelityPreviewSolution(before, preview.PreviewToken, route, store);
        await AssertVarargAsync(pending, route);
        var apply = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, store).ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "vararg_test", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertVarargAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), route);
    }

    private static Dictionary<string, byte[]> SnapshotSources(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".csproj", StringComparison.Ordinal))
        .ToDictionary(p => p, File.ReadAllBytes, StringComparer.Ordinal);

    [TestMethod]
    [DataRow("@class")]
    [DataRow("Original")]
    [DataRow("Projected")]
    public async Task Standalone_Handler_Probe_Retains_Supported_Generic_Method_Context(string typeParameter)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await AddHandlersAsync(workspace);
        var source = "namespace Contracts; public class ParameterService { public string Generic<" + typeParameter + ">(" + typeParameter + " value, int index, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument(\"\", \"index\")] ref ObjectHandler handler) => handler.Value; }";
        var calls = "namespace Contracts; public static class ContractCalls { public static string Run() { IParameterService service = new ParameterService(); return service.Generic<int>(12, 7, $\"text\"); } }";
        var path = workspace.GetPath("SampleLib", "ParameterService.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var references = (await before.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.References.OfType<PortableExecutableReference>().ToArray();
        Assert.AreEqual("7text", ExecuteContractCalls(source, null, calls.Replace("IParameterService", "ParameterService", StringComparison.Ordinal), references, false, SharedHandlers));
        var store = new CompositePreviewStore();
        var preview = await FidelityPreviewAsync(workspace, path, "standalone", store);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        await AssertCompilesAsync(pending);
        var generated = (await pending.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "IParameterService.cs").GetTextAsync()).ToString();
        var implementation = (await pending.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "ParameterService.cs").GetTextAsync()).ToString();
        Assert.AreEqual("7text", ExecuteContractCalls(implementation, generated, calls, references, false, SharedHandlers));
        var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "generic_handler", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        await AssertCompilesAsync(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    [DataRow("standalone", false, false)]
    [DataRow("cross-project", false, false)]
    [DataRow("dependency-inversion", false, false)]
    [DataRow("extract-and-wire", false, false)]
    [DataRow("standalone", true, false)]
    [DataRow("cross-project", true, false)]
    [DataRow("dependency-inversion", true, false)]
    [DataRow("extract-and-wire", true, false)]
    [DataRow("standalone", false, true)]
    [DataRow("cross-project", false, true)]
    [DataRow("dependency-inversion", false, true)]
    [DataRow("extract-and-wire", false, true)]
    public async Task Handler_Probe_Preserves_Static_Call_With_Dynamic_Neighbor(string route, bool overloads, bool manual)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await AddHandlersAsync(workspace);
        var source = overloads ? OverloadedHandlerSource : "namespace Contracts; public class ParameterService { public string Log(int index, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument(\"\", \"index\")] ref ObjectHandler handler, dynamic neighbor, PlainHandler ordinary) => handler.Value + ordinary.Value; }";
        var calls = overloads
            ? "namespace Contracts; public static class ContractCalls { public static string Run() { IParameterService service = new ParameterService(); return service.Log(baseMessage: $\"text\") + \"|\" + service.Log(derivedMessage: $\"text\"); } }"
            : "namespace Contracts; public static class ContractCalls { public static string Run() { IParameterService service = new ParameterService(); return service.Log(7, $\"text\", new object(), $\"plain\"); } }";
        var handlers = overloads ? SharedHandlers.Replace("public BaseHandler(int length, int count, System.IFormatProvider receiver)",
            "public BaseHandler(int length, int count, object receiver)", StringComparison.Ordinal) : SharedHandlers;
        await File.WriteAllTextAsync(workspace.GetPath("Contracts", "SharedHandler.cs"), handlers);
        if (manual)
        {
            source = source.Replace("PlainHandler", "ManualHandler", StringComparison.Ordinal);
            handlers = handlers.Replace("PlainHandler", "ManualHandler", StringComparison.Ordinal)
                .Replace("ManualHandler(int length, int count)", "ManualHandler(int length, int count, int mode)", StringComparison.Ordinal);
            calls = calls.Replace("return service.Log", "var ordinary = new ManualHandler(0, 0, 1); ordinary.AppendLiteral(\"plain\"); return service.Log", StringComparison.Ordinal)
                .Replace("$\"plain\"", "ordinary", StringComparison.Ordinal);
            await File.WriteAllTextAsync(workspace.GetPath("Contracts", "SharedHandler.cs"), handlers);
        }
        var expected = overloads ? "basetext|derivedtext" : "7textplain";
        var path = workspace.GetPath("SampleLib", "ParameterService.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var references = (await before.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.References.OfType<PortableExecutableReference>().ToArray();
        Assert.AreEqual(expected, ExecuteContractCalls(source, null, calls.Replace("IParameterService", "ParameterService", StringComparison.Ordinal), references, false, handlers));
        var store = new CompositePreviewStore();
        var preview = await FidelityPreviewAsync(workspace, path, route, store);
        var pending = FidelityPreviewSolution(before, preview.PreviewToken, route, store);
        await AssertCompilesAsync(pending);
        var generated = (await pending.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "IParameterService.cs").GetTextAsync()).ToString();
        var implementation = (await pending.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "ParameterService.cs").GetTextAsync()).ToString();
        if (!overloads) StringAssert.Contains(generated, "dynamic neighbor");
        Assert.AreEqual(expected, ExecuteContractCalls(implementation, generated, calls, references, false, handlers));
        var apply = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, store, undoService: UndoService).ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "handler_static_binding", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(reloaded);
        var persistedContract = (await reloaded.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "IParameterService.cs").GetTextAsync()).ToString();
        Assert.AreEqual(expected, ExecuteContractCalls(await File.ReadAllTextAsync(path), persistedContract, calls, references, false, handlers));
    }

    [TestMethod]
    [DataRow("standalone", false, false, "plain")]
    [DataRow("standalone", false, true, "plain")]
    [DataRow("standalone", true, false, "plain")]
    [DataRow("standalone", true, true, "plain")]
    [DataRow("cross-project", false, false, "plain")]
    [DataRow("cross-project", false, true, "plain")]
    [DataRow("dependency-inversion", false, false, "plain")]
    [DataRow("dependency-inversion", false, true, "plain")]
    [DataRow("extract-and-wire", false, false, "plain")]
    [DataRow("extract-and-wire", false, true, "plain")]
    [DataRow("standalone", false, false, "block")]
    [DataRow("standalone", false, true, "block")]
    [DataRow("cross-project", false, false, "block")]
    [DataRow("cross-project", false, true, "block")]
    [DataRow("dependency-inversion", false, false, "block")]
    [DataRow("dependency-inversion", false, true, "block")]
    [DataRow("extract-and-wire", false, false, "block")]
    [DataRow("extract-and-wire", false, true, "block")]
    [DataRow("standalone", false, false, "line")]
    [DataRow("standalone", false, true, "line")]
    [DataRow("cross-project", false, false, "line")]
    [DataRow("cross-project", false, true, "line")]
    [DataRow("dependency-inversion", false, false, "line")]
    [DataRow("dependency-inversion", false, true, "line")]
    [DataRow("extract-and-wire", false, false, "line")]
    [DataRow("extract-and-wire", false, true, "line")]
    [DataRow("standalone", false, false, "primary-block")]
    [DataRow("standalone", false, true, "primary-line")]
    [DataRow("cross-project", false, false, "primary-block")]
    [DataRow("cross-project", false, true, "primary-line")]
    [DataRow("dependency-inversion", false, false, "primary-block")]
    [DataRow("dependency-inversion", false, true, "primary-line")]
    [DataRow("extract-and-wire", false, false, "primary-block")]
    [DataRow("extract-and-wire", false, true, "primary-line")]
    public Task Handler_Probe_Retains_Unused_Constrained_Containing_Type_Parameter(string route, bool nested, bool existingBase, string layout) =>
        AssertHeaderProjectionAsync(route, nested, existingBase, layout);

    [TestMethod]
    [DataRow("standalone", "generic")]
    [DataRow("standalone", "base")]
    [DataRow("standalone", "primary")]
    [DataRow("cross-project", "generic")]
    [DataRow("cross-project", "base")]
    [DataRow("cross-project", "primary")]
    public Task Base_Type_Insertion_Separates_Constraint_Without_Existing_Trivia(string route, string anchor) =>
        AssertHeaderProjectionAsync(route, false, anchor == "base", anchor == "primary" ? "primary-tight" : "tight");

    private async Task AssertHeaderProjectionAsync(string route, bool nested, bool existingBase, string layout)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await AddHandlersAsync(workspace);
        var primary = layout.StartsWith("primary-", StringComparison.Ordinal);
        var headerTrivia = layout switch { "block" or "primary-block" => "/* header-boundary */", "line" or "primary-line" => " // header-boundary\r\n", "tight" or "primary-tight" => "", _ => " " };
        const string member = "public string Log(int index, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument(\"\", \"index\")] ref ObjectHandler handler) => handler.Value;";
        var source = "namespace Contracts; " + (nested ? "public class Outer<U> where U : class, new() { " : "") +
            "public class ParameterService<T>" + (primary ? "(int seed = 1)" : "") +
            (existingBase ? layout == "tight" ? " : System.Collections.Generic.List<T>" : " : object" : "") + headerTrivia +
            "where T : class, new()\r\n// body-boundary\r\n{ " + member + (primary ? " public int Seed => seed;" : "") + " }" + (nested ? " }" : "");
        var calls = "namespace Contracts; public static class ContractCalls { public static string Run() { IParameterService service = new " +
            (nested ? "Outer<object>." : "") + "ParameterService<object>(); return service.Log(7, $\"text\"); } }";
        var path = workspace.GetPath("SampleLib", "ParameterService.cs");
        await File.WriteAllTextAsync(path, source);
        var unrelatedPath = workspace.GetPath("SampleLib", "Unchanged.cs");
        var unrelated = System.Text.Encoding.UTF8.GetBytes("// unchanged bytes\r\nnamespace Contracts; public class Unchanged { }\r\n");
        await File.WriteAllBytesAsync(unrelatedPath, unrelated);
        await workspace.LoadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);
        var references = (await before.Projects.Single(p => p.Name == "SampleLib").GetCompilationAsync())!.References.OfType<PortableExecutableReference>().ToArray();
        var concreteCalls = calls.Replace("IParameterService service", "var service", StringComparison.Ordinal);
        Assert.AreEqual("7text", ExecuteContractCalls(source, null, concreteCalls, references, false, SharedHandlers));
        var store = new CompositePreviewStore();
        var preview = await FidelityPreviewAsync(workspace, path, route, store);
        var pending = FidelityPreviewSolution(before, preview.PreviewToken, route, store);
        await AssertCompilesAsync(pending);
        var generated = (await pending.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "IParameterService.cs").GetTextAsync()).ToString();
        var implementation = (await pending.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "ParameterService.cs").GetTextAsync()).ToString();
        if (headerTrivia.Trim().Length > 0) StringAssert.Contains(implementation, headerTrivia.TrimStart());
        if (existingBase)
        {
            var baseType = layout == "tight" ? "System.Collections.Generic.List<T>" : "object";
            StringAssert.Contains(implementation, baseType + (route == "standalone" ? ", IParameterService" : "," + Environment.NewLine + "    IParameterService"));
        }
        StringAssert.Contains(implementation, "// body-boundary\r\n");
        StringAssert.Contains(implementation, member);
        Assert.AreEqual("7text", ExecuteContractCalls(implementation, generated, calls, references, false, SharedHandlers));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        var apply = route == "extract-and-wire"
            ? await new CompositeApplyOrchestrator(WorkspaceManager, store, undoService: UndoService).ApplyCompositeAsync(preview.PreviewToken, CancellationToken.None)
            : await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "constrained_header", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        await workspace.ReloadAsync();
        var reloaded = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(reloaded);
        var persistedContract = (await reloaded.Projects.SelectMany(p => p.Documents).Single(d => d.Name == "IParameterService.cs").GetTextAsync()).ToString();
        var persisted = await File.ReadAllTextAsync(path);
        Assert.AreEqual(implementation, persisted);
        Assert.AreEqual("7text", ExecuteContractCalls(persisted, persistedContract, calls, references, false, SharedHandlers));
        CollectionAssert.AreEqual(unrelated, await File.ReadAllBytesAsync(unrelatedPath));
    }

    private static async Task AssertVarargAsync(Solution solution, string route)
    {
        await AssertCompilesAsync(solution);
        var project = solution.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
        var type = (await project.GetCompilationAsync())!.GetTypeByMetadataName("Contracts.IParameterService")!;
        Assert.IsTrue(type.GetMembers("Write").OfType<IMethodSymbol>().Single().IsVararg);
        var source = solution.Projects.Single(p => p.Name == "SampleLib");
        await AssertCompilesAsync(solution.AddDocument(DocumentId.CreateNewId(source.Id), "VarargCalls.cs", SourceText.From(
            "namespace Contracts; public static class VarargCalls { public static void Call(IParameterService service) => service.Write(1, __arglist(2, \"three\")); }")));
    }

    private static Task<RefactoringPreviewDto> FidelityPreviewAsync(IsolatedWorkspaceScope workspace, string path,
        string route, CompositePreviewStore store) => route switch
        {
            "standalone" => InterfaceExtractionService.PreviewExtractInterfaceAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", null, false, CancellationToken.None),
            "dependency-inversion" => CrossProjectRefactoringService.PreviewDependencyInversionAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", "Contracts", CancellationToken.None),
            "extract-and-wire" => new ExtractAndWireOrchestrator(WorkspaceManager, store, PreviewStore, CrossProjectRefactoringService, DiRegistrationService)
                .PreviewExtractAndWireInterfaceAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", "Contracts", true, CancellationToken.None),
            _ => CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId, path, "ParameterService", "IParameterService", "Contracts", CancellationToken.None),
        };

    private static Solution FidelityPreviewSolution(Solution before, string token, string route, CompositePreviewStore store)
    {
        if (route != "extract-and-wire") return PreviewStore.Retrieve(token)!.Value.ModifiedSolution;
        var solution = before;
        foreach (var mutation in store.Retrieve(token)!.Value.Mutations.Where(m => m.FilePath.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var document = solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => d.FilePath == mutation.FilePath);
            solution = document is null
                ? solution.AddDocument(DocumentId.CreateNewId(solution.Projects.Single(p => p.Name == "Contracts").Id), Path.GetFileName(mutation.FilePath), SourceText.From(mutation.UpdatedContent!), filePath: mutation.FilePath)
                : solution.WithDocumentText(document.Id, SourceText.From(mutation.UpdatedContent!));
        }
        var source = solution.Projects.Single(p => p.Name == "SampleLib");
        var targetId = solution.Projects.Single(p => p.Name == "Contracts").Id;
        return source.ProjectReferences.Any(r => r.ProjectId == targetId)
            ? solution : solution.AddProjectReference(source.Id, new ProjectReference(targetId));
    }

    private static async Task AssertFidelityAsync(Solution solution, INamedTypeSymbol original, string calls, string route,
        string shape, IReadOnlyList<MetadataReference> references, string expected)
    {
        await AssertCompilesAsync(solution);
        var project = solution.Projects.Single(p => p.Name == (route == "standalone" ? "SampleLib" : "Contracts"));
        var contract = (await project.GetCompilationAsync())!.GetTypeByMetadataName("Contracts.IParameterService")!;
        foreach (var method in original.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary &&
            m.DeclaredAccessibility == Accessibility.Public && !m.IsStatic))
        {
            var emitted = contract.GetMembers(method.Name).OfType<IMethodSymbol>().Single();
            Assert.AreEqual(method.RefKind, emitted.RefKind, method.Name);
            CollectionAssert.AreEqual(method.GetAttributes().Select(a => a.ToString()).ToArray(), emitted.GetAttributes().Select(a => a.ToString()).ToArray(), method.Name);
            CollectionAssert.AreEqual(method.Parameters.Select(ParameterContract).ToArray(), emitted.Parameters.Select(ParameterContract).ToArray(), method.Name);
        }
        foreach (var property in original.GetMembers().OfType<IPropertySymbol>())
        {
            var emitted = contract.GetMembers().OfType<IPropertySymbol>().Single(p => p.MetadataName == property.MetadataName &&
                p.Parameters.Select(a => a.Type.ToDisplayString()).SequenceEqual(property.Parameters.Select(a => a.Type.ToDisplayString())));
            Assert.AreEqual(property.RefKind, emitted.RefKind, property.Name);
            CollectionAssert.AreEqual(property.GetAttributes().Select(a => a.ToString()).ToArray(), emitted.GetAttributes().Select(a => a.ToString()).ToArray(), property.Name);
            if (property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
                CollectionAssert.AreEqual(property.GetMethod.GetAttributes().Select(a => a.ToString()).ToArray(), emitted.GetMethod!.GetAttributes().Select(a => a.ToString()).ToArray(), property.Name + " getter");
            else
                Assert.IsNull(emitted.GetMethod, property.Name + " non-public getter must remain omitted");
        }
        var source = solution.Projects.Single(p => p.Name == "SampleLib");
        var generated = (await project.Documents.Single(d => d.Name == "IParameterService.cs").GetTextAsync()).ToString();
        var implementation = (await source.Documents.Single(d => d.Name == "ParameterService.cs").GetTextAsync()).ToString();
        Assert.AreEqual(expected, ExecuteContractCalls(implementation, generated, calls, references, shape == "unsafe", shape == "attributes" ? SharedHandlers : null));
    }

    private static async Task AddHandlersAsync(IsolatedWorkspaceScope workspace)
    {
        await File.WriteAllTextAsync(workspace.GetPath("Contracts", "SharedHandler.cs"), SharedHandlers);
        var path = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var xml = XDocument.Load(path);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../Contracts/Contracts.csproj"))));
        xml.Save(path);
    }

    private static void SetAllowUnsafe(string path)
    {
        var xml = XDocument.Load(path);
        xml.Root!.Elements("PropertyGroup").First().SetElementValue("AllowUnsafeBlocks", "true");
        xml.Save(path);
    }

    private sealed class RejectPublicationStore : IPreviewStore
    {
        public int Publications { get; private set; }
        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description)
        {
            Publications++;
            throw new InvalidOperationException("Unexpected ordinary preview publication.");
        }
        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated) => Store(workspaceId, modifiedSolution, workspaceVersion, description);
        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes) => Store(workspaceId, modifiedSolution, workspaceVersion, description);
        public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token) => null;
        public void Invalidate(string token) { }
        public void InvalidateAll(string? workspaceId = null) { }
        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) { }
        public string? PeekWorkspaceId(string token) => null;
    }

    private sealed class RejectCompositePublicationStore : ICompositePreviewStore
    {
        public int Publications { get; private set; }
        public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations)
        {
            Publications++;
            throw new InvalidOperationException("Unexpected composite preview publication.");
        }
        public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token) => null;
        public void Invalidate(string token) { }
        public string? PeekWorkspaceId(string token) => null;
    }

    private const string RefSource = """
        namespace Contracts;
        public class ParameterService
        {
            private int _value = 1;
            public ref int Value() => ref _value;
            public ref readonly int Read() => ref _value;
            public ref int Mutable => ref _value;
            public ref readonly int ReadOnly => ref _value;
            public ref int this[int key] => ref _value;
            public ref readonly int this[string key] => ref _value;
            public int Ordinary() => _value;
        }
        """;
    private const string RefCalls = """
        namespace Contracts;
        public static class ContractCalls
        {
            public static string Run()
            {
                IParameterService service = new ParameterService();
                ref int value = ref service.Value(); value = 7;
                ref int property = ref service.Mutable; property++;
                ref int index = ref service[1]; index++;
                ref readonly int read = ref service.Read();
                ref readonly int readProperty = ref service.ReadOnly;
                ref readonly int readIndex = ref service["key"];
                return $"{read}|{readProperty}|{readIndex}|{service.Ordinary()}";
            }
        }
        """;
    private const string UnsafeSource = """
        namespace Contracts;
        public unsafe class ParameterService
        {
            public int* Echo(int* value) => value;
            public delegate*<int, int> Callback(delegate*<int, int> callback) => callback;
            public delegate*<int, int> GetCallback() => &Double;
            public delegate*<delegate*<int, int>, int> Nested() => null;
            private static int Double(int value) => value * 2;
            public int* Pointer { get; set; }
            public int* this[int* key] => key;
            public int Ordinary() => 5;
        }
        """;
    private const string StructRefSource = """
        namespace Contracts;
        public struct ParameterService
        {
            private int _value;
            [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int Value() => ref _value;
            [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref readonly int Read() => ref _value;
            [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int Mutable => ref _value;
            [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref readonly int ReadOnly => ref _value;
            [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref int this[int key] => ref _value;
            [System.Diagnostics.CodeAnalysis.UnscopedRef] public ref readonly int this[string key] => ref _value;
            public int Ordinary() => _value;
        }
        """;
    private const string RefGenericCalls = """
        namespace Contracts;
        public static class ContractCalls
        {
            public static string Run() { var concrete = new ParameterService(); return Invoke(ref concrete); }
            private static string Invoke<T>(ref T service) where T : IParameterService, allows ref struct
            {
                ref int value = ref service.Value(); value = 7;
                ref int property = ref service.Mutable; property++;
                ref int index = ref service[1]; index++;
                ref readonly int read = ref service.Read();
                ref readonly int readProperty = ref service.ReadOnly;
                ref readonly int readIndex = ref service["key"];
                return $"{read}|{readProperty}|{readIndex}|{service.Ordinary()}";
            }
        }
        """;
    private const string EscapedSource = """
        namespace Contracts;
        public class ParameterService { public int @class { get; set; } public int @return(int @event) => @class + @event; }
        """;
    private const string EscapedCalls = """
        namespace Contracts;
        public static class ContractCalls { public static string Run() { IParameterService service = new ParameterService(); service.@class = 4; return service.@return(@event: 7).ToString(); } }
        """;
    private const string UnsafeCalls = """
        namespace Contracts;
        public static unsafe class ContractCalls
        {
            public static string Run()
            {
                IParameterService service = new ParameterService();
                int value = 4; int* pointer = service.Echo(&value); *pointer = 5;
                service.Pointer = &value;
                var callback = service.Callback(service.GetCallback());
                return $"{*service[&value]}|{*service.Pointer}|{callback(3)}|{service.Ordinary()}";
            }
        }
        """;
    private const string AttributeSource = """
        namespace Contracts;
        public class ParameterService
        {
            private string _last = "";
            public object? GetFormat(System.Type? type) => null;
            public bool Try([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? result) { result = "value"; return true; }
            public string Log(int value, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("", "value")] ref ObjectHandler handler) => handler.Value;
            public string Alternative(int value, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("", "value")] ref AlternativeHandler handler) => handler.Value;
            public string this[int index, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("", "index")] ObjectHandler handler]
            { private get => handler.Value; set { _last = handler.Value + ":" + value; } }
            public string this[int index, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("", "index")] ObjectHandler handler, params int[] rest]
            { set { _last = handler.Value + ":" + value + ":" + rest.Length; } }
            public string Last() => _last;
        }
        public class DerivedParameterService : ParameterService { }
        """;
    private const string AttributeCalls = """
        namespace Contracts;
        public static class ContractCalls
        {
            public static string Run()
            {
                IParameterService service = new ParameterService();
                if (!service.Try(out string? result)) throw new System.InvalidOperationException();
                service[7, $"index{5}"] = "assigned";
                service[7, $"index{5}", 1, 2] = "assigned";
                return result.Length + "|" + service.Log(7, $"hello{3}") + "|" + service.Alternative(value: 8, handler: $"world{4}") + "|" + service.Last();
            }
        }
        """;
    private const string IncompatibleHandlerSource = """
        namespace Contracts;
        public class ParameterService : System.IFormatProvider
        {
            public object? GetFormat(System.Type? type) => null;
            public string Log([System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("")] ref ProviderHandler handler) => "ok";
        }
        """;
    private const string SharedHandlers = """
        namespace Contracts;
        [System.Runtime.CompilerServices.InterpolatedStringHandler]
        public ref struct ObjectHandler
        {
            public string Value;
            public ObjectHandler(int length, int count, object receiver, int value) { Value = value.ToString(); }
            public void AppendLiteral(string text) => Value += text;
            public void AppendFormatted<T>(T value) => Value += value?.ToString();
        }
        [System.Runtime.CompilerServices.InterpolatedStringHandler]
        public ref struct AlternativeHandler
        {
            public string Value;
            public AlternativeHandler(int length, int count, System.IFormatProvider receiver, int value) { Value = "provider" + value; }
            public AlternativeHandler(int length, int count, object receiver, int value) { Value = "object" + value; }
            public void AppendLiteral(string text) => Value += text;
            public void AppendFormatted<T>(T value) => Value += value?.ToString();
        }
        [System.Runtime.CompilerServices.InterpolatedStringHandler]
        public ref struct ProviderHandler
        {
            public ProviderHandler(int length, int count, System.IFormatProvider receiver) { }
            public void AppendLiteral(string text) { }
            public void AppendFormatted<T>(T value) { }
        }
        [System.Runtime.CompilerServices.InterpolatedStringHandler]
        public class BaseHandler
        {
            public string Value = "base";
            public BaseHandler(int length, int count, System.IFormatProvider receiver) { }
            public void AppendLiteral(string text) => Value += text;
        }
        [System.Runtime.CompilerServices.InterpolatedStringHandler]
        public class DerivedHandler : BaseHandler
        {
            public DerivedHandler(int length, int count, object receiver) : base(length, count, null!) { Value = "derived"; }
        }
        [System.Runtime.CompilerServices.InterpolatedStringHandler]
        public ref struct PlainHandler
        {
            public string Value;
            public PlainHandler(int length, int count) { Value = ""; }
            public void AppendLiteral(string text) => Value += text;
        }
        """;
    private const string OverloadedHandlerSource = """
        namespace Contracts;
        public class ParameterService : System.IFormatProvider
        {
            public object? GetFormat(System.Type? type) => null;
            public string Log([System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("")] BaseHandler baseMessage) => baseMessage.Value;
            public string Log([System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("")] DerivedHandler derivedMessage) => derivedMessage.Value;
        }
        """;
    private const string IncompatibleAlternateSource = """
        namespace Contracts;
        public class ParameterService : System.IFormatProvider
        {
            public object? GetFormat(System.Type? type) => null;
            public string Log(int index, [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("", "index")] ref AlternativeHandler handler) => handler.Value;
        }
        """;
}
