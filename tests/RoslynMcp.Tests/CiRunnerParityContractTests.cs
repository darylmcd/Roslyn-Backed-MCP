namespace RoslynMcp.Tests;

/// <summary>
/// Guards executable CI topology rather than accepting matching prose anywhere
/// in the workflow. Job- and step-scoped assertions keep comments or unrelated
/// jobs from satisfying the merge/publish parity contract accidentally.
/// </summary>
[TestClass]
public sealed class CiRunnerParityContractTests
{
    private static readonly System.Text.RegularExpressions.Regex _actionReferencePattern = new(
        @"^\s*(?:-\s*)?uses:\s*(?<reference>[^\s#]+)",
        System.Text.RegularExpressions.RegexOptions.Multiline |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string LoadCiWorkflow()
        => LoadRepositoryFile(".github", "workflows", "ci.yml");

    [TestMethod]
    public void Validate_FansOutOverTheRoutedMatrixWithExplicitLegTimeouts()
    {
        var workflow = LoadCiWorkflow();
        var route = GetJobBlock(workflow, "route");
        var validate = GetJobBlock(workflow, "validate");

        StringAssert.Contains(route, "runner_matrix: ${{ steps.decide.outputs.runner_matrix }}");
        StringAssert.Contains(route, "docs_only: ${{ steps.decide.outputs.docs_only }}");
        StringAssert.Contains(route, "[ValidateSet('ubuntu-latest', 'windows-latest')]");
        StringAssert.Contains(validate, "leg: ${{ fromJSON(needs.route.outputs.runner_matrix) }}");
        StringAssert.Contains(validate, "runs-on: ${{ matrix.leg.runs_on }}");
        StringAssert.Contains(validate, "timeout-minutes: ${{ matrix.leg.timeout_minutes }}");
        StringAssert.Contains(validate, "fail-fast: false");
        Assert.IsFalse(
            validate.Contains("matrix.leg.primary", StringComparison.Ordinal),
            "Artifact ownership must not double as the timeout/routing policy.");
    }

    [TestMethod]
    public void PullRequestTopology_UsesFourCompleteHostedWindowsAndTwoLinuxShards()
    {
        var decide = GetNamedStepBlock(GetJobBlock(LoadCiWorkflow(), "route"), "Decide validation topology");

        for (var shardIndex = 0; shardIndex < 4; shardIndex++)
        {
            Assert.AreEqual(
                1,
                CountOccurrences(
                    decide,
                    $"New-Leg -Name 'windows-hosted-{shardIndex + 1}-of-4'"));
            Assert.AreEqual(
                1,
                CountOccurrences(
                    decide,
                    "-RunsOn 'windows-latest' -ArtifactOwner $false " +
                    $"-TimeoutMinutes 45 -TestShardIndex {shardIndex} -TestShardCount 4"));
        }

        Assert.AreEqual(1, CountOccurrences(decide, "New-Leg -Name 'linux-1-of-2'"));
        Assert.AreEqual(1, CountOccurrences(decide, "New-Leg -Name 'linux-2-of-2'"));
        Assert.AreEqual(2, CountOccurrences(decide, "-TestShardIndex 0 -TestShardCount 2"));
        Assert.AreEqual(2, CountOccurrences(decide, "-TestShardIndex 1 -TestShardCount 2"));

        // One Linux shard owns artifacts in each of the code and policy-doc routes.
        Assert.AreEqual(2, CountOccurrences(
            decide,
            "-RunsOn 'ubuntu-latest' -ArtifactOwner $true -TimeoutMinutes 30 -TestShardIndex 0 -TestShardCount 2"));
        Assert.AreEqual(1, CountOccurrences(decide, "New-Leg -Name 'linux-full'"));
    }

    [TestMethod]
    public void Workflow_UsesOnlyHostedRunners()
    {
        var workflow = LoadCiWorkflow();
        var decide = GetNamedStepBlock(GetJobBlock(workflow, "route"), "Decide validation topology");

        StringAssert.Contains(
            decide,
            "Write-Route -Matrix $codePullRequest -Reason 'Code PR: four hosted Windows and two hosted Linux shards.'");
        Assert.IsFalse(workflow.Contains("self-hosted", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(workflow.Contains("roslynmcp-dev", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(workflow.Contains("RUNNER_STATUS_PAT", StringComparison.Ordinal));
        Assert.IsFalse(workflow.Contains("/actions/runners", StringComparison.Ordinal));
        Assert.IsFalse(workflow.Contains("secrets.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workflows_PinEveryExternalActionToAFullCommitSha()
    {
        foreach (var relativePath in new[]
        {
            new[] { ".github", "workflows", "ci.yml" },
            new[] { ".github", "workflows", "publish-nuget.yml" },
        })
        {
            var workflow = LoadRepositoryFile(relativePath);
            var references = _actionReferencePattern.Matches(workflow)
                .Select(match => match.Groups["reference"].Value)
                .ToArray();
            Assert.IsTrue(references.Length > 0, $"Workflow '{string.Join('/', relativePath)}' has no action references.");

            foreach (var reference in references)
            {
                if (reference.StartsWith("./", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.IsTrue(
                    System.Text.RegularExpressions.Regex.IsMatch(
                        reference,
                        @"\A[^@\s]+@[0-9a-f]{40}\z",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant),
                    $"External action '{reference}' must use a reviewed full commit SHA.");
            }
        }
    }

    [TestMethod]
    public void PolicyDocsDetection_IsFailClosedAndRoutesCompleteLinuxTestShards()
    {
        var workflow = LoadCiWorkflow();
        var decide = GetNamedStepBlock(GetJobBlock(workflow, "route"), "Decide validation topology");
        var validate = GetJobBlock(workflow, "validate");
        var releaseStep = GetNamedStepBlock(validate, "Verify release build (pull request)");

        StringAssert.Contains(decide, "gh api `");
        StringAssert.Contains(decide, "--paginate `");
        StringAssert.Contains(decide, "--slurp `");
        StringAssert.Contains(decide, "/pulls/$pullRequestNumber/files?per_page=100");
        StringAssert.Contains(decide, "$file.previous_filename");
        StringAssert.Contains(
            decide,
            "Write-Host \"Enumerated $enumeratedFileCount pull-request file records and $($changed.Count) unique current/original paths.\"");
        Assert.IsFalse(
            decide.Contains("Changed current/original paths:", StringComparison.Ordinal),
            "Attacker-controlled file names must not be emitted as raw workflow log lines.");
        StringAssert.Contains(decide, "if ($LASTEXITCODE -ne 0)");
        StringAssert.Contains(decide, "throw \"Could not enumerate pull-request files and rename origins");
        StringAssert.Contains(decide, "$reportedChangedFileCount = [int]'${{ github.event.pull_request.changed_files }}'");
        StringAssert.Contains(decide, "$enumeratedFileCount++");
        StringAssert.Contains(
            decide,
            "if ($enumeratedFileCount -ge 3000 -or $enumeratedFileCount -ne $reportedChangedFileCount)");
        StringAssert.Contains(decide, "route full validation because the API result may be capped");
        const string behaviorBearingMarkdownPattern =
            @"(^CHANGELOG\.md$|^(skills|\.claude/skills|agents|\.claude/agents|\.github/prompts)/)";
        StringAssert.Contains(
            decide,
            "$behaviorBearingMarkdownPattern = `\n              '" + behaviorBearingMarkdownPattern + "'");
        foreach (var path in new[]
        {
            "CHANGELOG.md",
            "skills/review/SKILL.md",
            ".claude/skills/release-cut/SKILL.md",
            "agents/audit-phase-runner.md",
            ".claude/agents/pr-reconciler.md",
            ".github/prompts/review.md",
        })
        {
            Assert.IsTrue(
                System.Text.RegularExpressions.Regex.IsMatch(path, behaviorBearingMarkdownPattern),
                $"Behavior-bearing Markdown path '{path}' must force full validation.");
        }
        Assert.IsFalse(
            System.Text.RegularExpressions.Regex.IsMatch("docs/setup.md", behaviorBearingMarkdownPattern));
        var completeEnumerationIndex = decide.IndexOf(
            "if ($enumeratedFileCount -ge 3000 -or $enumeratedFileCount -ne $reportedChangedFileCount)",
            StringComparison.Ordinal);
        var docsDecisionIndex = decide.IndexOf(
            "$docsOnly = $changed.Count -gt 0 -and $nonDocs.Count -eq 0",
            StringComparison.Ordinal);
        Assert.IsTrue(
            docsDecisionIndex > completeEnumerationIndex,
            "Docs-only routing must require a complete file enumeration.");
        StringAssert.Contains(decide, "Write-Route -Matrix $docsOnlyPullRequest");
        Assert.AreEqual(1, CountOccurrences(decide, "New-Leg -Name 'docs-linux-1-of-2'"));
        Assert.AreEqual(1, CountOccurrences(decide, "New-Leg -Name 'docs-linux-2-of-2'"));
        StringAssert.Contains(
            decide,
            "Write-Route -Matrix $docsOnlyPullRequest -Reason 'Policy-only docs PR: two hosted Linux test shards.'");
        StringAssert.Contains(validate, "if: matrix.leg.artifact_owner == true");
        StringAssert.Contains(validate, "if: github.event_name == 'pull_request'");
        StringAssert.Contains(validate, "'${{ needs.route.outputs.docs_only }}' -eq 'true'");
        Assert.IsFalse(
            releaseStep.Contains("if: needs.route.outputs.docs_only != 'true'", StringComparison.Ordinal),
            "Policy-only documentation must still execute the complete test class set.");

        StringAssert.Contains(workflow, "permissions:\n  contents: read\n  pull-requests: read\n");
    }

    [TestMethod]
    public void PullRequestAndLocalCi_UseTheDocumentedReleaseLane()
    {
        var workflow = LoadCiWorkflow();
        var validate = GetJobBlock(workflow, "validate");
        var releaseStep = GetNamedStepBlock(validate, "Verify release build (pull request)");
        var justfile = LoadRepositoryFile("justfile");

        StringAssert.Contains(releaseStep, "Configuration = 'Release'");
        StringAssert.Contains(releaseStep, "NoCoverage = $true");
        StringAssert.Contains(releaseStep, "ExcludeNetworkTests = $true");
        StringAssert.Contains(releaseStep, "TestShardIndex = ${{ matrix.leg.test_shard_index }}");
        StringAssert.Contains(releaseStep, "TestShardCount = ${{ matrix.leg.test_shard_count }}");
        StringAssert.Contains(releaseStep, "if ('${{ matrix.leg.artifact_owner }}' -ne 'true' -or");
        StringAssert.Contains(releaseStep, "'${{ needs.route.outputs.docs_only }}' -eq 'true')");
        StringAssert.Contains(releaseStep, "$parameters.TestShardOnly = $true");
        StringAssert.Contains(releaseStep, "./eng/verify-release.ps1 @parameters");
        Assert.IsFalse(releaseStep.Contains("--filter", StringComparison.Ordinal));

        StringAssert.Contains(justfile, "ci: verify-docs verify-skills verify-release-pr vuln-audit");
        StringAssert.Contains(
            justfile,
            "verify-release-pr:\n" +
            "    pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests");
        StringAssert.Contains(justfile, "full: verify-docs verify-skills verify-release vuln-audit");
    }

    [TestMethod]
    public void DeclaredSdkFloor_HasAnExactBoundedCompatibilityJob()
    {
        var workflow = LoadCiWorkflow();
        var floorJob = GetJobBlock(workflow, "sdk_floor");
        var gate = GetJobBlock(workflow, "validate-gate");
        using var globalJson = System.Text.Json.JsonDocument.Parse(LoadRepositoryFile("global.json"));
        var declaredFloor = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();

        Assert.AreEqual("10.0.400", declaredFloor);
        StringAssert.Contains(floorJob, "name: sdk-floor (10.0.400)");
        StringAssert.Contains(floorJob, "\"DOTNET_INSTALL_DIR=$($env:RUNNER_TEMP)/dotnet-floor\" >> $env:GITHUB_ENV");
        StringAssert.Contains(floorJob, "\"DOTNET_MULTILEVEL_LOOKUP=0\" >> $env:GITHUB_ENV");
        StringAssert.Contains(floorJob, "dotnet-version: 10.0.400");
        StringAssert.Contains(
            floorJob,
            "if: github.event_name != 'pull_request' || needs.route.outputs.docs_only != 'true'");
        StringAssert.Contains(floorJob, "if ($actual -cne '10.0.400')");
        StringAssert.Contains(floorJob, "dotnet restore RoslynMcp.slnx --nologo");
        StringAssert.Contains(floorJob, "dotnet build RoslynMcp.slnx -c Release --no-restore --nologo");
        StringAssert.Contains(
            floorJob,
            "FullyQualifiedName=RoslynMcp.Tests.IntegrationTests_WorkspaceCore.Workspace_Load_Returns_WorkspaceId_And_Metadata");
        StringAssert.Contains(floorJob, "--settings eng/ci.runsettings");
        Assert.AreEqual(1, CountOccurrences(floorJob, "dotnet test "));
        StringAssert.Contains(gate, "- sdk_floor\n");
        StringAssert.Contains(gate, "${{ needs.sdk_floor.result }}");
        StringAssert.Contains(gate, "$docsOnly = '${{ needs.route.outputs.docs_only }}' -eq 'true'");
        StringAssert.Contains(
            gate,
            "$sdkFloorSucceeded = $sdkFloorResult -eq 'success' -or ($docsOnly -and $sdkFloorResult -eq 'skipped')");
    }

    [TestMethod]
    public void TestResults_AreRetainedPerLegAndReleaseArtifactsHaveOneOwner()
    {
        var validate = GetJobBlock(LoadCiWorkflow(), "validate");
        var resultsSummary = GetNamedStepBlock(validate, "Summarize test timings");
        var resultsUpload = GetNamedStepBlock(validate, "Upload test results");
        var coverageSummary = GetNamedStepBlock(validate, "Generate coverage HTML summary");
        var hostUpload = GetNamedStepBlock(validate, "Upload published host artifact");
        var manifestUpload = GetNamedStepBlock(validate, "Upload release manifest");
        var coverageUpload = GetNamedStepBlock(validate, "Upload code coverage");

        StringAssert.Contains(resultsSummary, "if: always()");
        StringAssert.Contains(
            resultsSummary,
            "./eng/summarize-test-results.ps1 -ResultsPath artifacts/test-results -OutputPath $env:GITHUB_STEP_SUMMARY");
        StringAssert.Contains(resultsUpload, "if: always()");
        StringAssert.Contains(resultsUpload, "name: test-results-${{ matrix.leg.name }}");
        StringAssert.Contains(resultsUpload, "path: artifacts/test-results");
        StringAssert.Contains(coverageSummary, "-ErrorAction Stop");
        Assert.IsFalse(coverageSummary.Contains("SilentlyContinue", StringComparison.Ordinal));
        foreach (var ownedUpload in new[] { hostUpload, manifestUpload, coverageUpload })
        {
            StringAssert.Contains(ownedUpload, "matrix.leg.artifact_owner == true");
        }
    }

    [TestMethod]
    public void ValidateGate_UsesTheRequiredNameOnlyForPullRequests()
    {
        var workflow = LoadCiWorkflow();
        var gate = GetJobBlock(workflow, "validate-gate");

        StringAssert.Contains(
            gate,
            "name: ${{ github.event_name == 'pull_request' && 'validate' || 'validate-informational' }}");
        StringAssert.Contains(gate, "- route\n");
        StringAssert.Contains(gate, "- validate\n");
        StringAssert.Contains(gate, "- sdk_floor\n");
        StringAssert.Contains(gate, "if: always()");
        StringAssert.Contains(gate, "${{ needs.route.result }}");
        StringAssert.Contains(gate, "${{ needs.validate.result }}");
        Assert.AreEqual(1, CountOccurrences(workflow, "'validate-informational'"));
        Assert.IsFalse(
            workflow.Contains("\n    name: validate\n", StringComparison.Ordinal),
            "A static validate name would let dispatch/schedule runs report the required PR context.");
    }

    [TestMethod]
    public void ReleasePolicy_StatesTheCanonicalCiTriggerContract()
    {
        var policy = LoadRepositoryFile("docs", "release-policy.md");
        var normalizedPolicy = policy.Replace('\n', ' ');

        StringAssert.Contains(normalizedPolicy, "CI runs on pull requests, manual dispatch, and the weekly schedule.");
        StringAssert.Contains(
            normalizedPolicy,
            "Push-to-`main` is intentionally omitted because protected-branch changes arrive through a validated PR;");
        StringAssert.Contains(normalizedPolicy, "see `CI_POLICY.md` for the canonical trigger and runner contract.");
        Assert.IsFalse(policy.Contains("CI runs on every PR and `main`", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PublicReleaseDocs_UseCanonicalVulnerabilityAuditVerifier()
    {
        var releasePolicy = LoadRepositoryFile("docs", "release-policy.md");
        var upgradeMatrix = LoadRepositoryFile("docs", "upgrade-matrix.md");

        foreach (var document in new[] { releasePolicy, upgradeMatrix })
        {
            StringAssert.Contains(document, "eng/verify-nuget-audit.ps1");
            Assert.IsFalse(document.Contains("dotnet package list", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string GetJobBlock(string workflow, string jobId)
    {
        var lines = workflow.Split('\n');
        var header = $"  {jobId}:";
        var start = Array.FindIndex(lines, line => string.Equals(line, header, StringComparison.Ordinal));
        Assert.IsTrue(start >= 0, $"Workflow job '{jobId}' was not found.");

        var end = lines.Length;
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length > 2 &&
                line.StartsWith("  ", StringComparison.Ordinal) &&
                !char.IsWhiteSpace(line[2]) &&
                line[2] != '#' &&
                line.EndsWith(':'))
            {
                end = index;
                break;
            }
        }

        return string.Join('\n', lines[start..end]) + "\n";
    }

    private static string GetNamedStepBlock(string job, string stepName)
    {
        var lines = job.Split('\n');
        var header = $"      - name: {stepName}";
        var start = Array.FindIndex(lines, line => string.Equals(line, header, StringComparison.Ordinal));
        Assert.IsTrue(start >= 0, $"Workflow step '{stepName}' was not found.");

        var end = lines.Length;
        for (var index = start + 1; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("      - name: ", StringComparison.Ordinal))
            {
                end = index;
                break;
            }
        }

        return string.Join('\n', lines[start..end]) + "\n";
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string LoadRepositoryFile(params string[] relativePathSegments)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var path = Path.Combine([repositoryRoot, .. relativePathSegments]);
        return File.ReadAllText(path).ReplaceLineEndings("\n");
    }
}
