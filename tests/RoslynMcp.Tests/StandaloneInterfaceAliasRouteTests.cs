using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Services;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class StandaloneInterfaceAliasRouteTests : InterfaceRouteTestBase
{
    private const string SharedSource = """
        namespace Shared
        {
            public sealed class Shape { }
            public static class Utility { public sealed class Nested { } }
            public static class Patterns { public const string Name = "value"; }
        }
        namespace System.Diagnostics.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.ReturnValue)]
            public sealed class NotNullAttribute : System.Attribute { }
        }
        """;

    private const string InitialSource = "using Shared; namespace SampleLib; public sealed class RefService { public Shape Get() => new(); }";
    private const string Consumer = "namespace Consumer; public sealed class RefConsumer { public RefConsumer(SampleLib.RefService service) { _ = service; } }";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow("root")]
    [DataRow("file-scoped")]
    [DataRow("block")]
    [DataRow("nested-outer")]
    [DataRow("nested-inner")]
    [DataRow("named-alias")]
    [DataRow("qualified-using")]
    [DataRow("static-using")]
    [DataRow("attribute-name")]
    [DataRow("attribute-argument")]
    [DataRow("unused-qualified-using")]
    [DataRow("unused-static-using")]
    [DataRow("root-static-retention")]
    public async Task Standalone_Extraction_Preserves_Only_Needed_Visible_Aliases(string scope)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareInterfaceRouteFixtureAsync(workspace, "RefService.cs", InitialSource,
            "RefConsumer.cs", Consumer, SharedSource);
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var projectXml = XDocument.Load(projectPath);
        projectXml.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Contains("Shared.csproj", StringComparison.Ordinal) == true)
            .Add(new XElement("Aliases", "ext,unused"));
        projectXml.Save(projectPath);

        var source = scope switch
        {
            "root" => "extern alias ext; extern alias unused; namespace SampleLib; public sealed class RefService { public ext::Shared.Shape Get() => new(); }",
            "file-scoped" => "namespace SampleLib; extern alias ext; extern alias unused; public sealed class RefService { public ext::Shared.Shape Get() => new(); }",
            "block" => "namespace SampleLib { extern alias ext; extern alias unused; public sealed class RefService { public ext::Shared.Shape Get() => new(); } }",
            "nested-outer" => "namespace SampleLib { extern alias ext; extern alias unused; namespace Inner { public sealed class RefService { public ext::Shared.Shape Get() => new(); } } }",
            "nested-inner" => "namespace SampleLib { namespace Inner { extern alias ext; extern alias unused; public sealed class RefService { public ext::Shared.Shape Get() => new(); } } }",
            "named-alias" => "namespace SampleLib { extern alias ext; extern alias unused; using Payload = ext::Shared.Shape; public sealed class RefService { public Payload Get() => new(); } }",
            "qualified-using" => "namespace SampleLib { extern alias ext; extern alias unused; using ext::Shared; using System.Text; public sealed class RefService { public Shape Get() => new(); } }",
            "static-using" => "namespace SampleLib { extern alias ext; extern alias unused; using static ext::Shared.Utility; public sealed class RefService { public Nested Get() => new(); } }",
            "attribute-name" => "namespace SampleLib { extern alias ext; extern alias unused; using Contract = ext::System.Diagnostics.CodeAnalysis.NotNullAttribute; public sealed class RefService { [return: Contract] public string Get() => string.Empty; } }",
            "attribute-argument" => "namespace SampleLib { extern alias ext; extern alias unused; using Patterns = ext::Shared.Patterns; public sealed class RefService { [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(Patterns.Name)] public string? Get(string? value) => value; } }",
            "unused-qualified-using" => "namespace SampleLib { extern alias ext; extern alias unused; using ext::Shared; public sealed class RefService { public ext::Shared.Shape Get() => new(); } }",
            "unused-static-using" => "namespace SampleLib { extern alias ext; extern alias unused; using static ext::Shared.Utility; public sealed class RefService { public ext::Shared.Shape Get() => new(); } }",
            "root-static-retention" => "extern alias ext; extern alias unused; using static ext::Shared.Utility; namespace SampleLib; public sealed class RefService { public ext::Shared.Shape Get() => new(); }",
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
        await File.WriteAllTextAsync(sourcePath, source);
        if (scope.StartsWith("nested-", StringComparison.Ordinal))
            await File.WriteAllTextAsync(workspace.GetPath("Consumer", "RefConsumer.cs"),
                Consumer.Replace("SampleLib.RefService", "SampleLib.Inner.RefService", StringComparison.Ordinal));
        await workspace.ReloadAsync();
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        await AssertCompilesAsync(before);

        var preview = await InterfaceExtractionService.PreviewExtractInterfaceAsync(
            workspace.WorkspaceId, sourcePath, "RefService", "IRefService", null, false, CancellationToken.None);
        var pending = PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        var generated = (await pending.Projects.Single(project => project.Name == "SampleLib").Documents
            .Single(document => document.Name == "IRefService.cs").GetTextAsync()).ToString();
        var root = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseCompilationUnit(generated);
        Assert.AreEqual(1, root.Externs.Count, generated);
        Assert.AreEqual("ext", root.Externs.Single().Identifier.ValueText);
        Assert.IsFalse(generated.Contains("extern alias unused;", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("using System.Text;", StringComparison.Ordinal));
        if (scope.StartsWith("nested-", StringComparison.Ordinal))
            StringAssert.Contains(generated, "namespace SampleLib.Inner");
        if (scope == "named-alias") StringAssert.Contains(generated, "using Payload = ext::Shared.Shape;");
        if (scope == "qualified-using") StringAssert.Contains(generated, "using ext::Shared;");
        if (scope == "static-using" || scope == "root-static-retention") StringAssert.Contains(generated, "using static ext::Shared.Utility;");
        if (scope == "attribute-name") StringAssert.Contains(generated, "using Contract = ext::System.Diagnostics.CodeAnalysis.NotNullAttribute;");
        if (scope == "attribute-argument") StringAssert.Contains(generated, "using Patterns = ext::Shared.Patterns;");
        if (scope == "unused-qualified-using") Assert.IsFalse(generated.Contains("using ext::Shared;", StringComparison.Ordinal));
        if (scope == "unused-static-using") Assert.IsFalse(generated.Contains("using static ext::Shared.Utility;", StringComparison.Ordinal));
        AssertReferenceAliasesUnchanged(before, pending);
        await AssertCompilesAsync(pending);

        var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await workspace.ReloadAsync();
        var after = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        AssertReferenceAliasesUnchanged(before, after);
        await AssertCompilesAsync(after);
    }

    private static void AssertReferenceAliasesUnchanged(Solution before, Solution after)
    {
        foreach (var name in new[] { "SampleLib", "Contracts" })
        {
            var original = before.Projects.Single(project => project.Name == name);
            var current = after.Projects.Single(project => project.Name == name);
            var originalShared = before.Projects.Single(project => project.Name == "Shared");
            var currentShared = after.Projects.Single(project => project.Name == "Shared");
            var originalReference = original.ProjectReferences.Single(reference => reference.ProjectId == originalShared.Id);
            var currentReference = current.ProjectReferences.Single(reference => reference.ProjectId == currentShared.Id);
            CollectionAssert.AreEqual(originalReference.Aliases.ToArray(), currentReference.Aliases.ToArray(), name);
            Assert.AreEqual(originalReference.EmbedInteropTypes, currentReference.EmbedInteropTypes, name);
        }
    }
}
