using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;
using static RoslynMcp.Tests.TestInfrastructure.InterfaceExtractionTestContext;

namespace RoslynMcp.Tests;

/// <summary>
/// The generated interface is parsed under the receiving project's language version. A member whose emitted signature
/// needs a newer language than the receiver (collection params, <c>ref readonly</c>, <c>scoped</c>, function pointers)
/// must be refused before any token or file exists, without touching the receiver's LangVersion; a compatible receiver and
/// ordinary-array controls must keep working. Refusal rows are red on the old code (a token for an uncompilable interface was
/// stored); accept and control rows are guards that pass before and after.
/// </summary>
[TestClass]
public sealed class InterfaceReceivingLanguageRouteTests : InterfaceRouteTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private const string ConsumerSource = """
        namespace Contracts
        {
            public class LangConsumer
            {
                public LangConsumer(LangService service) { }
            }
        }
        """;

    // Block-scoped namespace: the source project may itself be pinned below C# 10.
    private static string ServiceSource(string members) => $$"""
        using System;

        namespace Contracts
        {
            public {{(members.Contains("delegate*", StringComparison.Ordinal) ? "unsafe " : "")}}class LangService
            {
                {{members}}
            }
        }
        """;

    /// <summary>Feature name, service members, source LangVersion, refused receiver LangVersion, pinned compiler ids.</summary>
    private sealed record Feature(string Members, string SourceVersion, string RefusedVersion, string[] Ids)
    {
        internal bool Unsafe => Members.Contains("delegate*", StringComparison.Ordinal);
    }

    // Ids are the compiler's own, pinned from a probe of the emitted declaration under the source vs receiving options.
    private static readonly Dictionary<string, Feature> Features = new()
    {
        ["params-collection"] = new("public int Sum(params ReadOnlySpan<int> values) => values.Length;", "13", "12", ["CS9202"]),
        ["ref-readonly"] = new("public int Read(ref readonly int value) => value;", "12", "11", ["CS9058"]),
        ["scoped-ref"] = new("public int Scoped(scoped ref int value) => value;", "11", "10", ["CS8936"]),
        ["scoped-span"] = new("public int Scoped(scoped Span<int> value) => value.Length;", "11", "10", ["CS8936"]),
        ["function-pointer"] = new("public void Call(delegate*<int, void> callback) { }", "9", "8", ["CS8400"]),
    };

    public static IEnumerable<object[]> RefusalRows() =>
        from route in new[] { "cross-project", "dependency-inversion", "extract-and-wire" }
        from feature in Features.Keys
        select new object[] { route, feature };

    public static IEnumerable<object[]> AcceptRows() =>
        from route in new[] { "standalone", "cross-project", "dependency-inversion", "extract-and-wire" }
        from feature in new[] { "params-collection", "ref-readonly", "scoped-ref", "scoped-span", "function-pointer" }
        select new object[] { route, feature };

    public static IEnumerable<object[]> ControlRows() =>
        from route in new[] { "standalone", "cross-project", "dependency-inversion", "extract-and-wire" }
        from receiver in new[] { "7.3", "9" }
        select new object[] { route, receiver };

    public static IEnumerable<object[]> MixedRoutes() =>
        new[] { "cross-project", "dependency-inversion", "extract-and-wire" }.Select(r => new object[] { r });

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Properties(
        string sourceVersion, string receiverVersion, bool allowUnsafe, bool nullable = true)
    {
        // ImplicitUsings emits `global using` (C# 10) into the project, which a project pinned below 10 cannot compile.
        var lib = new Dictionary<string, string> { ["LangVersion"] = sourceVersion, ["ImplicitUsings"] = "disable" };
        var contracts = new Dictionary<string, string> { ["LangVersion"] = receiverVersion, ["ImplicitUsings"] = "disable" };
        if (allowUnsafe)
        {
            lib["AllowUnsafeBlocks"] = "true";
            contracts["AllowUnsafeBlocks"] = "true";
        }

        // Nullable annotations need C# 8: a project pinned to 7.3 cannot also enable them.
        if (!nullable)
        {
            lib["Nullable"] = "disable";
            contracts["Nullable"] = "disable";
        }

        return new Dictionary<string, IReadOnlyDictionary<string, string>> { ["SampleLib"] = lib, ["Contracts"] = contracts };
    }

    private static Task<string> PrepareAsync(IsolatedWorkspaceScope workspace, string members, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> properties) =>
        PrepareInterfaceRouteFixtureAsync(workspace, "LangService.cs", ServiceSource(members), "LangConsumer.cs", ConsumerSource, projectProperties: properties);

    private static async Task AssertRefusedAsync(IsolatedWorkspaceScope workspace, string route, string sourcePath, string[] expectedIds)
    {
        var files = Snapshot(workspace.RootPath);
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var receiverOptions = before.Projects.Single(p => p.Name == "Contracts").ParseOptions;
        var tracking = (TrackingPreviewStore)PreviewStore;
        var ordinaryCount = tracking.GetRedeemableTokens(workspace.WorkspaceId).Count;
        var stores = new CompositePreviewStore();

        var error = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(
            () => PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "LangService", stores));

        var ids = Regex.Matches(error.Message, @"CS\d{4}").Select(m => m.Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(expectedIds, ids, error.Message);
        Assert.IsFalse(error.Message.Contains(sourcePath, StringComparison.Ordinal), error.Message);
        Assert.IsFalse(error.Message.Contains(workspace.RootPath, StringComparison.Ordinal), error.Message);
        Assert.IsFalse(error.Message.Contains("is not available", StringComparison.Ordinal), error.Message);
        Assert.AreEqual(ordinaryCount, tracking.GetRedeemableTokens(workspace.WorkspaceId).Count);
        Assert.AreEqual(0, StoreCount(stores));
        AssertSnapshot(files);
        Assert.IsFalse(File.Exists(workspace.GetPath("Contracts", "ILangService.cs")));
        Assert.AreSame(before, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.AreEqual(receiverOptions, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.Single(p => p.Name == "Contracts").ParseOptions);
    }

    private static async Task<Solution> AcceptedSolutionAsync(IsolatedWorkspaceScope workspace, string route, string sourcePath)
    {
        var before = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var stores = new CompositePreviewStore();
        var preview = await PreviewInterfaceRouteAsync(route, workspace.WorkspaceId, sourcePath, "LangService", stores);
        var pending = route == "extract-and-wire"
            ? SerializedCompositePreview(before, stores.Retrieve(preview.PreviewToken, null)!.Value.Mutations)
            : PreviewStore.Retrieve(preview.PreviewToken)!.Value.ModifiedSolution;
        pending = await ReparseSerializedDocumentsAsync(pending);
        await AssertCompilesAsync(pending);
        return pending;
    }

    [TestMethod]
    [DynamicData(nameof(RefusalRows))]
    public async Task Interface_Needing_A_Newer_Language_Than_The_Receiver_Is_Refused_Before_Publication(string route, string feature)
    {
        var spec = Features[feature];
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareAsync(workspace, spec.Members, Properties(spec.SourceVersion, spec.RefusedVersion, spec.Unsafe));

        await AssertRefusedAsync(workspace, route, sourcePath, spec.Ids);
    }

    [TestMethod]
    [DynamicData(nameof(AcceptRows))]
    public async Task Interface_Is_Emitted_When_The_Receiver_Language_Supports_It(string route, string feature)
    {
        var spec = Features[feature];
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareAsync(workspace, spec.Members, Properties(spec.SourceVersion, spec.SourceVersion, spec.Unsafe));

        var pending = await AcceptedSolutionAsync(workspace, route, sourcePath);

        var receiver = route == "standalone" ? "SampleLib" : "Contracts";
        Assert.IsTrue(pending.Projects.Single(p => p.Name == receiver).Documents.Any(d => d.Name == "ILangService.cs"));
    }

    [TestMethod]
    [DynamicData(nameof(ControlRows))]
    public async Task Ordinary_Array_Params_Are_Accepted_By_Old_Receivers(string route, string receiverVersion)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var sourcePath = await PrepareAsync(workspace, "public int Sum(params int[] values) => values.Length;",
            Properties(receiverVersion == "7.3" ? "7.3" : "9", receiverVersion, allowUnsafe: false, nullable: false));

        var pending = await AcceptedSolutionAsync(workspace, route, sourcePath);

        var receiver = route == "standalone" ? "SampleLib" : "Contracts";
        var text = (await pending.Projects.Single(p => p.Name == receiver).Documents.Single(d => d.Name == "ILangService.cs").GetTextAsync()).ToString();
        StringAssert.Contains(text, "params int[]");
    }

    [TestMethod]
    [DynamicData(nameof(MixedRoutes))]
    public async Task Several_Offending_Members_Are_Refused_With_One_Deduplicated_Compiler_Id(string route)
    {
        const string members = """
            public int Sum(params ReadOnlySpan<int> values) => values.Length;
                public int Read(ref readonly int value) => value;
            """;
        await using var workspace = CreateIsolatedWorkspaceCopy();
        // Pinned from the run: the compiler's feature-availability id is keyed by the receiver's language version (CS9058 at C# 11,
        // CS9202 at 12, CS8936 at 10, CS8400 at 8), not by the feature, so two offending members share one id. Reporting every
        // distinct id rather than a prefix is pinned in the unit row `Refusal_Lists_Every_Distinct_Id_In_Order`.
        var sourcePath = await PrepareAsync(workspace, members, Properties("13", "11", allowUnsafe: false));

        await AssertRefusedAsync(workspace, route, sourcePath, ["CS9058"]);
    }
}
