# render-finding-linux-pwsh-exit-139 — Diagnose and repair the Linux renderer child failure

**row:** `render-finding-linux-pwsh-exit-139` · **pri:** `High` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/Skills/RenderFindingScriptTests.cs:51`
- `skills/mcp-server-surface-test/lib/render-finding.ps1:217`

## Acceptance

- [ ] Preserve first-party PR1848 run40359428470 job121700670090 failure evidence and exact merge/base/software identities; do not classify environment, flake or pre-existing failure without base proof.
- [ ] Use controlled base reproduction and native phase/crash evidence to identify ownership, then repair the proven root cause with a regression observed red before green. A passing isolated control is non-reproduction only.
- [ ] Preserve determinism assertions, output/schema, process budgets and hosted case coverage; no retry-until-green, skip, quarantine, suppression or unrelated harness rewrite as claimed cure.
- [ ] Qualify the actual failing Linux context and retain original failure receipts; state native/runtime evidence limits if a correct repair cannot yet be established.

## Evidence

- Docs-only PR1848 headbe8b9d16fa09377792e7468b9450ed9dca652091 syntheticmerge1a81441fe21a6d72728b3fac06f1e24a1a7f94fe: RenderFindingBody_IsDeterministic_SameInputProducesIdenticalOutput [2s], pwsh exited with code139; stdout/stderr empty. 2606passed/1failed/35platformskips. Renderer/test unchanged vs ca6 proves source identity, not failure attribution.

- Base ca6 isolated framework-dependent PowerShell7.6.6/.NET10.0.14 control:1passed/0failed/0skipped; runtime differs from hosted portable distribution, so not equivalent.
- Correct portable7.6.6/.NET10.0.12 withSDK10.0.403, Ubuntu24.04 Docker/WSL4CPU16GiB, full exact Linuxshard0:2605passed/4failed/35skipped/2644total18m54. Renderer determinism and other named renderer cases passed; no139/core. Three parent-exit assertions and one Git-revert cleanliness assertion failed. This is non-reproduction only, not a green Linux gate or proof of an environment cause. Container lacked init; post-failure snapshot had no zombies, so zombie attribution is unproven.
- Upstream PowerShell issue28119 reports close7.6.6/Linux/parallel139 symptoms and tentatively links open runtime issue121977 startup-profile cache concurrency. No matching native stack or released fix established; upgrade/cache isolation/shared-runner adoption are not demonstrated repairs.
- Raw reports and reproduction commands are retained in the delivery evidence; original hosted failure remains unresolved. Missing original native stack/cache evidence and non-reproduction prevent an evidenced product/runtime repair at this checkpoint.
