using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeSemanticContextCurrentReceiptTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize] public static void Initialize(TestContext _) => InitializeServices();

    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("deny")]
    [DataRow("change")]
    public async Task SemanticMismatch_RechecksTheFailedContextsNestedResponseReceipt(string action)
    {
        var source = await CreateNativeProjectFixtureAsync("Owner", CancellationToken.None);
        Exception? primary = null;
        EventHandler<FirstChanceExceptionEventArgs>? handler = null;
        try
        {
            var projectPath = source.GetPath("Owner", "Owner.csproj");
            var nested = source.GetPath("Owner", "nested.rsp");
            await File.WriteAllTextAsync(source.GetPath("Owner", "outer.rsp"), "@nested.rsp\n");
            await File.WriteAllTextAsync(nested, "/define:NESTED_RECEIPT_CONTEXT\n");
            await File.WriteAllTextAsync(source.GetPath("Owner", "Entry.cs"), "public class Entry { }");
            var xml = XDocument.Load(projectPath);
            xml.Root!.Add(new XElement("PropertyGroup", new XElement("CompilerResponseFile", "outer.rsp")),
                new XElement("ItemGroup", new XElement("None", new XAttribute("Remove", "*.rsp"))),
                new XElement("PropertyGroup", new XAttribute("Condition", "'$(SolutionName)' == 'Fixture'"),
                    new XElement("DefineConstants", "$(DefineConstants);NATIVE_GRAPH_CONTEXT")));
            await File.WriteAllTextAsync(projectPath, xml.ToString());
            await RestoreWorkspaceAsync(source);
            var graph = EvaluatedBuildGraph.FromRoots([source.SolutionPath],
                OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string>()),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            Assert.IsFalse(graph.Nodes.SelectMany(node => node.DocumentFiles.Concat(node.ArtifactFiles)
                .Concat(node.PathEvidence.InputFiles)).Contains(nested, FileSystemPath.Comparer),
                "The nested response input must be protected by the failed parser receipt, independently of ordinary native graph inputs.");
            var inner = await NativeSdkRestoreFixture.CreatePolicyAsync(source.SolutionPath, CancellationToken.None);
            var armed = 0;
            var acted = 0;
            var denial = new UnauthorizedAccessException("Nested response grant revoked at semantic publication.");
            handler = (_, args) =>
            {
                if (args.Exception is NativeSemanticContextMismatchException mismatch
                    && mismatch.Parsing.Any(receipt => receipt.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, nested))
                        && receipt.Arguments.ParseOptions.PreprocessorSymbolNames.Contains("NESTED_RECEIPT_CONTEXT")
                        && receipt.Arguments.ParseOptions.PreprocessorSymbolNames.Contains("NATIVE_GRAPH_CONTEXT")))
                    Volatile.Write(ref armed, 1);
            };
            AppDomain.CurrentDomain.FirstChanceException += handler;
            var policy = new CurrentResponsePolicy(inner, async (paths, ct) =>
            {
                if (action == "unchanged" || Volatile.Read(ref armed) == 0
                    || !paths.Contains(nested, FileSystemPath.Comparer)
                    || Interlocked.CompareExchange(ref acted, 1, 0) != 0) return;
                if (action == "deny") throw denial;
                await File.AppendAllTextAsync(nested, "/define:CHANGED_AFTER_MISMATCH\n", ct);
            });
            async Task LoadAsync()
            {
                var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("response-closing-" + action,
                    source.SolutionPath, OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string>()),
                    policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
                Exception? loadPrimary = null;
                try
                {
                    Assert.AreEqual(NativeCompilerContextAvailability.SemanticContextMismatch, loaded.Context.Availability);
                    var preserved = (await loaded.Context.NormalizeAsync("response-closing-" + action, policy, CancellationToken.None)).Solution;
                    Assert.AreEqual(1, preserved.Projects.Count());
                    Assert.IsTrue(preserved.Projects.Single().Documents.Any(document => document.FilePath == source.GetPath("Owner", "Entry.cs")));
                    var compilation = await preserved.Projects.Single().GetCompilationAsync();
                    Assert.IsNotNull(compilation);
                    Assert.IsFalse(compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
                }
                catch (Exception failure) { loadPrimary = failure; throw; }
                finally { OwnedResourceCleanup.Dispose("Response mismatch native load cleanup failed.", loadPrimary, loaded.Workspace, loaded.Lease); }
            }
            if (action == "deny") Assert.AreSame(denial, await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(LoadAsync));
            else if (action == "change") await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(LoadAsync);
            else await LoadAsync();
            Assert.AreEqual(1, Volatile.Read(ref armed), "The real native mismatch must carry this exact nested response receipt and compiler symbols.");
            Assert.AreEqual(action == "unchanged" ? 0 : 1, Volatile.Read(ref acted));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            if (handler is not null) AppDomain.CurrentDomain.FirstChanceException -= handler;
            await CleanupFailureCollector.RunAfterFailureAsync("Response mismatch fixture cleanup failed.", primary, () => source.DisposeAsync());
        }
    }

    private sealed class CurrentResponsePolicy(IWorkspacePathPolicy inner,
        Func<IReadOnlyList<string>, CancellationToken, Task> afterAuthorize) : IWorkspacePathPolicy
    {
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await inner.ValidateAsync(workspaceId, paths, ct);
            await afterAuthorize(paths, ct);
        }
    }
}
