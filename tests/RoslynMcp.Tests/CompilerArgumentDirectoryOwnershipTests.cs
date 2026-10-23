using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentDirectoryOwnershipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RecursiveListing_RefusesReplacedRootBeforeForeignNamesAreObserved(bool replaceAncestor)
    {
        var fixture = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentDirectoryOwnershipTests), Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(fixture, "parent");
        var source = Path.Combine(parent, "source");
        var retained = Path.Combine(fixture, "retained");
        var foreignParent = Path.Combine(fixture, "foreign");
        var foreign = Path.Combine(foreignParent, "source");
        var rootChecks = 0;
        var replaced = false;
        var blocked = false;
        var foreignProbes = 0;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(foreign);
            await File.WriteAllTextAsync(Path.Combine(source, "Original.cs"), "class Original { }");
            await File.WriteAllTextAsync(Path.Combine(foreign, "PRIVATE-FOREIGN.cs"), "class Foreign { }");
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                foreach (var path in paths)
                {
                    if (Path.GetFileName(path) == "PRIVATE-FOREIGN.cs") foreignProbes++;
                    if (FileSystemPath.Comparer.Equals(path, source) && ++rootChecks == 2)
                    {
                        try { Directory.Move(replaceAncestor ? parent : source, retained); }
                        catch (IOException) when (OperatingSystem.IsWindows()) { blocked = true; continue; }
                        Directory.Move(replaceAncestor ? foreignParent : foreign, replaceAncestor ? parent : source);
                        replaced = true;
                    }
                }
                return Task.CompletedTask;
            }
            CompilerArgumentDirectory? observed = null;
            Exception? refusal = null;
            try { observed = await CompilerArgumentDirectory.CaptureAsync(source, "*.cs", recursive: true, Authorize, CancellationToken.None); }
            catch (PublicInvalidOperationException failure) { refusal = failure; }
            Assert.IsTrue(replaced || blocked, "The real namespace swap must execute or be prevented by the held Windows scope.");
            Assert.AreEqual(0, foreignProbes, "A refused physical replacement must not leak its filenames into authorization probes.");
            if (replaced) Assert.IsNotNull(refusal, "A changed directory identity must refuse before listing the replacement.");
            else CollectionAssert.AreEqual(new[] { Path.Combine(source, "Original.cs") }, observed!.Matches.ToArray());
            Assert.AreEqual("class Foreign { }", await File.ReadAllTextAsync(Path.Combine(replaced ? source : foreign, "PRIVATE-FOREIGN.cs")));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler listing fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }
    [TestMethod]
    [DataRow("*")]
    [DataRow("*.*")]
    [DataRow("?.cs")]
    [DataRow("*.?")]
    [DataRow("*.")]
    [DataRow("a*.cs")]
    [DataRow("*.CS")]
    [DataRow("[literal].cs")]
    [DataRow("<literal>.cs")]
    [DataRow("quote\"file.cs")]
    [DataRow("back\\slash.cs")]
    [DataRow("*.absent")]
    public async Task HeldEnumeration_PreservesNativeWildcardMembershipAndOrder(string pattern)
    {
        if (OperatingSystem.IsWindows() && pattern.Contains('\\'))
            Assert.Inconclusive("A literal backslash filename is a POSIX pattern profile.");
        await InFixtureAsync(async root =>
        {
            foreach (var name in new[] { "alpha.cs", "a.cs", "B.CS", "plain", ".hidden.cs", "[literal].cs" })
                await File.WriteAllTextAsync(Path.Combine(root, name), "class Fixture { }");
            if (!OperatingSystem.IsWindows())
                foreach (var name in new[] { "<literal>.cs", "quote\"file.cs", "back\\slash.cs" })
                    await File.WriteAllTextAsync(Path.Combine(root, name), "class UnixFixture { }");
            foreach (var name in new[] { "Nested.cs", "Other" })
            {
                Directory.CreateDirectory(Path.Combine(root, name));
                await File.WriteAllTextAsync(Path.Combine(root, name, "child.cs"), "class Child { }");
            }
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;
            foreach (var recursive in new[] { false, true })
            {
                var expectedFiles = new List<string>();
                var expectedDirectories = new List<string>();
                NativeVisit(root);
                var observed = await CompilerArgumentDirectory.CaptureAsync(root, pattern, recursive, Authorize, CancellationToken.None);
                CollectionAssert.AreEqual(expectedFiles, observed.Matches.ToArray(), pattern + ":" + recursive);
                CollectionAssert.AreEqual(expectedDirectories, observed.Traversed.ToArray(), pattern + ":" + recursive);
                void NativeVisit(string directory)
                {
                    expectedDirectories.Add(directory);
                    expectedFiles.AddRange(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly));
                    if (recursive)
                        foreach (var child in Directory.GetDirectories(directory)) NativeVisit(child);
                }
            }
        });
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    public async Task FileSystemLinks_RequireCurrentGrantOnlyWhenSelectedOrRecursivelyEncountered(
        bool selected, bool recursive, bool directoryTarget)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The native symbolic-link profile is exercised on POSIX.");
        await InFixtureAsync(async fixture =>
        {
            var source = Path.Combine(fixture, "source");
            var external = Path.Combine(fixture, "external");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(external);
            var target = Path.Combine(external, "preserve.txt");
            await File.WriteAllTextAsync(target, "Unrelated target bytes");
            await File.WriteAllTextAsync(Path.Combine(source, "Entry.cs"), "class Entry { }");
            var link = Path.Combine(source, "unmatched.txt");
            if (directoryTarget) Directory.CreateSymbolicLink(link, external);
            else File.CreateSymbolicLink(link, target);
            var linkGrants = 0;
            var targetGrants = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                foreach (var path in paths)
                {
                    if (FileSystemPath.Comparer.Equals(path, link)) linkGrants++;
                    if (FileSystemPath.Comparer.Equals(path, external) || FileSystemPath.IsStrictDescendant(external, path)) targetGrants++;
                }
                return Task.CompletedTask;
            }
            var pattern = selected ? "*.txt" : "*.cs";
            if (selected || recursive)
            {
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    CompilerArgumentDirectory.CaptureAsync(source, pattern, recursive, Authorize, CancellationToken.None));
                Assert.AreEqual(1, linkGrants, "The encountered link must receive current authorization before its precise compiler-authority refusal.");
            }
            else
            {
                var observed = await CompilerArgumentDirectory.CaptureAsync(source, pattern, recursive, Authorize, CancellationToken.None);
                CollectionAssert.AreEqual(new[] { Path.Combine(source, "Entry.cs") }, observed.Matches.ToArray());
                Assert.AreEqual(0, linkGrants, "An unmatched nonrecursive link is not a consumed input.");
            }
            Assert.AreEqual(0, targetGrants, "Lexical parent authority must not enroll link targets.");
            Assert.AreEqual("Unrelated target bytes", await File.ReadAllTextAsync(target));
        });
    }

    private static async Task InFixtureAsync(Func<string, Task> body)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentDirectoryOwnershipTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try { Directory.CreateDirectory(root); await body(root); }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler directory qualification fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

}