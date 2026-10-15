namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestFixtureFileSystemTests
{
    [TestMethod]
    public void DeleteDirectoryIfExists_NativeJunctionPreservesTargetContentsAndAttributes()
        => AssertLinkCleanupBoundary("junction");

    [TestMethod]
    [DataRow("directory-symlink")]
    [DataRow("file-symlink")]
    [DataRow("cycle")]
    [DataRow("cycle-junction")]
    [DataRow("root-junction")]
    [DataRow("root-directory-symlink")]
    [DataRow("dangling-directory-symlink")]
    [DataRow("root-dangling-directory-symlink")]
    public void DeleteDirectoryIfExists_LinksPreserveTargetContentsAndAttributes(string kind)
        => AssertLinkCleanupBoundary(kind);

    private static void AssertLinkCleanupBoundary(string kind)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TestFixtureFileSystemTests), Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, "owned");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(owned);
        Directory.CreateDirectory(outside);
        var marker = Path.Combine(outside, "marker.txt");
        File.WriteAllText(marker, "separately-owned-target");
        File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.ReadOnly);
        var originalAttributes = File.GetAttributes(marker);
        var rootLink = kind.StartsWith("root-", StringComparison.Ordinal);
        var link = rootLink ? Path.Combine(root, "linked-root") : Path.Combine(owned, "link");
        var directoryLink = kind != "file-symlink";
        try
        {
            try
            {
                if (kind.Contains("junction", StringComparison.Ordinal))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        Assert.Inconclusive("NTFS junctions require Windows; symbolic links cover the Unix boundary.");
                        return;
                    }
                    WindowsDirectoryJunction.Create(link, kind == "cycle-junction" ? owned : outside);
                }
                else if (directoryLink)
                    Directory.CreateSymbolicLink(link, kind switch
                    {
                        "cycle" => owned,
                        "dangling-directory-symlink" => Path.Combine(root, "absent-target"),
                        "root-dangling-directory-symlink" => Path.Combine(root, "absent-target"),
                        _ => outside,
                    });
                else
                    File.CreateSymbolicLink(link, marker);
            }
            catch (Exception exception) when (!kind.Contains("junction", StringComparison.Ordinal)
                && exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Inconclusive($"Native {kind} creation unavailable: {exception.GetType().Name}: {exception.Message}");
            }

            var ownedReadOnly = Path.Combine(owned, "owned.txt");
            File.WriteAllText(ownedReadOnly, "owned-read-only-control");
            File.SetAttributes(ownedReadOnly, FileAttributes.ReadOnly);
            TestFixtureFileSystem.DeleteDirectoryIfExists(rootLink ? link : owned);

            Assert.IsFalse(Directory.Exists(rootLink ? link : owned));
            Assert.IsFalse(new DirectoryInfo(link).Exists, "Owned links, including dangling links, must be removed.");
            Assert.IsFalse(new FileInfo(link).Exists, "Owned file links must be removed.");
            Assert.IsFalse(Directory.GetFileSystemEntries(root).Contains(link, StringComparer.Ordinal),
                "Dangling root links must be unlinked even when their targets do not exist.");
            Assert.IsTrue(Directory.Exists(outside));
            Assert.AreEqual("separately-owned-target", File.ReadAllText(marker));
            Assert.AreEqual(originalAttributes, File.GetAttributes(marker), "Cleanup must not alter an external target's attributes.");
        }
        finally
        {
            // Unlink explicitly before generic cleanup, including when testing the old traversal.
            // These nonrecursive operations remove only the link, never its target.
            try
            {
                if (directoryLink) Directory.Delete(link, recursive: false);
                else File.Delete(link);
            }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
            File.SetAttributes(marker, FileAttributes.Normal);
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    public void WindowsDirectoryJunction_Create_ResolvesToTargetWithoutLaunchingAProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("NTFS junctions are Windows-only.");
            return;
        }

        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(TestFixtureFileSystemTests),
            Guid.NewGuid().ToString("N"));
        var target = Path.Combine(fixtureRoot, "target");
        var link = Path.Combine(fixtureRoot, "link");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "probe.txt"), "through-the-junction");

        try
        {
            WindowsDirectoryJunction.Create(link, target);

            var linkInfo = new DirectoryInfo(link);
            Assert.IsTrue(
                linkInfo.Attributes.HasFlag(FileAttributes.ReparsePoint),
                "The link must be a reparse point, not a plain directory.");
            Assert.AreEqual(
                Path.GetFullPath(target),
                Path.GetFullPath(linkInfo.ResolveLinkTarget(returnFinalTarget: true)!.FullName),
                "The junction must resolve to its target.");
            Assert.AreEqual(
                "through-the-junction",
                File.ReadAllText(Path.Combine(link, "probe.txt")),
                "Content under the target must be reachable through the junction.");

            IOException? occupied = null;
            try
            {
                WindowsDirectoryJunction.Create(target, link);
            }
            catch (IOException ex)
            {
                occupied = ex;
            }

            Assert.IsNotNull(occupied, "Creating a junction over an existing directory must fail.");
            Assert.IsTrue(
                File.Exists(Path.Combine(target, "probe.txt")),
                "A refused junction must never delete the directory already at its path.");
            StringAssert.Contains(
                occupied.Message,
                target,
                "A failed junction must name the path it could not create, never degrade to a bare timeout.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public void DeleteDirectoryIfExists_LockedReadOnlyFile_RetriesAndSurfacesTerminalFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file-sharing semantics are required for this regression.");
        }

        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(TestFixtureFileSystemTests),
            Guid.NewGuid().ToString("N"));
        var lockedFile = Path.Combine(fixtureRoot, "locked.txt");
        Directory.CreateDirectory(fixtureRoot);
        File.WriteAllText(lockedFile, "fixture");
        File.SetAttributes(lockedFile, FileAttributes.ReadOnly);

        try
        {
            using (var lockStream = new FileStream(
                lockedFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                _ = Assert.Throws<IOException>(() =>
                    TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot));
            }

            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
            Assert.IsFalse(
                Directory.Exists(fixtureRoot),
                "The shared helper must clear read-only state and delete once the lock is released.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    public void CreateSampleSolutionCopy_SkipsBinDirectoriesAndTransientScratchFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpFixtureTests", Guid.NewGuid().ToString("N"));
        var repositoryRoot = Path.Combine(tempRoot, "repo");
        var sampleRoot = Path.Combine(tempRoot, "samples", "SampleSolution");
        try
        {
            Directory.CreateDirectory(repositoryRoot);
            Directory.CreateDirectory(sampleRoot);

            File.WriteAllText(Path.Combine(repositoryRoot, "Directory.Build.props"), "<Project />");

            var solutionPath = Path.Combine(sampleRoot, "SampleSolution.slnx");
            File.WriteAllText(solutionPath, "<Solution />");

            var sampleProjectDir = Path.Combine(sampleRoot, "SampleLib");
            Directory.CreateDirectory(sampleProjectDir);
            File.WriteAllText(Path.Combine(sampleProjectDir, "Dog.cs"), "namespace SampleLib; public sealed class Dog {}");

            var objDir = Path.Combine(sampleProjectDir, "obj");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "project.assets.json"), "{}");

            var scratchDir = Path.Combine(objDir, "Debug", "net10.0");
            Directory.CreateDirectory(scratchDir);
            File.WriteAllText(Path.Combine(scratchDir, "hweuycta.hxx~"), "transient");

            var binDir = Path.Combine(sampleProjectDir, "bin", "Debug");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "SampleLib.dll"), "ignored");

            var copiedSolutionPath = TestFixtureFileSystem.CreateSampleSolutionCopy(repositoryRoot, solutionPath);
            var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

            Assert.IsTrue(File.Exists(copiedSolutionPath), "Copied fixture must contain the solution file.");
            Assert.IsTrue(File.Exists(Path.Combine(copiedRoot, "SampleLib", "Dog.cs")), "Source files must be copied.");
            Assert.IsTrue(File.Exists(Path.Combine(copiedRoot, "SampleLib", "obj", "project.assets.json")), "Stable restore artefacts under obj/ must be preserved.");
            Assert.IsFalse(File.Exists(Path.Combine(copiedRoot, "SampleLib", "obj", "Debug", "net10.0", "hweuycta.hxx~")), "Transient scratch files must not be copied into isolated fixtures.");
            Assert.IsFalse(Directory.Exists(Path.Combine(copiedRoot, "SampleLib", "bin")), "bin/ must not be copied into isolated fixtures.");
            Assert.IsTrue(File.Exists(Path.Combine(copiedRoot, "Directory.Build.props")), "Repository support files must still be copied.");

            TestFixtureFileSystem.DeleteDirectoryIfExists(copiedRoot);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(tempRoot);
        }
    }
}
