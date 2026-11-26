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
    private sealed class ThrowingBatchInputs(Exception error) : IReadOnlyList<int>
    {
        public int Count => 2;
        public int this[int index] => index == 0 ? 0 : throw error;
        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class IndexedBatchInputs(int count, int failureIndex, Exception error) : IReadOnlyList<int>
    {
        public List<int> Accessed { get; } = [];
        public int CountReads { get; private set; }
        public int Count { get { CountReads++; return count; } }
        public int this[int index]
        {
            get
            {
                Accessed.Add(index);
                if (index == failureIndex) throw error;
                return index;
            }
        }
        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public async Task ChangeSignaturePreview_ReadBatch_OneReaderStagedAdmission(bool cancel, bool inputFailure, bool beyondBoundary)
    {
        var bound = AssertSignatureReaderWidth();
        var failureIndex = beyondBoundary ? bound + 1 : 1;
        var count = failureIndex + bound + 1;
        using var cancellation = new CancellationTokenSource();
        Exception authored = cancel ? new OperationCanceledException("owned reader cancellation", cancellation.Token)
            : new PublicInvalidOperationException("staged read failure");
        var inputs = new IndexedBatchInputs(count, inputFailure ? failureIndex : -1, authored);
        var started = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var active = 0;
        var peak = 0;
        var sync = new object();
        async Task<int> ReadStagedAsync(int index)
        {
            lock (sync) peak = Math.Max(peak, ++active);
            started[index].SetResult();
            try
            {
                await release[index].Task;
                if (index == failureIndex) throw authored;
                return index;
            }
            finally { lock (sync) active--; }
        }
        var pending = ReadSignatureBatchAsync(inputs, ReadStagedAsync);
        try
        {
            var initialWidth = Math.Min(bound, inputFailure ? failureIndex : count);
            await Task.WhenAll(started.Take(initialWidth).Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(initialWidth, active);
            for (var index = 0; index < failureIndex; index++)
            {
                await started[index].Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.IsFalse(pending.IsCompleted, "Every held reader must join before publication.");
                Assert.IsTrue(active <= bound);
                release[index].SetResult();
            }
            if (!inputFailure)
            {
                await started[failureIndex].Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.IsFalse(pending.IsCompleted);
                if (cancel) cancellation.Cancel();
                release[failureIndex].SetResult();
            }
            foreach (var signal in release) signal.TrySetResult();
            var outcomes = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(0, active);
            Assert.AreEqual(initialWidth, peak);
            Assert.AreEqual(1, inputs.CountReads, "The finite input count must be snapshotted once.");
            for (var index = 0; index < failureIndex; index++) Assert.AreEqual(index, outcomes[index]());
            Exception observed = cancel
                ? Assert.ThrowsExactly<OperationCanceledException>(() => outcomes[failureIndex]())
                : Assert.ThrowsExactly<PublicInvalidOperationException>(() => outcomes[failureIndex]());
            Assert.AreSame(authored, observed);
            Assert.AreEqual(authored.Message, observed.Message);
            StringAssert.Contains(observed.StackTrace!, inputFailure ? "get_Item" : "ReadStagedAsync");
            if (observed is OperationCanceledException canceled) Assert.AreEqual(cancellation.Token, canceled.CancellationToken);
            CollectionAssert.AreEqual(Enumerable.Range(0, inputs.Accessed.Count).ToArray(), inputs.Accessed);
            if (inputFailure || bound == 1)
                Assert.AreEqual(failureIndex + 1, inputs.Accessed.Count, "No descriptor after an observed failure may be admitted.");
            if (bound == 1) Assert.AreEqual(failureIndex + 1, outcomes.Length);
        }
        finally
        {
            foreach (var signal in release) signal.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [TestMethod]
    [DataRow(3, false)]
    [DataRow(4, false)]
    [DataRow(5, false)]
    [DataRow(3, true)]
    [DataRow(4, true)]
    [DataRow(5, true)]
    [DataRow(-1, true)]
    public async Task ChangeSignaturePreview_JoinedPass_OrderedCancellationAndFailurePrecedence(int failureIndex, bool cancel)
    {
        var bound = AssertSignatureReaderWidth();
        // Two project spans of three descriptors remain one globally indexed candidate pass.
        var projects = new[] { "first", "first", "first", "second", "second", "second" };
        var active = 0;
        var completed = new bool[projects.Length];
        var authored = new PublicInvalidOperationException("global read " + failureIndex);
        using var cancellation = new CancellationTokenSource();
        async Task<int> ReadGlobalAsync(int index)
        {
            Interlocked.Increment(ref active);
            try
            {
                await Task.Yield();
                if (index == failureIndex) throw authored;
                return index;
            }
            finally { completed[index] = true; Interlocked.Decrement(ref active); }
        }
        var outcomes = await ReadSignatureOutcomesAsync(Enumerable.Range(0, projects.Length).ToArray(), ReadGlobalAsync);
        var consumed = new List<int>();
        void ConsumeGlobal(int index)
        {
            Assert.AreEqual(0, active, "The entire pass must join before any consumer action.");
            if (failureIndex >= 0) Assert.IsTrue(completed[failureIndex]);
            consumed.Add(index);
            if (cancel && index == 2) cancellation.Cancel();
        }
        try
        {
            var boundary = ((2 / bound) + 1) * bound;
            if (cancel && (failureIndex < 0 || failureIndex >= boundary))
            {
                var error = Assert.ThrowsExactly<OperationCanceledException>(() =>
                    ConsumeSignatureOutcomes<int>(outcomes, ConsumeGlobal, cancellation.Token));
                Assert.AreEqual(cancellation.Token, error.CancellationToken);
                CollectionAssert.AreEqual(Enumerable.Range(0, boundary).ToArray(), consumed);
            }
            else
            {
                var error = Assert.ThrowsExactly<PublicInvalidOperationException>(() =>
                    ConsumeSignatureOutcomes<int>(outcomes, ConsumeGlobal, cancellation.Token));
                Assert.AreSame(authored, error);
                Assert.AreEqual("global read " + failureIndex, error.Message);
                StringAssert.Contains(error.StackTrace!, "ReadGlobalAsync");
                CollectionAssert.AreEqual(Enumerable.Range(0, failureIndex).ToArray(), consumed);
            }
        }
        finally
        {
            Assert.AreEqual(0, active);
            Assert.IsTrue(outcomes.Cast<object?>().All(outcome => outcome is null), "Every retained slot must clear on exit.");
        }
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public async Task ChangeSignaturePreview_JoinedPass_EarlierApplyOwnsFailure(bool applyCancellation, bool readerCancellation, bool beyondBoundary)
    {
        var bound = AssertSignatureReaderWidth();
        var applyIndex = beyondBoundary ? bound + 1 : 0;
        var readIndex = applyIndex + bound + 1;
        using var request = new CancellationTokenSource();
        using var owner = new CancellationTokenSource();
        owner.Cancel();
        Exception applyError = applyCancellation ? new OperationCanceledException("apply owner", owner.Token)
            : new PublicInvalidOperationException("earlier apply");
        Exception readError = readerCancellation ? new OperationCanceledException("reader owner", owner.Token)
            : new PublicInvalidOperationException("later read");
        var active = 0;
        async Task<int> ReadLaterFailureAsync(int index)
        {
            Interlocked.Increment(ref active);
            try { await Task.Yield(); if (index == readIndex) throw readError; return index; }
            finally { Interlocked.Decrement(ref active); }
        }
        var outcomes = await ReadSignatureOutcomesAsync(Enumerable.Range(0, readIndex + 1).ToArray(), ReadLaterFailureAsync);
        var consumed = new List<int>();
        void ApplyEarlier(int index)
        {
            Assert.AreEqual(0, active);
            consumed.Add(index);
            if (index != applyIndex) return;
            request.Cancel();
            throw applyError;
        }
        Exception observed = applyCancellation
            ? Assert.ThrowsExactly<OperationCanceledException>(() => ConsumeSignatureOutcomes<int>(outcomes, ApplyEarlier, request.Token))
            : Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConsumeSignatureOutcomes<int>(outcomes, ApplyEarlier, request.Token));
        Assert.AreSame(applyError, observed);
        Assert.AreEqual(applyError.Message, observed.Message);
        StringAssert.Contains(observed.StackTrace!, "ApplyEarlier");
        if (observed is OperationCanceledException canceled) Assert.AreEqual(owner.Token, canceled.CancellationToken);
        CollectionAssert.AreEqual(Enumerable.Range(0, applyIndex + 1).ToArray(), consumed);
        Assert.AreEqual(0, active);
        Assert.IsTrue(outcomes.Cast<object?>().All(outcome => outcome is null));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_JoinedPass_ZeroAndEmptyPreserveFailureAndClearing()
    {
        AssertSignatureReaderWidth();
        using var request = new CancellationTokenSource();
        var authored = new PublicInvalidOperationException("reader zero");
        async Task<int> ReadZeroAsync(int index) { await Task.Yield(); throw authored; }
        var outcomes = await ReadSignatureOutcomesAsync<int>([0], ReadZeroAsync);
        request.Cancel();
        var calls = 0;
        var observed = Assert.ThrowsExactly<PublicInvalidOperationException>(() =>
            ConsumeSignatureOutcomes<int>(outcomes, _ => calls++, request.Token));
        Assert.AreSame(authored, observed);
        Assert.AreEqual("reader zero", observed.Message);
        StringAssert.Contains(observed.StackTrace!, "ReadZeroAsync");
        Assert.AreEqual(0, calls);
        Assert.IsTrue(outcomes.Cast<object?>().All(outcome => outcome is null));
        var empty = await ReadSignatureOutcomesAsync<int>([], _ => throw new AssertFailedException("Empty pass launched a reader."));
        ConsumeSignatureOutcomes<int>(empty, _ => calls++, request.Token);
        Assert.AreEqual(0, calls);
        Assert.AreEqual(0, empty.Length);
        var success = await ReadSignatureOutcomesAsync<int>([0, 1], Task.FromResult);
        ConsumeSignatureOutcomes<int>(success, _ => calls++, CancellationToken.None);
        Assert.AreEqual(2, calls);
        Assert.IsTrue(success.Cast<object?>().All(outcome => outcome is null));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ReadBatch_InputFailureJoinsLaunchedReader()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launchError = new PublicInvalidOperationException("input acquisition failed");
        var active = 0;
        var pending = ReadSignatureBatchAsync(new ThrowingBatchInputs(launchError), async index =>
        {
            Interlocked.Increment(ref active);
            started.SetResult();
            try { await release.Task; return index; }
            finally { Interlocked.Decrement(ref active); }
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsFalse(pending.IsCompleted, "Input acquisition failure must still join the launched reader.");
            release.SetResult();
            var outcomes = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(0, active);
            Assert.AreEqual(0, outcomes[0]());
            Assert.AreSame(launchError, Assert.ThrowsExactly<PublicInvalidOperationException>(() => outcomes[1]()));
        }
        finally
        {
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_ReadBatch_JoinsFailuresBeforeOrderedConsumption(bool cancel)
    {
        var started = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var release = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var finished = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cancellation = new CancellationTokenSource();
        var earlier = new PublicInvalidOperationException("earlier read");
        var later = new PublicInvalidOperationException("later read");
        var active = 0;
        async Task<int> ReadFailureAsync(int index)
        {
            Interlocked.Increment(ref active);
            started[index].SetResult();
            try
            {
                await release[index].Task;
                cancellation.Token.ThrowIfCancellationRequested();
                throw index == 0 ? earlier : later;
            }
            finally
            {
                Interlocked.Decrement(ref active);
                finished[index].SetResult();
            }
        }
        var pending = ReadSignatureBatchAsync<int>([0, 1], ReadFailureAsync);
        try
        {
            var bound = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
            await started[0].Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (bound > 1)
                await started[1].Task.WaitAsync(TimeSpan.FromSeconds(30));
            else
                Assert.IsFalse(started[1].Task.IsCompleted, "The sole reader owns admission until it joins.");
            if (cancel) cancellation.Cancel();
            Assert.IsFalse(pending.IsCompleted);
            if (bound > 1)
            {
                release[1].SetResult();
                await finished[1].Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.IsFalse(pending.IsCompleted, "Neither later failure nor cancellation may bypass the first reader join.");
            }
            release[0].SetResult();
            var outcomes = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(0, active);
            Assert.AreEqual(bound == 1 ? 1 : 2, outcomes.Length);
            if (bound == 1) Assert.IsFalse(started[1].Task.IsCompleted);
            if (cancel)
            {
                foreach (var outcome in outcomes)
                {
                    var error = Assert.ThrowsExactly<OperationCanceledException>(() => outcome());
                    Assert.AreEqual(cancellation.Token, error.CancellationToken);
                    StringAssert.Contains(error.StackTrace!, "ReadFailureAsync");
                }
            }
            else
            {
                var error = Assert.ThrowsExactly<PublicInvalidOperationException>(() => outcomes[0]());
                Assert.AreSame(earlier, error);
                Assert.AreEqual("earlier read", error.Message);
                StringAssert.Contains(error.StackTrace!, "ReadFailureAsync");
                if (bound > 1) Assert.AreSame(later, Assert.ThrowsExactly<PublicInvalidOperationException>(() => outcomes[1]()));
            }
        }
        finally
        {
            foreach (var signal in release) signal.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_EarlierDocumentFailure_PrecedesLaterScan(bool rewriteFailure)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var targetPath = workspace.GetPath("SampleLib", "Target.cs");
        var earlierPath = workspace.GetPath("SampleLib", "A.cs");
        var laterPath = workspace.GetPath("SampleLib", "B.cs");
        var targetSource = "namespace SampleLib; public static class Target { public static void Compute(int a" + (rewriteFailure ? ", int b" : "") + ") {} }";
        var earlierSource = "namespace SampleLib; public static class Earlier { public static void Run(dynamic d) { "
            + (rewriteFailure ? "Target.Compute(1, b: 2);" : "Target.Compute(d);") + " } }";
        var laterSource = "namespace SampleLib; public static class Later { public static void Run(dynamic d) { "
            + (rewriteFailure ? "Target.Compute(d, 2);" : "Target.Compute(missing);") + " } }";
        await File.WriteAllTextAsync(targetPath, targetSource);
        await File.WriteAllTextAsync(earlierPath, earlierSource);
        await File.WriteAllTextAsync(laterPath, laterSource);
        var bound = AssertSignatureReaderWidth();
        for (var index = 0; index < bound; index++)
            await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "AZPadding" + index + ".cs"),
                "namespace SampleLib; public static class SignaturePadding" + index + " { }");
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        Assert.IsTrue(project.Documents.ToList().FindIndex(d => d.FilePath == earlierPath)
            < project.Documents.ToList().FindIndex(d => d.FilePath == laterPath));
        Assert.IsTrue(project.Documents.ToList().FindIndex(d => d.FilePath == laterPath)
            - project.Documents.ToList().FindIndex(d => d.FilePath == earlierPath) > bound,
            "The later scan refusal must be beyond the earlier document's old batch.");
        var targetDocument = project.Documents.Single(d => d.FilePath == targetPath);
        var targetRoot = (await targetDocument.GetSyntaxRootAsync())!;
        var targetModel = (await targetDocument.GetSemanticModelAsync())!;
        var selected = targetModel.GetDeclaredSymbol(targetRoot.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single())!;
        foreach (var (path, dynamicCall) in new[] { (earlierPath, !rewriteFailure), (laterPath, rewriteFailure) })
        {
            var document = project.Documents.Single(d => d.FilePath == path);
            var root = (await document.GetSyntaxRootAsync())!;
            var model = (await document.GetSemanticModelAsync())!;
            var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
            if (dynamicCall)
            {
                Assert.IsInstanceOfType<Microsoft.CodeAnalysis.Operations.IDynamicInvocationOperation>(model.GetOperation(call));
                var receiver = model.GetTypeInfo(((Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax)call.Expression).Expression).Type!;
                Assert.AreNotEqual(TypeKind.Dynamic, receiver.TypeKind);
                Assert.IsTrue(model.LookupSymbols(call.SpanStart, receiver, "Compute").OfType<IMethodSymbol>()
                    .Any(method => SymbolEqualityComparer.Default.Equals(selected, method)));
            }
            else if (rewriteFailure)
            {
                Assert.IsTrue(SymbolEqualityComparer.Default.Equals(selected, model.GetSymbolInfo(call).Symbol));
                Assert.IsTrue(call.ArgumentList.Arguments.Any(a => a.NameColon is not null));
                Assert.IsTrue(call.ArgumentList.Arguments.Any(a => a.NameColon is null));
            }
            else
                Assert.IsTrue(model.GetSymbolInfo(call).CandidateSymbols.Any(method => SymbolEqualityComparer.Default.Equals(selected, method)));
        }
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var request = rewriteFailure ? new ChangeSignatureRequest("reorder", NewOrder: "b,a")
            : new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(targetPath, 1, targetSource.IndexOf("Compute", StringComparison.Ordinal) + 1), request, CancellationToken.None));
        Assert.AreEqual(rewriteFailure
            ? "op='reorder' refuses to rewrite a callsite that mixes positional and named arguments. Convert it to all-positional or all-named, then retry."
            : "A dynamic invocation may bind to the edited member at runtime. Use a statically bound call before changing this signature.", error.Message);
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        foreach (var (path, source) in new[] { (targetPath, targetSource), (earlierPath, earlierSource), (laterPath, laterSource) })
        {
            Assert.AreEqual(source, await File.ReadAllTextAsync(path));
            Assert.AreEqual(source, (await original.GetDocument(original.GetDocumentIdsWithFilePath(path).Single())!.GetTextAsync()).ToString());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_OrdinaryCapture_PrecedesLaterDynamicInSameDocument(bool laterDynamic)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "CaptureOrder.cs");
        var source = "namespace SampleLib; class Target { public static void Compute(int value) {} public static void Run(dynamic d) { Compute(1); "
            + (laterDynamic ? "Compute(d);" : "") + " } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var document = original.GetDocument(original.GetDocumentIdsWithFilePath(path).Single())!;
        var root = (await document.GetSyntaxRootAsync())!;
        var model = (await document.GetSemanticModelAsync())!;
        var selected = model.GetDeclaredSymbol(root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Compute"))!;
        var calls = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().ToArray();
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(selected, model.GetSymbolInfo(calls[0]).Symbol));
        var owner = RoslynMcp.Roslyn.Helpers.ArgumentOperationResolver.Resolve(model, calls[0].ArgumentList.Arguments[0].Expression, CancellationToken.None);
        Assert.AreEqual(0, owner?.Parameter?.Ordinal);
        Assert.AreEqual(Microsoft.CodeAnalysis.Operations.ArgumentKind.Explicit, owner?.ArgumentKind);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var locator = SymbolLocator.BySource(path, 1, source.IndexOf("Compute", StringComparison.Ordinal) + 1);
        var request = new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0);
        if (laterDynamic)
        {
            Assert.IsInstanceOfType<Microsoft.CodeAnalysis.Operations.IDynamicInvocationOperation>(model.GetOperation(calls[1]));
            Assert.IsTrue(model.LookupSymbols(calls[1].SpanStart, name: "Compute").Any(method => SymbolEqualityComparer.Default.Equals(selected, method)));
            var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator, request, CancellationToken.None));
            Assert.AreEqual("A dynamic invocation may bind to the edited member at runtime. Use a statically bound call before changing this signature.", error.Message);
            Assert.AreEqual(0, probe.StoreCalls);
        }
        else
        {
            var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId, locator, request, CancellationToken.None);
            Assert.AreEqual(1, probe.StoreCalls);
            var candidate = Services.PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
            var updated = candidate.GetDocument(document.Id)!;
            var updatedModel = (await updated.GetSemanticModelAsync())!;
            var call = (await updated.GetSyntaxRootAsync())!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
            var operation = (Microsoft.CodeAnalysis.Operations.IInvocationOperation)updatedModel.GetOperation(call)!;
            Assert.AreEqual("marker", operation.Arguments[0].Parameter!.Name);
            Assert.AreEqual("value", operation.Arguments[1].Parameter!.Name);
            Assert.AreEqual(1, operation.Arguments[1].Value.ConstantValue.Value);
        }
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_ArgumentOwnership_PrecedesLaterDynamicInSameDocument()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "GuardOrder.cs");
        const string source = "namespace SampleLib; class Target { public static void Compute(int value, __arglist) {} public static void Run(dynamic d) { Compute(1, __arglist(2)); Compute(d, __arglist()); } }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var document = original.GetDocument(original.GetDocumentIdsWithFilePath(path).Single())!;
        var root = (await document.GetSyntaxRootAsync())!;
        var model = (await document.GetSemanticModelAsync())!;
        var declaration = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Compute");
        var selected = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var calls = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Where(node => node.Expression.ToString() == "Compute").ToArray();
        Assert.AreEqual(2, calls.Length);
        var firstTarget = model.GetSymbolInfo(calls[0]).Symbol as IMethodSymbol;
        var firstOperation = model.GetOperation(calls[0]);
        var laterOperation = model.GetOperation(calls[1]);
        var firstOwners = calls[0].ArgumentList.Arguments.Select(argument =>
            RoslynMcp.Roslyn.Helpers.ArgumentOperationResolver.Resolve(model, argument.Expression, CancellationToken.None)).ToArray();
        TestContext.WriteLine("Selected: " + selected.ToDisplayString());
        TestContext.WriteLine("First target: " + firstTarget?.ToDisplayString());
        TestContext.WriteLine("First operation: " + firstOperation?.Kind);
        TestContext.WriteLine("First parameter owners: " + string.Join(",", firstOwners.Select(owner => owner?.Parameter?.ToDisplayString() ?? "<null>")));
        TestContext.WriteLine("Later operation: " + laterOperation?.Kind);
        TestContext.WriteLine("Diagnostics: " + string.Join(" | ", (await document.Project.GetCompilationAsync())!.GetDiagnostics().Select(d => d.ToString())));
        Assert.IsTrue(SymbolEqualityComparer.Default.Equals(selected, firstTarget), "The first call must select the edited method.");
        Assert.IsFalse(firstOperation is Microsoft.CodeAnalysis.Operations.IDynamicInvocationOperation);
        Assert.IsTrue(firstOwners.Any(owner => owner?.Parameter is null), "The first call must reach the existing missing-parameter guard.");
        Assert.IsInstanceOfType<Microsoft.CodeAnalysis.Operations.IDynamicInvocationOperation>(laterOperation,
            "The later call must reach the actual dynamic refusal branch.");
        Assert.IsTrue(model.LookupSymbols(calls[1].SpanStart, name: "Compute").OfType<IMethodSymbol>()
            .Any(method => SymbolEqualityComparer.Default.Equals(method, selected)));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => service.PreviewChangeSignatureAsync(
            workspace.WorkspaceId, SymbolLocator.BySource(path, 1, source.IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0), CancellationToken.None));
        Assert.AreEqual("An existing argument has no original semantic parameter binding.", error.Message);
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, (await document.GetTextAsync()).ToString());
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_UnchangedDocument_RetainsExactDiagnosticOccurrence()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "RetainedDiagnosticTarget.cs");
        var retainedPath = workspace.GetPath("SampleLib", "RetainedDiagnostic.cs");
        const string source = "namespace SampleLib; public static class DiagnosticTarget { public static int M(int unused, int retained) => retained; public static int Run() => M(1, 2); }";
        const string retainedSource = "namespace SampleLib; public static class DiagnosticEvidenceControl { public static MissingRetainedType? Missing; public static object Make() => new object(); public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; public static int Location() => Line(); }";
        await File.WriteAllTextAsync(path, source);
        await File.WriteAllTextAsync(retainedPath, retainedSource);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var document = project.Documents.Single(d => d.FilePath == retainedPath);
        var tree = (await document.GetSyntaxTreeAsync())!;
        var before = (await project.GetCompilationAsync())!.GetDiagnostics().Single(d => d.Severity == DiagnosticSeverity.Error);
        Assert.AreEqual("CS0246", before.Id);
        Assert.AreSame(tree, before.Location.SourceTree);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 1, source.IndexOf("M(", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Position: 0), CancellationToken.None);
        Assert.AreEqual(1, probe.StoreCalls);
        Assert.AreEqual(path, preview.Changes.Single().FilePath, "The diagnostic-bearing document must have no actual edit.");
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var candidate = stored.Value.ModifiedSolution;
        var actualDocument = candidate.GetDocument(document.Id)!;
        Assert.AreSame(tree, await actualDocument.GetSyntaxTreeAsync());
        var after = (await candidate.GetProject(project.Id)!.GetCompilationAsync())!.GetDiagnostics()
            .Single(d => d.Severity == DiagnosticSeverity.Error);
        Assert.AreEqual(document.Id, candidate.GetDocument(after.Location.SourceTree!)!.Id);
        Assert.AreSame(before.Location.SourceTree, after.Location.SourceTree);
        Assert.AreEqual(before.Location.SourceSpan, after.Location.SourceSpan);
        Assert.AreEqual(before.Id, after.Id);
        Assert.AreEqual(before.Severity, after.Severity);
        Assert.AreEqual(before.GetMessage(System.Globalization.CultureInfo.InvariantCulture), after.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        Assert.AreEqual(retainedSource, await File.ReadAllTextAsync(retainedPath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_CancellationBeforePublication_StoresNoToken(bool lateAlias)
    {
        var fixture = await CreateLateAliasFixtureAsync(diagnostic: false, exchange: false);
        await using var workspace = fixture.Workspace;
        if (!lateAlias)
        {
            var current = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
            var factory = current.GetDocument(current.GetDocumentIdsWithFilePath(fixture.FactoryPath).Single())!;
            var root = (await factory.GetSyntaxRootAsync())!;
            Assert.IsTrue(Services.WorkspaceManager.TryApplyChanges(workspace.WorkspaceId,
                current.WithDocumentSyntaxRoot(factory.Id, CSharpSyntaxTree.ParseText(
                    root.ToFullString().Replace("extern alias first;", "extern alias first; global using __signatureAssembly = first;", StringComparison.Ordinal)).GetRoot())));
        }
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var factoryBeforeCancellation = await File.ReadAllTextAsync(fixture.FactoryPath);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(fixture.TargetPath, 3, fixture.Source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "missing"), cancellation.Token));
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(fixture.Source, await File.ReadAllTextAsync(fixture.TargetPath));
        Assert.AreEqual(factoryBeforeCancellation, await File.ReadAllTextAsync(fixture.FactoryPath));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LateGlobalAlias_DiagnosticOccurrenceIsNotExchanged()
    {
        var fixture = await CreateLateAliasFixtureAsync(diagnostic: true, exchange: true);
        await using var workspace = fixture.Workspace;
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var diagnostics = (await project.GetCompilationAsync())!.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        CollectionAssert.AreEquivalent(new[] { "CS0103", "CS0246" }, diagnostics.Select(d => d.Id).ToArray());
        var document = project.Documents.Single(d => d.FilePath == fixture.TargetPath);
        var model = (await document.GetSemanticModelAsync())!;
        var root = (await document.GetSyntaxRootAsync())!;
        var call = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(node => node.Expression.ToString() == "Compute");
        var target = model.GetSymbolInfo(call).Symbol as IMethodSymbol;
        Assert.IsNotNull(target, "The conditional call must bind before editing, despite its missing condition.");
        Assert.AreEqual("AliasFirst", target.TypeArguments.Single().ContainingAssembly.Name);
        var originalMissing = diagnostics.Single(d => d.Id == "CS0103");
        Assert.AreSame(await document.GetSyntaxTreeAsync(), originalMissing.Location.SourceTree);
        Assert.IsTrue(originalMissing.Location.SourceSpan.Start > fixture.Source.IndexOf("Run()", StringComparison.Ordinal));
        await AssertLateAliasFactoryBindingsAsync(original, original, fixture.FactoryPath);
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(fixture.TargetPath, 3, fixture.Source.Split('\n')[2].IndexOf("Compute", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Name: "missing"), CancellationToken.None));
        Assert.AreEqual("The requested signature change introduces compiler diagnostic CS0103. Retain declaration grammar, parameter dependencies, and valid constructor initialization.", error.Message);
        Assert.AreEqual(0, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(fixture.Source, await File.ReadAllTextAsync(fixture.TargetPath));
        Assert.AreEqual(fixture.FactorySource, await File.ReadAllTextAsync(fixture.FactoryPath));
    }


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
