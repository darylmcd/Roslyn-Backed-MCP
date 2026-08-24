# public-repo-self-hosted-runner-isolation — Remove the public runner trust-boundary bypass

**row:** `public-repo-self-hosted-runner-isolation` · **pri:** `Critical` · **size:** `M`

## Anchors

- Repository Actions runner registration and Windows service `actions.runner.darylmcd-Roslyn-Backed-MCP.darylmcd-windows-dev`
- `.github/workflows/ci.yml`
- `docs/self-hosted-runner.md`
- `tests/RoslynMcp.Tests/CiRunnerParityContractTests.cs`

## Acceptance

- [ ] Deregister the repository-level runner from this public repository and stop/disable its `LocalSystem` service, or move execution to an isolated disposable worker behind an infrastructure-enforced trust boundary.
- [ ] If organization/enterprise runner groups are used, restrict repository and workflow access to a base-branch ref or reviewed full SHA; fork-authored workflow YAML cannot select the runner.
- [ ] If a local Windows shard returns, base-controlled automation binds it to an explicitly trusted same-repository head SHA and reports a distinct required check for that SHA.
- [ ] An adversarial fork workflow that directly requests every public runner label cannot obtain the local worker; record the API/job evidence.
- [ ] Run the complete local shard under a dedicated least-privilege service identity with scoped filesystem/tool access; `LocalSystem` is forbidden.

## Evidence

GitHub explicitly warns that public-repository fork pull requests can alter workflow YAML and compromise self-hosted runners. The live runner is repository-level on a personal-account public repository and its Windows service runs automatically as `LocalSystem`. An in-workflow fork predicate is mutable by the attacker and therefore cannot enforce trust. Hosted-only checked-in routing reduces normal use but does not remove the registered runner's reachability.
2026-08-24 live containment evidence: the repository runner (id 22, darylmcd-windows-dev) remained online and idle; its Windows service remained Running, Auto, LocalSystem. Non-elevated Stop-Service and Set-Service attempts failed with access denied, so containment is not complete. Repository fork-workflow approval was tightened to all_external_contributors as secondary defense only; it is not a trust boundary and does not close this row.
