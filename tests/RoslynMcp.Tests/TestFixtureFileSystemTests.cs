namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestFixtureFileSystemTests
{
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
