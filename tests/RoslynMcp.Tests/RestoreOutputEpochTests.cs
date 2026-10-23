using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RestoreOutputEpochTests
{
    [TestMethod]
    public async Task SuccessfulStep_AdvancesOnlyExactGeneratedFilesIncludingMissingInputs()
    {
        await WithFixtureAsync(async fixture =>
        {
            var previous = await fixture.CaptureAsync();
            await File.WriteAllTextAsync(fixture.Props, "restored props");
            await File.WriteAllTextAsync(fixture.Assets, "restored assets");
            var epoch = await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(previous, fixture.Generated,
                fixture.Protected, fixture.AuthorizeAsync, CancellationToken.None);
            await epoch.AssertCurrentAsync(fixture.AuthorizeAsync, CancellationToken.None);
            Assert.AreEqual(3, epoch.Inputs.Length);
            Assert.IsTrue(epoch.Inputs.Single(input => input.FilePath == fixture.Assets).Exists);
            Assert.AreEqual("authored", await File.ReadAllTextAsync(fixture.Authored));
            Assert.IsTrue(fixture.ReadApprovals.All(path => path == fixture.Authored || fixture.Generated.Contains(path)));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedNonproducerOrProtectedInput_RefusesWithoutRestoringUnknownBytes(bool sameOutputDirectory)
    {
        await WithFixtureAsync(async fixture =>
        {
            var path = sameOutputDirectory ? Path.Combine(Path.GetDirectoryName(fixture.Props)!, "unreviewed.config") : fixture.Authored;
            await File.WriteAllTextAsync(path, "known");
            var previous = (await fixture.CaptureAsync()).Where(input => input.FilePath != path)
                .Append(await PhysicalInputIdentity.CaptureAsync(path, CancellationToken.None)).ToArray();
            await File.WriteAllTextAsync(path, "external");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(
                previous, fixture.Generated, fixture.Protected, fixture.AuthorizeAsync, CancellationToken.None));
            Assert.AreEqual("external", await File.ReadAllTextAsync(path));
        });
    }

    [TestMethod]
    public async Task ProtectedGeneratedPath_CannotBeReclassifiedAsRestoreOutput()
    {
        await WithFixtureAsync(async fixture =>
        {
            var previous = await fixture.CaptureAsync();
            var protectedFiles = fixture.Protected.Append(fixture.Props).ToHashSet(FileSystemPath.Comparer);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(
                previous, fixture.Generated, protectedFiles, fixture.AuthorizeAsync, CancellationToken.None));
            Assert.AreEqual("old props", await File.ReadAllTextAsync(fixture.Props));
        });
    }

    [TestMethod]
    public async Task GeneratedFileOccupiedByDirectory_IsNotAnAcceptedOutput()
    {
        await WithFixtureAsync(async fixture =>
        {
            var previous = await fixture.CaptureAsync();
            Directory.CreateDirectory(fixture.Assets);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(
                previous, fixture.Generated, fixture.Protected, fixture.AuthorizeAsync, CancellationToken.None));
            Assert.IsTrue(Directory.Exists(fixture.Assets));
        });
    }

    [TestMethod]
    public async Task AcceptedOutputChangedBeforeNextCommand_Refuses()
    {
        await WithFixtureAsync(async fixture =>
        {
            var epoch = await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(await fixture.CaptureAsync(),
                fixture.Generated, fixture.Protected, fixture.AuthorizeAsync, CancellationToken.None);
            await File.WriteAllTextAsync(fixture.Props, "external");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => epoch.AssertCurrentAsync(fixture.AuthorizeAsync, CancellationToken.None));
            Assert.AreEqual("external", await File.ReadAllTextAsync(fixture.Props));
        });
    }

    [TestMethod]
    public async Task CurrentGrantDenial_PrecedesLockedOutputRead()
    {
        await WithFixtureAsync(async fixture =>
        {
            var epoch = await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(await fixture.CaptureAsync(),
                fixture.Generated, fixture.Protected, fixture.AuthorizeAsync, CancellationToken.None);
            using var held = new FileStream(fixture.Props, FileMode.Open, FileAccess.Read, FileShare.None);
            fixture.DeniedPath = fixture.Props;
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => epoch.AssertCurrentAsync(fixture.AuthorizeAsync, CancellationToken.None));
        });
    }

    [TestMethod]
    public async Task AuthoredChangeDuringOutputCapture_IsDetectedBeforeEpochPublication()
    {
        await WithFixtureAsync(async fixture =>
        {
            var previous = await fixture.CaptureAsync();
            var changed = false;
            async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                await fixture.AuthorizeAsync(paths, ct);
                if (!changed && paths.Contains(fixture.Props, FileSystemPath.Comparer))
                {
                    changed = true;
                    await File.WriteAllTextAsync(fixture.Authored, "external", ct);
                }
            }
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(
                previous, fixture.Generated, fixture.Protected, Authorize, CancellationToken.None));
            Assert.IsTrue(changed);
            Assert.AreEqual("external", await File.ReadAllTextAsync(fixture.Authored));
        });
    }

    [TestMethod]
    public async Task Cancellation_PreservesTokenBeforeAnyInputRead()
    {
        await WithFixtureAsync(async fixture =>
        {
            var previous = await fixture.CaptureAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(
                previous, fixture.Generated, fixture.Protected, fixture.AuthorizeAsync, cancellation.Token));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
            Assert.HasCount(0, fixture.ReadApprovals);
        });
    }

    [TestMethod]
    public async Task FinalGrantChangingEarlierInput_RefusesExistingEpoch()
    {
        await WithFixtureAsync(async fixture =>
        {
            var epoch = await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(await fixture.CaptureAsync(),
                fixture.Generated, fixture.Protected, fixture.AuthorizeAsync, CancellationToken.None);
            var authoredIndex = epoch.Inputs.Select((input, index) => (input, index)).Single(pair => FileSystemPath.Comparer.Equals(pair.input.FilePath, fixture.Authored)).index;
            var laterOutput = epoch.Inputs.Skip(authoredIndex + 1).First(input => fixture.Generated.Contains(input.FilePath)).FilePath;
            Assert.IsTrue(authoredIndex >= 0);
            var changed = false;
            async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                await fixture.AuthorizeAsync(paths, ct);
                if (changed || !paths.Contains(laterOutput, FileSystemPath.Comparer)) return;
                Assert.AreEqual("authored", await File.ReadAllTextAsync(fixture.Authored, ct));
                await File.WriteAllTextAsync(fixture.Authored, "external", ct);
                changed = true;
            }
            Exception? observed = null;
            try { await epoch.AssertCurrentAsync(Authorize, CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.IsTrue(changed, "The later output grant must execute the owned mutation actor.");
            Assert.AreEqual("external", await File.ReadAllTextAsync(fixture.Authored));
            Assert.IsInstanceOfType<PublicInvalidOperationException>(observed);
        });
    }

    [TestMethod]
    public async Task LastCaptureGrantChangingRetainedInput_RefusesEpochPublication()
    {
        await WithFixtureAsync(async fixture =>
        {
            var previous = await fixture.CaptureAsync();
            var baseline = new List<string[]>();
            Task ObserveBaseline(IReadOnlyList<string> paths, CancellationToken ct)
            {
                baseline.Add(paths.ToArray());
                return fixture.AuthorizeAsync(paths, ct);
            }
            await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(previous, fixture.Generated,
                fixture.Protected, ObserveBaseline, CancellationToken.None);
            Assert.IsTrue(baseline.Count > 0);
            await PhysicalInputIdentity.AssertCurrentAsync(previous, CancellationToken.None);
            var actual = new List<string[]>();
            var actorCount = 0;
            async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                await fixture.AuthorizeAsync(paths, ct);
                actual.Add(paths.ToArray());
                Assert.IsTrue(actual.Count <= baseline.Count, "Identical mutation-free capture inputs must retain the qualified grant protocol up to the actor.");
                CollectionAssert.AreEqual(baseline[actual.Count - 1], actual[^1]);
                if (actual.Count != baseline.Count) return;
                Assert.AreEqual("authored", await File.ReadAllTextAsync(fixture.Authored, ct));
                await File.WriteAllTextAsync(fixture.Authored, "external", ct);
                actorCount++;
            }
            Exception? observed = null;
            try
            {
                await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(previous, fixture.Generated,
                    fixture.Protected, Authorize, CancellationToken.None);
            }
            catch (Exception failure) { observed = failure; }
            Assert.AreEqual(1, actorCount, "The mutation must occur at the dynamically qualified final capture grant.");
            Assert.AreEqual("external", await File.ReadAllTextAsync(fixture.Authored));
            Assert.AreEqual(baseline.Count, actual.Count);
            for (var index = 0; index < baseline.Count; index++) CollectionAssert.AreEqual(baseline[index], actual[index]);
            Assert.IsInstanceOfType<PublicInvalidOperationException>(observed,
                "An external write after the last policy callback must prevent publication of a stale generated-output epoch.");
        });
    }

    [TestMethod]
    public async Task LaterGrantDenial_PrecedesEarlierLockedInputRead()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows native sharing establishes the locked-read ordering oracle."); return; }
        await WithFixtureAsync(async fixture =>
        {
            var epoch = await RestoreOutputEpoch.CaptureAfterSuccessfulRestoreAsync(await fixture.CaptureAsync(),
                fixture.Generated, fixture.Protected, fixture.AuthorizeAsync, CancellationToken.None);
            Assert.AreEqual(fixture.Authored, epoch.Inputs[0].FilePath);
            Assert.IsTrue(epoch.Inputs.Skip(1).Any(input => FileSystemPath.Comparer.Equals(input.FilePath, fixture.Props)));
            using var held = new FileStream(fixture.Authored, FileMode.Open, FileAccess.Read, FileShare.None);
            await Assert.ThrowsAsync<IOException>(() => PhysicalInputIdentity.CaptureAsync(fixture.Authored, CancellationToken.None));
            fixture.DeniedPath = fixture.Props;
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => epoch.AssertCurrentAsync(fixture.AuthorizeAsync, CancellationToken.None));
        });
    }

    private static async Task WithFixtureAsync(Func<OutputFixture, Task> action)
    {
        var root = Path.Combine(TestTempRoot.Current, "restore-output-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            var fixture = new OutputFixture(root);
            fixture.Initialize();
            await action(fixture);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Restore output epoch fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class OutputFixture
    {
        private readonly string _root;
        internal string Authored => Path.Combine(_root, "Owner.csproj");
        internal string Props => Path.Combine(_root, "obj", "Owner.csproj.nuget.g.props");
        internal string Assets => Path.Combine(_root, "obj", "project.assets.json");
        internal HashSet<string> Generated => new([Props, Assets], FileSystemPath.Comparer);
        internal HashSet<string> Protected => new([Authored], FileSystemPath.Comparer);
        internal List<string> ReadApprovals { get; } = [];
        internal string? DeniedPath { get; set; }
        internal OutputFixture(string root) => _root = root;
        internal void Initialize()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Props)!);
            File.WriteAllText(Authored, "authored");
            File.WriteAllText(Props, "old props");
        }
        internal async Task<PhysicalInputIdentity[]> CaptureAsync()
            => [await PhysicalInputIdentity.CaptureAsync(Authored, CancellationToken.None),
                await PhysicalInputIdentity.CaptureAsync(Props, CancellationToken.None),
                await PhysicalInputIdentity.CaptureAsync(Assets, CancellationToken.None)];
        internal Task AuthorizeAsync(IReadOnlyList<string> paths, CancellationToken ct)
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                ReadApprovals.Add(path);
                if (FileSystemPath.Comparer.Equals(path, DeniedPath))
                    throw new UnauthorizedAccessException("The current request excludes this exact restore output.");
            }
            return Task.CompletedTask;
        }
    }
}
