using RoslynMcp.Tests.Helpers;

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
        StringAssert.Contains(validate, "leg: ${{ fromJSON(needs.route.outputs.runner_matrix) }}");
        StringAssert.Contains(validate, "runs-on: ${{ matrix.leg.runs_on }}");
        StringAssert.Contains(validate, "timeout-minutes: ${{ matrix.leg.timeout_minutes }}");
        StringAssert.Contains(validate, "fail-fast: false");
        Assert.IsFalse(
            validate.Contains("matrix.leg.primary", StringComparison.Ordinal),
            "Artifact ownership must not double as the timeout/routing policy.");
    }

    /// <summary>
    /// The route job's leg construction, docs-only classification, and rename/count-mismatch/cap
    /// fail-closed behavior now live in <c>eng/resolve-ci-topology.ps1</c> -- a real process
    /// invoked and table-tested directly by <see cref="CiTopologyDecisionContractTests"/>. This
    /// method stays a workflow-integration sentinel: it proves the `decide` step still fetches
    /// pull-request files, still fails closed on a `gh api` error, still calls the extracted
    /// script with the documented inputs, and still writes its JSON decision to the step outputs
    /// the `validate` job's matrix and `validate-gate`'s docs-only check consume.
    /// </summary>
    [TestMethod]
    public void DecideStep_FetchesPullRequestFilesAndDelegatesToTheTopologyScript()
    {
        var workflow = LoadCiWorkflow();
        var route = GetJobBlock(workflow, "route");
        var decide = GetNamedStepBlock(route, "Decide validation topology");

        StringAssert.Contains(route, "- name: Check out repository");
        StringAssert.Contains(decide, "gh api `");
        StringAssert.Contains(decide, "--paginate `");
        StringAssert.Contains(decide, "--slurp `");
        StringAssert.Contains(decide, "/pulls/$pullRequestNumber/files?per_page=100");
        StringAssert.Contains(decide, "if ($LASTEXITCODE -ne 0)");
        StringAssert.Contains(decide, "throw \"Could not enumerate pull-request files and rename origins");
        StringAssert.Contains(decide, "$reportedChangedFileCount = [int]'${{ github.event.pull_request.changed_files }}'");
        StringAssert.Contains(decide, "& ./eng/resolve-ci-topology.ps1");
        StringAssert.Contains(decide, "-EventName $eventName");
        StringAssert.Contains(decide, "-ChangedFilesJson $changedFilesJson");
        StringAssert.Contains(decide, "-ReportedChangedFileCount $reportedChangedFileCount");
        StringAssert.Contains(decide, "$decision = $decisionJson | ConvertFrom-Json");
        StringAssert.Contains(decide, "docs_only=$($decision.docs_only.ToString().ToLowerInvariant())");
        StringAssert.Contains(decide, "$decision.runner_matrix | ConvertTo-Json -Compress -Depth 6 -AsArray");
        StringAssert.Contains(decide, "\"runner_matrix=$matrixJson\"");
        StringAssert.Contains(decide, "::notice title=Validation route::$($decision.reason)");
        Assert.IsFalse(
            decide.Contains("Changed current/original paths:", StringComparison.Ordinal),
            "Attacker-controlled file names must not be emitted as raw workflow log lines.");

        StringAssert.Contains(workflow, "permissions:\n  contents: read\n  pull-requests: read\n");
    }

    [TestMethod]
    public void Workflow_UsesOnlyHostedRunners()
    {
        var workflow = LoadCiWorkflow();

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

    /// <summary>
    /// The router's docs-only/rename/count-mismatch classification behavior itself is table-tested
    /// in <see cref="CiTopologyDecisionContractTests"/>. This is the surviving integration sentinel:
    /// it proves the `validate` job's steps actually gate on the `route` job's <c>docs_only</c>
    /// output rather than silently ignoring it.
    /// </summary>
    [TestMethod]
    public void ValidateJob_GatesStepsOnTheRoutedDocsOnlyOutput()
    {
        var validate = GetJobBlock(LoadCiWorkflow(), "validate");
        var releaseStep = GetNamedStepBlock(validate, "Verify release build (pull request)");

        StringAssert.Contains(validate, "if: matrix.leg.artifact_owner == true");
        StringAssert.Contains(validate, "if: github.event_name == 'pull_request'");
        StringAssert.Contains(validate, "'${{ needs.route.outputs.docs_only }}' -eq 'true'");
        Assert.IsFalse(
            releaseStep.Contains("if: needs.route.outputs.docs_only != 'true'", StringComparison.Ordinal),
            "Policy-only documentation must still execute the complete test class set.");
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

        StringAssert.Contains(justfile, "ci: verify-docs verify-changed-format verify-actionlint verify-release-pr vuln-audit");
        StringAssert.Contains(
            justfile,
            "verify-release-pr:\n" +
            "    pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests");
        StringAssert.Contains(justfile, "full: verify-docs verify-changed-format verify-actionlint verify-release vuln-audit");
        // The standalone release gate owns this check, so the aggregates must not run it twice.
        var release = LoadRepositoryFile("eng/verify-release.ps1");
        StringAssert.Contains(release, "verify-skills-are-generic.ps1");
        Assert.IsFalse(justfile.Contains("ci: verify-docs verify-skills ", StringComparison.Ordinal));
        Assert.IsFalse(justfile.Contains("full: verify-docs verify-skills ", StringComparison.Ordinal));

        // The changed-file formatter gate is a standalone quality step, deliberately NOT a
        // verify-release child (that script's child set is a separate hard-listed contract). Local
        // `just ci` therefore has to invoke it directly or it would run only in CI.
        var formatStep = GetNamedStepBlock(GetJobBlock(workflow, "quality"), "Verify changed-file formatting");
        StringAssert.Contains(
            formatStep,
            "if: github.event_name == 'pull_request' || github.event_name == 'merge_group'");
        StringAssert.Contains(
            formatStep,
            "./eng/verify-changed-format.ps1 -BaseRef ${{ github.event_name == 'merge_group' && github.event.merge_group.base_sha || format('origin/{0}', github.base_ref) }} -NoRestore");
        StringAssert.Contains(
            justfile,
            "verify-changed-format:\n" +
            "    pwsh -NoProfile -File ./eng/verify-changed-format.ps1");
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
        StringAssert.Contains(floorJob,
            "FullyQualifiedName=RoslynMcp.Tests.ForkNativeAdmissionTests.NativeBuildHost_LateChild_FirstMetadataAndCachedCopy_AreGuarded");
        StringAssert.Contains(floorJob, "--settings eng/ci.runsettings");
        Assert.AreEqual(1, CountOccurrences(floorJob, "dotnet test "));
        StringAssert.Contains(gate, "- sdk_floor\n");
        StringAssert.Contains(gate, "${{ needs.sdk_floor.result }}");
        StringAssert.Contains(gate, "$docsOnly = '${{ needs.route.outputs.docs_only }}' -eq 'true'");
        StringAssert.Contains(
            gate,
            "$sdkFloorSucceeded = if ($docsOnly) { $sdkFloorResult -eq 'skipped' } else { $sdkFloorResult -eq 'success' }");
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
    public void EvidenceRoute_SkipsTheMatrixBehindALintJobThatTheRequiredGateEnforces()
    {
        var workflow = LoadCiWorkflow();
        var route = GetJobBlock(workflow, "route");
        var validate = GetJobBlock(workflow, "validate");
        var lint = GetJobBlock(workflow, "evidence_lint");
        var gate = GetJobBlock(workflow, "validate-gate");

        StringAssert.Contains(route, "evidence_only: ${{ steps.decide.outputs.evidence_only }}");
        StringAssert.Contains(route, "evidence_only=$($decision.evidence_only.ToString().ToLowerInvariant())");
        StringAssert.Contains(validate, "if: needs.route.outputs.evidence_only != 'true'");

        StringAssert.Contains(lint, "needs: route");
        StringAssert.Contains(lint, "if: needs.route.outputs.evidence_only == 'true'");
        StringAssert.Contains(lint, "fetch-depth: 0");
        StringAssert.Contains(lint, "run: ./eng/verify-changelog-fragments.ps1");
        StringAssert.Contains(lint, "run: ./eng/verify-ai-docs.ps1");
        Assert.IsFalse(
            lint.Contains("setup-dotnet", StringComparison.Ordinal),
            "The evidence route must stay pwsh-only; a .NET setup belongs to the docs or code route.");

        // The required `validate` check must fail closed: evidence routing only passes when the
        // lint job succeeded AND the build/test matrix and SDK floor were skipped, never failed.
        StringAssert.Contains(gate, "- evidence_lint\n");
        StringAssert.Contains(gate, "$evidenceOnly = '${{ needs.route.outputs.evidence_only }}' -eq 'true'");
        StringAssert.Contains(gate, "$evidenceLintResult = '${{ needs.evidence_lint.result }}'");
        StringAssert.Contains(gate, "$evidenceLintResult -ne 'success'");
        StringAssert.Contains(gate, "$validateResult -ne 'skipped'");
        StringAssert.Contains(gate, "$sdkFloorResult -ne 'skipped'");
    }

    [TestMethod]
    public void DocsRoute_RunsTheReleaseVersionGatesThatGuardChangelog()
    {
        var validate = GetJobBlock(LoadCiWorkflow(), "validate");
        var gates = GetNamedStepBlock(validate, "Verify release version gates");

        StringAssert.Contains(
            gates,
            "if: matrix.leg.artifact_owner == true && needs.route.outputs.docs_only == 'true'");
        StringAssert.Contains(gates, "./eng/verify-version-drift.ps1");
        StringAssert.Contains(gates, "./eng/verify-breaking-version-bump.ps1");
    }

    [TestMethod]
    public void ValidateGate_UsesTheRequiredNameOnlyForPullRequestsAndMergeQueue()
    {
        var workflow = LoadCiWorkflow();
        var gate = GetJobBlock(workflow, "validate-gate");

        StringAssert.Contains(
            gate,
            "name: ${{ (github.event_name == 'pull_request' || github.event_name == 'merge_group') && 'validate' || 'validate-informational' }}");
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
    public void MergeQueue_RunsThePullRequestShapedValidation()
    {
        var workflow = LoadCiWorkflow();
        StringAssert.Contains(workflow, "\n  merge_group:\n", "The merge queue must trigger ci so the required validate check reports.");

        var validate = GetJobBlock(workflow, "validate");
        StringAssert.Contains(
            GetNamedStepBlock(validate, "Verify release build (pull request)"),
            "if: github.event_name == 'pull_request' || github.event_name == 'merge_group'");
        StringAssert.Contains(
            GetNamedStepBlock(validate, "Verify release build (pull request)"),
            "CHANGELOG_BASE_SHA: ${{ github.event.merge_group.base_sha }}");
        var format = GetNamedStepBlock(GetJobBlock(workflow, "quality"), "Verify changed-file formatting");
        StringAssert.Contains(format, "github.event.merge_group.base_sha");
        StringAssert.Contains(
            GetNamedStepBlock(validate, "Verify release build (dispatch / schedule)"),
            "if: github.event_name != 'pull_request' && github.event_name != 'merge_group'");
    }

    [TestMethod]
    public void ReleasePolicy_StatesTheCanonicalCiTriggerContract()
    {
        var policy = LoadRepositoryFile("docs", "release-policy.md");
        var normalizedPolicy = policy.Replace('\n', ' ');

        StringAssert.Contains(normalizedPolicy, "CI runs on pull requests, merge-queue groups, manual dispatch, and the weekly schedule.");
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

    [TestMethod]
    public void Quality_IndependentlyRestoresAndPreparesTheFormatterBeforeRunningIt()
    {
        var workflow = LoadCiWorkflow();
        var quality = GetJobBlock(workflow, "quality");
        var validate = GetJobBlock(workflow, "validate");
        var header = quality[..quality.IndexOf("    steps:", StringComparison.Ordinal)];
        StringAssert.Contains(header, "needs: route\n");
        StringAssert.Contains(header, "if: needs.route.outputs.docs_only != 'true'\n");
        StringAssert.Contains(header, "runs-on: ubuntu-latest\n");
        StringAssert.Contains(header, "timeout-minutes: 30\n");
        Assert.IsFalse(header.Contains("validate", StringComparison.Ordinal));
        Assert.IsFalse(header.Contains("sdk_floor", StringComparison.Ordinal));
        StringAssert.Contains(GetNamedStepBlock(quality, "Check out repository"), "fetch-depth: 0");
        var setup = GetNamedStepBlock(quality, "Set up .NET");
        StringAssert.Contains(setup, "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68");
        StringAssert.Contains(setup, "dotnet-version: 10.0.x");

        const string auditCall = "./eng/verify-nuget-audit.ps1 -SolutionPath RoslynMcp.slnx";
        const string formatCall = "./eng/verify-changed-format.ps1";
        Assert.AreEqual(1, CountOccurrences(workflow, auditCall));
        Assert.AreEqual(1, CountOccurrences(workflow, formatCall));
        Assert.IsFalse(validate.Contains(auditCall, StringComparison.Ordinal));
        Assert.IsFalse(validate.Contains(formatCall, StringComparison.Ordinal));
        var audit = GetNamedStepBlock(quality, "Audit packages for known vulnerabilities");
        StringAssert.Contains(audit, "run: " + auditCall);
        Assert.IsFalse(audit.Contains("if:", StringComparison.Ordinal));
        var preparation = GetNamedStepBlock(quality, "Prepare formatter analyzer");
        var format = GetNamedStepBlock(quality, "Verify changed-file formatting");
        const string condition = "if: github.event_name == 'pull_request' || github.event_name == 'merge_group'";
        StringAssert.Contains(preparation, condition);
        StringAssert.Contains(format, condition);
        StringAssert.Contains(preparation,
            "dotnet build analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.csproj -c Debug --no-restore --nologo\n" +
            "          if ($LASTEXITCODE -ne 0) { throw \"Formatter analyzer preparation failed with exit code $LASTEXITCODE.\" }");
        StringAssert.Contains(format, "-NoRestore");
        var auditPosition = quality.IndexOf("      - name: Audit packages for known vulnerabilities\n", StringComparison.Ordinal);
        var preparationPosition = quality.IndexOf("      - name: Prepare formatter analyzer\n", StringComparison.Ordinal);
        var formatPosition = quality.IndexOf("      - name: Verify changed-file formatting\n", StringComparison.Ordinal);
        Assert.IsTrue(auditPosition >= 0 && auditPosition < preparationPosition && preparationPosition < formatPosition);
        StringAssert.Contains(GetJobBlock(workflow, "validate-gate"), "- quality\n");
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("pull_request", "false", "false", "success", "success", "skipped", "success")]
    [DataRow("merge_group", "false", "false", "success", "success", "skipped", "success")]
    [DataRow("workflow_dispatch", "false", "false", "success", "success", "skipped", "success")]
    [DataRow("schedule", "false", "false", "success", "success", "skipped", "success")]
    [DataRow("pull_request", "true", "false", "success", "skipped", "skipped", "skipped")]
    [DataRow("pull_request", "true", "true", "skipped", "skipped", "success", "skipped")]
    public async Task ValidateGate_ExecutesTheRealAggregateAndRejectsEveryInvalidDependencyAsync(
        string eventName, string docsOnly, string evidenceOnly, string matrix, string floor, string lint, string quality)
    {
        var step = GetNamedStepBlock(GetJobBlock(LoadCiWorkflow(), "validate-gate"), "Require routing and every validate leg green");
        var runStart = step.IndexOf("        run: |\n", StringComparison.Ordinal);
        Assert.IsTrue(runStart >= 0);
        var script = string.Join('\n', step[(runStart + "        run: |\n".Length)..]
            .Split('\n').Where(line => line.StartsWith("          ", StringComparison.Ordinal)).Select(line => line[10..]));
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["github.event_name"] = eventName,
            ["needs.route.result"] = "success",
            ["needs.route.outputs.docs_only"] = docsOnly,
            ["needs.route.outputs.evidence_only"] = evidenceOnly,
            ["needs.validate.result"] = matrix,
            ["needs.sdk_floor.result"] = floor,
            ["needs.evidence_lint.result"] = lint,
            ["needs.quality.result"] = quality,
        };
        await AssertAggregateAsync(script, values, succeeds: true);
        foreach (var dependency in new[] { "quality", "route", "validate", "sdk_floor", "evidence_lint" })
        {
            var key = $"needs.{dependency}.result";
            foreach (var result in new[] { "success", "failure", "cancelled", "skipped", "", "unexpected" })
            {
                if (result == values[key])
                {
                    continue;
                }

                var changed = new Dictionary<string, string>(values, StringComparer.Ordinal) { [key] = result };
                await AssertAggregateAsync(script, changed, succeeds: false);
            }
        }

        foreach (var key in new[] { "needs.route.outputs.docs_only", "needs.route.outputs.evidence_only" })
        {
            foreach (var output in new[] { "", "unexpected" })
            {
                var changed = new Dictionary<string, string>(values, StringComparer.Ordinal) { [key] = output };
                await AssertAggregateAsync(script, changed, succeeds: false);
            }
        }

        if (evidenceOnly == "true")
        {
            var contradictoryRoute = new Dictionary<string, string>(values, StringComparer.Ordinal)
            {
                ["needs.route.outputs.docs_only"] = "false",
            };
            await AssertAggregateAsync(script, contradictoryRoute, succeeds: false);
        }
    }

    private static async Task AssertAggregateAsync(string script, IReadOnlyDictionary<string, string> values, bool succeeds)
    {
        foreach (var (key, value) in values)
        {
            script = script.Replace("${{ " + key + " }}", value, StringComparison.Ordinal);
        }

        Assert.IsFalse(script.Contains("${{", StringComparison.Ordinal), "Every Actions expression must be substituted before execution.");
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'\n" + script));
        var result = await PwshScriptRunner.RunAsync(
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            timeout: TimeSpan.FromSeconds(30),
            description: "workflow aggregate contract");
        Assert.AreEqual(succeeds, result.ExitCode == 0,
            $"Aggregate results: {string.Join(", ", values.Select(pair => pair.Key + "=" + pair.Value))}; stdout={result.StdOut}; stderr={result.StdErr}");
    }

    [TestMethod]
    public void NuGetCache_UsesRestoreOnlyAndExplicitPreTestWriters()
    {
        var workflow = LoadCiWorkflow();
        Assert.IsFalse(workflow.Contains("actions/cache@", StringComparison.Ordinal), "Combined cache actions register late post-save compression in every reader.");
        Assert.AreEqual(2, CountOccurrences(workflow, "actions/cache/restore@55cc8345863c7cc4c66a329aec7e433d2d1c52a9"));
        Assert.AreEqual(2, CountOccurrences(workflow, "actions/cache/save@55cc8345863c7cc4c66a329aec7e433d2d1c52a9"));
        StringAssert.Contains(workflow, "NUGET_CACHE_PATH: ~/.nuget/packages");
        Assert.IsFalse(workflow.Contains("cache: true", StringComparison.Ordinal));
        foreach (var jobName in new[] { "validate", "quality" })
        {
            var job = GetJobBlock(workflow, jobName);
            var restore = GetNamedStepBlock(job, "Restore NuGet packages");
            var preparation = GetNamedStepBlock(job, "Prepare NuGet cache for persistence");
            var save = GetNamedStepBlock(job, "Save NuGet packages before tests");
            StringAssert.Contains(restore, "id: nuget_cache");
            StringAssert.Contains(restore, "path: ${{ env.NUGET_CACHE_PATH }}");
            StringAssert.Contains(restore, "key: ${{ runner.os }}-nuget-${{ hashFiles('**/Directory.Packages.props', '**/Directory.Build.props', '**/global.json') }}");
            StringAssert.Contains(restore, "${{ runner.os }}-nuget-");
            StringAssert.Contains(preparation, "id: prepare_nuget_cache");
            StringAssert.Contains(preparation, "steps.nuget_cache.outputs.cache-hit != 'true'");
            StringAssert.Contains(save, "if: steps.prepare_nuget_cache.outcome == 'success'");
            StringAssert.Contains(save, "path: ${{ env.NUGET_CACHE_PATH }}");
            StringAssert.Contains(save, "key: ${{ steps.nuget_cache.outputs.cache-primary-key }}");
            Assert.IsFalse(save.Contains("hashFiles", StringComparison.Ordinal));
            Assert.IsFalse(save.Contains("always()", StringComparison.Ordinal));
            var savePosition = job.IndexOf("      - name: Save NuGet packages before tests", StringComparison.Ordinal);
            Assert.IsTrue(job.IndexOf("      - name: Prepare NuGet cache for persistence", StringComparison.Ordinal) < savePosition);
            if (jobName == "quality")
                Assert.IsTrue(job.IndexOf("      - name: Audit packages for known vulnerabilities", StringComparison.Ordinal) <
                    job.IndexOf("      - name: Prepare NuGet cache for persistence", StringComparison.Ordinal));
            foreach (var downstream in jobName == "validate"
                ? new[] { "Verify release build (pull request)", "Verify release build (dispatch / schedule)" }
                : new[] { "Prepare formatter analyzer", "Verify changed-file formatting" })
                Assert.IsTrue(savePosition < job.IndexOf("      - name: " + downstream, StringComparison.Ordinal));
            StringAssert.Contains(preparation, jobName == "validate"
                ? "./eng/prepare-validation-dependencies.ps1 -RepoRoot $PWD.Path"
                : "./eng/prepare-test-fixtures.ps1");
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    [DataRow("pull_request", "src/Code.cs", 1, 1)]
    [DataRow("merge_group", "", 1, 1)]
    [DataRow("pull_request", "docs/setup.md", 0, 1)]
    [DataRow("pull_request", "ai_docs/reports/probe.md", 0, 0)]
    [DataRow("workflow_dispatch", "", 0, 1)]
    [DataRow("schedule", "", 0, 1)]
    public async Task NuGetCache_ActualPredicatesSelectOneWriterPerRoutedOsAsync(
        string eventName, string changedPath, int windowsWriters, int linuxWriters)
    {
        var workflow = LoadCiWorkflow();
        var validate = GetJobBlock(workflow, "validate");
        var quality = GetJobBlock(workflow, "quality");
        static string Condition(string source) => source.Split('\n').First(line => line.TrimStart().StartsWith("if: ", StringComparison.Ordinal)).Trim()[4..];
        static string Translate(string expression) => expression
            .Replace("steps.nuget_cache.outputs.cache-hit", "$cacheHit", StringComparison.Ordinal)
            .Replace("steps.prepare_nuget_cache.outcome", "$preparationOutcome", StringComparison.Ordinal)
            .Replace("runner.os", "$os", StringComparison.Ordinal)
            .Replace("matrix.leg.test_shard_index", "$leg.test_shard_index", StringComparison.Ordinal)
            .Replace("matrix.leg.artifact_owner", "$leg.artifact_owner", StringComparison.Ordinal)
            .Replace("needs.route.outputs.docs_only", "$docsOnly", StringComparison.Ordinal)
            .Replace("needs.route.outputs.evidence_only", "$evidenceOnly", StringComparison.Ordinal)
            .Replace("== true", "-eq $true", StringComparison.Ordinal)
            .Replace("!=", "-ne", StringComparison.Ordinal).Replace("==", "-eq", StringComparison.Ordinal)
            .Replace("&&", "-and", StringComparison.Ordinal).Replace("||", "-or", StringComparison.Ordinal);
        var matrixCondition = Translate(Condition(GetNamedStepBlock(validate, "Prepare NuGet cache for persistence")));
        var qualityCondition = Translate(Condition(GetNamedStepBlock(quality, "Prepare NuGet cache for persistence")));
        var matrixEnabled = Translate(Condition(validate));
        var qualityEnabled = Translate(Condition(quality));
        var matrixSave = Translate(Condition(GetNamedStepBlock(validate, "Save NuGet packages before tests")));
        var qualitySave = Translate(Condition(GetNamedStepBlock(quality, "Save NuGet packages before tests")));
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $files = if ('{{changedPath}}') { '[{"filename":"{{changedPath}}"}]' } else { '[]' }
            $route = & ./eng/resolve-ci-topology.ps1 -EventName '{{eventName}}' -ChangedFilesJson $files -ReportedChangedFileCount $(if ('{{changedPath}}') { 1 } else { 0 }) | ConvertFrom-Json
            $docsOnly = $route.docs_only.ToString().ToLowerInvariant()
            $evidenceOnly = $route.evidence_only.ToString().ToLowerInvariant()
            foreach ($cacheHit in @('true', 'false', '')) {
                $writers = @{ Windows = 0; Linux = 0 }
                foreach ($leg in $route.runner_matrix) {
                    $os = if ($leg.runs_on -like 'windows-*') { 'Windows' } else { 'Linux' }
                    $selected = ({{matrixEnabled}}) -and ({{matrixCondition}})
                    $preparationOutcome = if ($selected) { 'success' } else { 'skipped' }
                    if ({{matrixSave}}) { $writers[$os]++ }
                }
                $selected = ({{qualityEnabled}}) -and ({{qualityCondition}})
                $preparationOutcome = if ($selected) { 'success' } else { 'skipped' }
                if ({{qualitySave}}) { $writers.Linux++ }
                $expectedWindows = if ($cacheHit -eq 'true') { 0 } else { {{windowsWriters}} }
                $expectedLinux = if ($cacheHit -eq 'true') { 0 } else { {{linuxWriters}} }
                if ($writers.Windows -ne $expectedWindows -or $writers.Linux -ne $expectedLinux) {
                    throw "Writer ownership mismatch: hit='$cacheHit'; Windows=$($writers.Windows); Linux=$($writers.Linux)"
                }
            }
            foreach ($preparationOutcome in @('failure', 'cancelled', 'skipped', '')) {
                if (({{matrixSave}}) -or ({{qualitySave}})) { throw 'Failed or skipped preparation must never save.' }
            }
            """;
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var result = await PwshScriptRunner.RunAsync(
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            workingDirectory: TestFixtureFileSystem.FindRepositoryRoot(),
            timeout: TimeSpan.FromSeconds(30), description: "actual cache ownership predicates");
        Assert.AreEqual(0, result.ExitCode, result.StdOut + result.StdErr);
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
