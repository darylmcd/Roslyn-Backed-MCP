# dependency-gate-process-tests-runner-adoption — Share dependency-gate process fixtures

**row:** `dependency-gate-process-tests-runner-adoption` · **pri:** `Low` · **size:** `S` · **deps:** `powershell-script-test-runner-foundation`

## Anchors

- `tests/RoslynMcp.Tests/NuGetAuditGateTests.cs`
- `tests/RoslynMcp.Tests/PackageFamilyContractTests.cs`

## Acceptance

- [ ] Replace both private PowerShell process launchers, timeout/tree-reap helpers, and result records with the shared `PwshScriptRunner`.
- [ ] Preserve argument boundaries, isolated child environments, concurrent stdout/stderr draining, the 60-second contention-tolerant process bound, and full timeout diagnostics.
- [ ] Preserve every fail-closed audit, package-family, and upgrade-matrix assertion.
- [ ] One source scan proves neither anchored file retains private `ProcessStartInfo`, process-tree cleanup, or duplicate result plumbing.

## Evidence

The unsharded Windows gate on 2026-08-24 ran with 24 class workers while another local workload contended for the machine. Three prior full runs completed each dependency-gate subprocess in about 0.3–0.6 seconds; the contended run stretched the same cases to 3–28 seconds and tripped the former 20-second NuGet-audit bound. The immediate hardening keeps these isolated classes parallel while making timeout cleanup deterministic. Both files still duplicate the process machinery already tracked by `powershell-script-test-runner-foundation`; this dependent row bounds their later migration to two test consumers.
2026-08-24 review clarification: Kill(entireProcessTree: true) requests descendant termination, but Process.WaitForExitAsync proves only the root exited. The shared runner must retain a bounded root wait and redirected-stream drain and must not claim that root reaping independently proves every descendant exited.
