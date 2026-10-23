using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DotnetTestModeResolverTests
{
    [TestMethod]
    [DataRow("{}", false)]
    [DataRow("{\"test\":{\"runner\":\"VSTest\"}}", false)]
    [DataRow("{/* SDK-independent test configuration */\"test\":{\"runner\":\"Microsoft.Testing.Platform\",},}", true)]
    public async Task AuthorizedNearestConfiguration_SelectsModeWithoutDependingOnSdkVersion(string json, bool mtp)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"), json);
        var context = await DotnetTestModeResolver.ResolveAsync("mode", fixture.Project, fixture.Policy, CancellationToken.None);
        Assert.AreEqual(mtp, context.UsesNativeMtp);
        Assert.AreEqual(Path.Combine(fixture.Root, "global.json"), context.GlobalJsonPath);
        Assert.AreEqual(2, context.Candidates.Length);
        Assert.IsFalse(context.Candidates[0].Exists);
        await DotnetTestModeResolver.ValidateAsync("mode", fixture.Project, context, fixture.Policy, CancellationToken.None);
    }

    [TestMethod]
    public async Task UnauthorizedAncestor_RefusesInsteadOfAssumingVSTestOrReadingInheritedConfiguration()
    {
        using var fixture = new Fixture();
        var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            DotnetTestModeResolver.ResolveAsync("mode", fixture.Project, fixture.Policy, CancellationToken.None));
        StringAssert.Contains(failure.Message, "Place an explicit global.json");
        Assert.IsNull(failure.InnerException);
        Assert.AreEqual(3, fixture.Policy.Requests.Count);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("[]")]
    [DataRow("{\"test\":17}")]
    [DataRow("{\"test\":{\"runner\":17}}")]
    [DataRow("{\"test\":{\"runner\":\"synthetic-private-unknown-runner\"}}")]
    public async Task InvalidAuthorizedConfiguration_RefusesWithoutLeakingRawContents(string json)
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"), json);
        var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            DotnetTestModeResolver.ResolveAsync("mode", fixture.Project, fixture.Policy, CancellationToken.None));
        Assert.IsFalse(failure.ToString().Contains("synthetic-private-unknown-runner", StringComparison.Ordinal));
        Assert.IsNull(failure.InnerException);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparedContext_RejectsChangedSelectedOrNewNearerConfiguration(bool createNearer)
    {
        using var fixture = new Fixture();
        var selected = Path.Combine(fixture.Root, "global.json");
        await File.WriteAllTextAsync(selected, "{}");
        var context = await DotnetTestModeResolver.ResolveAsync("mode", fixture.Project, fixture.Policy, CancellationToken.None);
        await File.WriteAllTextAsync(createNearer ? Path.Combine(fixture.Project, "global.json") : selected,
            "{\"test\":{\"runner\":\"Microsoft.Testing.Platform\"}}");
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            DotnetTestModeResolver.ValidateAsync("mode", fixture.Project, context, fixture.Policy, CancellationToken.None));
    }

    [TestMethod]
    public async Task OwnedConfigurationOverlay_ResolvesDesiredBytesWithoutReadingOrWritingAppliedContents()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "global.json");
        const string applied = "{\"test\":{\"runner\":\"Microsoft.Testing.Platform\"}}";
        await File.WriteAllTextAsync(path, applied);
        var owned = new Dictionary<string, FileSnapshotDto>(FileSystemPath.Comparer)
        {
            [path] = FileSnapshotDto.FromExistingBytes(path, System.Text.Encoding.UTF8.GetBytes("{}"))
        };
        var context = await DotnetTestModeResolver.ResolveOverlayAsync("mode", fixture.Project, owned, fixture.Policy, CancellationToken.None);
        Assert.IsFalse(context.UsesNativeMtp);
        Assert.AreEqual(applied, File.ReadAllText(path));
        Assert.AreEqual(path, context.GlobalJsonPath);
    }

    [TestMethod]
    public async Task NearestOwnedConfiguration_StopsBeforeUnauthorizedParent()
    {
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Project, "global.json"), "{}");
        var context = await DotnetTestModeResolver.ResolveAsync("mode", fixture.Project, fixture.Policy, CancellationToken.None);
        Assert.IsFalse(context.UsesNativeMtp);
        Assert.AreEqual(1, fixture.Policy.Requests.Count);
        Assert.AreEqual(1, context.Candidates.Length);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(TestTempRoot.Current, nameof(DotnetTestModeResolverTests), Guid.NewGuid().ToString("N"));
        internal string Project => Path.Combine(Root, "Project");
        internal BoundaryPolicy Policy { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(Project);
            Policy = new BoundaryPolicy(Root);
        }
        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(Root);
    }
    private sealed class BoundaryPolicy(string root) : IWorkspacePathPolicy
    {
        internal List<string> Requests { get; } = [];
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            Requests.AddRange(paths);
            if (paths.Any(path => !FileSystemPath.IsStrictDescendant(root, path)))
                throw new UnauthorizedAccessException("Synthetic current authority boundary.");
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }
}
