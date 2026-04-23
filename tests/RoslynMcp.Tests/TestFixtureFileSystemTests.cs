namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestFixtureFileSystemTests
{
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
