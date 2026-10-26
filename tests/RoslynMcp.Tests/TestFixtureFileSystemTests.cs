using System.Xml.Linq;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestFixtureFileSystemTests
{
    [TestMethod]
    public void EscapeMsBuildItemInclude_EncodesOriginalCharactersExactlyOnce()
    {
        Assert.AreEqual("%25%2A%3F%40%24%28%29%3B%27", TestFixtureFileSystem.EscapeMsBuildItemInclude("%*?@$();'"));
        Assert.AreEqual("literal%253B", TestFixtureFileSystem.EscapeMsBuildItemInclude("literal%3B"));
    }

    [TestMethod]
    [DataRow("solution", true)]
    [DataRow("library", true)]
    [DataRow("library-tests", true)]
    [DataRow("solution", false)]
    [DataRow("library", false)]
    [DataRow("library-tests", false)]
    public void CopyFixture_ProvidesActualDetachedSourceRoot_WithoutChangingCompilerPolicy(string kind, bool existingProps)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TestFixtureFileSystemTests), "root %;()$' " + Guid.NewGuid().ToString("N"));
        var sample = Path.Combine(root, "sample");
        Directory.CreateDirectory(Path.Combine(sample, "SampleLib"));
        Directory.CreateDirectory(Path.Combine(sample, "SampleLib.Tests"));
        var solution = Path.Combine(sample, "SampleSolution.slnx");
        File.WriteAllText(solution, "<Solution />");
        foreach (var project in new[] { "SampleLib", "SampleLib.Tests" })
            File.WriteAllText(Path.Combine(sample, project, project + ".csproj"), "<Project />");
        const string original = "<Project><!--preserve--><PropertyGroup><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><ContinuousIntegrationBuild Condition=\"'$(CI)' == 'true'\">true</ContinuousIntegrationBuild><DeterministicSourcePaths>true</DeterministicSourcePaths></PropertyGroup></Project>";
        var sourceProps = Path.Combine(root, "Directory.Build.props");
        if (existingProps) File.WriteAllText(sourceProps, original);
        string? copiedRoot = null;
        try
        {
            copiedRoot = kind switch
            {
                "library" => TestFixtureFileSystem.CreateSampleLibraryCopy(root, solution).RootPath,
                "library-tests" => TestFixtureFileSystem.CreateSampleLibraryTestsCopy(root, solution).RootPath,
                _ => Path.GetDirectoryName(TestFixtureFileSystem.CreateSampleSolutionCopy(root, solution))!,
            };
            var props = XDocument.Load(Path.Combine(copiedRoot, "Directory.Build.props"));
            var sourceRoot = props.Descendants("SourceRoot").Single();
            Assert.AreEqual(TestFixtureFileSystem.EscapeMsBuildItemInclude(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(copiedRoot)) + Path.DirectorySeparatorChar),
                sourceRoot.Attribute("Include")?.Value);
            Assert.AreEqual("", sourceRoot.Element("NestedRoot")?.Value ?? "");
            if (existingProps)
            {
                var expected = XDocument.Parse(original);
                CollectionAssert.AreEqual(expected.Descendants("PropertyGroup").Select(e => e.ToString()).ToArray(),
                    props.Descendants("PropertyGroup").Select(e => e.ToString()).ToArray());
                Assert.AreEqual(original, File.ReadAllText(sourceProps), "Repository props must remain byte-identical.");
                Assert.IsTrue(props.DescendantNodes().OfType<XComment>().Any(c => c.Value == "preserve"));
            }
            else Assert.IsFalse(File.Exists(sourceProps));
            Assert.IsFalse(Directory.Exists(Path.Combine(copiedRoot, ".git")));
        }
        finally
        {
            if (copiedRoot is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(copiedRoot);
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CopyFixture_MissingEntryPoint_RemovesPartialCopy(bool libraryOnly) =>
        AssertMissingEntryPointCleanup((root, solution) =>
        {
            if (libraryOnly)
                TestFixtureFileSystem.CreateSampleLibraryCopy(root, solution);
            else
                TestFixtureFileSystem.CreateSampleSolutionCopy(root, solution);
        }, includeLibraryProject: false);

    [TestMethod]
    public void CopyLibraryTestsFixture_MissingTestProject_RemovesPartialCopy() =>
        AssertMissingEntryPointCleanup(
            (root, solution) => TestFixtureFileSystem.CreateSampleLibraryTestsCopy(root, solution),
            includeLibraryProject: true);

    private static void AssertMissingEntryPointCleanup(Action<string, string> copy, bool includeLibraryProject)
    {
        var sourceRoot = Path.Combine(TestTempRoot.Current, nameof(TestFixtureFileSystemTests), Guid.NewGuid().ToString("N"));
        var sampleRoot = Path.Combine(sourceRoot, "sample");
        var markerName = Guid.NewGuid().ToString("N") + ".txt";
        Directory.CreateDirectory(Path.Combine(sampleRoot, "SampleLib"));
        Directory.CreateDirectory(Path.Combine(sampleRoot, "SampleLib.Tests"));
        if (includeLibraryProject)
            File.WriteAllText(Path.Combine(sampleRoot, "SampleLib", "SampleLib.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(sampleRoot, "SampleLib", markerName), "copied before entry-point validation");
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
            {
                var missingSolution = Path.Combine(sampleRoot, "SampleSolution.slnx");
                copy(sourceRoot, missingSolution);
            });
            Assert.IsFalse(Directory.EnumerateDirectories(TestTempRoot.Current)
                .Any(root => File.Exists(Path.Combine(root, "SampleLib", markerName))),
                "A failed fixture copy must remove the partially copied root.");
        }
        finally
        {
            foreach (var root in Directory.EnumerateDirectories(TestTempRoot.Current)
                .Where(root => File.Exists(Path.Combine(root, "SampleLib", markerName))))
                TestFixtureFileSystem.DeleteDirectoryIfExists(root);
            TestFixtureFileSystem.DeleteDirectoryIfExists(sourceRoot);
        }
    }

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
