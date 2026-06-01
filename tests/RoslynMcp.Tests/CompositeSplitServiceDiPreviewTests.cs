using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Exercises the <c>split_service_with_di_preview</c> composite preset on
/// <see cref="SymbolRefactorService"/>. Covers the happy path (two public methods split into
/// two partitions with forwarding facade + DI-registration rewrite) plus the fallback when no
/// DI registration is discoverable.
/// </summary>
[TestClass]
public sealed class CompositeSplitServiceDiPreviewTests : IsolatedWorkspaceTestBase
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
    public async Task Split_Service_With_Di_Preview_Emits_Partitions_Facade_And_Di_Registration_Rewrite()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var serviceFilePath = workspace.GetPath("SampleLib", "FooService.cs");
        await File.WriteAllTextAsync(
            serviceFilePath,
            "namespace SampleLib;\n\npublic class FooService\n{\n    public string Alpha(string input) => input + \"-alpha\";\n\n    public string Beta(string input) => input + \"-beta\";\n}\n",
            CancellationToken.None);

        var registrationsFile = workspace.GetPath("SampleApp", "SplitServiceRegistration.cs");
        await File.WriteAllTextAsync(
            registrationsFile,
            "using Microsoft.Extensions.DependencyInjection;\nusing SampleLib;\n\npublic static class SplitServiceRegistration\n{\n    public static void AddServices(IServiceCollection services)\n    {\n        services.AddTransient<FooService>();\n    }\n}\n",
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);
        var preview = await service.PreviewSplitServiceWithDiAsync(
            workspace.WorkspaceId,
            serviceFilePath,
            "FooService",
            new[]
            {
                new SplitServicePartition("FooServiceA", new[] { "Alpha" }),
                new SplitServicePartition("FooServiceB", new[] { "Beta" }),
            },
            hostRegistrationFile: registrationsFile,
            CancellationToken.None);

        Assert.IsNull(preview.Warnings, "No warnings expected when the host registration file is supplied and contains a matching registration.");

        await ApplyMutationsAsync(compositeStore, preview.PreviewToken, CancellationToken.None);

        var partitionAContents = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "FooServiceA.cs"), CancellationToken.None);
        var partitionBContents = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "FooServiceB.cs"), CancellationToken.None);
        StringAssert.Contains(partitionAContents, "class FooServiceA",
            "Partition A file must declare FooServiceA.");
        StringAssert.Contains(partitionAContents, "Alpha(string input)",
            "Partition A must carry the Alpha method implementation.");
        StringAssert.Contains(partitionBContents, "class FooServiceB",
            "Partition B file must declare FooServiceB.");
        StringAssert.Contains(partitionBContents, "Beta(string input)",
            "Partition B must carry the Beta method implementation.");

        var facadeContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);
        StringAssert.Contains(facadeContents, "private readonly FooServiceA",
            "Facade must hold the partition-A implementation in a private readonly field.");
        StringAssert.Contains(facadeContents, "private readonly FooServiceB",
            "Facade must hold the partition-B implementation in a private readonly field.");
        StringAssert.Contains(facadeContents, "public FooService(FooServiceA",
            "Facade must inject partitions through its public constructor.");
        StringAssert.Contains(facadeContents, "_fooServiceA.Alpha(input)",
            "Facade must forward Alpha to the injected partition-A implementation.");
        StringAssert.Contains(facadeContents, "_fooServiceB.Beta(input)",
            "Facade must forward Beta to the injected partition-B implementation.");

        var registrationsContents = await File.ReadAllTextAsync(registrationsFile, CancellationToken.None);
        StringAssert.Contains(registrationsContents, "services.AddTransient<FooServiceA>()",
            "Host registration must register partition A with the inferred lifetime.");
        StringAssert.Contains(registrationsContents, "services.AddTransient<FooServiceB>()",
            "Host registration must register partition B with the inferred lifetime.");
        StringAssert.Contains(registrationsContents, "services.AddTransient<FooService>()",
            "Host registration must keep the facade available so existing consumers keep working.");
    }

    [TestMethod]
    public async Task Split_Service_With_Di_Preview_Migrates_Fields_And_Strips_Async_From_Forwarding_Stubs()
    {
        // Regression for split-service-with-di-broken-output (gh #766): a service with private
        // instance fields plus async ValueTask methods was emitting non-compiling code.
        //   - Bug 1: partition class lacked the `_channel` field referenced by its migrated
        //     method body → CS0103 'name does not exist in the current context'.
        //   - Bug 2: facade's forwarding stub copied `async` verbatim, producing
        //     `async ValueTask Foo() { return _field.Foo(...); }` → CS4016.
        // This test reproduces the JobQueue shape (Channel<int> field + async ValueTask method
        // + sync TryDequeue) and pins both fixes.
        await using var workspace = CreateIsolatedWorkspaceCopy();

        var serviceFilePath = workspace.GetPath("SampleLib", "JobQueue.cs");
        await File.WriteAllTextAsync(
            serviceFilePath,
            "using System.Threading.Channels;\n" +
            "using System.Threading.Tasks;\n" +
            "\n" +
            "namespace SampleLib;\n" +
            "\n" +
            "public class JobQueue\n" +
            "{\n" +
            "    private readonly Channel<int> _channel;\n" +
            "\n" +
            "    public JobQueue(Channel<int> channel)\n" +
            "    {\n" +
            "        _channel = channel;\n" +
            "    }\n" +
            "\n" +
            "    public async ValueTask EnqueueAsync(int item)\n" +
            "    {\n" +
            "        await _channel.Writer.WriteAsync(item);\n" +
            "    }\n" +
            "\n" +
            "    public bool TryDequeue(out int item)\n" +
            "    {\n" +
            "        return _channel.Reader.TryRead(out item);\n" +
            "    }\n" +
            "}\n",
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);
        var preview = await service.PreviewSplitServiceWithDiAsync(
            workspace.WorkspaceId,
            serviceFilePath,
            "JobQueue",
            new[]
            {
                new SplitServicePartition("JobQueueWriter", new[] { "EnqueueAsync" }),
                new SplitServicePartition("JobQueueReader", new[] { "TryDequeue" }),
            },
            hostRegistrationFile: null,
            CancellationToken.None);

        await ApplyMutationsAsync(compositeStore, preview.PreviewToken, CancellationToken.None);

        var writerPartition = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "JobQueueWriter.cs"), CancellationToken.None);
        var readerPartition = await File.ReadAllTextAsync(workspace.GetPath("SampleLib", "JobQueueReader.cs"), CancellationToken.None);
        var facadeContents = await File.ReadAllTextAsync(serviceFilePath, CancellationToken.None);

        // Bug 1 — field migration: both partitions touch `_channel`, so both must declare it.
        // The shared field is duplicated (not split exclusively into one partition) — see the
        // Sonnet handoff "shared field referenced by methods in two different partitions" note.
        StringAssert.Contains(writerPartition, "private readonly Channel<int> _channel",
            "Writer partition must migrate the _channel field referenced by EnqueueAsync.");
        StringAssert.Contains(readerPartition, "private readonly Channel<int> _channel",
            "Reader partition must also migrate the shared _channel field referenced by TryDequeue.");

        // Each partition gets a synthesized ctor that accepts and assigns its migrated fields.
        StringAssert.Contains(writerPartition, "public JobQueueWriter(Channel<int> channel)",
            "Writer partition needs a constructor taking the migrated field as a parameter.");
        StringAssert.Contains(writerPartition, "_channel = channel",
            "Writer partition ctor body must assign the constructor parameter to the field.");
        StringAssert.Contains(readerPartition, "public JobQueueReader(Channel<int> channel)",
            "Reader partition needs a constructor taking the migrated field as a parameter.");

        // The partition keeps the original `async` modifier on its method (it does the awaiting).
        StringAssert.Contains(writerPartition, "async ValueTask EnqueueAsync",
            "Writer partition method must retain its async modifier — the partition does the awaiting.");

        // Bug 2 — async fix: facade forwarding stub for EnqueueAsync must NOT carry `async`,
        // because its body is sync `return _field.EnqueueAsync(item);` with no `await`.
        // Constructing the assertion against the EnqueueAsync stub specifically (not the whole
        // file) lets us catch a regression even if some other async method appears nearby.
        var enqueueIndex = facadeContents.IndexOf("EnqueueAsync", StringComparison.Ordinal);
        Assert.IsTrue(enqueueIndex > 0, "Facade should contain an EnqueueAsync forwarding stub.");
        var enqueueStubWindow = facadeContents.Substring(
            Math.Max(0, enqueueIndex - 60), Math.Min(120, facadeContents.Length - Math.Max(0, enqueueIndex - 60)));
        Assert.IsFalse(enqueueStubWindow.Contains("async ", StringComparison.Ordinal),
            $"Facade's EnqueueAsync forwarding stub must NOT carry the async modifier (would trigger CS4016). Window: {enqueueStubWindow}");

        // Facade must still forward correctly.
        StringAssert.Contains(facadeContents, "_jobQueueWriter.EnqueueAsync(item)",
            "Facade must forward EnqueueAsync to the writer partition.");
        StringAssert.Contains(facadeContents, "_jobQueueReader.TryDequeue(item)",
            "Facade must forward TryDequeue to the reader partition.");

        // Facade should NOT re-emit the migrated _channel field — state lives on the partitions.
        Assert.IsFalse(facadeContents.Contains("Channel<int> _channel", StringComparison.Ordinal),
            "Facade must not re-emit the migrated _channel field; state belongs to the partitions.");
    }

    [TestMethod]
    public async Task Split_Service_With_Di_Preview_Returns_Warning_When_Registration_Not_Found()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var serviceFilePath = workspace.GetPath("SampleLib", "BarService.cs");
        await File.WriteAllTextAsync(
            serviceFilePath,
            "namespace SampleLib;\n\npublic class BarService\n{\n    public int Add(int a, int b) => a + b;\n\n    public int Subtract(int a, int b) => a - b;\n}\n",
            CancellationToken.None);

        await workspace.LoadAsync(CancellationToken.None);

        var compositeStore = new CompositePreviewStore();
        var service = CreateSymbolRefactorService(compositeStore);
        var preview = await service.PreviewSplitServiceWithDiAsync(
            workspace.WorkspaceId,
            serviceFilePath,
            "BarService",
            new[]
            {
                new SplitServicePartition("BarServiceAdd", new[] { "Add" }),
                new SplitServicePartition("BarServiceSubtract", new[] { "Subtract" }),
            },
            hostRegistrationFile: null,
            CancellationToken.None);

        Assert.IsNotNull(preview.Warnings, "A warning must be attached when no DI registration is discoverable.");
        Assert.IsTrue(
            preview.Warnings!.Any(warning => warning.Contains("No DI registration", StringComparison.Ordinal)),
            "Fallback warning must mention the missing registration.");

        await ApplyMutationsAsync(compositeStore, preview.PreviewToken, CancellationToken.None);

        // Partition and facade mutations still apply so the preview is useful even without DI.
        Assert.IsTrue(File.Exists(workspace.GetPath("SampleLib", "BarServiceAdd.cs")));
        Assert.IsTrue(File.Exists(workspace.GetPath("SampleLib", "BarServiceSubtract.cs")));
    }

    private static SymbolRefactorService CreateSymbolRefactorService(CompositePreviewStore compositeStore)
    {
        var restructureService = new RestructureService(WorkspaceManager, PreviewStore);
        return new SymbolRefactorService(
            WorkspaceManager,
            RefactoringService,
            EditService,
            restructureService,
            compositeStore,
            DiRegistrationService);
    }

    private static async Task ApplyMutationsAsync(CompositePreviewStore store, string token, CancellationToken ct)
    {
        var entry = store.Retrieve(token)
            ?? throw new AssertFailedException($"Composite preview token '{token}' not found.");
        var (_, _, _, mutations) = entry;
        foreach (var mutation in mutations)
        {
            if (mutation.DeleteFile)
            {
                if (File.Exists(mutation.FilePath))
                {
                    File.Delete(mutation.FilePath);
                }
                continue;
            }

            var directory = Path.GetDirectoryName(mutation.FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(mutation.FilePath, mutation.UpdatedContent ?? string.Empty, ct).ConfigureAwait(false);
        }
    }
}
