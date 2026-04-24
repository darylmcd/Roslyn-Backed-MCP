using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #3 — regression guard for `severity-high-fail-produces-code-that-does-not-compi`
/// and `dr-9-1-emits-interface-file-without-required-directive`. The NetworkDocumentation
/// audit §9.1 repro: extract_interface on DiagramService (method signatures referencing
/// NetworkInventory from NetworkDocumentation.Core.Models) produced IDiagramService.cs
/// without the required using. Post-apply: CS0246 on every use of NetworkInventory.
///
/// Fix: the legacy text-grep FilterRelevantUsings was replaced with a semantic walker
/// that inspects the candidate members' type symbols (return types, parameters, property
/// types, event types, type-parameter constraints, recursive generic arguments) and
/// collects their containing namespaces. The generated interface file's using block is
/// derived from that set, with source-file aliases / static-usings / global-usings
/// preserved.
/// </summary>
[TestClass]
public sealed class ExtractInterfaceSemanticUsingsTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task ExtractInterface_Emits_Using_For_Parameter_Type_Namespace()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var modelsDir = Path.Combine(sampleLibDir, "Models");
        Directory.CreateDirectory(modelsDir);

        // Set up: a type living at the project root whose methods reference a type
        // from a sibling namespace. The interface file we'll generate must add a
        // `using SampleLib.Models;` directive.
        await File.WriteAllTextAsync(Path.Combine(modelsDir, "Item3Inventory.cs"),
            """
            namespace SampleLib.Models;

            public class Item3Inventory
            {
                public int Count { get; set; }
            }
            """);

        var servicePath = Path.Combine(sampleLibDir, "Item3InventoryService.cs");
        await File.WriteAllTextAsync(servicePath,
            """
            using System.Threading.Tasks;
            using SampleLib.Models;

            namespace SampleLib;

            public class Item3InventoryService
            {
                public Task<Item3Inventory> LoadAsync()
                {
                    return Task.FromResult(new Item3Inventory { Count = 1 });
                }

                public void Consume(Item3Inventory inventory)
                {
                    _ = inventory.Count;
                }
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var previewDto = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
                workspaceId,
                servicePath,
                typeName: "Item3InventoryService",
                interfaceName: "IItem3InventoryService",
                memberNames: null,
                replaceUsages: false,
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Apply failed: {applyResult.Error}");

            var interfaceFilePath = Path.Combine(sampleLibDir, "IItem3InventoryService.cs");
            Assert.IsTrue(File.Exists(interfaceFilePath), "Interface file must be generated.");

            var generatedText = await File.ReadAllTextAsync(interfaceFilePath);

            // Core correctness check from Item #3 — the using MUST be present.
            StringAssert.Contains(
                generatedText,
                "using SampleLib.Models;",
                "Generated interface MUST `using SampleLib.Models;` — parameter and return types reference Item3Inventory from that namespace. " +
                "Before Item #3, the text-grep FilterRelevantUsings couldn't see the full namespace string in the short-name-rendered interface text.");

            // Also verify: Task is available because System.Threading.Tasks was both declared
            // in the source AND is needed by the interface (LoadAsync returns Task<...>).
            StringAssert.Contains(generatedText, "using System.Threading.Tasks;");

            // Sanity: generated file must parse cleanly (no fabricated 'publicinterfaceI…' shapes).
            var tree = CSharpSyntaxTree.ParseText(generatedText);
            var parseDiagnostics = tree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(
                0,
                parseDiagnostics.Count,
                $"Generated interface must parse as valid C#. Diagnostics: {string.Join("; ", parseDiagnostics.Select(d => d.ToString()))}");

            // Sanity: an `interface IItem3InventoryService` declaration exists.
            var root = await tree.GetRootAsync();
            Assert.IsTrue(
                root.DescendantNodes()
                    .OfType<InterfaceDeclarationSyntax>()
                    .Any(i => string.Equals(i.Identifier.Text, "IItem3InventoryService", StringComparison.Ordinal)),
                "Generated file must declare the expected interface.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractInterface_Recurses_Into_Generic_Arguments()
    {
        // Task<Item3Inventory> → both System.Threading.Tasks AND SampleLib.Models must appear.
        // Before Item #3, walking only the outer type captured Task but missed Item3Inventory.
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Re-use the fixture setup from the other test's model class.
        var modelsDir = Path.Combine(sampleLibDir, "Models");
        Directory.CreateDirectory(modelsDir);
        await File.WriteAllTextAsync(Path.Combine(modelsDir, "Item3GenericModel.cs"),
            """
            namespace SampleLib.Models;

            public class Item3GenericModel
            {
                public int Value { get; set; }
            }
            """);

        var servicePath = Path.Combine(sampleLibDir, "Item3GenericService.cs");
        await File.WriteAllTextAsync(servicePath,
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using SampleLib.Models;

            namespace SampleLib;

            public class Item3GenericService
            {
                public Task<IReadOnlyList<Item3GenericModel>> FetchAllAsync() => throw null!;
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var previewDto = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
                workspaceId,
                servicePath,
                typeName: "Item3GenericService",
                interfaceName: "IItem3GenericService",
                memberNames: null,
                replaceUsages: false,
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Apply failed: {applyResult.Error}");

            var interfaceFilePath = Path.Combine(sampleLibDir, "IItem3GenericService.cs");
            var generatedText = await File.ReadAllTextAsync(interfaceFilePath);

            StringAssert.Contains(generatedText, "using System.Threading.Tasks;",
                "Outer generic container's namespace must be present.");
            StringAssert.Contains(generatedText, "using System.Collections.Generic;",
                "Intermediate generic's namespace must be present.");
            StringAssert.Contains(generatedText, "using SampleLib.Models;",
                "Innermost generic argument's namespace must be present — recursion through TypeArguments is the Item #3 fix.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractInterface_PreservesSpecialUsings_And_Drops_UnusedPlainUsings()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var modelsDir = Path.Combine(sampleLibDir, "Models");
        Directory.CreateDirectory(modelsDir);

        await File.WriteAllTextAsync(Path.Combine(modelsDir, "Item3SpecialUsingModel.cs"),
            """
            namespace SampleLib.Models;

            public class Item3SpecialUsingModel
            {
                public int Count { get; set; }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(modelsDir, "Item3AliasModel.cs"),
            """
            namespace SampleLib.Models;

            public class Item3AliasModel
            {
                public int Count { get; set; }
            }
            """);

        var servicePath = Path.Combine(sampleLibDir, "Item3SpecialUsingService.cs");
        await File.WriteAllTextAsync(servicePath,
            """
            using System.Text;
            using System.Threading.Tasks;
            using SampleLib.Models;
            using AliasModel = SampleLib.Models.Item3AliasModel;
            using static System.Math;
            global using System.Globalization;

            namespace SampleLib;

            public class Item3SpecialUsingService
            {
                public Task<Item3SpecialUsingModel> LoadAsync(AliasModel model)
                {
                    _ = Sqrt(model.Count);
                    return Task.FromResult(new Item3SpecialUsingModel { Count = model.Count });
                }
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var previewDto = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
                workspaceId,
                servicePath,
                typeName: "Item3SpecialUsingService",
                interfaceName: "IItem3SpecialUsingService",
                memberNames: null,
                replaceUsages: false,
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Apply failed: {applyResult.Error}");

            var interfaceFilePath = Path.Combine(sampleLibDir, "IItem3SpecialUsingService.cs");
            var generatedText = await File.ReadAllTextAsync(interfaceFilePath);

            StringAssert.Contains(generatedText, "using System.Threading.Tasks;");
            StringAssert.Contains(generatedText, "using SampleLib.Models;");
            StringAssert.Contains(generatedText, "using AliasModel = SampleLib.Models.Item3AliasModel;");
            StringAssert.Contains(generatedText, "using static System.Math;");
            StringAssert.Contains(generatedText, "global using System.Globalization;");
            Assert.IsFalse(
                generatedText.Contains("using System.Text;", StringComparison.Ordinal),
                $"Unused plain using should be dropped. Generated:\n{generatedText}");

            var threadingIndex = generatedText.IndexOf("using System.Threading.Tasks;", StringComparison.Ordinal);
            var modelsIndex = generatedText.IndexOf("using SampleLib.Models;", StringComparison.Ordinal);
            var aliasIndex = generatedText.IndexOf("using AliasModel = SampleLib.Models.Item3AliasModel;", StringComparison.Ordinal);
            var staticIndex = generatedText.IndexOf("using static System.Math;", StringComparison.Ordinal);
            var globalIndex = generatedText.IndexOf("global using System.Globalization;", StringComparison.Ordinal);

            Assert.IsTrue(
                threadingIndex >= 0
                && modelsIndex > threadingIndex
                && aliasIndex > modelsIndex
                && staticIndex > aliasIndex
                && globalIndex > staticIndex,
                $"Expected sorted plain usings followed by special usings in source order. Generated:\n{generatedText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    [TestMethod]
    public async Task ExtractInterface_Appends_To_Existing_Base_List_Inline_Without_Newline_Continuation()
    {
        // Regression for dr-9-6-emits-continuation-on-a-new-line-instead-of-inli
        // (samplesolution audit 2026-04-15 §9.6). Repro: a class with an existing base
        // (`public class Foo : BaseClass\n{`) — the newline after `BaseClass` used to
        // carry over, producing `public class Foo : BaseClass\n    , IFoo{` after
        // extract_interface_preview. Expected:
        //     public class Foo : BaseClass, IFoo
        //     {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        var basePath = Path.Combine(sampleLibDir, "Item96Base.cs");
        await File.WriteAllTextAsync(basePath,
            """
            namespace SampleLib;

            public abstract class Item96Base
            {
                public abstract int BaseValue { get; }
            }
            """);

        var servicePath = Path.Combine(sampleLibDir, "Item96Service.cs");
        await File.WriteAllTextAsync(servicePath,
            """
            namespace SampleLib;

            public class Item96Service : Item96Base
            {
                public override int BaseValue => 42;

                public int Compute() => BaseValue * 2;
            }
            """);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            var previewDto = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
                workspaceId,
                servicePath,
                typeName: "Item96Service",
                interfaceName: "IItem96Service",
                memberNames: new[] { "Compute" },
                replaceUsages: false,
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(previewDto.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applyResult.Success, $"Apply failed: {applyResult.Error}");

            var updatedSourceText = await File.ReadAllTextAsync(servicePath);

            // (1) The new interface must be added to the base list.
            StringAssert.Contains(updatedSourceText, "IItem96Service",
                $"Interface must be appended to the base list. Source:\n{updatedSourceText}");

            // (2) CRITICAL — the comma separator must be INLINE with the existing base type.
            // Before the fix: the last base type (Item96Base) carried the original trailing
            // newline, and SeparatedSyntaxList.Add inserted a zero-trivia comma AFTER that
            // newline — producing `: Item96Base\n    , IItem96Service\n{`.
            Assert.IsTrue(
                updatedSourceText.Contains("Item96Base, IItem96Service", StringComparison.Ordinal),
                $"Continuation must be inline (`Item96Base, IItem96Service`). Source:\n{updatedSourceText}");

            // (3) No stray leading-comma continuation line.
            Assert.IsFalse(
                System.Text.RegularExpressions.Regex.IsMatch(
                    updatedSourceText,
                    @"Item96Base\s*\r?\n\s*,\s*IItem96Service"),
                $"Comma must not land on a new line after Item96Base. Source:\n{updatedSourceText}");

            // (4) Sanity: the class body's `{` stays on its own line.
            Assert.IsFalse(
                updatedSourceText.Contains("IItem96Service{", StringComparison.Ordinal),
                $"Opening brace must not be glued to the interface name. Source:\n{updatedSourceText}");

            // (5) Sanity: the updated source must parse cleanly.
            var tree = CSharpSyntaxTree.ParseText(updatedSourceText);
            var parseDiagnostics = tree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            Assert.AreEqual(
                0,
                parseDiagnostics.Count,
                $"Updated source must parse as valid C#. Diagnostics: {string.Join("; ", parseDiagnostics.Select(d => d.ToString()))}\nSource:\n{updatedSourceText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            TryDeleteDirectory(solutionDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
