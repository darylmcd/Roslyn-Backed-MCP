# process-environment-fixture-isolation — Isolate shared process environment fixture actors

**row:** `process-environment-fixture-isolation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `tests/RoslynMcp.Tests/ValidationBundleToolsTests.cs:320`
- `tests/RoslynMcp.Tests/ValidationBundleToolsTests.cs:384`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:245`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:775`
- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:798`

## Acceptance

- [ ] Inventory every current writer and concurrent reader of the fork TTL, restore timeout and dotnet-path environment variables, including shared fixture setup and child process inheritance.
- [ ] Prove the interference deterministically before correction, then isolate the complete actor set using owned process environment or the test framework supported shared-state exclusion; serializing only writers cannot protect parallel readers. Preserve genuine unrelated test parallelism.
- [ ] Preserve existing override/default/invalid-value assertions, exact environment restoration and child cleanup on primary failure; retain native-membership-specific absent protocol findings under the architectural decision.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. INVESTIGATION/current isolation defect: ValidationBundleToolsTests has no DoNotParallelize and temporarily mutates process globals320-396, including a synthetic C:\sdk\dotnet.exe path. Parallel WorkspaceForkApplyTests exercises current service reads at245/775/798. No causal runtime failure was executed in this audit; the absent NativeBuildMembership remainder is separate.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
