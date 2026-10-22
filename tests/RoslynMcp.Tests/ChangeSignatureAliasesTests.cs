using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureAliasesTests : ChangeSignatureTestBase
{

    [TestMethod]
    [DataRow("ordinary")]
    [DataRow("extension")]
    [DataRow("array")]
    [DataRow("nested")]
    [DataRow("owner")]
    [DataRow("missing-directive")]
    [DataRow("missing-directive-preamble")]
    [DataRow("missing-directive-existing")]
    [DataRow("missing-directive-shadow")]
    [DataRow("missing-directive-shadow-use")]
    [DataRow("missing-directive-no-root-scope")]
    [DataRow("missing-directive-block")]
    [DataRow("missing-directive-global")]
    [DataRow("missing-directive-global-existing")]
    [DataRow("missing-directive-namespace-existing")]
    [DataRow("missing-directive-namespace-using")]
    [DataRow("existing-directive-shadow")]
    [DataRow("existing-directive-shadow-nested")]
    [DataRow("existing-directive-shadow-trivia")]
    [DataRow("global-visible-b")]
    [DataRow("optional")]
    [DataRow("expanded-array")]
    [DataRow("expanded-collection")]
    [DataRow("pointer")]
    [DataRow("function-pointer")]
    [DataRow("unmanaged-function-pointer")]
    [DataRow("ref-function-pointer")]
    [DataRow("conditional")]
    public async Task ChangeSignaturePreview_ExternAliasSubstitution_PreservesAssembly(string shape)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "AliasSubstitution.cs");
        var dotnetRoot = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../.."));
        var targetFramework = "net" + AppContext.TargetFrameworkName!.Split("Version=v", StringSplitOptions.None)[1];
        var referenceDirectory = Directory.GetDirectories(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"))
            .OrderByDescending(p => Version.Parse(Path.GetFileName(p).Split('-')[0]))
            .Select(p => Path.Combine(p, "ref", targetFramework)).First(Directory.Exists);
        var references = Directory.GetFiles(referenceDirectory, "*.dll")
            .Select(p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p));
        foreach (var assemblyName in new[] { "AliasFirst", "AliasSecond" })
        {
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(assemblyName,
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace Shared; public struct Value { }")],
                references, new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emission = compilation.Emit(image);
            Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics));
            await File.WriteAllBytesAsync(workspace.GetPath("SampleLib", assemblyName + ".dll"), image.ToArray());
        }
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var projectXml = System.Xml.Linq.XDocument.Load(projectPath);
        projectXml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup",
            new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", "AliasFirst"),
                new System.Xml.Linq.XElement("HintPath", "AliasFirst.dll"), new System.Xml.Linq.XElement("Aliases", "first")),
            new System.Xml.Linq.XElement("Reference", new System.Xml.Linq.XAttribute("Include", "AliasSecond"),
                new System.Xml.Linq.XElement("HintPath", "AliasSecond.dll"), new System.Xml.Linq.XElement("Aliases", "global,second"))));
        projectXml.Root.Add(new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("AllowUnsafeBlocks", "true")));
        projectXml.Save(projectPath);
        var extension = shape is "extension" or "conditional";
        var declaration = shape switch
        {
            "optional" => "public static unsafe class SignatureProof { public static string Compute(int unused, first::Shared.Value retained = default) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "expanded-array" => "public static unsafe class SignatureProof { public static string Compute(int unused, params first::Shared.Value[] retained) => retained.GetType().GetElementType()!.Assembly.GetName().Name!;",
            "expanded-collection" => "public static unsafe class SignatureProof { public static string Compute(int unused, params System.Collections.Generic.List<first::Shared.Value> retained) => retained.GetType().GetGenericArguments()[0].Assembly.GetName().Name!;",
            "pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, first::Shared.Value* retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "function-pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, delegate*<first::Shared.Value, first::Shared.Value> retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "unmanaged-function-pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, delegate* unmanaged[Cdecl]<first::Shared.Value, first::Shared.Value> retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "ref-function-pointer" => "public static unsafe class SignatureProof { public static string Compute(int unused, delegate*<first::Shared.Value, ref first::Shared.Value> retained) => typeof(first::Shared.Value).Assembly.GetName().Name!;",
            "conditional" => "public static unsafe class SignatureProof { public static first::Shared.Value Compute(this AliasReceiver unused, int retained) => new first::Shared.Value();",
            _ => "public static unsafe class SignatureProof { public static string Compute<T>("
                + (extension ? "this AliasReceiver receiver, " : "") + "T unused, int retained) => "
                + "(typeof(T).IsArray ? typeof(T).GetElementType()! : typeof(T).IsGenericType ? typeof(T).GetGenericArguments()[0] : typeof(T)).Assembly.GetName().Name!;",
        };
        var call = shape switch
        {
            "optional" => "Compute(1)",
            "expanded-array" or "expanded-collection" => "Compute(1, new first::Shared.Value(), new first::Shared.Value())",
            "pointer" => "Compute(1, default(first::Shared.Value*))",
            "function-pointer" => "Compute(1, default(delegate*<first::Shared.Value, first::Shared.Value>))",
            "unmanaged-function-pointer" => "Compute(1, default(delegate* unmanaged[Cdecl]<first::Shared.Value, first::Shared.Value>))",
            "ref-function-pointer" => "Compute(1, default(delegate*<first::Shared.Value, ref first::Shared.Value>))",
            "conditional" => "((object)(new AliasReceiver()?.Compute(1))!).GetType().Assembly.GetName().Name!",
            "owner" => "Forward(new first::Shared.Value())",
            "missing-directive" or "missing-directive-preamble" or "missing-directive-existing" or "missing-directive-shadow" or "missing-directive-block" or "missing-directive-namespace-existing" or "missing-directive-namespace-using" or "existing-directive-shadow" or "existing-directive-shadow-nested" => "Compute(AliasFactory.Create(), 1) + \"|\" + Line()",
            "missing-directive-no-root-scope" or "missing-directive-shadow-use" or "existing-directive-shadow-trivia" => "Compute(AliasFactory.Create(), 1) + \"|\" + first.Empty + Line() + \"|\" + nameof(first)",
            "global-visible-b" => "Compute(new global::Shared.Value(), 1) + \"|\" + second.Empty + Line()",
            "missing-directive-global" or "missing-directive-global-existing" => "Compute(SampleLib.AliasFactory.Create(), 1) + \"|\" + Line()",
            _ => (extension ? "new AliasReceiver().Compute" : "Compute") + "(" + (shape switch
            {
                "array" => "new first::Shared.Value[0]",
                "nested" => "new System.Collections.Generic.List<first::Shared.Value>()",
                _ => "new first::Shared.Value()",
            }) + ", 1)",
        };
        var source = (shape.StartsWith("missing-directive", StringComparison.Ordinal) || shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal) || shape == "global-visible-b"
            ? shape switch
            {
                "existing-directive-shadow" => "extern alias first;\nnamespace SampleLib;\nusing first = System.String;\n",
                "existing-directive-shadow-nested" => "extern alias first;\nnamespace SampleLib { using first = System.String;\nnamespace Nested {\n",
                "existing-directive-shadow-trivia" => "extern alias first;\nnamespace SampleLib;\nusing /*alias*/ first /*name*/ = /*target*/ System.String;\n",
                "global-visible-b" => "extern alias first;\nusing second = System.String;\nnamespace SampleLib;\n",
                "missing-directive-preamble" => "#nullable enable\n/*header*/\nnamespace SampleLib;\n",
                "missing-directive-existing" => "extern alias second;\nnamespace SampleLib;\n",
                "missing-directive-shadow" => "using first = System.Object;\nnamespace SampleLib;\n",
                "missing-directive-shadow-use" or "missing-directive-no-root-scope" => "using first = System.String;\nnamespace SampleLib;\n",
                "missing-directive-block" => "namespace SampleLib {\n",
                "missing-directive-global" => "",
                "missing-directive-global-existing" => "extern alias second;\n",
                "missing-directive-namespace-existing" => "namespace SampleLib;\nextern alias second;\n",
                "missing-directive-namespace-using" => "namespace SampleLib;\nusing Collections = System.Collections.Generic;\n",
                _ => "namespace SampleLib;\n",
            }
            : "extern alias first;\nnamespace SampleLib;\n") + declaration
            + " public static string Run() => " + call + "; "
            + (shape == "owner" ? "private static string Forward<T>(T value) => Compute(value, 1); " : "")
            + "private static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; } public class AliasReceiver { }"
            + (shape == "missing-directive-block" ? " }" : shape == "existing-directive-shadow-nested" ? " } }" : "");
        if (shape.StartsWith("missing-directive", StringComparison.Ordinal) || shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal))
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "AliasFactory.cs"),
                shape == "missing-directive-no-root-scope"
                    ? "using first = System.String; namespace SampleLib { extern alias first; public static class AliasFactory { public static first::Shared.Value Create() => new first::Shared.Value(); } }"
                    : "extern alias first; namespace SampleLib; public static class AliasFactory { public static first::Shared.Value Create() => new first::Shared.Value(); }");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        if (shape == "missing-directive-no-root-scope")
        {
            var updated = original;
            foreach (var sourceDocument in original.Projects.Single(p => p.Name == "SampleLib").Documents)
            {
                var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)(await sourceDocument.GetSyntaxRootAsync())!;
                if (root.Usings.Any(u => u.Alias?.Name.Identifier.ValueText == "first")) continue;
                var alias = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseCompilationUnit("using first = System.String; ").Usings.Single();
                updated = updated.WithDocumentSyntaxRoot(sourceDocument.Id, root.WithUsings(root.Usings.Add(alias)));
            }
            Assert.IsTrue(Services.WorkspaceManager.TryApplyChanges(workspace.WorkspaceId, updated));
            original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
            foreach (var sourceDocument in original.Projects.Single(p => p.Name == "SampleLib").Documents)
                Assert.IsTrue(((Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)(await sourceDocument.GetSyntaxRootAsync())!)
                    .Usings.Any(u => u.Alias?.Name.Identifier.ValueText == "first"));
        }
        var expected = (shape == "global-visible-b" ? "AliasSecond" : "AliasFirst") + (shape.StartsWith("missing-directive", StringComparison.Ordinal) || shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal) || shape == "global-visible-b"
            ? "|" + (shape switch { "missing-directive-preamble" or "existing-directive-shadow" or "existing-directive-shadow-nested" or "existing-directive-shadow-trivia" or "global-visible-b" => "4", "missing-directive-existing" or "missing-directive-shadow" or "missing-directive-shadow-use" or "missing-directive-no-root-scope" or "missing-directive-namespace-existing" or "missing-directive-namespace-using" => "3", "missing-directive-global" => "1", _ => "2" }) : "") + (shape is "existing-directive-shadow-trivia" or "missing-directive-shadow-use" or "missing-directive-no-root-scope" ? "|first" : "");
        var proofType = shape.StartsWith("missing-directive-global", StringComparison.Ordinal) ? "SignatureProof"
            : shape == "existing-directive-shadow-nested" ? "SampleLib.Nested.SignatureProof" : "SampleLib.SignatureProof";
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(original, proofType));
        if (shape == "existing-directive-shadow")
        {
            var document = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var representable = source.Replace("extern alias first;", "extern alias first; using __signatureAssembly = first;", StringComparison.Ordinal)
                .Replace("T unused, int retained", "int retained", StringComparison.Ordinal)
                .Replace("Compute(AliasFactory.Create(), 1)", "Compute<__signatureAssembly::Shared.Value>(1)", StringComparison.Ordinal);
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(original.WithDocumentText(document.Id,
                Microsoft.CodeAnalysis.Text.SourceText.From(representable)), proofType));
        }
        if (shape is "missing-directive-shadow-use" or "missing-directive-no-root-scope")
        {
            var caller = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var factory = caller.Project.Documents.Single(d => d.Name == "AliasFactory.cs");
            var represented = original.WithDocumentText(caller.Id, Microsoft.CodeAnalysis.Text.SourceText.From(source
                .Replace("T unused, int retained", "int retained", StringComparison.Ordinal)
                .Replace("Compute(AliasFactory.Create(), 1)", "Compute<__signatureAssembly::Shared.Value>(1)", StringComparison.Ordinal)));
            represented = shape == "missing-directive-no-root-scope"
                ? represented.AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(caller.Project.Id), "AssemblyAlias.cs",
                    Microsoft.CodeAnalysis.Text.SourceText.From("extern alias first; global using __signatureAssembly = first;"))
                : represented.WithDocumentText(factory.Id, Microsoft.CodeAnalysis.Text.SourceText.From(
                    (await factory.GetTextAsync()).ToString().Replace("extern alias first;", "extern alias first; global using __signatureAssembly = first;", StringComparison.Ordinal)));
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(represented, proofType));
        }
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, source[..source.IndexOf(declaration, StringComparison.Ordinal)].Count(c => c == '\n') + 1, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var additions = stored.Value.ModifiedSolution.GetChanges(original).GetProjectChanges()
            .SelectMany(p => p.GetAddedDocuments()).Select(id => stored.Value.ModifiedSolution.GetDocument(id)!).ToArray();
        if (shape == "missing-directive-no-root-scope")
        {
            Assert.AreEqual(1, additions.Length);
            StringAssert.StartsWith(additions.Single().Name, "__SignatureAssemblyAliases_");
            Assert.AreEqual(Path.GetDirectoryName(path), Path.GetDirectoryName(additions.Single().FilePath));
            Assert.IsFalse(File.Exists(additions.Single().FilePath));
        }
        else Assert.AreEqual(0, additions.Length);
        async Task VerifyAsync(Microsoft.CodeAnalysis.Solution solution)
        {
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(solution, proofType));
            var document = solution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var root = (await document.GetSyntaxRootAsync())!;
            var model = (await document.GetSemanticModelAsync())!;
            var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => model.GetSymbolInfo(n).Symbol is Microsoft.CodeAnalysis.IMethodSymbol { Name: "Compute" });
            var target = (Microsoft.CodeAnalysis.IMethodSymbol)model.GetSymbolInfo(call).Symbol!;
            var type = shape == "conditional" ? target.ReturnType : target.IsGenericMethod ? target.TypeArguments.Single() : target.Parameters.Single().Type;
            if (type is Microsoft.CodeAnalysis.IArrayTypeSymbol array) type = array.ElementType;
            else if (type is Microsoft.CodeAnalysis.IPointerTypeSymbol pointer) type = pointer.PointedAtType;
            else if (type is Microsoft.CodeAnalysis.IFunctionPointerTypeSymbol function) type = function.Signature.ReturnType;
            else if (type is Microsoft.CodeAnalysis.INamedTypeSymbol { IsGenericType: true } named) type = named.TypeArguments.Single();
            if (shape == "owner")
            {
                Assert.AreEqual(Microsoft.CodeAnalysis.TypeKind.TypeParameter, type.TypeKind);
                Assert.AreEqual("Forward", type.ContainingSymbol.Name);
            }
            else Assert.AreEqual(shape == "global-visible-b" ? "AliasSecond" : "AliasFirst", type.ContainingAssembly.Name);
            var updatedText = (await document.GetTextAsync()).ToString();
            if (shape.StartsWith("existing-directive-shadow", StringComparison.Ordinal))
            {
                StringAssert.Contains(updatedText, "__signatureAssembly::Shared.Value");
                StringAssert.Contains(updatedText, "using __signatureAssembly = first;");
                Assert.AreEqual(1, root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                    .Count(u => u.Alias?.Name.Identifier.ValueText == "__signatureAssembly"));
            }
            else if (shape is "missing-directive-shadow" or "missing-directive-shadow-use" or "missing-directive-no-root-scope")
            {
                StringAssert.Contains(updatedText, "__signatureAssembly::Shared.Value");
                var globalAliases = new List<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
                foreach (var sourceDocument in document.Project.Documents)
                    globalAliases.AddRange((await sourceDocument.GetSyntaxRootAsync())!.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                        .Where(u => u.Alias?.Name.Identifier.ValueText == "__signatureAssembly"));
                Assert.AreEqual(1, globalAliases.Count);
                Assert.AreEqual((int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.GlobalKeyword, globalAliases.Single().GlobalKeyword.RawKind);
                Assert.AreEqual("first", globalAliases.Single().NamespaceOrType!.ToString());
            }
            else StringAssert.Contains(updatedText, shape == "global-visible-b" ? "global::Shared.Value" : "first::");
            foreach (var marker in new[] { "/*header*/", "#nullable enable", "extern alias second;", "using first = System.Object;", "using Collections = System.Collections.Generic;", "/*alias*/", "/*name*/", "/*target*/", "using second = System.String;" })
                Assert.AreEqual(source.Split(marker, StringSplitOptions.None).Length - 1, updatedText.Split(marker, StringSplitOptions.None).Length - 1, marker);
            Assert.AreEqual(shape is "missing-directive-shadow" or "missing-directive-shadow-use" or "missing-directive-no-root-scope" ? 0 : 1, root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExternAliasDirectiveSyntax>()
                .Count(a => a.Identifier.ValueText == "first"));
        }
        await VerifyAsync(stored.Value.ModifiedSolution);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        if (shape == "missing-directive-no-root-scope")
        {
            Assert.IsTrue(File.Exists(additions.Single().FilePath));
            Assert.AreEqual((await additions.Single().GetTextAsync()).ToString(), await File.ReadAllTextAsync(additions.Single().FilePath!));
            await Services.WorkspaceManager.ReloadAsync(workspace.WorkspaceId, CancellationToken.None);
            await VerifyAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        }
    }
}
