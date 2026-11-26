using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureBindingsTests : ChangeSignatureTestBase
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ChangeSignaturePreview_DenseAnnotations_MatchOriginalReplacement(bool script, bool nestedRoot)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose,
            script ? SourceCodeKind.Script : SourceCodeKind.Regular, ["SIGNATURE_CONTROL"]);
        const string source = "class C { /// <summary><see cref=\"Missing\"/></summary>\nint M(int x) { return F(,); } }";
        var root = CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(source,
            System.Text.Encoding.UTF8, Microsoft.CodeAnalysis.Text.SourceHashAlgorithm.Sha256), options, "control.csx").GetRoot();
        if (nestedRoot)
            root = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single();
        root = root.ReplaceSyntax(root.DescendantNodesAndSelf(descendIntoTrivia: true),
            (_, rewritten) => rewritten.WithAdditionalAnnotations(new SyntaxAnnotation("unrelated", "node")),
            root.DescendantTokens(descendIntoTrivia: true), (_, rewritten) => rewritten.WithAdditionalAnnotations(new SyntaxAnnotation("unrelated", "token")),
            [], (_, rewritten) => rewritten);
        var kinds = new[] { "signature-declaration", "signature-binding", "signature-argument",
            "signature-conditional", "signature-alias-scope", ChangeSignatureStructConstruction.OwnerTag,
            ChangeSignatureStructConstruction.OriginalOnlyAttributeTag };
        var nodes = root.DescendantNodesAndSelf(descendIntoTrivia: true).ToDictionary(node => node,
            node => kinds.Select(kind => new SyntaxAnnotation(kind, node.RawKind.ToString()))
                .Append(new SyntaxAnnotation("signature-binding", "second")).ToList());
        var tokens = root.DescendantTokens(descendIntoTrivia: true).ToDictionary(token => token,
            token => new List<SyntaxAnnotation> { new("signature-diagnostic", "first"), new("signature-diagnostic", "second") });
        Assert.IsTrue(tokens.Keys.Any(token => token.IsMissing));
        Assert.IsTrue(nodes.Keys.Any(node => node.IsStructuredTrivia));
        var expected = root.ReplaceSyntax(nodes.Keys,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(nodes[original].ToArray()),
            tokens.Keys, (original, rewritten) => rewritten.WithAdditionalAnnotations(tokens[original].ToArray()),
            [], (_, rewritten) => rewritten);
        var method = typeof(ChangeSignatureAddRemovePreviewBuilder).GetMethod("AttachAnnotations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);
        var actual = (SyntaxNode)method.Invoke(null, [root, nodes, tokens])!;
        AssertSignatureSyntaxEqual(expected, actual, kinds.Append("signature-diagnostic").Append("unrelated").ToArray());
        Assert.IsTrue(source.Contains(root.ToFullString(), StringComparison.Ordinal));
        var unchanged = (SyntaxNode)method.Invoke(null, [root,
            new Dictionary<SyntaxNode, List<SyntaxAnnotation>>(), new Dictionary<SyntaxToken, List<SyntaxAnnotation>>()])!;
        Assert.AreSame(root, unchanged);
    }

    [TestMethod]
    public void ChangeSignaturePreview_RoundTripAnnotations_PreserveNestedAndDiagnosticIdentities()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose,
            SourceCodeKind.Script, ["SIGNATURE_CONTROL"]);
        const string source = "/// <summary><see cref=\"Missing\"/></summary>\nclass C { int M(int x) { return F(,); } }";
        var root = CSharpSyntaxTree.ParseText(source, options, "control.csx").GetRoot();
        var selected = root.DescendantNodesAndSelf().Where(node => node == root
            || node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax
            || node is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax).ToArray();
        var nodeMap = selected.ToDictionary(node => node, node => new[]
        {
            new SyntaxAnnotation("signature-declaration", node.RawKind.ToString()),
            new SyntaxAnnotation("signature-binding", "first"),
            new SyntaxAnnotation("signature-binding", "second"),
            new SyntaxAnnotation("signature-argument", "argument"),
        });
        var tokenMap = root.DescendantTokens(descendIntoTrivia: true).ToDictionary(token => token,
            token => new[] { new SyntaxAnnotation("signature-diagnostic", token.RawKind.ToString()),
                new SyntaxAnnotation("signature-diagnostic", "second") });
        Assert.IsTrue(tokenMap.Keys.Any(token => token.IsMissing));
        Assert.IsTrue(tokenMap.Keys.Any(token => token.Parent!.AncestorsAndSelf().Any(node => node.IsStructuredTrivia)));
        root = root.ReplaceSyntax(nodeMap.Keys,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(nodeMap[original]),
            tokenMap.Keys, (original, rewritten) => rewritten.WithAdditionalAnnotations(tokenMap[original]),
            [], (_, rewritten) => rewritten);
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("AnnotationControl", LanguageNames.CSharp).WithParseOptions(options);
        var document = project.AddDocument("control.csx", source, filePath: "control.csx");
        var method = typeof(ChangeSignatureAddRemovePreviewBuilder).GetMethod("RoundTripRoot",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(method);
        var result = (SyntaxNode)method.Invoke(null, [root, document, CancellationToken.None])!;
        Assert.AreEqual(source, result.ToFullString());
        Assert.AreEqual(options, result.SyntaxTree.Options);
        Assert.AreEqual("control.csx", result.SyntaxTree.FilePath);
        Assert.AreEqual(SourceCodeKind.Script, result.SyntaxTree.Options.Kind);
        foreach (var kind in new[] { "signature-declaration", "signature-binding", "signature-argument" })
            CollectionAssert.AreEqual(root.DescendantNodesAndSelf().SelectMany(node => node.GetAnnotations(kind)).ToArray(),
                result.DescendantNodesAndSelf().SelectMany(node => node.GetAnnotations(kind)).ToArray());
        CollectionAssert.AreEqual(root.DescendantTokens(descendIntoTrivia: true)
            .SelectMany(token => token.GetAnnotations("signature-diagnostic")).ToArray(),
            result.DescendantTokens(descendIntoTrivia: true)
                .SelectMany(token => token.GetAnnotations("signature-diagnostic")).ToArray());
    }

    [TestMethod]
    public void ChangeSignaturePreview_GeneratedConstructorCleanup_MatchesOriginalReplacement()
    {
        var constructor = (Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax)
            SyntaxFactory.ParseMemberDeclaration("/// <summary>C</summary>\nC(int x) { /* retain shape */ F(x); }")!;
        var kinds = new[] { "signature-declaration", "signature-binding", "signature-argument",
            "signature-conditional", "signature-alias-scope", ChangeSignatureStructConstruction.OwnerTag,
            ChangeSignatureStructConstruction.OriginalOnlyAttributeTag };
        constructor = constructor.ReplaceSyntax(constructor.DescendantNodesAndSelf(),
            (_, rewritten) => rewritten.WithAdditionalAnnotations(kinds.Select(kind => new SyntaxAnnotation(kind))
                .Append(new SyntaxAnnotation("unrelated", "node")).ToArray()),
            constructor.DescendantTokens(), (_, rewritten) => rewritten.WithAdditionalAnnotations(
                new SyntaxAnnotation("signature-diagnostic"), new SyntaxAnnotation("unrelated", "token")),
            [], (_, rewritten) => rewritten);
        var identity = constructor.GetAnnotations("signature-declaration").ToArray();
        var expected = constructor.ReplaceSyntax(constructor.DescendantNodesAndSelf(),
            (_, rewritten) => rewritten.WithoutAnnotations(kinds.SelectMany(rewritten.GetAnnotations).ToArray()),
            constructor.DescendantTokens(), (_, rewritten) => rewritten.WithoutAnnotations("signature-diagnostic").WithoutTrivia(),
            [], (_, rewritten) => rewritten)
            .WithAdditionalAnnotations(identity).NormalizeWhitespace(eol: " ")
            .WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space);
        var actual = ChangeSignatureAddRemovePreviewBuilder.PrepareGeneratedConstructor(constructor);
        AssertSignatureSyntaxEqual(expected, actual, kinds.Append("signature-diagnostic").Append("unrelated").ToArray());
        CollectionAssert.AreEqual(identity, actual.GetAnnotations("signature-declaration").ToArray());
        Assert.AreEqual(" C(int x) {     F(x); } ", actual.ToFullString());
    }

    private static void AssertSignatureSyntaxEqual(SyntaxNode expected, SyntaxNode actual, string[] kinds)
    {
        Assert.AreEqual(expected.ToFullString(), actual.ToFullString());
        Assert.AreEqual(expected.RawKind, actual.RawKind);
        Assert.AreEqual(expected.SyntaxTree.Options, actual.SyntaxTree.Options);
        Assert.AreEqual(expected.SyntaxTree.FilePath, actual.SyntaxTree.FilePath);
        Assert.AreEqual(expected.SyntaxTree.Encoding, actual.SyntaxTree.Encoding);
        Assert.AreEqual(expected.SyntaxTree.GetText().ChecksumAlgorithm, actual.SyntaxTree.GetText().ChecksumAlgorithm);
        Assert.AreEqual(expected.SyntaxTree.HasCompilationUnitRoot, actual.SyntaxTree.HasCompilationUnitRoot);
        var expectedNodes = expected.DescendantNodesAndSelf(descendIntoTrivia: true).ToArray();
        var actualNodes = actual.DescendantNodesAndSelf(descendIntoTrivia: true).ToArray();
        Assert.AreEqual(expectedNodes.Length, actualNodes.Length);
        for (var index = 0; index < expectedNodes.Length; index++)
        {
            Assert.AreEqual(expectedNodes[index].RawKind, actualNodes[index].RawKind);
            Assert.AreEqual(expectedNodes[index].Span, actualNodes[index].Span);
            foreach (var kind in kinds)
                CollectionAssert.AreEqual(expectedNodes[index].GetAnnotations(kind).ToArray(), actualNodes[index].GetAnnotations(kind).ToArray());
        }
        var expectedTokens = expected.DescendantTokens(descendIntoTrivia: true).ToArray();
        var actualTokens = actual.DescendantTokens(descendIntoTrivia: true).ToArray();
        Assert.AreEqual(expectedTokens.Length, actualTokens.Length);
        for (var index = 0; index < expectedTokens.Length; index++)
        {
            Assert.AreEqual(expectedTokens[index].RawKind, actualTokens[index].RawKind);
            Assert.AreEqual(expectedTokens[index].Span, actualTokens[index].Span);
            Assert.AreEqual(expectedTokens[index].IsMissing, actualTokens[index].IsMissing);
            foreach (var kind in kinds)
                CollectionAssert.AreEqual(expectedTokens[index].GetAnnotations(kind).ToArray(), actualTokens[index].GetAnnotations(kind).ToArray());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_CompetingAliasRequests_ReuseInputOrderedPlan(bool diagnostic)
    {
        var fixture = await CreateLateAliasFixtureAsync(diagnostic, exchange: false);
        await using var workspace = fixture.Workspace;
        AddSignatureAliasReference(workspace, "AliasSecond", "second");
        var secondFactoryPath = workspace.GetPath("SampleLib", "SecondAliasFactory.cs");
        const string secondFactorySource = "extern alias second; namespace SampleLib; public static class SecondAliasFactory { public static second::Shared.Value Create() => new second::Shared.Value(); }";
        await File.WriteAllTextAsync(secondFactoryPath, secondFactorySource);
        var secondPath = workspace.GetPath("SampleLib", "ZAliasRequest.cs");
        const string secondSource = "using second = System.String; namespace SampleLib; public static class SecondAliasRequest { public static int Run() => SignatureProof.Compute(SecondAliasFactory.Create(), 2); }";
        await File.WriteAllTextAsync(secondPath, secondSource);
        await workspace.ReloadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        Assert.IsTrue(project.Documents.ToList().FindIndex(d => d.FilePath == fixture.TargetPath)
            < project.Documents.ToList().FindIndex(d => d.FilePath == secondPath));
        var before = (await project.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(diagnostic ? 1 : 0, before.Length);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(fixture.TargetPath, 3, fixture.Source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "missing"), CancellationToken.None);
        Assert.AreEqual(2, preview.CallsiteUpdates!.Sum(update => update.CallsiteCount));
        var candidate = Services.PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        Assert.AreEqual(original.Projects.Sum(p => p.DocumentIds.Count), candidate.Projects.Sum(p => p.DocumentIds.Count));
        var aliasDirectives = new List<string>();
        foreach (var document in candidate.GetProject(project.Id)!.Documents)
        {
            var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)(await document.GetSyntaxRootAsync())!;
            aliasDirectives.AddRange(root.Usings.Where(use => use.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)
                && use.Alias?.Name.Identifier.ValueText.StartsWith("__signatureAssembly", StringComparison.Ordinal) == true)
                .Select(use => use.ToString()));
        }
        CollectionAssert.AreEquivalent(new[] { "global using __signatureAssembly = first;", "global using __signatureAssembly0 = second;" }, aliasDirectives);
        var firstCaller = candidate.GetDocument(candidate.GetDocumentIdsWithFilePath(fixture.TargetPath).Single())!;
        StringAssert.Contains((await firstCaller.GetTextAsync()).ToString(), "__signatureAssembly::Shared.Value");
        var secondCaller = candidate.GetDocument(candidate.GetDocumentIdsWithFilePath(secondPath).Single())!;
        StringAssert.Contains((await secondCaller.GetTextAsync()).ToString(), "__signatureAssembly0::Shared.Value");
        await AssertLateAliasFactoryBindingsAsync(original, candidate, fixture.FactoryPath);
        var after = (await candidate.GetProject(project.Id)!.GetCompilationAsync())!.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(before.Length, after.Length);
        if (diagnostic)
        {
            Assert.AreEqual(before[0].Id, after[0].Id);
            Assert.AreEqual(before[0].GetMessage(), after[0].GetMessage());
            Assert.AreEqual(original.GetDocument(before[0].Location.SourceTree!)!.Id, candidate.GetDocument(after[0].Location.SourceTree!)!.Id);
        }
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(fixture.Source, await File.ReadAllTextAsync(fixture.TargetPath));
        Assert.AreEqual(fixture.FactorySource, await File.ReadAllTextAsync(fixture.FactoryPath));
        Assert.AreEqual(secondSource, await File.ReadAllTextAsync(secondPath));
        Assert.AreEqual(secondFactorySource, await File.ReadAllTextAsync(secondFactoryPath));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LaterBatchAndDependentProject_RetainCompleteBindings()
    {
        await using var workspace = CreateLibraryTestsWorkspace();
        var targetPath = workspace.GetPath("SampleLib", "BatchTarget.cs");
        const string targetSource = "namespace SampleLib; public static class BatchTarget { public static int Compute(int unused, int retained) => retained; }";
        await File.WriteAllTextAsync(targetPath, targetSource);
        var bound = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
        for (var index = 0; index <= bound; index++)
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "ABatch" + index + ".cs"),
                "namespace SampleLib; public static class BatchPadding" + index + " { public static object Make() => new object(); }");
        var evidencePath = workspace.GetPath("SampleLib", "BatchEvidence.cs");
        const string evidenceSource = "namespace SampleLib; public struct BatchValue { public static implicit operator int(BatchValue value) => 7; public static BatchValue operator +(BatchValue a, BatchValue b) => new BatchValue(); } public static class BatchEvidence { public static int Unrelated(int x) => x; public static T Identity<T>(T x) => x; public static System.Func<int, int> Reference => Unrelated; public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; public static int Run() => Reference((int)(new BatchValue() + new BatchValue())) + Line() + Identity(1) + Unrelated(new BatchValue()); }";
        await File.WriteAllTextAsync(evidencePath, evidenceSource);
        var callerPath = workspace.GetPath("SampleLib", "ZBatchCaller.cs");
        const string callerSource = "namespace SampleLib; public static class BatchCaller { public static int Run() => BatchTarget.Compute(1, 2) + BatchEvidence.Run(); }";
        await File.WriteAllTextAsync(callerPath, callerSource);
        var dependentPath = workspace.GetPath("SampleLib.Tests", "BatchDependent.cs");
        const string dependentSource = "namespace SampleLib.Tests; public static class BatchDependent { public static int Run() => SampleLib.BatchTarget.Compute(3, 4); }";
        await File.WriteAllTextAsync(dependentPath, dependentSource);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        Assert.IsTrue(project.Documents.ToList().FindIndex(d => d.FilePath == callerPath) >= bound,
            "The related call must occur after the first finite batch.");
        Assert.IsTrue(original.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(project.Id)
            .Any(id => original.GetProject(id)!.Name == "SampleLib.Tests"));
        var evidenceDocument = original.GetDocument(original.GetDocumentIdsWithFilePath(evidencePath).Single())!;
        var evidenceModel = (await evidenceDocument.GetSemanticModelAsync())!;
        var evidenceRoot = (await evidenceDocument.GetSyntaxRootAsync())!;
        var unrelatedInvocation = evidenceRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(node => node.Expression.ToString() == "Unrelated");
        var unrelatedMethod = (IMethodSymbol)evidenceModel.GetSymbolInfo(unrelatedInvocation).Symbol!;
        Assert.AreSame(unrelatedMethod, (unrelatedMethod.ReducedFrom ?? unrelatedMethod).OriginalDefinition);
        var identityInvocation = evidenceRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(node => node.Expression.ToString() == "Identity");
        var identityMethod = (IMethodSymbol)evidenceModel.GetSymbolInfo(identityInvocation).Symbol!;
        Assert.AreNotSame(identityMethod, (identityMethod.ReducedFrom ?? identityMethod).OriginalDefinition);
        Assert.AreEqual(SpecialType.System_Int32, identityMethod.TypeArguments.Single().SpecialType);
        var identityTypes = evidenceModel.GetTypeInfo(identityInvocation);
        Assert.AreEqual(SpecialType.System_Int32, identityTypes.Type!.SpecialType);
        Assert.AreSame(identityTypes.Type, identityTypes.ConvertedType);
        var convertedConstruction = unrelatedInvocation.ArgumentList.Arguments.Single().Expression;
        Assert.IsInstanceOfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>(convertedConstruction);
        Assert.AreEqual("new BatchValue()", convertedConstruction.ToString());
        var convertedTypes = evidenceModel.GetTypeInfo(convertedConstruction);
        Assert.AreEqual("SampleLib.BatchValue", convertedTypes.Type!.ToDisplayString());
        Assert.AreEqual(SpecialType.System_Int32, convertedTypes.ConvertedType!.SpecialType);
        Assert.AreNotSame(convertedTypes.Type, convertedTypes.ConvertedType);
        Assert.IsTrue(evidenceModel.GetConversion(convertedConstruction).IsUserDefined);
        async Task<string[]> EvidenceAsync(Solution solution)
        {
            var document = solution.GetDocument(solution.GetDocumentIdsWithFilePath(evidencePath).Single())!;
            var model = (await document.GetSemanticModelAsync())!;
            var root = (await document.GetSyntaxRootAsync())!;
            return root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax>()
                .Where(node => model.GetSymbolInfo(node).Symbol is IMethodSymbol)
                .Select(node => model.GetSymbolInfo(node).Symbol!.ToDisplayString() + "|" + model.GetTypeInfo(node).Type?.ToDisplayString()
                    + "|" + model.GetTypeInfo(node).ConvertedType?.ToDisplayString() + "|" + model.GetConversion(node)).ToArray();
        }
        var before = await EvidenceAsync(original);
        Assert.IsTrue(before.Length >= 8, "Retain constructions, invocation, method reference, operator and conversion bindings.");
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(targetPath, 1, targetSource.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        Assert.AreEqual(2, preview.CallsiteUpdates!.Sum(update => update.CallsiteCount));
        CollectionAssert.AreEquivalent(new[] { targetPath, callerPath, dependentPath }, preview.Changes.Select(change => change.FilePath).ToArray());
        var candidate = Services.PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        CollectionAssert.AreEqual(before, await EvidenceAsync(candidate));
        Assert.AreEqual(await RunSignatureFixtureAsync(original, "SampleLib.BatchCaller"), await RunSignatureFixtureAsync(candidate, "SampleLib.BatchCaller"));
        foreach (var (path, source) in new[] { (targetPath, targetSource), (callerPath, callerSource), (dependentPath, dependentSource), (evidencePath, evidenceSource) })
            Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ConcurrentWorkspaces_IsolateAliasesDiagnosticsAndTokens()
    {
        var left = await CreateLateAliasFixtureAsync(diagnostic: true, exchange: false);
        await using var leftWorkspace = left.Workspace;
        var right = await CreateLateAliasFixtureAsync(diagnostic: true, exchange: false);
        await using var rightWorkspace = right.Workspace;
        async Task PrepareAsync(string workspaceId, string targetPath, string factoryPath, string name)
        {
            var solution = Services.WorkspaceManager.GetCurrentSolution(workspaceId);
            foreach (var path in new[] { targetPath, factoryPath })
            {
                var document = solution.GetDocument(solution.GetDocumentIdsWithFilePath(path).Single())!;
                var text = (await document.GetTextAsync()).ToString().Replace("SignatureProof", name + "Proof", StringComparison.Ordinal)
                    .Replace("MissingAliasHostType", "Missing" + name + "Type", StringComparison.Ordinal);
                solution = solution.WithDocumentText(document.Id, Microsoft.CodeAnalysis.Text.SourceText.From(text));
            }
            Assert.IsTrue(Services.WorkspaceManager.TryApplyChanges(workspaceId, solution));
        }
        await PrepareAsync(leftWorkspace.WorkspaceId, left.TargetPath, left.FactoryPath, "Left");
        await PrepareAsync(rightWorkspace.WorkspaceId, right.TargetPath, right.FactoryPath, "Right");
        var originals = new[] { Services.WorkspaceManager.GetCurrentSolution(leftWorkspace.WorkspaceId), Services.WorkspaceManager.GetCurrentSolution(rightWorkspace.WorkspaceId) };
        var fixtures = new[] { left, right };
        var disk = await Task.WhenAll(fixtures.SelectMany(f => new[] { f.TargetPath, f.FactoryPath }).Select(path => File.ReadAllTextAsync(path)));
        async Task<RefactoringPreviewDto> PreviewAsync(int index)
        {
            var fixture = fixtures[index];
            var source = (await originals[index].GetDocument(originals[index].GetDocumentIdsWithFilePath(fixture.TargetPath).Single())!.GetTextAsync()).ToString();
            return await ChangeSignature.PreviewChangeSignatureAsync(fixture.Workspace.WorkspaceId,
                SymbolLocator.BySource(fixture.TargetPath, 3, source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Name: "missing"), CancellationToken.None);
        }
        var previews = await Task.WhenAll(PreviewAsync(0), PreviewAsync(1));
        Assert.AreNotEqual(previews[0].PreviewToken, previews[1].PreviewToken);
        for (var index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            var other = fixtures[1 - index];
            var candidate = Services.PreviewStore.Retrieve(previews[index].PreviewToken)!.Value.ModifiedSolution;
            Assert.AreEqual(1, previews[index].CallsiteUpdates!.Sum(update => update.CallsiteCount));
            CollectionAssert.AreEquivalent(new[] { fixture.TargetPath, fixture.FactoryPath }, previews[index].Changes.Select(change => change.FilePath).ToArray());
            Assert.IsFalse(candidate.GetDocumentIdsWithFilePath(other.TargetPath).Any());
            var factory = candidate.GetDocument(candidate.GetDocumentIdsWithFilePath(fixture.FactoryPath).Single())!;
            StringAssert.Contains((await factory.GetTextAsync()).ToString(), "global using __signatureAssembly = first;");
            await AssertLateAliasFactoryBindingsAsync(originals[index], candidate, fixture.FactoryPath);
            var diagnostic = (await factory.Project.GetCompilationAsync())!.GetDiagnostics().Single(d => d.Severity == DiagnosticSeverity.Error);
            Assert.AreEqual("CS0246", diagnostic.Id);
            StringAssert.Contains(diagnostic.GetMessage(), index == 0 ? "MissingLeftType" : "MissingRightType");
            Assert.AreEqual(factory.Id, candidate.GetDocument(diagnostic.Location.SourceTree!)!.Id);
            Assert.AreSame(originals[index], Services.WorkspaceManager.GetCurrentSolution(fixture.Workspace.WorkspaceId));
        }
        CollectionAssert.AreEqual(disk, await Task.WhenAll(fixtures.SelectMany(f => new[] { f.TargetPath, f.FactoryPath }).Select(path => File.ReadAllTextAsync(path))));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ReadBatch_FinitePassBoundAndRefill()
    {
        var bound = AssertSignatureReaderWidth();
        var count = bound + 3;
        var started = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var active = 0;
        var peak = 0;
        var sync = new object();
        var pending = ReadSignatureBatchAsync(Enumerable.Range(0, count).ToArray(), async index =>
        {
            lock (sync) peak = Math.Max(peak, ++active);
            started[index].SetResult();
            try { await release[index].Task; return index; }
            finally { lock (sync) active--; }
        });
        try
        {
            await Task.WhenAll(started.Take(bound).Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(bound, peak, "The finite pass must own at most B active readers.");
            Assert.IsFalse(started[bound].Task.IsCompleted, "An input beyond B cannot start while B readers are held.");
            var firstReleased = bound == 1 ? 0 : 1;
            release[firstReleased].SetResult();
            await started[bound].Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsFalse(pending.IsCompleted, "Refill must not publish before all owned readers join.");
            if (bound > 1) Assert.IsFalse(release[0].Task.IsCompleted, "Refill must proceed while the earliest reader is held.");
            if (bound == 1)
            {
                for (var index = 1; index < count; index++)
                {
                    await started[index].Task.WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.IsFalse(pending.IsCompleted, "The last held sole reader must still join.");
                    Assert.AreEqual(1, active);
                    release[index].SetResult();
                }
            }
            foreach (var signal in release) signal.TrySetResult();
            var outcomes = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            CollectionAssert.AreEqual(Enumerable.Range(0, count).ToArray(), outcomes.Select(result => result()).ToArray());
            Assert.AreEqual(bound, peak);
            Assert.AreEqual(0, active);
        }
        finally
        {
            foreach (var signal in release) signal.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ReadBatch_JoinsReverseCompletionAndRetainsInputOrder()
    {
        var bound = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
        var started = Enumerable.Range(0, bound).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = Enumerable.Range(0, bound).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var finished = Enumerable.Range(0, bound).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var calls = new int[bound];
        var active = 0;
        var peak = 0;
        var order = new List<int>();
        var sync = new object();
        var pending = ReadSignatureBatchAsync(Enumerable.Range(0, bound).ToArray(), async index =>
        {
            Interlocked.Increment(ref calls[index]);
            lock (sync) peak = Math.Max(peak, ++active);
            started[index].SetResult();
            try
            {
                await release[index].Task;
                lock (sync) order.Add(index);
                return index;
            }
            finally
            {
                lock (sync) active--;
                finished[index].SetResult();
            }
        });
        try
        {
            await Task.WhenAll(started.Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(bound, active);
            Assert.AreEqual(bound, peak);
            if (Environment.ProcessorCount > 1) Assert.IsTrue(peak > 1);
            for (var index = bound - 1; index >= 0; index--)
            {
                release[index].SetResult();
                await finished[index].Task.WaitAsync(TimeSpan.FromSeconds(30));
                if (index > 0) Assert.IsFalse(pending.IsCompleted, "The first held reader must be joined.");
            }
            var outcomes = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            CollectionAssert.AreEqual(Enumerable.Range(0, bound).ToArray(), outcomes.Select(result => result()).ToArray());
            CollectionAssert.AreEqual(Enumerable.Range(0, bound).Reverse().ToArray(), order);
            CollectionAssert.AreEqual(Enumerable.Repeat(1, bound).ToArray(), calls);
            Assert.AreEqual(0, active);
        }
        finally
        {
            foreach (var signal in release) signal.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [TestMethod]
    [DataRow("left", false)]
    [DataRow("left", true)]
    [DataRow("right", true)]
    public async Task ChangeSignaturePreview_AliasDirectory_PreservesPerDocumentFallback(string targetDirectory, bool nullProjectPath)
    {
        var fixture = await CreateLateAliasFixtureAsync(diagnostic: false, exchange: false);
        await using var workspace = fixture.Workspace;
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var target = project.Documents.Single(d => d.FilePath == fixture.TargetPath);
        var targetPath = Path.Combine(Path.GetDirectoryName(fixture.TargetPath)!, targetDirectory, target.Name);
        var updated = original.WithDocumentFilePath(target.Id, targetPath);
        if (nullProjectPath) updated = updated.WithProjectFilePath(project.Id, null);
        foreach (var document in project.Documents)
        {
            var root = (Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax)(await document.GetSyntaxRootAsync())!;
            // Every original source shadows the root alias. The only legal plan is a new
            // directive-only document, whose directory comes from this caller's descriptor.
            if (root.Externs.Count > 0)
            {
                var scope = root.Members.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax>().Single();
                root = root.ReplaceNode(scope, scope.WithExterns(root.Externs)).WithExterns(default);
            }
            if (!root.Usings.Any(use => use.Alias?.Name.Identifier.ValueText == "first"))
                root = root.WithUsings(root.Usings.Add(SyntaxFactory.ParseCompilationUnit("using first = System.String; ").Usings.Single()));
            updated = updated.WithDocumentSyntaxRoot(document.Id, root);
        }
        var fixtureSolution = updated;
        var targetDocument = fixtureSolution.GetDocument(target.Id)!;
        var targetRoot = (await targetDocument.GetSyntaxRootAsync())!;
        var targetModel = (await targetDocument.GetSemanticModelAsync())!;
        var declaration = targetRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Compute");
        var method = (IMethodSymbol)targetModel.GetDeclaredSymbol(declaration)!;
        var preview = await ChangeSignatureAddRemovePreviewBuilder.BuildAsync(fixtureSolution, method,
            list => list.WithParameters(list.Parameters.RemoveAt(0)),
            (context, arguments) => context.Remove(arguments, 0), CancellationToken.None);
        var added = preview.Accumulator.GetChanges(fixtureSolution).GetProjectChanges()
            .SelectMany(change => change.GetAddedDocuments()).Select(id => preview.Accumulator.GetDocument(id)!).Single();
        Assert.AreEqual(nullProjectPath ? Path.GetDirectoryName(targetPath) : Path.GetDirectoryName(project.FilePath),
            Path.GetDirectoryName(added.FilePath));
        StringAssert.Contains((await added.GetTextAsync()).ToString(), "global using __signatureAssembly = first;");
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.IsFalse(File.Exists(added.FilePath));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_Restoration_RetainsActualCandidateTreeAndChangedAnnotations()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Retention", LanguageNames.CSharp)
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var retained = project.AddDocument("Retained.cs", "class Retained { object M() => new object(); }");
        var changed = retained.Project.AddDocument("Changed.cs", "class Changed { int M(int x) => x; }");
        var original = changed.Project.Solution;
        var retainedRoot = (await original.GetDocument(retained.Id)!.GetSyntaxRootAsync())!;
        var retainedTree = (await original.GetDocument(retained.Id)!.GetSyntaxTreeAsync())!;
        var changedRoot = (await original.GetDocument(changed.Id)!.GetSyntaxRootAsync())!;
        var annotation = new SyntaxAnnotation("signature-binding");
        var declarationAnnotation = new SyntaxAnnotation("signature-declaration");
        var argumentAnnotation = new SyntaxAnnotation("signature-argument");
        var diagnosticAnnotation = new SyntaxAnnotation("signature-diagnostic");
        var changedAnnotated = changedRoot.ReplaceToken(changedRoot.GetFirstToken(),
            changedRoot.GetFirstToken().WithAdditionalAnnotations(diagnosticAnnotation))
            .WithAdditionalAnnotations(annotation, declarationAnnotation, argumentAnnotation);
        var candidate = original.WithDocumentSyntaxRoot(retained.Id, retainedRoot.WithAdditionalAnnotations(annotation))
            .WithDocumentSyntaxRoot(changed.Id, changedAnnotated);
        var addedIds = new[] { DocumentId.CreateNewId(project.Id), DocumentId.CreateNewId(project.Id) };
        for (var index = 0; index < addedIds.Length; index++)
        {
            var text = Microsoft.CodeAnalysis.Text.SourceText.From("global using Alias" + index + " = System.String;",
                System.Text.Encoding.UTF8, Microsoft.CodeAnalysis.Text.SourceHashAlgorithm.Sha256);
            candidate = candidate.AddDocument(addedIds[index], "Alias" + index + ".cs", text,
                folders: ["Aliases", index.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                filePath: Path.Combine(TestTempRoot.Current, "Aliases", "Alias" + index + ".cs"));
        }
        var changedDocuments = new HashSet<DocumentId>(addedIds) { changed.Id };
        Assert.AreNotSame(retainedTree, await candidate.GetDocument(retained.Id)!.GetSyntaxTreeAsync());
        var restore = typeof(ChangeSignatureService).Assembly
            .GetType("RoslynMcp.Roslyn.Services.ChangeSignatureAddRemovePreviewBuilder")!
            .GetMethod("RestoreUnchangedDocumentTreesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(restore);
        var restored = await (Task<Solution>)restore.Invoke(null,
            [original, candidate, new HashSet<ProjectId> { project.Id }, changedDocuments, CancellationToken.None])!;
        var actual = restored.GetDocument(retained.Id)!;
        var actualRoot = (await actual.GetSyntaxRootAsync())!;
        var actualModel = (await actual.GetSemanticModelAsync())!;
        Assert.AreSame(retainedRoot, actualRoot);
        Assert.AreSame(retainedTree, actualRoot.SyntaxTree);
        Assert.AreSame(retainedTree, actualModel.SyntaxTree);
        var originalConstruction = retainedRoot.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax>().Single();
        Assert.AreSame(originalConstruction.SyntaxTree, actualModel.SyntaxTree);
        Assert.IsNotNull(actualModel.GetSymbolInfo(originalConstruction).Symbol);
        var actualChangedRoot = (await restored.GetDocument(changed.Id)!.GetSyntaxRootAsync())!;
        Assert.IsTrue(actualChangedRoot.HasAnnotation(annotation),
            "Changed membership must retain annotations even when its text equals the original.");
        Assert.IsTrue(actualChangedRoot.HasAnnotation(declarationAnnotation));
        Assert.IsTrue(actualChangedRoot.HasAnnotation(argumentAnnotation));
        Assert.IsTrue(actualChangedRoot.GetFirstToken().HasAnnotation(diagnosticAnnotation));
        Assert.AreNotSame(changedRoot.SyntaxTree, await restored.GetDocument(changed.Id)!.GetSyntaxTreeAsync());
        CollectionAssert.AreEqual(candidate.GetProject(project.Id)!.DocumentIds.ToArray(), restored.GetProject(project.Id)!.DocumentIds.ToArray());
        Assert.AreSame(candidate.GetProject(project.Id)!.ParseOptions, restored.GetProject(project.Id)!.ParseOptions);
        foreach (var id in addedIds)
        {
            var expectedDocument = candidate.GetDocument(id)!;
            var actualDocument = restored.GetDocument(id);
            Assert.IsNotNull(actualDocument);
            Assert.AreEqual(expectedDocument.Id, actualDocument.Id);
            Assert.AreEqual(expectedDocument.Project.Id, actualDocument.Project.Id);
            Assert.AreEqual(expectedDocument.Name, actualDocument.Name);
            CollectionAssert.AreEqual(expectedDocument.Folders.ToArray(), actualDocument.Folders.ToArray());
            Assert.AreEqual(expectedDocument.FilePath, actualDocument.FilePath);
            Assert.AreEqual(expectedDocument.SourceCodeKind, actualDocument.SourceCodeKind);
            var expectedText = await expectedDocument.GetTextAsync();
            var actualText = await actualDocument.GetTextAsync();
            Assert.AreEqual(expectedText.ToString(), actualText.ToString());
            Assert.AreEqual(expectedText.Encoding, actualText.Encoding);
            Assert.AreEqual(expectedText.ChecksumAlgorithm, actualText.ChecksumAlgorithm);
            CollectionAssert.AreEqual(expectedText.GetChecksum().ToArray(), actualText.GetChecksum().ToArray());
            Assert.AreEqual(await expectedDocument.GetTextVersionAsync(), await actualDocument.GetTextVersionAsync());
        }
        var mismatched = candidate.WithDocumentSyntaxRoot(retained.Id, CSharpSyntaxTree.ParseText("class Different { }").GetRoot());
        var failed = (Task<Solution>)restore.Invoke(null,
            [original, mismatched, new HashSet<ProjectId> { project.Id }, changedDocuments, CancellationToken.None])!;
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () => await failed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = (Task<Solution>)restore.Invoke(null,
            [original, candidate, new HashSet<ProjectId> { project.Id }, changedDocuments, cancellation.Token])!;
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelled);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LateGlobalAlias_PreservesInitiallyUnchangedHostEvidence(bool diagnostic)
    {
        var fixture = await CreateLateAliasFixtureAsync(diagnostic, exchange: false);
        await using var workspace = fixture.Workspace;
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var factory = project.Documents.Single(d => d.FilePath == fixture.FactoryPath);
        var factoryTree = (await factory.GetSyntaxTreeAsync())!;
        var beforeDiagnostics = (await project.GetCompilationAsync())!.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(diagnostic ? 1 : 0, beforeDiagnostics.Length, string.Join("\n", beforeDiagnostics.Select(d => d.ToString())));
        if (diagnostic)
        {
            Assert.AreEqual("CS0246", beforeDiagnostics.Single().Id);
            Assert.AreSame(factoryTree, beforeDiagnostics.Single().Location.SourceTree);
        }
        await AssertLateAliasFactoryBindingsAsync(original, original, fixture.FactoryPath);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(fixture.TargetPath, 3, fixture.Source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "missing"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var candidate = stored.Value.ModifiedSolution;
        Assert.AreEqual(1, preview.CallsiteUpdates!.Sum(update => update.CallsiteCount));
        var updatedFactory = candidate.GetDocument(factory.Id)!;
        var updatedText = (await updatedFactory.GetTextAsync()).ToString();
        StringAssert.Contains(updatedText, "global using __signatureAssembly = first;");
        await AssertLateAliasFactoryBindingsAsync(original, candidate, fixture.FactoryPath);
        var afterDiagnostics = (await candidate.GetProject(project.Id)!.GetCompilationAsync())!.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.AreEqual(beforeDiagnostics.Length, afterDiagnostics.Length);
        if (diagnostic)
        {
            var before = beforeDiagnostics.Single();
            var after = afterDiagnostics.Single();
            Assert.AreEqual(before.Id, after.Id);
            Assert.AreEqual(before.Severity, after.Severity);
            Assert.AreEqual(before.GetMessage(System.Globalization.CultureInfo.InvariantCulture), after.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual(factory.Id, candidate.GetDocument(after.Location.SourceTree!)!.Id);
            Assert.AreSame(await updatedFactory.GetSyntaxTreeAsync(), after.Location.SourceTree);
            Assert.AreEqual(before.Location.SourceSpan.Length, after.Location.SourceSpan.Length);
            Assert.AreEqual(updatedText.IndexOf("MissingAliasHostType", StringComparison.Ordinal), after.Location.SourceSpan.Start);
            Assert.IsTrue(after.Location.SourceSpan.Start > before.Location.SourceSpan.Start);
        }
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(fixture.Source, await File.ReadAllTextAsync(fixture.TargetPath));
        Assert.AreEqual(fixture.FactorySource, await File.ReadAllTextAsync(fixture.FactoryPath));
        if (!diagnostic)
        {
            var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(candidate, "SampleLib.SignatureProof"));
            var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(
                Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
        }
    }

    [TestMethod]
    [TestCategory("Benchmark")]
    public Task ChangeSignaturePreview_RepositoryBudget_UnknownType() =>
        AssertRepositorySignatureBudgetAsync("UnknownType");

    [TestMethod]
    [TestCategory("Benchmark")]
    public Task ChangeSignaturePreview_RepositoryBudget_Add() =>
        AssertRepositorySignatureBudgetAsync("Add");

    [TestMethod]
    [TestCategory("Benchmark")]
    public Task ChangeSignaturePreview_RepositoryBudget_Remove() =>
        AssertRepositorySignatureBudgetAsync("Remove");

    [TestMethod]
    [TestCategory("Benchmark")]
    public Task ChangeSignaturePreview_RepositoryBudget_Reorder() =>
        AssertRepositorySignatureBudgetAsync("Reorder");


    [TestMethod]
    [DataRow("add-omitted")]
    [DataRow("reorder-omitted")]
    [DataRow("reorder-expanded")]
    [DataRow("remove-expanded")]
    public async Task ChangeSignaturePreview_PreservesOriginalRuntimeBindings(string scenario)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureRuntimeFixture.cs");
        var expanded = scenario.EndsWith("expanded", StringComparison.Ordinal);
        var declaration = expanded
            ? "public static int Compute(int a, int b, params int[] tail) => a * 100 + b * 10" +
                (scenario == "remove-expanded" ? ";" : " + System.Linq.Enumerable.Sum(tail);")
            : "public static int Compute(int a = 1, int b = 2) => a * 10 + b;";
        var call = expanded ? "Compute(Trace(1), Trace(2), Trace(3), Trace(4))" : "Compute(9)";
        var source = string.Join("\n",
            "namespace SampleLib;",
            "public static class SignatureRuntimeFixture",
            "{",
            "    private static string trace = \"\";",
            "    private static int Trace(int value) { trace += value.ToString(); return value; }",
            "    " + declaration,
            "    public static string Run() { var value = " + call + "; return trace + \"|\" + value.ToString(); }",
            "}");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var request = scenario switch
        {
            "add-omitted" => new ChangeSignatureRequest("add", Name: "c", ParameterType: "int", DefaultValue: "7"),
            "remove-expanded" => new ChangeSignatureRequest("remove", Name: "tail"),
            _ => new ChangeSignatureRequest("reorder", NewOrder: expanded ? "b,a,tail" : "b,a"),
        };
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 6, 23), request, CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var originalValue = await RunSignatureFixtureAsync(original, "SampleLib.SignatureRuntimeFixture");
        var candidateValue = await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureRuntimeFixture");
        // Removing a parameter intentionally removes its argument expressions. Retained expressions
        // must keep their original evaluation order and original parameter values.
        Assert.AreEqual(scenario == "remove-expanded" ? "12|120" : originalValue, candidateValue);
        if (scenario.EndsWith("omitted", StringComparison.Ordinal))
            await AssertOptionalArgumentBindingsAsync(original, stored.Value.ModifiedSolution, path);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        if (scenario.EndsWith("omitted", StringComparison.Ordinal))
            await AssertOptionalArgumentBindingsAsync(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), path);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_CallerInfo_IsMaterializedFromOriginalOperation()
    {
        var declaration = "public class CallerTarget(int value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0, " +
            "[System.Runtime.CompilerServices.CallerMemberName] string member = \"\", " +
            "[System.Runtime.CompilerServices.CallerArgumentExpression(\"value\")] string expression = \"\") " +
            "{ public string Value => value + \"|\" + line + \"|\" + member + \"|\" + expression; }";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "public static class SignatureProof { public static string Run() => new CallerTarget(1 + 2).Value; }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0),
            2, declaration.IndexOf("CallerTarget", StringComparison.Ordinal) + 1, "3|3|Run|1 + 2", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_CrossDocument_DefaultStructAndUnrelatedOverloadStayBound()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var declarationPath = workspace.GetPath("SampleLib", "CrossTarget.cs");
        var callerPath = workspace.GetPath("SampleLib", "CrossCaller.cs");
        var declaration = "public struct CrossTarget(int removed, int retained) { public int Value => retained; public CrossTarget(string value) : this(0, int.Parse(value)) { } }";
        await File.WriteAllTextAsync(declarationPath, "namespace SampleLib;\n" + declaration);
        await File.WriteAllTextAsync(callerPath, "namespace SampleLib;\npublic static class CrossCaller { public static int Run() => " +
            "new CrossTarget(1, 2).Value + new CrossTarget(\"3\").Value + new CrossTarget().Value + default(CrossTarget).Value; }");
        await workspace.LoadAsync(CancellationToken.None);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(declarationPath, 2, declaration.IndexOf("CrossTarget", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        Assert.AreEqual("5", await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.CrossCaller"));
        Assert.AreEqual(2, preview.Changes.Count);
        Assert.AreEqual(2, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var apply = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        var changed = await File.ReadAllTextAsync(callerPath);
        StringAssert.Contains(changed, "new CrossTarget(\"3\")");
        StringAssert.Contains(changed, "new CrossTarget()");
        StringAssert.Contains(changed, "default(CrossTarget)");
    }

    [TestMethod]
    [DataRow("float", "float.NaN")]
    [DataRow("float", "float.PositiveInfinity")]
    [DataRow("float", "float.NegativeInfinity")]
    [DataRow("double", "double.NaN")]
    [DataRow("double", "double.PositiveInfinity")]
    [DataRow("double", "double.NegativeInfinity")]
    [DataRow("float", "-0F")]
    [DataRow("double", "-0D")]
    public async Task ChangeSignaturePreview_OptionalConstants_PreserveExactRuntimeValues(string kind, string defaultValue)
    {
        var bits = kind == "float" ? "System.BitConverter.SingleToInt32Bits(value)" : "System.BitConverter.DoubleToInt64Bits(value)";
        var declaration = $"public static string Compute({kind} value = {defaultValue}, decimal amount = 12.50M, " +
            "Choice choice = Choice.Third, char character = 'z', bool flag = true, string? label = null) => " +
            bits + " + \"|\" + string.Join(\",\", decimal.GetBits(amount)) + \"|\" + (int)choice + \"|\" + character + \"|\" + flag + \"|\" + (label ?? \"null\");";
        var source = "namespace SampleLib;\npublic enum Choice { Third = 3 }\n" +
            "public static class SignatureProof { " + declaration + " public static string Run() => Compute(); }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "9"),
            3, source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ParamsElements_PreserveUserDefinedConversionOrder()
    {
        var declaration = "public class ConversionTarget(int first, params int[] tail) { public int Sum => first + System.Linq.Enumerable.Sum(tail); }";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "public readonly struct Number(int value) { public int Value => value; public static implicit operator int(Number number) " +
            "{ SignatureProof.TraceText += \"C\" + number.Value; return number.Value; } } " +
            "public static class SignatureProof { public static string TraceText = \"\"; " +
            "public static Number Trace(int value) { TraceText += \"T\" + value; return new Number(value); } " +
            "public static string Run() { var value = new ConversionTarget(Trace(1), Trace(2), Trace(3)); return TraceText + \"|\" + value.Sum; } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0),
            2, declaration.IndexOf("ConversionTarget", StringComparison.Ordinal) + 1, "T1C1T2C2T3C3|6", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_MethodGroupOverloadDrift_IsRefusedWithoutToken()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "MethodGroupDrift.cs");
        var source = "namespace SampleLib;\npublic static class MethodGroupDrift { " +
            "public static int Compute(int first) => first; " +
            "public static int Compute(int first, object second) => first; " +
            "public static System.Func<int, string, int> Capture() => Compute; " +
            "public static int Direct() => Compute(1); }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("add", Name: "second", ParameterType: "string"),
                CancellationToken.None));
        StringAssert.Contains(error.Message, "retained target");
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    [DataRow("ref")]
    [DataRow("out")]
    [DataRow("in")]
    public async Task ChangeSignaturePreview_OrdinaryMethod_PreservesRefKindBindings(string modifier)
    {
        var assignment = modifier == "out" ? "value = 1; " : "";
        var source = "namespace SampleLib;\npublic static class SignatureProof { " +
            "public static int Compute(" + modifier + " int value, int optional = 7) { " + assignment + "return value + optional; } " +
            "public static int Run() { int value = 1; return Compute(" + modifier + " value); } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "9"),
            2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "8", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_PartialMethod_UpdatesDefinitionAndImplementation()
    {
        var source = "namespace SampleLib;\n" +
            "public partial class PartialTarget { public partial int Compute(int value); }\n" +
            "public partial class PartialTarget { public partial int Compute(int value) => value; }\n" +
            "public static class SignatureProof { public static int Run() => new PartialTarget().Compute(1); }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "0"),
            2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "1", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_OverrideTarget_UpdatesBaseAndSiblingOverrides()
    {
        var source = "namespace SampleLib;\n" +
            "public class SignatureBase { public virtual int Compute(int value) => value; }\n" +
            "public class SignatureLeft : SignatureBase { public override int Compute(int value) => value + 1; }\n" +
            "public class SignatureRight : SignatureBase { public override int Compute(int value) => value + 2; }\n" +
            "public static class SignatureProof { public static int Run() { SignatureBase left = new SignatureLeft(), right = new SignatureRight(); return left.Compute(1) + right.Compute(1); } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", DefaultValue: "0"),
            3, source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1, "5", 2);
    }

    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("reorder")]
    public async Task ChangeSignaturePreview_ReducedExtension_PreservesOriginalParameterBindings(string op)
    {
        var source = "namespace SampleLib;\npublic static class SignatureExtensions { " +
            "public static int Compute(this int receiver, int unused = 7, int retained = 2) => receiver * 10 + retained; }\n" +
            "public static class SignatureProof { public static int Run() => 1.Compute(); }";
        var request = op switch
        {
            "add" => new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 1),
            "remove" => new ChangeSignatureRequest("remove", Name: "unused"),
            _ => new ChangeSignatureRequest("reorder", NewOrder: "receiver,retained,unused"),
        };
        await AssertSignatureProofAsync(source, request, 2,
            source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "12", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_RemoveUnusedExtensionReceiver_RemovesOnlyItsEvaluation()
    {
        var source = "namespace SampleLib;\npublic static class SignatureExtensions { " +
            "public static int Compute(this int unused, int retained) => retained; }\n" +
            "public static class SignatureProof { private static string trace = \"\"; " +
            "public static int Trace(int value) { trace += value; return value; } " +
            "public static string Run() { var value = Trace(1).Compute(Trace(2)); return trace + \"|\" + value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"),
            2, source.Split('\n')[1].IndexOf("Compute", StringComparison.Ordinal) + 1, "2|2", 1);
    }


    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_RemoveExtensionReceiver_PreservesGenericSyntax(bool anonymous)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "ExtensionGenericProof.cs");
        var declaration = "public static class SignatureExtensions { public static string Compute<T>(this Receiver receiver, T value) where T : notnull => typeof(T).Name + \":\" + value.ToString(); }";
        var call = anonymous
            ? "new Receiver() /*before-dot*/ . /*after-dot*/ Compute /*method*/ (new { Value = 2 })"
            : "new Receiver() /*before-dot*/ . /*after-dot*/ Compute /*method*/ < /*type*/ int /*type-close*/ >(2)";
        var source = "namespace SampleLib;\npublic sealed class Receiver { }\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => " + call + "; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 3, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var candidate = stored.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var text = (await candidate.GetTextAsync()).ToString();
        foreach (var marker in anonymous ? new[] { "before-dot", "after-dot", "method" } : new[] { "before-dot", "after-dot", "method", "type", "type-close" })
            Assert.AreEqual(1, text.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1, marker);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    [DataRow("value", false)]
    [DataRow("value", true)]
    [DataRow("void", false)]
    [DataRow("void", true)]
    [DataRow("arrow", false)]
    [DataRow("arrow", true)]
    [DataRow("for", false)]
    [DataRow("for", true)]
    [DataRow("direct-loop", false)]
    [DataRow("flow", false)]
    [DataRow("return", false)]
    [DataRow("init", false)]
    [DataRow("chain", false)]
    [DataRow("chain", true)]
    [DataRow("lambda", false)]
    [DataRow("lambda", true)]
    [DataRow("lambda-value", false)]
    [DataRow("lambda-value", true)]
    [DataRow("ctor", false)]
    [DataRow("ctor", true)]
    public async Task ChangeSignaturePreview_RemoveExtensionReceiver_PreservesConditionalControl(string shape, bool isNull)
    {
        var declaration = shape is "value" or "lambda-value"
            ? "public static class SignatureExtensions { public static int Compute(this Receiver receiver, int value) => SignatureProof.Mark(\"M\", value); }"
            : "public static class SignatureExtensions { public static void Compute(this Receiver receiver, int value) { SignatureProof.Mark(\"M\", value); } }";
        var run = shape switch
        {
            "value" => "var value = Get()?.Compute(Argument()); return trace + \"|\" + Select(value);",
            "void" => "Get()?.Compute(Argument()); return trace;",
            "arrow" => "Call(); return trace;",
            "direct-loop" => "int inner = 0; for (int i = 0; i < 2; i++, Get()?.Compute(Argument())) while (inner++ < 3) { trace += \"W\"; continue; } return trace;",
            "flow" => "for (int i = 0; i < 3; i++, Get()?.Compute(Argument())) { try { for (int j = 0; j < 2; j++) { trace += \"N\"; continue; } if (i == 1) break; trace += \"B\"; continue; } finally { trace += \"F\"; } } return trace;",
            "return" => "try { for (int i = 0; i < 2; i++, Get()?.Compute(Argument())) { trace += \"B\"; return trace; } } finally { trace += \"F\"; } return trace;",
            "init" => "int i = 0; for (Mark(\"I\", 0) /*signature-init-comma*/, Get() /*signature-receiver*/ ? /*signature-question*/ . /*signature-dot*/ Compute /*signature-name*/ (Argument()), Mark(\"J\", 0); i < 1; Mark(\"K\", 0) /*signature-iterator-comma*/, Get()?.Compute(Argument()), i++) { trace += \"B\"; } return trace;",
            "chain" => "Get()?.Self()?.Compute(Argument()); return trace;",
            "lambda" or "lambda-value" => "System.Action action = () => Get()?.Compute(Argument()); action(); return trace;",
            "ctor" => "new Holder(); return trace;",
            _ => "for (int i = 0; i < 2; i++, Get()?.Compute(Argument())) { try { trace += \"B\"; continue; } finally { trace += \"F\"; } } return trace;",
        };
        var arrow = shape == "arrow" ? "public static void Call() => /*signature-arrow*/ Get()?.Compute(Argument()); /*signature-semicolon*/"
            : shape == "ctor" ? "private sealed class Holder { public Holder() => Get()?.Compute(Argument()); ~Holder() => ((Receiver?)null)?.Compute(Argument()); }" : "";
        var source = "namespace SampleLib;\npublic sealed class Receiver { public Receiver Self() { SignatureProof.Mark(\"S\", 0); return this; } }\n" + declaration + "\n"
            + "public static class SignatureProof { private static string trace = \"\"; "
            + "public static int Mark(string id, int value) { trace += id; return value; } "
            + "private static Receiver? Get() { trace += \"R\"; return " + (isNull ? "null" : "new Receiver()") + "; } "
            + "private static int Argument() => Mark(\"A\", 7); "
            + "private static string Select(int? value) => \"nullable:\" + value; "
            + "private static string Select(int value) => \"value:\" + value; " + arrow
            + "public static string Run() { " + run + " } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Position: 0),
            3, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1, null, shape is "ctor" or "init" ? 2 : 1);
    }

    [TestMethod]
    [DataRow("static", false)]
    [DataRow("instance", false)]
    [DataRow("conditional-instance", false)]
    [DataRow("conditional-instance", true)]
    [DataRow("extension", false)]
    [DataRow("conditional-extension", false)]
    [DataRow("conditional-extension", true)]
    [DataRow("explicit", false)]
    public async Task ChangeSignaturePreview_RemoveInferenceBearingParameter_RetainsSubstitution(string shape, bool isNull)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "GenericRemoval.cs");
        var extension = shape is "extension" or "conditional-extension";
        var declaration = extension
            ? "public static class GenericExtension { public static int Compute<T>(this GenericTarget receiver, T unused, int value) => SignatureProof.Trace(value); }"
            : "public class GenericTarget { public " + (shape is "static" or "explicit" ? "static " : "")
                + "int Compute<T>(T unused, int value) => SignatureProof.Trace(value); }";
        var call = shape switch
        {
            "static" => "GenericTarget.Compute(\"x\", Argument())",
            "explicit" => "GenericTarget.Compute /*method*/ < /*type*/ string /*close*/ >(\"x\", Argument())",
            "instance" or "extension" => "Get() /*receiver*/ . /*dot*/ Compute /*method*/ (\"x\", Argument())",
            _ => "Get() /*receiver*/ ? /*question*/ . /*dot*/ Compute /*method*/ (\"x\", Argument())",
        };
        var source = "namespace SampleLib;\n" + declaration + "\n"
            + (extension ? "public class GenericTarget { }" : "")
            + "public static class SignatureProof { private static string trace = \"\"; public static int Trace(int value) { trace += value; return value; }"
            + "private static int Argument() => Trace(1); private static GenericTarget? Get() { trace += \"R\"; return "
            + (isNull ? "null" : "new GenericTarget()") + "; } public static string Run() { var value = "
            + call + "; return trace + \"|\" + value; } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        async Task VerifyAsync(Microsoft.CodeAnalysis.Solution solution)
        {
            var document = solution.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
            var root = (await document.GetSyntaxRootAsync())!;
            var callNode = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => n.Expression.ToString().Contains("Compute", StringComparison.Ordinal));
            var model = (await document.GetSemanticModelAsync())!;
            var target = (Microsoft.CodeAnalysis.IMethodSymbol)model.GetSymbolInfo(callNode).Symbol!;
            Assert.AreEqual(Microsoft.CodeAnalysis.SpecialType.System_String, target.TypeArguments.Single().SpecialType);
            Assert.AreEqual("value", ((Microsoft.CodeAnalysis.Operations.IInvocationOperation)model.GetOperation(callNode)!)
                .Arguments.Single(a => a.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax).Parameter!.Name);
            Assert.AreEqual(expected, await RunSignatureFixtureAsync(solution, "SampleLib.SignatureProof"));
            var text = (await document.GetTextAsync()).ToString();
            foreach (var marker in new[] { "receiver", "question", "dot", "method", "type", "close" })
                Assert.AreEqual(source.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1,
                    text.Split("/*" + marker + "*/", StringSplitOptions.None).Length - 1, marker);
        }
        var originalDoc = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var originalModel = (await originalDoc.GetSemanticModelAsync())!;
        var originalCall = (await originalDoc.GetSyntaxRootAsync())!.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(n => n.Expression.ToString().Contains("Compute", StringComparison.Ordinal));
        Assert.AreEqual(Microsoft.CodeAnalysis.SpecialType.System_String,
            ((Microsoft.CodeAnalysis.IMethodSymbol)originalModel.GetSymbolInfo(originalCall).Symbol!).TypeArguments.Single().SpecialType);
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        await VerifyAsync(stored.Value.ModifiedSolution);
        Assert.AreEqual(1, preview.CallsiteUpdates!.Sum(c => c.CallsiteCount));
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        await VerifyAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
    }

    private static (int Line, int Column) Locate(string source, string needle)
    {
        var lines = source.Split('\n');
        var line = Array.FindIndex(lines, l => l.Contains(needle, StringComparison.Ordinal));
        Assert.IsTrue(line >= 0, needle);
        return (line + 1, lines[line].IndexOf(needle, StringComparison.Ordinal) + 1);
    }

    private static ChangeSignatureRequest ForgivingRequest(string op) => op switch
    {
        "add" => new ChangeSignatureRequest("add", Name: "extra", ParameterType: "int", DefaultValue: "7"),
        "remove" => new ChangeSignatureRequest("remove", Name: "unused"),
        _ => new ChangeSignatureRequest("reorder", NewOrder: "b,a,unused"),
    };

    // change-signature-unrelated-invocation-rewrite: postfix `!` and checked(...) have no operation of their
    // own; the builder used to treat them as arguments without a semantic parameter binding and refuse.
    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("reorder")]
    public async Task ChangeSignaturePreview_NullOperationArguments_AreResolvedThroughTheirOperand(string op)
    {
        var source = string.Join("\n",
            "namespace SampleLib;",
            "public static class SignatureProof",
            "{",
            "    public static int Compute(string a, int b, int unused = 0) => a.Length * 10 + b;",
            "    public static string Run() { string name = \"ab\"; int n = 3; return Compute(name!, checked(n)).ToString(); }",
            "}");
        var (line, column) = Locate(source, "Compute(string");
        await AssertSignatureProofAsync(source, ForgivingRequest(op), line, column, null, 1);
    }

    // An unrelated call that merely carries a caller-info default is still scanned: its `!` argument must not
    // fail the whole preview, and it is only touched when its compiler-supplied values drift.
    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("reorder")]
    public async Task ChangeSignaturePreview_UnrelatedCallerInfoCallWithNullOperationArgument_ReturnsPreview(string op)
    {
        var source = string.Join("\n",
            "namespace SampleLib;",
            "public static class Log",
            "{",
            "    public static string Tag(string v, [System.Runtime.CompilerServices.CallerArgumentExpression(\"v\")] string e = \"\") => v + \"/\" + e;",
            "}",
            "public static class SignatureProof",
            "{",
            "    public static string Compute(string a, int b, int unused = 0) => a + b;",
            "    public static string Run() { string name = \"ab\"; return Log.Tag(name!) + Compute(name, 1); }",
            "}");
        var (line, column) = Locate(source, "Compute(string");
        await AssertSignatureProofAsync(source, ForgivingRequest(op), line, column, null, null);
    }

    private const string CallerExpression = "[System.Runtime.CompilerServices.CallerArgumentExpression(\"{0}\")]";

    private async Task<(string Before, string After, string Text)> PreviewProofAsync(string source, ChangeSignatureRequest request)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var (line, column) = Locate(source, "Compute(string");
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, line, column), request, CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        var text = (await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib")
            .Documents.Single(d => d.FilePath == path).GetTextAsync()).ToString();
        return (await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"),
            await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.SignatureProof"), text);
    }

    private static string Proof(string caller, params string[] members) => string.Join("\n",
        "namespace SampleLib;",
        "public static class Log",
        "{",
        "    public static string Tag(string v, " + string.Format(CallerExpression, "v") + " string e = \"\") => v + \"/\" + e;",
        "    public static string TagG<T>(T v, " + string.Format(CallerExpression, "v") + " string e = \"\") => v?.ToString() + \"/\" + e;",
        "    public static string Outer(string o, " + string.Format(CallerExpression, "o") + " string oe = \"\") => o + \"|\" + oe;",
        "}",
        "public static class SignatureProof",
        "{",
        "    public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;",
        "    public static string Compute(string a, int b, int unused = 0) => a + b;",
        string.Join("\n", members),
        "    public static string Run() { string name = \"ab\"; " + caller + " }",
        "}");

    // change-signature-unrelated-invocation-rewrite: an unrelated generic call that only carries a caller-info
    // default must stay byte-identical (it used to become `Tag<string>(v: name, e: "name")`).
    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("reorder")]
    public async Task ChangeSignaturePreview_UnrelatedGenericCallerInfoCall_StaysByteIdentical(string op)
    {
        var (before, after, text) = await PreviewProofAsync(
            Proof("return Log.TagG(name) + Compute(name, 1);"), ForgivingRequest(op));
        Assert.AreEqual(before, after);
        StringAssert.Contains(text, "Log.TagG(name) + Compute(");
        Assert.IsFalse(text.Contains("Log.TagG<", StringComparison.Ordinal), text);
    }

    // The edited call is the caller-info argument of an unrelated call: its text drifts, so only that
    // parameter is frozen (`e:`); the unrelated call's own argument is not named.
    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("reorder")]
    public async Task ChangeSignaturePreview_UnrelatedCallerInfoExpressionDrift_FreezesOnlyTheDriftedParameter(string op)
    {
        var (before, after, text) = await PreviewProofAsync(
            Proof("return Log.Tag(Compute(name, 1));"), ForgivingRequest(op));
        Assert.AreEqual(before, after);
        StringAssert.Contains(text, "e: \"Compute(name, 1)\"");
        Assert.IsFalse(text.Contains("v:", StringComparison.Ordinal), text);
    }

    // Freezing the inner call changes the outer call's argument text, so the freeze converges outward.
    [TestMethod]
    public async Task ChangeSignaturePreview_NestedUnrelatedCallerInfoCalls_FreezeToAFixpoint()
    {
        var (before, after, text) = await PreviewProofAsync(
            Proof("return Log.Outer(Log.Tag(Compute(name, 1)));"), ForgivingRequest("add"));
        Assert.AreEqual(before, after);
        StringAssert.Contains(text, "e: \"Compute(name, 1)\"");
        StringAssert.Contains(text, "oe: \"Log.Tag(Compute(name, 1))\"");
        Assert.IsFalse(text.Contains("o:", StringComparison.Ordinal), text);
    }

    // Guard (green on base): removing a middle line of a multi-line call shifts a later caller-line value.
    [TestMethod]
    public async Task ChangeSignaturePreview_MultiLineRemoval_KeepsUnrelatedCallerLine()
    {
        var source = string.Join("\n",
            "namespace SampleLib;",
            "public static class SignatureProof",
            "{",
            "    public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;",
            "    public static string Compute(string a, int b, int unused = 0) => a + b;",
            "    public static string Run() { string name = \"ab\"; var x = Compute(",
            "        name,",
            "        1,",
            "        0);",
            "        return x + \"|\" + Line(); }",
            "}");
        var (before, after, text) = await PreviewProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"));
        Assert.AreEqual(before, after);
        StringAssert.Contains(text, "Line(line:");
    }

    // Guard (green on base): an unrelated caller-info call inside a removed argument loses its identity with
    // the argument and must neither refuse the preview nor be frozen.
    [TestMethod]
    public async Task ChangeSignaturePreview_UnrelatedCallerInfoCallInsideRemovedArgument_ReturnsPreview()
    {
        var source = string.Join("\n",
            "namespace SampleLib;",
            "public static class Log { public static string Tag(string v, " + string.Format(CallerExpression, "v") + " string e = \"\") => v + \"/\" + e; }",
            "public static class SignatureProof",
            "{",
            "    public static string Compute(string a, int b, int unused = 0) => b.ToString();",
            "    public static string Run() { string name = \"ab\"; return Compute(Log.Tag(name), 1); }",
            "}");
        var (_, _, text) = await PreviewProofAsync(source, new ChangeSignatureRequest("remove", Name: "a"));
        Assert.IsFalse(text.Contains("Log.Tag(", StringComparison.Ordinal), text);
    }

    // The freeze step reads the candidate before it is validated: a candidate that does not compile (unknown
    // parameter type) must still surface the documented diagnostic refusal, not a null-reference failure.
    [TestMethod]
    public async Task ChangeSignaturePreview_FreezeOnNonCompilingCandidate_RefusesWithTheDiagnostic()
    {
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => PreviewProofAsync(
            Proof("return Log.Tag(Compute(name, 1));"),
            new ChangeSignatureRequest("add", Name: "extra", ParameterType: "NoSuchTypeZz")));
        StringAssert.Contains(error.Message, "CS0246");
    }
}
