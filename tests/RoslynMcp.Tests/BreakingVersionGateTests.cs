using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BreakingVersionGateTests
{
    [TestMethod]
    public void ReleaseWorkflows_InvokePowerShellGatesInRequiredOrder()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var skillPath = Path.Combine(repositoryRoot, ".claude", "skills", "bump", "SKILL.md");
        var skill = File.ReadAllText(skillPath);
        var fragmentVerifier = "pwsh -NoProfile -File ./eng/verify-changelog-fragments.ps1";
        var breakingVerifier =
            "pwsh -NoProfile -File ./eng/verify-breaking-version-bump.ps1 -BumpType \"$ARGUMENTS\"";
        var versionVerifier = "pwsh -NoProfile -File ./eng/verify-version-drift.ps1";
        var firstMutation = "touch .release-managed-edit-allowed";

        StringAssert.Contains(skill, fragmentVerifier);
        StringAssert.Contains(skill, breakingVerifier);
        StringAssert.Contains(skill, versionVerifier);
        Assert.IsTrue(
            skill.IndexOf(fragmentVerifier, StringComparison.Ordinal) <
            skill.IndexOf(firstMutation, StringComparison.Ordinal),
            "The fragment verifier must run through PowerShell before the first mutation.");
        Assert.IsTrue(
            skill.IndexOf(breakingVerifier, StringComparison.Ordinal) <
            skill.IndexOf(firstMutation, StringComparison.Ordinal),
            "The breaking-version verifier must run through PowerShell before the first mutation.");

        var releaseVerifierPath = Path.Combine(repositoryRoot, "eng", "verify-release.ps1");
        var releaseVerifier = File.ReadAllText(releaseVerifierPath);
        var fragmentGateIndex = releaseVerifier.IndexOf(
            "'verify-changelog-fragments.ps1'",
            StringComparison.Ordinal);
        var breakingGateIndex = releaseVerifier.IndexOf(
            "'verify-breaking-version-bump.ps1'",
            StringComparison.Ordinal);

        Assert.IsTrue(fragmentGateIndex >= 0, "verify-release.ps1 must validate fragment syntax.");
        Assert.IsTrue(
            breakingGateIndex > fragmentGateIndex,
            "verify-release.ps1 must run the breaking-version check after fragment syntax validation.");
        StringAssert.Contains(
            releaseVerifier,
            "-ScriptPath (Join-Path $PSScriptRoot 'verify-breaking-version-bump.ps1')");
        StringAssert.Contains(
            releaseVerifier,
            "-Parameters @{ RequireConsumedFragments = $true }",
            "Publish validation must pass the consumed-fragment requirement through the checked child-script wrapper.");

        var publishWorkflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "publish-nuget.yml");
        var publishWorkflow = File.ReadAllText(publishWorkflowPath);
        var workflowGateCommand =
            "./eng/verify-release.ps1 -Configuration Release -NoCoverage -RequireConsumedFragments";
        var workflowGateIndex = publishWorkflow.IndexOf(workflowGateCommand, StringComparison.Ordinal);
        var workflowPackIndex = publishWorkflow.IndexOf("- name: Pack NuGet package", StringComparison.Ordinal);
        Assert.IsTrue(workflowGateIndex >= 0, "The publish workflow must require consumed fragments.");
        Assert.IsTrue(
            workflowPackIndex > workflowGateIndex,
            "The publish workflow must validate the release contract before packaging.");

        var manualPublisherPath = Path.Combine(repositoryRoot, "eng", "publish-nuget.ps1");
        var manualPublisher = File.ReadAllText(manualPublisherPath);
        var manualGateIndex = manualPublisher.IndexOf(
            "'verify-release.ps1'",
            StringComparison.Ordinal);
        var manualGateFragmentIndex = manualPublisher.IndexOf(
            "-RequireConsumedFragments",
            StringComparison.Ordinal);
        var manualPackIndex = manualPublisher.IndexOf("dotnet pack", StringComparison.Ordinal);
        var manualPushIndex = manualPublisher.IndexOf("dotnet nuget push", StringComparison.Ordinal);
        Assert.IsTrue(
            manualGateIndex >= 0,
            "The manual publisher must run the publish-mode release gate (verify-release.ps1).");
        Assert.IsTrue(manualGateFragmentIndex >= 0, "The manual publisher must require consumed fragments.");
        Assert.IsTrue(
            manualPackIndex > manualGateIndex,
            "The manual publisher must validate the release contract before packing a package.");
        Assert.IsTrue(
            manualPushIndex > manualGateIndex,
            "The manual publisher must validate the release contract before pushing a package.");
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task BreakingVersionGate_EnforcesPendingAndConsumedMajorVersionContracts()
    {
        var cases = new[]
        {
            new GateCase(
                "clean patch",
                BumpType: "patch",
                FragmentCategory: null,
                TopVersion: "3.0.1",
                PrecedingVersion: "3.0.0",
                TopHasBreakingSection: false,
                RequireConsumedFragments: false,
                ExpectedExitCode: 0,
                ExpectedText: "Breaking-version release gate verified"),
            new GateCase(
                "clean minor",
                BumpType: "minor",
                FragmentCategory: null,
                TopVersion: "3.1.0",
                PrecedingVersion: "3.0.0",
                TopHasBreakingSection: false,
                RequireConsumedFragments: false,
                ExpectedExitCode: 0,
                ExpectedText: "Breaking-version release gate verified"),
            new GateCase(
                "breaking patch",
                BumpType: "patch",
                FragmentCategory: "Changed — BREAKING",
                TopVersion: "3.0.0",
                PrecedingVersion: "2.3.8",
                TopHasBreakingSection: false,
                RequireConsumedFragments: false,
                ExpectedExitCode: 1,
                ExpectedText: "require a major bump"),
            new GateCase(
                "breaking minor",
                BumpType: "minor",
                FragmentCategory: "Changed — BREAKING",
                TopVersion: "3.0.0",
                PrecedingVersion: "2.3.8",
                TopHasBreakingSection: false,
                RequireConsumedFragments: false,
                ExpectedExitCode: 1,
                ExpectedText: "require a major bump"),
            new GateCase(
                "breaking major",
                BumpType: "major",
                FragmentCategory: "Changed — BREAKING",
                TopVersion: "3.0.0",
                PrecedingVersion: "2.3.8",
                TopHasBreakingSection: false,
                RequireConsumedFragments: false,
                ExpectedExitCode: 0,
                ExpectedText: "permit requested major bump"),
            new GateCase(
                "ordinary validation allows pending breaking fragment",
                BumpType: null,
                FragmentCategory: "Changed — BREAKING",
                TopVersion: "3.0.0",
                PrecedingVersion: "2.3.8",
                TopHasBreakingSection: false,
                RequireConsumedFragments: false,
                ExpectedExitCode: 0,
                ExpectedText: "Pending breaking fragments require the next bump to be major"),
            new GateCase(
                "publish rejects pending non-breaking fragment",
                BumpType: null,
                FragmentCategory: "Changed",
                TopVersion: "3.0.0",
                PrecedingVersion: "2.3.8",
                TopHasBreakingSection: false,
                RequireConsumedFragments: true,
                ExpectedExitCode: 1,
                ExpectedText: "Refusing publish validation"),
            new GateCase(
                "publish accepts consumed fragments",
                BumpType: null,
                FragmentCategory: null,
                TopVersion: "3.0.0",
                PrecedingVersion: "2.3.8",
                TopHasBreakingSection: false,
                RequireConsumedFragments: true,
                ExpectedExitCode: 0,
                ExpectedText: "Breaking-version release gate verified"),
            new GateCase(
                "consumed without major advance",
                BumpType: null,
                FragmentCategory: null,
                TopVersion: "3.1.0",
                PrecedingVersion: "3.0.0",
                TopHasBreakingSection: true,
                RequireConsumedFragments: true,
                ExpectedExitCode: 1,
                ExpectedText: "does not advance the major"),
            new GateCase(
                "consumed with major advance",
                BumpType: null,
                FragmentCategory: null,
                TopVersion: "4.0.0",
                PrecedingVersion: "3.0.0",
                TopHasBreakingSection: true,
                RequireConsumedFragments: true,
                ExpectedExitCode: 0,
                ExpectedText: "Breaking-version release gate verified"),
        };

        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "eng", "verify-breaking-version-bump.ps1");

        foreach (var testCase in cases)
        {
            var fixtureRoot = Path.Combine(
                TestTempRoot.Current,
                "BreakingVersionGate",
                Guid.NewGuid().ToString("N"));

            try
            {
                WriteFixture(fixtureRoot, testCase);
                var result = await RunGateAsync(
                    scriptPath,
                    fixtureRoot,
                    testCase.BumpType,
                    testCase.RequireConsumedFragments);
                var diagnostic = $"{testCase.Name}: stdout={result.StdOut} stderr={result.StdErr}";

                // Normalize: PowerShell's error formatter wraps at console width, so the
                // expected phrase splits across a '|' gutter at a host-dependent column.
                var normalizedOutput = PowerShellOutputNormalizer.Normalize(
                    result.StdOut + result.StdErr);

                Assert.AreEqual(testCase.ExpectedExitCode, result.ExitCode, diagnostic);
                StringAssert.Contains(normalizedOutput, testCase.ExpectedText, diagnostic);
            }
            finally
            {
                TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
            }
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task BreakingVersionGate_MajorSurfacesBodyAndWarnsOnlyForNonBreakingFamilySiblings()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "eng", "verify-breaking-version-bump.ps1");
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(BreakingVersionGateTests),
            Guid.NewGuid().ToString("N"));
        var testCase = new GateCase(
            "breaking family",
            BumpType: "major",
            FragmentCategory: "Changed — BREAKING",
            TopVersion: "4.0.0",
            PrecedingVersion: "3.0.0",
            TopHasBreakingSection: false,
            RequireConsumedFragments: false,
            ExpectedExitCode: 0,
            ExpectedText: "Breaking fragments permit requested major bump");

        try
        {
            WriteFixture(fixtureRoot, testCase);
            var fragmentDirectory = Path.Combine(fixtureRoot, "changelog.d");
            var breakingPath = Path.Combine(fragmentDirectory, "contract-family-breaking.md");
            File.Move(Path.Combine(fragmentDirectory, "pending.md"), breakingPath);
            WriteFragment(
                Path.Combine(fragmentDirectory, "contract-family-safe.md"),
                "Fixed",
                "Sibling safe change.");

            var familyResult = await RunGateAsync(scriptPath, fixtureRoot, "major", false);
            var familyOutput = PowerShellOutputNormalizer.Normalize(familyResult.AllOutput);
            Assert.AreEqual(0, familyResult.ExitCode, familyResult.AllOutput);
            StringAssert.Contains(familyOutput, "contract-family-breaking.md");
            StringAssert.Contains(familyOutput, "Fixture contract change.");
            StringAssert.Contains(familyOutput, "Breaking fragment family mismatch");
            StringAssert.Contains(familyOutput, "contract-family-safe.md");

            File.Delete(Path.Combine(fragmentDirectory, "contract-family-safe.md"));
            var loneResult = await RunGateAsync(scriptPath, fixtureRoot, "major", false);
            Assert.AreEqual(0, loneResult.ExitCode, loneResult.AllOutput);
            Assert.IsFalse(
                loneResult.AllOutput.Contains("Breaking fragment family mismatch", StringComparison.Ordinal),
                loneResult.AllOutput);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task ManualPublisher_StopsBeforePackagingWhenReleaseGateExitsNonzero()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(BreakingVersionGateTests),
            Guid.NewGuid().ToString("N"));

        try
        {
            var engDirectory = Path.Combine(fixtureRoot, "eng");
            var packageDirectory = Path.Combine(fixtureRoot, "nupkg");
            Directory.CreateDirectory(engDirectory);
            Directory.CreateDirectory(packageDirectory);
            File.Copy(
                Path.Combine(repositoryRoot, "eng", "publish-nuget.ps1"),
                Path.Combine(engDirectory, "publish-nuget.ps1"));
            File.WriteAllText(
                Path.Combine(engDirectory, "verify-release.ps1"),
                "exit 37\n");
            File.WriteAllText(
                Path.Combine(packageDirectory, "Darylmcd.RoslynMcp.9.9.9.nupkg"),
                "not a real package");

            var result = await PwshScriptRunner.RunAsync(
                [
                    "-NoProfile",
                    "-File",
                    Path.Combine(engDirectory, "publish-nuget.ps1"),
                    "-Version",
                    "9.9.9",
                ],
                environment: new Dictionary<string, string?>
                {
                    ["NUGET_API_KEY"] = "fixture-not-a-secret",
                },
                timeout: TimeSpan.FromSeconds(30),
                description: "manual publisher fixture");

            var output = result.AllOutput;
            Assert.AreNotEqual(0, result.ExitCode, output);
            StringAssert.Contains(output, "Release validation failed with exit code 37.");
            Assert.IsFalse(
                output.Contains("Packing ", StringComparison.Ordinal),
                "The publisher continued to packaging after the release gate failed.");
            Assert.IsFalse(
                output.Contains("Pushing ", StringComparison.Ordinal),
                "The publisher continued to pushing after the release gate failed.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static void WriteFixture(string fixtureRoot, GateCase testCase)
    {
        var fragmentDirectory = Path.Combine(fixtureRoot, "changelog.d");
        Directory.CreateDirectory(fragmentDirectory);
        File.WriteAllText(Path.Combine(fragmentDirectory, "README.md"), "# Changelog fragments");

        if (testCase.FragmentCategory is not null)
        {
            File.WriteAllText(
                Path.Combine(fragmentDirectory, "pending.md"),
                $$"""
                ---
                category: {{testCase.FragmentCategory}}
                ---

                - **{{testCase.FragmentCategory}}:** Fixture contract change.
                """);
        }

        var topCategory = testCase.TopHasBreakingSection
            ? "Changed — BREAKING"
            : "Changed";
        File.WriteAllText(
            Path.Combine(fixtureRoot, "CHANGELOG.md"),
            $$"""
            # Changelog

            ## [Unreleased]

            ## [{{testCase.TopVersion}}] - 2026-08-14

            ### {{topCategory}}

            - Fixture release.

            ## [{{testCase.PrecedingVersion}}] - 2026-08-13

            ### Changed

            - Preceding fixture release.
            """);
    }

    private static void WriteFragment(string path, string category, string body)
    {
        File.WriteAllText(
            path,
            $$"""
            ---
            category: {{category}}
            ---

            - **{{category}}:** {{body}}
            """);
    }

    private static Task<PwshScriptResult> RunGateAsync(
        string scriptPath,
        string fixtureRoot,
        string? bumpType,
        bool requireConsumedFragments)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-File",
            scriptPath,
            "-RepoRoot",
            fixtureRoot,
        };
        if (bumpType is not null)
        {
            arguments.Add("-BumpType");
            arguments.Add(bumpType);
        }
        if (requireConsumedFragments)
        {
            arguments.Add("-RequireConsumedFragments");
        }

        return PwshScriptRunner.RunAsync(
            arguments,
            timeout: TimeSpan.FromSeconds(30),
            description: "breaking-version gate");
    }

    private sealed record GateCase(
        string Name,
        string? BumpType,
        string? FragmentCategory,
        string TopVersion,
        string PrecedingVersion,
        bool TopHasBreakingSection,
        bool RequireConsumedFragments,
        int ExpectedExitCode,
        string ExpectedText);
}
