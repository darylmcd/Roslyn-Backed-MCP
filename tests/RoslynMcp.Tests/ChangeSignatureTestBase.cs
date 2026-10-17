using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public abstract class ChangeSignatureTestBase : OwnedRefactoringTestBase
{
    private ChangeSignatureService? _changeSignatureService;
    private protected ChangeSignatureService ChangeSignature => _changeSignatureService ??= new(
        Services.WorkspaceManager, Services.PreviewStore, Services.RefactoringService);
    private protected static async Task<string> RunSignatureFixtureAsync(
            Microsoft.CodeAnalysis.Solution solution, string typeName)
    {
        var project = solution.Projects.Single(p => p.Name == "SampleLib");
        var compilation = await project.GetCompilationAsync();
        Assert.IsNotNull(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.IsTrue(emission.Success, string.Join("\n", emission.Diagnostics.OrderByDescending(d => d.Severity)));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("signature-proof", isCollectible: true);
        context.Resolving += (loader, name) =>
        {
            var reference = compilation.References.OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
                .FirstOrDefault(r => r.FilePath is { } file && File.Exists(file)
                    && System.Reflection.AssemblyName.GetAssemblyName(file).Name == name.Name);
            if (reference?.FilePath is not { } dependency) return null;
            using var dependencyImage = new MemoryStream(File.ReadAllBytes(dependency));
            return loader.LoadFromStream(dependencyImage);
        };
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType(typeName, throwOnError: true)!.GetMethod("Run");
            Assert.IsNotNull(method);
            return Convert.ToString(method.Invoke(null, null), System.Globalization.CultureInfo.InvariantCulture)!;
        }
        finally
        {
            context.Unload();
        }
    }
    private protected async Task AssertSignatureProofAsync(
            string source, ChangeSignatureRequest request, int line, int column, string? expected, int expectedCalls)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var originalValue = await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof");
        var expectedValue = expected ?? originalValue;
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, line, column), request, CancellationToken.None);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(pair);
        Assert.AreEqual(expectedValue, await RunSignatureFixtureAsync(pair.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        Assert.AreEqual(expectedCalls, preview.CallsiteUpdates?.Sum(c => c.CallsiteCount));
        var proofText = (await pair.Value.ModifiedSolution.Projects.Single(p => p.Name == "SampleLib")
            .Documents.Single(d => d.FilePath == path).GetTextAsync()).ToString();
        foreach (var marker in new[] { "init-comma", "iterator-comma", "receiver", "question", "dot", "name", "arrow", "semicolon" })
        {
            var comment = "/*signature-" + marker + "*/";
            Assert.AreEqual(source.Split(comment, StringSplitOptions.None).Length - 1,
                proofText.Split(comment, StringSplitOptions.None).Length - 1, comment);
        }
        var apply = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(apply.Success, apply.Error);
        Assert.AreEqual(expectedValue, await RunSignatureFixtureAsync(
            Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }
    private protected sealed class SignatureStoreProbe(IPreviewStore inner) : IPreviewStore
    {
        public int StoreCalls { get; private set; }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description);
        }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated);
        }

        public string Store(string workspaceId, Microsoft.CodeAnalysis.Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes)
        {
            StoreCalls++;
            return inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, changes);
        }

        public (string WorkspaceId, Microsoft.CodeAnalysis.Solution OriginalSolution, Microsoft.CodeAnalysis.Solution ModifiedSolution,
            int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token) => inner.Retrieve(token);

        public void Invalidate(string token) => inner.Invalidate(token);
        public void InvalidateAll(string? workspaceId = null) => inner.InvalidateAll(workspaceId);
        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion) => inner.InvalidateOnVersionBump(workspaceId, newWorkspaceVersion);
        public string? PeekWorkspaceId(string token) => inner.PeekWorkspaceId(token);
    }
    private protected static async Task AssertOptionalArgumentBindingsAsync(
            Microsoft.CodeAnalysis.Solution original, Microsoft.CodeAnalysis.Solution candidate, string path)
    {
        static async Task<Microsoft.CodeAnalysis.Operations.IInvocationOperation> ComputeOperationAsync(
            Microsoft.CodeAnalysis.Solution solution, string file)
        {
            var document = solution.GetDocument(solution.GetDocumentIdsWithFilePath(file).Single())!;
            var root = await document.GetSyntaxRootAsync();
            var model = await document.GetSemanticModelAsync();
            Assert.IsNotNull(root);
            Assert.IsNotNull(model);
            var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(n => n.Expression.ToString() == "Compute");
            var operation = model.GetOperation(call) as Microsoft.CodeAnalysis.Operations.IInvocationOperation;
            Assert.IsNotNull(operation);
            return operation;
        }
        var before = await ComputeOperationAsync(original, path);
        var after = await ComputeOperationAsync(candidate, path);
        Assert.AreEqual(before.TargetMethod.ContainingType.Name, after.TargetMethod.ContainingType.Name);
        Assert.AreEqual(before.TargetMethod.Name, after.TargetMethod.Name);
        foreach (var argument in before.Arguments)
        {
            Assert.IsNotNull(argument.Parameter);
            var retained = after.Arguments.Single(a => a.Parameter?.Name == argument.Parameter.Name);
            Assert.IsNotNull(retained.Parameter);
            Assert.AreEqual(argument.Parameter.Type.ToDisplayString(), retained.Parameter.Type.ToDisplayString());
            Assert.IsTrue(argument.Value.ConstantValue.HasValue);
            Assert.IsTrue(retained.Value.ConstantValue.HasValue);
            Assert.AreEqual(argument.Value.ConstantValue.Value, retained.Value.ConstantValue.Value,
                $"original binding for {argument.Parameter.Name} must retain its value");
            Assert.AreEqual(argument.InConversion.IsIdentity, retained.InConversion.IsIdentity);
        }
    }
}
