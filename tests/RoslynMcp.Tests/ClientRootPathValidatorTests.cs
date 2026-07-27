using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public class ClientRootPathValidatorTests
{
    // Converts a Windows-style absolute path to the platform's native absolute path so
    // IsPathUnderAnyRoot tests work on Linux CI without a separate test matrix.
    // "C:\foo\bar" → "/foo/bar" on Linux, unchanged on Windows.
    private static string P(string winPath)
    {
        if (OperatingSystem.IsWindows())
            return winPath;
        var withoutDrive = winPath.Length > 2 && winPath[1] == ':' ? winPath[2..] : winPath;
        return withoutDrive.Replace('\\', '/');
    }

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

    // ───────── SecurityOptions fail-open/fail-closed default tests ─────────

    [TestMethod]
    public void SecurityOptions_Default_Is_FailClosed()
    {
        // The trust-boundary default: a fresh SecurityOptions (what BindSecurityOptions supplies
        // when ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN is unset/unparseable) must fail CLOSED. A
        // roots-lookup failure then rejects the write/edit rather than silently allowing it.
        Assert.IsFalse(new SecurityOptions().PathValidationFailOpen,
            "PathValidationFailOpen must default to false (fail-closed) on a security-relevant path check.");
    }

    [TestMethod]
    public void SecurityOptions_FailOpen_Is_Opt_In()
    {
        // The escape hatch is explicit-only: fail-open is reachable solely by setting the value
        // to true (mirrors ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true). The null-coalescing fallback
        // in ValidatePathAgainstRootsAsync (`securityOptions?.PathValidationFailOpen ?? false`)
        // therefore resolves to fail-closed whenever no options object is supplied.
        Assert.IsTrue(new SecurityOptions { PathValidationFailOpen = true }.PathValidationFailOpen,
            "Fail-open must remain reachable via explicit opt-in.");
    }

    // ───────── IsPathUnderAnyRoot tests (sanctioned-root + expandSanctionedRoots widening) ─────────

    [TestMethod]
    public void IsPathUnderAnyRoot_Path_Inside_Root_Allowed()
    {
        var roots = new[] { P(@"C:\repo\main") };
        Assert.IsTrue(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\repo\main\src\file.cs"), roots, expandSanctionedRoots: false));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Path_Equal_To_Root_Allowed()
    {
        var roots = new[] { P(@"C:\repo\main") };
        Assert.IsTrue(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\repo\main"), roots, expandSanctionedRoots: false));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Sibling_Worktree_Rejected_Without_Flag()
    {
        // The whole reason this initiative exists: a sibling worktree at parent/.worktrees/foo
        // is structurally OUTSIDE parent/main — without the opt-in flag it must be rejected.
        var roots = new[] { P(@"C:\repo\main") };
        Assert.IsFalse(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\repo\sibling\src\file.cs"), roots, expandSanctionedRoots: false));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Sibling_Worktree_Allowed_With_Flag()
    {
        // With expandSanctionedRoots=true, the parent directory (/repo on Linux, C:\repo on Windows)
        // is also accepted, so a sibling at {parent}/sibling falls under the widened allowlist.
        var roots = new[] { P(@"C:\repo\main") };
        Assert.IsTrue(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\repo\sibling\src\file.cs"), roots, expandSanctionedRoots: true));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Worktree_Subdir_Allowed_With_Flag()
    {
        // The mcp-server-surface-test skill's disposable worktree at ../<sibling> —
        // verifies the canonical Phase 6/9/10/12/13 disposable-worktree audit path.
        var roots = new[] { P(@"C:\Code-Repo\TradeWise") };
        Assert.IsTrue(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\Code-Repo\TradeWise-surface-audit-20260511\src\App.csproj"),
            roots,
            expandSanctionedRoots: true));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Grandparent_Path_Rejected_Even_With_Flag()
    {
        // Widening is exactly ONE level — a grandparent path must still be rejected.
        var roots = new[] { P(@"C:\Code-Repo\TradeWise") };
        Assert.IsFalse(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\Other\anything.cs"), roots, expandSanctionedRoots: true));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Drive_Root_Never_Widens()
    {
        // Defensive: if a client sanctions /repo (or C:\repo), widening to / (or C:\) is
        // dangerous. The implementation skips filesystem-root parents.
        var roots = new[] { P(@"C:\repo") };
        Assert.IsFalse(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\Windows\System32\cmd.exe"), roots, expandSanctionedRoots: true));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_TrailingSlash_Root_Normalized()
    {
        // Trailing separator in the root must not cause a false negative.
        var trailingSlash = P(@"C:\repo\main") + Path.DirectorySeparatorChar;
        var roots = new[] { trailingSlash };
        Assert.IsTrue(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\repo\main\src\file.cs"), roots, expandSanctionedRoots: false));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Prefix_Trap_Rejected()
    {
        // Naive prefix match would allow /repo/main2 against root /repo/main —
        // normalized comparison must reject this.
        var roots = new[] { P(@"C:\repo\main") };
        Assert.IsFalse(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\repo\main2\file.cs"), roots, expandSanctionedRoots: false));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Empty_Roots_Rejects()
    {
        Assert.IsFalse(ClientRootPathValidator.IsPathUnderAnyRoot(
            P(@"C:\any\path"), Array.Empty<string>(), expandSanctionedRoots: true));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Case_Insensitive_On_Windows()
    {
        // Windows filesystem semantics — root casing should not matter.
        // Linux filesystems are case-sensitive, so this test is Windows-only.
        if (!OperatingSystem.IsWindows())
            return;

        var roots = new[] { @"C:\Repo\Main" };
        Assert.IsTrue(ClientRootPathValidator.IsPathUnderAnyRoot(
            @"C:\repo\main\src\file.cs", roots, expandSanctionedRoots: false));
    }

    [TestMethod]
    public void IsPathUnderAnyRoot_Case_Sensitive_Outside_Windows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.IsFalse(ClientRootPathValidator.IsPathUnderAnyRoot(
            "/repo/main/src/file.cs",
            ["/repo/Main"],
            expandSanctionedRoots: false));
    }

    [TestMethod]
    public async Task ResolvePathAsync_MatchesCanonicalSyncResult()
    {
        var path = Path.Combine(Path.GetTempPath(), "RoslynMcp", "..", "RoslynMcp", "future.cs");

        var resolved = await ClientRootPathValidator.ResolvePathAsync(
            path,
            CancellationToken.None);

        Assert.AreEqual(ClientRootPathValidator.ResolvePath(path), resolved);
    }

    [TestMethod]
    public async Task ResolvePathAsync_PreCancelled_DoesNotStartFilesystemWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => ClientRootPathValidator.ResolvePathAsync(
                Path.Combine(Path.GetTempPath(), "future.cs"),
                cancellation.Token));
    }
}
