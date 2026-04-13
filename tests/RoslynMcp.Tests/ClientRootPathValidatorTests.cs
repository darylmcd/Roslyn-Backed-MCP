using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public class ClientRootPathValidatorTests
{
    // ───────── ResolvePath tests ─────────

    [TestMethod]
    public void ResolvePath_Absolute_Returns_Canonical_Form()
    {
        var result = ClientRootPathValidator.ResolvePath(TestFixtureFileSystem.FindRepositoryRoot());
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Assert.IsTrue(Path.IsPathFullyQualified(result));
    }

    [TestMethod]
    public void ResolvePath_Relative_Path_Resolves_Against_CurrentDirectory()
    {
        var result = ClientRootPathValidator.ResolvePath(".");
        var expected = Path.GetFullPath(".");
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ResolvePath_DotDot_Segments_Are_Resolved()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var pathWithTraversal = Path.Combine(repoRoot, "src", "..", "tests");
        var result = ClientRootPathValidator.ResolvePath(pathWithTraversal);
        var expected = Path.GetFullPath(Path.Combine(repoRoot, "tests"));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ResolvePath_NonExistent_Path_Returns_FullPath()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "fake.cs");
        var result = ClientRootPathValidator.ResolvePath(fakePath);
        Assert.AreEqual(Path.GetFullPath(fakePath), result);
    }

    [TestMethod]
    public void ResolvePath_Existing_Directory_Returns_Resolved_Path()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.IsTrue(Directory.Exists(srcDir), "src directory must exist for this test");

        var result = ClientRootPathValidator.ResolvePath(srcDir);
        Assert.AreEqual(Path.GetFullPath(srcDir), result);
    }

    [TestMethod]
    public void ResolvePath_Existing_File_Returns_Resolved_Path()
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var filePath = Path.Combine(repoRoot, "Directory.Build.props");
        Assert.IsTrue(File.Exists(filePath), "Directory.Build.props must exist for this test");

        var result = ClientRootPathValidator.ResolvePath(filePath);
        Assert.AreEqual(Path.GetFullPath(filePath), result);
    }

    [TestMethod]
    public void ResolvePath_Path_Traversal_Out_Of_Root_Is_Canonicalized()
    {
        // Simulates a traversal attack: /allowed/root/../../etc/passwd
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var traversal = Path.Combine(repoRoot, "..", "..", "Windows", "System32");
        var result = ClientRootPathValidator.ResolvePath(traversal);

        // After resolution, the path should NOT start with repoRoot
        Assert.IsFalse(result.StartsWith(repoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Traversal should resolve outside the repo root");
    }

    // ───────── ValidatePathAgainstRootsAsync null/no-capability tests ─────────

    [TestMethod]
    public async Task ValidatePath_NullServer_AllowsAccess()
    {
        // server == null means no MCP server context — path should be allowed
        await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
            null!, "C:\\any\\path", CancellationToken.None);
        // No exception = pass
    }
}
