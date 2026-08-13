using RoslynMcp.Host.Stdio.Security;
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
        var fakePath = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"), "fake.cs");
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

    [TestMethod]
    public async Task ValidatePath_ExistingRegularFileUnderLinkedAncestor_RejectsPhysicalEscape()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-linked-ancestor-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var physicalRoot = Path.Combine(testRoot, "outside");
        var logicalRoot = Path.Combine(sanctionedRoot, "escape");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(physicalRoot);
        var physicalFile = Path.Combine(physicalRoot, "file.cs");
        File.WriteAllText(physicalFile, "// linked ancestor regression");

        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(logicalRoot, physicalRoot))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var resolved = ClientRootPathValidator.ResolvePath(Path.Combine(logicalRoot, "file.cs"));

            Assert.AreEqual(Path.GetFullPath(physicalFile), resolved,
                "An existing regular leaf must not hide a symlink or junction in its ancestor chain.");

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                    server: null,
                    Path.Combine(logicalRoot, "file.cs"),
                    CancellationToken.None,
                    securityOptions: new SecurityOptions { SanctionedRoots = [sanctionedRoot] }));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public async Task ValidatePath_LinkThenParentTraversal_UsesPhysicalOrderingAndRejectsEscape()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-link-parent-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var physicalTarget = Path.Combine(testRoot, "outside", "target");
        var physicalSecret = Path.Combine(testRoot, "outside", "secret.cs");
        var logicalLink = Path.Combine(sanctionedRoot, "link");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(physicalTarget);
        File.WriteAllText(physicalSecret, "// outside secret");

        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(logicalLink, physicalTarget))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var candidate = Path.Combine(logicalLink, "..", "secret.cs");
            var resolved = ClientRootPathValidator.ResolvePath(candidate);

            Assert.AreEqual(Path.GetFullPath(physicalSecret), resolved,
                "Parent traversal must occur after resolving the preceding link component.");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                    server: null,
                    candidate,
                    CancellationToken.None,
                    securityOptions: new SecurityOptions { SanctionedRoots = [sanctionedRoot] }));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public async Task ValidatePath_LinkTargetWithLinkedAncestor_RejectsPhysicalEscape()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-link-target-alias-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outsideRoot = Path.Combine(testRoot, "outside");
        var outsideTarget = Path.Combine(outsideRoot, "target");
        var targetAlias = Path.Combine(testRoot, "target-alias");
        var entryLink = Path.Combine(sanctionedRoot, "entry");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outsideTarget);
        var physicalFile = Path.Combine(outsideTarget, "file.cs");
        File.WriteAllText(physicalFile, "// nested link target regression");

        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(targetAlias, outsideRoot)
                || !TestFixtureFileSystem.TryCreateDirectoryLink(
                    entryLink,
                    Path.Combine(targetAlias, "target")))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var candidate = Path.Combine(entryLink, "file.cs");
            var resolved = ClientRootPathValidator.ResolvePath(candidate);

            Assert.AreEqual(Path.GetFullPath(physicalFile), resolved,
                "Links introduced by another link target must be recursively canonicalized.");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                    server: null,
                    candidate,
                    CancellationToken.None,
                    securityOptions: new SecurityOptions { SanctionedRoots = [sanctionedRoot] }));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public async Task ValidatePath_RawLinkTargetAliasThenParent_UsesPhysicalOrderingAndRejectsEscape()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-raw-link-parent-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outsideRoot = Path.Combine(testRoot, "outside");
        var outsideAliasTarget = Path.Combine(outsideRoot, "deep");
        var outsideSecretRoot = Path.Combine(outsideRoot, "secret");
        var targetAlias = Path.Combine(sanctionedRoot, "alias");
        var entryLink = Path.Combine(sanctionedRoot, "entry");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outsideAliasTarget);
        Directory.CreateDirectory(outsideSecretRoot);
        var physicalFile = Path.Combine(outsideSecretRoot, "file.cs");
        File.WriteAllText(physicalFile, "// raw link-target ordering regression");

        try
        {
            var rawEntryTarget = Path.Join("alias", "..", "secret");
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(targetAlias, outsideAliasTarget)
                || !TestFixtureFileSystem.TryCreateDirectorySymbolicLink(entryLink, rawEntryTarget))
            {
                Assert.Inconclusive("Directory symbolic links with raw relative targets are unavailable in this test environment.");
                return;
            }

            var storedEntryTarget = new DirectoryInfo(entryLink).LinkTarget;
            if (storedEntryTarget is null
                || !storedEntryTarget.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries).Contains("..", StringComparer.Ordinal))
            {
                Assert.Inconclusive("The platform normalized the directory link target during creation.");
                return;
            }

            var candidate = Path.Combine(entryLink, "file.cs");
            var resolved = ClientRootPathValidator.ResolvePath(candidate);

            Assert.AreEqual(Path.GetFullPath(physicalFile), resolved,
                "Parent traversal inside a raw link target must occur after resolving its preceding alias.");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                    server: null,
                    candidate,
                    CancellationToken.None,
                    securityOptions: new SecurityOptions { SanctionedRoots = [sanctionedRoot] }));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public async Task ValidatePath_DanglingFileLinkToOutside_RejectsEscape()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-dangling-link-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outsideRoot = Path.Combine(testRoot, "outside");
        var danglingTarget = Path.Combine(outsideRoot, "future.cs");
        var entryLink = Path.Combine(sanctionedRoot, "future.cs");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(outsideRoot);

        try
        {
            if (!TestFixtureFileSystem.TryCreateFileLink(entryLink, danglingTarget))
            {
                Assert.Inconclusive("File symbolic links are unavailable in this test environment.");
                return;
            }

            Assert.IsFalse(File.Exists(entryLink), "The regression requires a dangling link entry.");
            Assert.AreEqual(
                Path.GetFullPath(danglingTarget),
                ClientRootPathValidator.ResolvePath(entryLink),
                "Canonicalization must inspect a dangling link entry instead of treating it as a missing ordinary file.");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                    server: null,
                    entryLink,
                    CancellationToken.None,
                    securityOptions: new SecurityOptions { SanctionedRoots = [sanctionedRoot] }));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public async Task ValidatePath_DanglingDirectoryLinkToOutside_RejectsEscape()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-dangling-directory-link-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "sanctioned");
        var outsideRoot = Path.Combine(testRoot, "outside");
        var entryLink = Path.Combine(sanctionedRoot, "outside-link");
        var candidate = Path.Combine(entryLink, "future.cs");
        Directory.CreateDirectory(sanctionedRoot);

        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectorySymbolicLink(entryLink, outsideRoot))
            {
                Assert.Inconclusive("Directory symbolic links are unavailable in this test environment.");
                return;
            }

            Assert.IsFalse(Directory.Exists(entryLink), "The regression requires a dangling link entry.");
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(outsideRoot, "future.cs")),
                ClientRootPathValidator.ResolvePath(candidate),
                "Canonicalization must inspect a dangling directory link before appending a missing leaf.");
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                    server: null,
                    candidate,
                    CancellationToken.None,
                    securityOptions: new SecurityOptions { SanctionedRoots = [sanctionedRoot] }));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public void GetLinkTargetPath_WindowsPartiallyQualifiedRawTargets_FailClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Partially-qualified rooted paths are a Windows filesystem concept.");
            return;
        }

        var linkPath = Path.Combine(TestTempRoot.Current, "ambiguous-link");
        var driveRoot = Path.GetPathRoot(linkPath)
            ?? throw new InvalidOperationException("The test temp root must be fully qualified.");
        foreach (var rawLinkTarget in new[]
                 {
                     Path.DirectorySeparatorChar + "ambiguous-target",
                     driveRoot[..2] + "ambiguous-target",
                 })
        {
            Assert.IsTrue(Path.IsPathRooted(rawLinkTarget));
            Assert.IsFalse(Path.IsPathFullyQualified(rawLinkTarget));
            var error = Assert.ThrowsExactly<IOException>(() =>
                ConfiguredRootBoundary.GetLinkTargetPath(linkPath, rawLinkTarget));
            StringAssert.Contains(error.Message, "ambiguous partially-qualified target");
        }
    }

    [TestMethod]
    public void ResolvePath_UnixColonRelativeRawLinkTarget_RemainsRelativeToLinkParent()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("A colon in this position denotes a drive-relative path on Windows.");
            return;
        }

        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-colon-link-target-" + Guid.NewGuid().ToString("N"));
        var rawLinkTarget = "C:target";
        var targetPath = Path.Combine(testRoot, rawLinkTarget);
        var linkPath = Path.Combine(testRoot, "colon-link");
        Directory.CreateDirectory(testRoot);
        Directory.CreateDirectory(targetPath);

        try
        {
            if (!TestFixtureFileSystem.TryCreateDirectorySymbolicLink(linkPath, rawLinkTarget))
            {
                Assert.Inconclusive("Directory symbolic links are unavailable in this test environment.");
                return;
            }

            Assert.AreEqual(
                Path.GetFullPath(targetPath),
                ClientRootPathValidator.ResolvePath(linkPath),
                "A Unix colon-relative link target is an ordinary filename relative to the link parent.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    [TestMethod]
    public async Task ValidatePath_ParentTraversalToSibling_RemainsAllowedWithExplicitWidening()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-sibling-parent-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "main");
        var siblingRoot = Path.Combine(testRoot, "sibling");
        Directory.CreateDirectory(sanctionedRoot);
        Directory.CreateDirectory(siblingRoot);
        var candidate = Path.Combine(sanctionedRoot, "..", "sibling", "file.cs");

        try
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server: null,
                candidate,
                CancellationToken.None,
                securityOptions: new SecurityOptions
                {
                    SanctionedRoots = [sanctionedRoot],
                    AllowRootExpansion = true,
                },
                expandSanctionedRoots: true);

            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(siblingRoot, "file.cs")),
                ClientRootPathValidator.ResolvePath(candidate));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(testRoot);
        }
    }

    // ───────── ValidatePathAgainstRootsAsync direct-call seam ─────────

    [TestMethod]
    public async Task ValidatePath_NullServerWithoutOptions_RejectsFailClosed()
    {
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server: null,
                Path.Combine(TestTempRoot.Current, "any", "path"),
                CancellationToken.None));

        StringAssert.Contains(error.Message, "no sanctioned roots are configured");
    }

    // ───────── SecurityOptions fail-open/fail-closed default tests ─────────

    [TestMethod]
    public void SecurityOptions_Default_Is_FailClosed()
    {
        // The trust-boundary default: a fresh SecurityOptions (what BindSecurityOptions supplies
        // when ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN is unset/unparseable) must fail CLOSED. Missing
        // sanctioned-root configuration then rejects the write/edit rather than allowing it.
        Assert.IsFalse(new SecurityOptions().PathValidationFailOpen,
            "PathValidationFailOpen must default to false (fail-closed) on a security-relevant path check.");
    }

    [TestMethod]
    public void SecurityOptions_Default_Has_No_Implicit_Sanctioned_Roots()
    {
        Assert.IsEmpty(new SecurityOptions().SanctionedRoots,
            "The server must not silently trust its process working directory or client roots.");
    }

    [TestMethod]
    public void SecurityOptions_Default_Disables_RequestRootExpansion()
    {
        Assert.IsFalse(new SecurityOptions().AllowRootExpansion,
            "A request parameter alone must never widen the server-owned boundary.");
    }

    [TestMethod]
    public void SecurityOptions_FailOpen_Is_Opt_In()
    {
        // The escape hatch is explicit-only: fail-open is reachable solely by setting the value
        // to true (mirrors ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true). A non-empty configured
        // boundary remains authoritative even when this compatibility escape hatch is enabled.
        Assert.IsTrue(new SecurityOptions { PathValidationFailOpen = true }.PathValidationFailOpen,
            "Fail-open must remain reachable via explicit opt-in.");
    }

    [TestMethod]
    public void SecurityOptionsEnvironmentBinder_ParsesRootsAndExplicitSecurityOptIns()
    {
        var separator = Path.PathSeparator.ToString();
        var options = SecurityOptionsEnvironmentBinder.Bind(
            $" first {separator}{separator} second ",
            "true",
            "true");

        CollectionAssert.AreEqual(new[] { "first", "second" }, options.SanctionedRoots.ToArray());
        Assert.IsTrue(options.PathValidationFailOpen);
        Assert.IsTrue(options.AllowRootExpansion);
    }

    [TestMethod]
    public void SecurityOptionsEnvironmentBinder_InvalidOrMissingValuesRemainFailClosed()
    {
        var options = SecurityOptionsEnvironmentBinder.Bind(
            null,
            "not-a-boolean",
            "not-a-boolean");

        Assert.IsEmpty(options.SanctionedRoots);
        Assert.IsFalse(options.PathValidationFailOpen);
        Assert.IsFalse(options.AllowRootExpansion);
    }

    [TestMethod]
    public async Task ValidatePath_EmptyConfiguredRoots_RejectsUnlessFailOpen()
    {
        var path = Path.Combine(TestTempRoot.Current, "rmcp-empty-boundary", "file.cs");

        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server: null,
                path,
                CancellationToken.None,
                securityOptions: new SecurityOptions()));
        StringAssert.Contains(error.Message, "no sanctioned roots are configured");

        await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
            server: null,
            path,
            CancellationToken.None,
            securityOptions: new SecurityOptions { PathValidationFailOpen = true });
    }

    [TestMethod]
    public async Task ValidatePath_ConfiguredRoots_AreAuthoritative_AndRequestRootsOnlyNarrow()
    {
        var configuredRoot = Path.Combine(TestTempRoot.Current, "rmcp-configured-" + Guid.NewGuid().ToString("N"));
        var configuredChild = Path.Combine(configuredRoot, "src", "file.cs");
        var outsideRoot = Path.Combine(TestTempRoot.Current, "rmcp-outside-" + Guid.NewGuid().ToString("N"));
        var outsideFile = Path.Combine(outsideRoot, "file.cs");
        var options = new SecurityOptions
        {
            SanctionedRoots = [configuredRoot],
            PathValidationFailOpen = true,
        };

        await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
            server: null,
            configuredChild,
            CancellationToken.None,
            securityOptions: options);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server: null,
                outsideFile,
                CancellationToken.None,
                securityOptions: options,
                narrowingRootPaths: [outsideRoot]));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server: null,
                configuredChild,
                CancellationToken.None,
                securityOptions: options,
                narrowingRootPaths: [Path.Combine(configuredRoot, "other")]));
    }

    [TestMethod]
    public async Task ValidatePath_RequestExpansionCannotWidenWithoutOperatorOptIn()
    {
        var testRoot = Path.Combine(TestTempRoot.Current, "rmcp-request-expansion-" + Guid.NewGuid().ToString("N"));
        var sanctionedRoot = Path.Combine(testRoot, "main");
        var siblingFile = Path.Combine(testRoot, "sibling", "file.cs");

        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server: null,
                siblingFile,
                CancellationToken.None,
                securityOptions: new SecurityOptions { SanctionedRoots = [sanctionedRoot] },
                expandSanctionedRoots: true));

        StringAssert.Contains(error.Message, "outside the configured sanctioned-root boundary");
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
        var path = Path.Combine(TestTempRoot.Current, "RoslynMcp", "..", "RoslynMcp", "future.cs");

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
                Path.Combine(TestTempRoot.Current, "future.cs"),
                cancellation.Token));
    }
}
