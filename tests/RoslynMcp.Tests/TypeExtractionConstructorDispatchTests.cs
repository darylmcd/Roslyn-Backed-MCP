using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TypeExtractionConstructorDispatchTests : TypeExtractionTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    /// <summary>
    /// extract-type-composition-assigned-before-use: the composition field used to be assigned at the END of
    /// every root constructor, so constructor code that reaches an extracted member through a retained
    /// helper dereferenced a null field. The previewed solution is emitted and run: <c>V</c> must be 7.
    /// </summary>
    [TestMethod]
    [DataRow("AssignFirstBlockFixture", "public AssignFirstBlockFixture() { _v = Init(); }")]
    [DataRow("AssignFirstExpressionFixture", "public AssignFirstExpressionFixture() => _v = Init();")]
    public async Task ExtractType_CompositionFieldAssignedBeforeConstructorCodeRuns(string typeName, string constructor)
    {
        var fixture = await CreateExtractionFixtureAsync(
            typeName + ".cs",
            $$"""
            namespace SampleLib;

            public class {{typeName}}
            {
                private int _v;
                public int V => _v;
                {{constructor}}
                private int Init() => Compute();
                private int Compute() => 7;
            }
            """);

        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, typeName, ["Compute"], "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);

            Assert.AreEqual(7, await EmitAndReadHostPropertyAsync(preview.PreviewToken, typeName, "ComputeHelper", "V"),
                "constructor code must observe an assigned composition field");
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("Read()", "public override int Read() => Compute();")]
    [DataRow("((IBaseRead)this).Read()", "int IBaseRead.Read() => Compute();")]
    [DataRow("Read()", "public override int Read() => Relay(); private int Relay() => Compute();")]
    [DataRow("Dispatch()", "public override int Read() => Compute();")]
    public async Task ExtractType_BaseConstructorDispatchToExtractedHelper_RefusesBeforeStorage(
        string baseCall, string implementation)
    {
        var fixture = await CreateExtractionFixtureAsync("BaseDispatchFixture.cs", $$"""
            namespace SampleLib;

            public interface IBaseRead { int Read(); }
            public class BaseDispatchFixture : IBaseRead
            {
                public BaseDispatchFixture() { _ = {{baseCall}}; }
                public virtual int Read() => 0;
                private int Dispatch() => Read();
            }
            public class DerivedDispatchFixture : BaseDispatchFixture, IBaseRead
            {
                {{implementation}}
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            var target = Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs");
            var exception = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DerivedDispatchFixture", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            Assert.IsTrue(exception.BlockingDependencies.Any(d => d.Member.Contains("Read", StringComparison.Ordinal)),
                string.Join("; ", exception.BlockingDependencies.Select(d => $"{d.Member}: {d.Reason}")));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(target));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorWithoutDispatch_AllowsRetainedOverride()
    {
        var fixture = await CreateExtractionFixtureAsync("SafeBaseFixture.cs", """
            namespace SampleLib;
            public class SafeBase { public SafeBase() { } public virtual int Read() => 0; }
            public class SafeDerived : SafeBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "SafeDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("this[0]", "public override int this[int index] => Compute();")]
    [DataRow("Read()", "public override int Read() => this[0]; public override int this[int index] => Compute();")]
    [DataRow("Read()", "public override int Read() => -this; public static int operator -(IndexerDerived value) => value.Compute();")]
    public async Task ExtractType_BaseConstructorIndexerDispatch_RefusesBeforeStorage(
        string baseCall, string implementation)
    {
        var fixture = await CreateExtractionFixtureAsync("IndexerDispatchFixture.cs", $$"""
            namespace SampleLib;
            public class IndexerBase
            {
                public IndexerBase() { _ = {{baseCall}}; }
                public virtual int Read() => 0;
                public virtual int this[int index] => 0;
            }
            public class IndexerDerived : IndexerBase
            {
                {{implementation}}
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            var exception = await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "IndexerDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            Assert.IsTrue(exception.BlockingDependencies.Count > 0);
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_UnselectedBaseConstructorDispatch_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("SelectedCtorFixture.cs", """
            namespace SampleLib;
            public class SelectedCtorBase
            {
                public SelectedCtorBase() { }
                public SelectedCtorBase(int value) { _ = Read(); }
                public virtual int Read() => 0;
            }
            public class SelectedCtorDerived : SelectedCtorBase
            {
                public SelectedCtorDerived() : base() { }
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "SelectedCtorDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("using (this) { }")]
    [DataRow("using (var owned = this) { }")]
    [DataRow("using var owned = this;")]
    public async Task ExtractType_BaseConstructorImplicitDispose_RefusesBeforeStorage(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("DisposeDispatchFixture.cs", $$"""
            namespace SampleLib;
            public class DisposeBase : System.IDisposable
            {
                public DisposeBase() { {{constructorBody}} }
                public virtual void Dispose() { }
            }
            public class DisposeDerived : DisposeBase
            {
                public override void Dispose() => Compute();
                private void Compute() { }
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DisposeDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("foreach (var item in this) { break; }")]
    [DataRow("ForeachBase resource = new ForeachBase(true); resource = this; foreach (var item in resource) { break; }")]
    public async Task ExtractType_BaseConstructorImplicitGetEnumerator_RefusesBeforeStorage(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("ForeachDispatchFixture.cs", $$"""
            namespace SampleLib;
            public class ForeachBase
            {
                public ForeachBase() { {{constructorBody}} }
                private ForeachBase(bool unused) { }
                public virtual System.Collections.Generic.IEnumerator<int> GetEnumerator()
                    => System.Linq.Enumerable.Empty<int>().GetEnumerator();
            }
            public class ForeachDerived : ForeachBase
            {
                public override System.Collections.Generic.IEnumerator<int> GetEnumerator()
                    => System.Linq.Enumerable.Repeat(Compute(), 1).GetEnumerator();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ForeachDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.FilePath)!, "ComputeHelper.cs")));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorDisposesNewBaseInstance_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("NewResourceFixture.cs", """
            namespace SampleLib;
            public class NewResourceBase : System.IDisposable
            {
                public NewResourceBase() { using (new NewResourceBase(true)) { } }
                private NewResourceBase(bool unused) { }
                public virtual void Dispose() { }
            }
            public class NewResourceDerived : NewResourceBase
            {
                public override void Dispose() => Compute();
                private void Compute() { }
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "NewResourceDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorDisposesReassignedCurrentInstance_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("ReassignedResourceFixture.cs", """
            namespace SampleLib;
            public class ReassignedResourceBase : System.IDisposable
            {
                public ReassignedResourceBase()
                {
                    ReassignedResourceBase resource = new ReassignedResourceBase(true);
                    resource = this;
                    using (resource) { }
                }
                private ReassignedResourceBase(bool unused) { }
                public virtual void Dispose() { }
            }
            public class ReassignedResourceDerived : ReassignedResourceBase
            {
                public override void Dispose() => Compute();
                private void Compute() { }
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ReassignedResourceDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    [DataRow("this", "public override bool MoveNext() => Compute() > 0;", true)]
    [DataRow("this", "public override int Current => Compute();", true)]
    [DataRow("this", "public override void Dispose() => _ = Compute();", true)]
    [DataRow("new SelfEnumeratorBase(true)", "public override bool MoveNext() => Compute() > 0;", false)]
    public async Task ExtractType_BaseConstructorEnumeratorMembers_RespectReceiverIdentity(
        string enumerator, string memberOverride, bool shouldRefuse)
    {
        var fixture = await CreateExtractionFixtureAsync("SelfEnumeratorFixture.cs", $$"""
            namespace SampleLib;
            public class SelfEnumeratorBase : System.Collections.Generic.IEnumerator<int>
            {
                public SelfEnumeratorBase() { foreach (var item in this) { break; } }
                private SelfEnumeratorBase(bool unused) { }
                public SelfEnumeratorBase GetEnumerator() => {{enumerator}};
                public virtual bool MoveNext() => true;
                public virtual int Current => 0;
                object System.Collections.IEnumerator.Current => Current;
                public void Reset() { }
                public virtual void Dispose() { }
            }
            public class SelfEnumeratorDerived : SelfEnumeratorBase
            {
                {{memberOverride}}
                private int Compute() => 7;
            }
            """);
        try
        {
            if (shouldRefuse)
            {
                var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
                await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                    TypeExtractionService.PreviewExtractTypeAsync(
                        fixture.WorkspaceId, fixture.FilePath, "SelfEnumeratorDerived", ["Compute"],
                        "ComputeHelper", null, CancellationToken.None));
                FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            }
            else
            {
                var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "SelfEnumeratorDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None);
                await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            }
        }
        finally
        {
            WorkspaceManager.Close(fixture.WorkspaceId);
        }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorCallsVirtualOnNewBase_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("NewReceiverFixture.cs", """
            namespace SampleLib;
            public class NewReceiverBase
            {
                public NewReceiverBase() { _ = new NewReceiverBase(true).Read(); }
                private NewReceiverBase(bool unused) { }
                public virtual int Read() => 0;
            }
            public class NewReceiverDerived : NewReceiverBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "NewReceiverDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("System.Func<int> unused = () => Read();")]
    [DataRow("int Local() => Read();")]
    [DataRow("_ = nameof(Read);")]
    public async Task ExtractType_UninvokedNestedBodiesAndNameof_AllowsPreview(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("UninvokedFixture.cs", $$"""
            namespace SampleLib;
            public class UninvokedBase
            {
                public UninvokedBase()
                {
                    {{constructorBody}}
                }
                public virtual int Read() => 0;
            }
            public class UninvokedDerived : UninvokedBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "UninvokedDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorImplicitAsyncDispose_RefusesBeforeStorage()
    {
        var fixture = await CreateExtractionFixtureAsync("AsyncDisposeFixture.cs", """
            namespace SampleLib;
            public class AsyncDisposeBase : System.IAsyncDisposable
            {
                public AsyncDisposeBase() { _ = InitAsync(); }
                private async System.Threading.Tasks.Task InitAsync() { await using (this) { } }
                public virtual System.Threading.Tasks.ValueTask DisposeAsync()
                    => System.Threading.Tasks.ValueTask.CompletedTask;
            }
            public class AsyncDisposeDerived : AsyncDisposeBase
            {
                public override System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    Compute();
                    return System.Threading.Tasks.ValueTask.CompletedTask;
                }
                private void Compute() { }
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "AsyncDisposeDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("foreach (var item in this) { break; }", "public System.Collections.Generic.IEnumerator<int> GetEnumerator() { _ = Read(); return System.Linq.Enumerable.Empty<int>().GetEnumerator(); } public void Dispose() { }")]
    [DataRow("using (this) { }", "public void Dispose() { _ = Read(); }")]
    public async Task ExtractType_ImplicitMemberBodyCallsOverride_RefusesBeforeStorage(
        string constructorBody, string implicitMember)
    {
        var fixture = await CreateExtractionFixtureAsync("ImplicitBodyFixture.cs", $$"""
            namespace SampleLib;
            public class ImplicitBodyBase : System.IDisposable
            {
                public ImplicitBodyBase() { {{constructorBody}} }
                public virtual int Read() => 0;
                {{implicitMember}}
            }
            public class ImplicitBodyDerived : ImplicitBodyBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ImplicitBodyDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorInvokedLambdaCallsOverride_RefusesBeforeStorage()
    {
        var fixture = await CreateExtractionFixtureAsync("InvokedLambdaFixture.cs", """
            namespace SampleLib;
            public class InvokedLambdaBase
            {
                public InvokedLambdaBase()
                {
                    System.Action action = () => _ = Read();
                    action();
                }
                public virtual int Read() => 0;
            }
            public class InvokedLambdaDerived : InvokedLambdaBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "InvokedLambdaDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("System.Action first = () => _ = Read(); System.Action second = first; second();")]
    [DataRow("System.Action first = () => { }; first = () => _ = Read(); System.Action second = first; second();")]
    [DataRow("Run(() => _ = Read());")]
    public async Task ExtractType_BaseConstructorInvokedDelegateAliasCallsOverride_Refuses(string constructorBody)
    {
        var fixture = await CreateExtractionFixtureAsync("DelegateAliasFixture.cs", $$"""
            namespace SampleLib;
            public class DelegateAliasBase
            {
                public DelegateAliasBase()
                {
                    {{constructorBody}}
                }
                private void Run(System.Action callback) => callback();
                public virtual int Read() => 0;
            }
            public class DelegateAliasDerived : DelegateAliasBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DelegateAliasDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("return callback();", true)]
    [DataRow("return 0;", false)]
    public async Task ExtractType_RetainedOverrideDelegateUse_RespectsInvocation(
        string returnStatement, bool shouldRefuse)
    {
        var fixture = await CreateExtractionFixtureAsync("RetainedDelegateFixture.cs", $$"""
            namespace SampleLib;
            public class RetainedDelegateBase
            {
                public RetainedDelegateBase() { _ = Read(); }
                public virtual int Read() => 0;
            }
            public class RetainedDelegateDerived : RetainedDelegateBase
            {
                public override int Read()
                {
                    System.Func<int> callback = () => Compute();
                    {{returnStatement}}
                }
                private int Compute() => 7;
            }
            """);
        try
        {
            if (shouldRefuse)
            {
                var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
                await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                    TypeExtractionService.PreviewExtractTypeAsync(
                        fixture.WorkspaceId, fixture.FilePath, "RetainedDelegateDerived", ["Compute"],
                        "ComputeHelper", null, CancellationToken.None));
                FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
            }
            else
            {
                var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "RetainedDelegateDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None);
                await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            }
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("action = () => action();", false)]
    [DataRow("action = () => { _ = Read(); action(); };", true)]
    public async Task ExtractType_RecursiveDelegateAnalysisTerminates(string assignment, bool shouldRefuse)
    {
        var fixture = await CreateExtractionFixtureAsync("RecursiveDelegateFixture.cs", $$"""
            namespace SampleLib;
            public class RecursiveDelegateBase
            {
                public RecursiveDelegateBase()
                {
                    System.Action action = () => { };
                    {{assignment}}
                    action();
                }
                public virtual int Read() => 0;
            }
            public class RecursiveDelegateDerived : RecursiveDelegateBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            if (shouldRefuse)
            {
                await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                    TypeExtractionService.PreviewExtractTypeAsync(
                        fixture.WorkspaceId, fixture.FilePath, "RecursiveDelegateDerived", ["Compute"],
                        "ComputeHelper", null, CancellationToken.None));
            }
            else
            {
                var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "RecursiveDelegateDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None);
                await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
            }
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorInvokesSafeDelegateParameter_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("SafeDelegateParameterFixture.cs", """
            namespace SampleLib;
            public class SafeDelegateParameterBase
            {
                public SafeDelegateParameterBase() { Run(() => { }); }
                private void Run(System.Action callback) => callback();
                public virtual int Read() => 0;
            }
            public class SafeDelegateParameterDerived : SafeDelegateParameterBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "SafeDelegateParameterDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorReassignsDelegateParameter_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("ReassignedDelegateParameterFixture.cs", """
            namespace SampleLib;
            public class ReassignedDelegateParameterBase
            {
                public ReassignedDelegateParameterBase() { Run(() => { }); }
                private void Run(System.Action callback)
                {
                    callback = () => _ = Read();
                    callback();
                }
                public virtual int Read() => 0;
            }
            public class ReassignedDelegateParameterDerived : ReassignedDelegateParameterBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "ReassignedDelegateParameterDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorReassignsDelegateAfterInvocation_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("LateDelegateParameterFixture.cs", """
            namespace SampleLib;
            public class LateDelegateParameterBase
            {
                public LateDelegateParameterBase() { Run(() => { }); }
                private void Run(System.Action callback)
                {
                    callback();
                    callback = () => _ = Read();
                }
                public virtual int Read() => 0;
            }
            public class LateDelegateParameterDerived : LateDelegateParameterBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "LateDelegateParameterDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorLoopReassignsDelegateBeforeNextCall_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("LoopDelegateParameterFixture.cs", """
            namespace SampleLib;
            public class LoopDelegateParameterBase
            {
                public LoopDelegateParameterBase() { Run(() => true); }
                private void Run(System.Func<bool> callback)
                {
                    while (callback() && (callback = () => Read() > 0) != null) { }
                }
                public virtual int Read() => 0;
            }
            public class LoopDelegateParameterDerived : LoopDelegateParameterBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "LoopDelegateParameterDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorRecursiveCallUsesReassignedDelegate_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("RecursiveParameterFixture.cs", """
            namespace SampleLib;
            public class RecursiveParameterBase
            {
                public RecursiveParameterBase() { Run(() => { }); }
                private void Run(System.Action callback)
                {
                    callback();
                    callback = () => _ = Read();
                    Run(callback);
                }
                public virtual int Read() => 0;
            }
            public class RecursiveParameterDerived : RecursiveParameterBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "RecursiveParameterDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorUnrelatedCallAfterDelegateWrite_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("UnrelatedDelegateCallFixture.cs", """
            namespace SampleLib;
            public class UnrelatedDelegateCallBase
            {
                public UnrelatedDelegateCallBase() { Run(() => { }); }
                private void Run(System.Action callback)
                {
                    callback();
                    callback = () => _ = Read();
                    System.Console.WriteLine("done");
                }
                public virtual int Read() => 0;
            }
            public class UnrelatedDelegateCallDerived : UnrelatedDelegateCallBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "UnrelatedDelegateCallDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("new System.Collections.Generic.List<int> { 0 }.ForEach(_ => Run(callback));")]
    [DataRow("System.Action<int> again = _ => Run(callback); new System.Collections.Generic.List<int> { 0 }.ForEach(again);")]
    public async Task ExtractType_BaseConstructorMetadataCallbackReentersWithChangedDelegate_Refuses(
        string callbackCall)
    {
        var fixture = await CreateExtractionFixtureAsync("MetadataCallbackFixture.cs", $$"""
            namespace SampleLib;
            public class MetadataCallbackBase
            {
                public MetadataCallbackBase() { Run(() => { }); }
                private void Run(System.Action callback)
                {
                    callback();
                    callback = () => _ = Read();
                    {{callbackCall}}
                }
                public virtual int Read() => 0;
            }
            public class MetadataCallbackDerived : MetadataCallbackBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "MetadataCallbackDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorOpaqueCallReceivesThis_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("OpaqueThisFixture.cs", """
            namespace SampleLib;
            public class OpaqueThisBase
            {
                public OpaqueThisBase() { System.Console.WriteLine(this); }
            }
            public class OpaqueThisDerived : OpaqueThisBase
            {
                public override string ToString() => Compute();
                private string Compute() => "value";
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "OpaqueThisDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorUnrelatedMetadataCallback_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("UnrelatedMetadataCallbackFixture.cs", """
            namespace SampleLib;
            public class UnrelatedMetadataCallbackBase
            {
                public UnrelatedMetadataCallbackBase() { Run(() => { }); }
                private void Run(System.Action callback)
                {
                    callback();
                    callback = () => _ = Read();
                    new System.Collections.Generic.List<int> { 0 }.ForEach(_ => { });
                }
                public virtual int Read() => 0;
            }
            public class UnrelatedMetadataCallbackDerived : UnrelatedMetadataCallbackBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "UnrelatedMetadataCallbackDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorOpaqueDelegateAliasCallsOverride_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("OpaqueDelegateAliasFixture.cs", """
            namespace SampleLib;
            public class OpaqueDelegateAliasBase
            {
                public OpaqueDelegateAliasBase()
                {
                    System.Action<int> callback = _ => { _ = Read(); };
                    new System.Collections.Generic.List<int> { 0 }.ForEach(callback);
                }
                public virtual int Read() => 0;
            }
            public class OpaqueDelegateAliasDerived : OpaqueDelegateAliasBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "OpaqueDelegateAliasDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorOpaqueCarrierOfThis_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("OpaqueCarrierFixture.cs", """
            namespace SampleLib;
            public sealed class OpaqueCarrier
            {
                private readonly OpaqueCarrierBase _owner;
                public OpaqueCarrier(OpaqueCarrierBase owner) { _owner = owner; }
                public override string ToString() => _owner.Read().ToString();
            }
            public class OpaqueCarrierBase
            {
                public OpaqueCarrierBase() { System.Console.WriteLine(new OpaqueCarrier(this)); }
                public virtual int Read() => 0;
            }
            public class OpaqueCarrierDerived : OpaqueCarrierBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "OpaqueCarrierDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorRepeatedSafeLocalInCarrier_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("RepeatedSafeCarrierFixture.cs", """
            namespace SampleLib;
            public sealed class SafeCarrier
            {
                private readonly RepeatedSafeBase _first;
                private readonly RepeatedSafeBase _second;
                public SafeCarrier(RepeatedSafeBase first, RepeatedSafeBase second)
                {
                    _first = first;
                    _second = second;
                }
                public override string ToString() => (_first.Read() + _second.Read()).ToString();
            }
            public class RepeatedSafeBase
            {
                public RepeatedSafeBase()
                {
                    RepeatedSafeBase other = new RepeatedSafeBase(true);
                    System.Console.WriteLine(new SafeCarrier(other, other));
                }
                private RepeatedSafeBase(bool unused) { }
                public virtual int Read() => 0;
            }
            public class RepeatedSafeDerived : RepeatedSafeBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "RepeatedSafeDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorCollectionCarrierOfThis_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("CollectionCarrierFixture.cs", """
            namespace SampleLib;
            public class CollectionCarrierBase
            {
                public CollectionCarrierBase()
                {
                    _ = string.Join<CollectionCarrierBase>(",",
                        new System.Collections.Generic.List<CollectionCarrierBase> { this });
                }
            }
            public class CollectionCarrierDerived : CollectionCarrierBase
            {
                public override string ToString() => Compute();
                private string Compute() => "value";
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "CollectionCarrierDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("this.Any()")]
    [DataRow("System.Linq.Enumerable.Any(this)")]
    public async Task ExtractType_BaseConstructorOpaqueEnumerableCall_Refuses(string call)
    {
        var fixture = await CreateExtractionFixtureAsync("OpaqueEnumerableFixture.cs", $$"""
            using System.Linq;
            namespace SampleLib;
            public class OpaqueEnumerableBase : System.Collections.Generic.IEnumerable<int>
            {
                public OpaqueEnumerableBase() { _ = {{call}}; }
                public virtual System.Collections.Generic.IEnumerator<int> GetEnumerator()
                    => System.Linq.Enumerable.Empty<int>().GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public class OpaqueEnumerableDerived : OpaqueEnumerableBase
            {
                public override System.Collections.Generic.IEnumerator<int> GetEnumerator()
                    => System.Linq.Enumerable.Repeat(Compute(), 1).GetEnumerator();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "OpaqueEnumerableDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorUninvokedMethodGroup_AllowsPreview()
    {
        var fixture = await CreateExtractionFixtureAsync("UninvokedMethodGroupFixture.cs", """
            namespace SampleLib;
            public class UninvokedMethodGroupBase
            {
                public UninvokedMethodGroupBase() { System.Func<int> unused = Read; }
                public virtual int Read() => 0;
            }
            public class UninvokedMethodGroupDerived : UninvokedMethodGroupBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var preview = await TypeExtractionService.PreviewExtractTypeAsync(
                fixture.WorkspaceId, fixture.FilePath, "UninvokedMethodGroupDerived", ["Compute"],
                "ComputeHelper", null, CancellationToken.None);
            await AssertModifiedSolutionCompilesAsync(preview.PreviewToken);
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    public async Task ExtractType_BaseConstructorDynamicAliasDispatch_Refuses()
    {
        var fixture = await CreateExtractionFixtureAsync("DynamicAliasFixture.cs", """
            namespace SampleLib;
            public class DynamicAliasBase
            {
                public DynamicAliasBase() { dynamic target = this; _ = target.Read(); }
                public virtual int Read() => 0;
            }
            public class DynamicAliasDerived : DynamicAliasBase
            {
                public override int Read() => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DynamicAliasDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }

    [TestMethod]
    [DataRow("target.Value")]
    [DataRow("target[0]")]
    public async Task ExtractType_BaseConstructorDynamicPropertyDispatch_Refuses(string access)
    {
        var fixture = await CreateExtractionFixtureAsync("DynamicPropertyFixture.cs", $$"""
            namespace SampleLib;
            public class DynamicPropertyBase
            {
                public DynamicPropertyBase() { dynamic target = this; _ = {{access}}; }
                public virtual int Value => 0;
                public virtual int this[int index] => 0;
            }
            public class DynamicPropertyDerived : DynamicPropertyBase
            {
                public override int Value => Compute();
                public override int this[int index] => Compute();
                private int Compute() => 7;
            }
            """);
        try
        {
            var version = WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId);
            await Assert.ThrowsExactlyAsync<ExtractTypeBlockingDependencyException>(() =>
                TypeExtractionService.PreviewExtractTypeAsync(
                    fixture.WorkspaceId, fixture.FilePath, "DynamicPropertyDerived", ["Compute"],
                    "ComputeHelper", null, CancellationToken.None));
            FileOperationIntegrationTests.AssertNoStoredPreviews(PreviewStore, fixture.WorkspaceId);
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(fixture.WorkspaceId));
        }
        finally { WorkspaceManager.Close(fixture.WorkspaceId); }
    }
}
