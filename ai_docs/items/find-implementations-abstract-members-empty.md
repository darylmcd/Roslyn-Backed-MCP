# find-implementations-abstract-members-empty — Return implementations for abstract members and abstract classes

**row:** `find-implementations-abstract-members-empty` · **pri:** `Medium` · **size:** `M` · **deps:** `symbol-handle-constructed-generic-roundtrip,workspace-load-abstract-lane-internal-error`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:67-134`
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs:350-374`
- (new) `tests/RoslynMcp.Tests/FindImplementationsAbstractTests.cs`

## Acceptance

- [ ] `find_implementations` at `src/RoslynMcp.Roslyn/Services/ForkAdmissionTask.cs` 52:37 and for `RoslynMcp.Roslyn.Services.ForkAdmissionTask` returns the 3 derived implementations, as the description states ("interface or abstract member").

## Evidence

- G2-F1: 3/3 calls returned count 0.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).

## Notes

2026-11-25 re-derived root: shared ReferenceService dispatch, not the old navigation anchor. ForkAdmissionTask.cs52:37 is ExecuteAdmission (three concrete overrides); class metadata names three derived class declarations. Frozen WIP at rem/abstract-implementations has original-base7 semantic failures/1 control pass, changed7 pass/1 real constructed-handle failure. Preserve the failing test and source; no normalization/removal authorized. Separate named-type identity prerequisite requires D5/D8/V3 design decisions and fresh cold review; no minor qualification established. A separate head legacy workspace_load InternalError also blocks the complete wire done-check; base proof/root-cause qualification remains missing. No commit/push/PR or success claim for this lane.

Operator decision 2026-11-25: Park this lane; continue other rows. Preserve the frozen abstract-dispatch source and failing constructed-handle regression. Exact named-type identity design/prerequisite is deferred from this run; no new locator format, mechanism, narrowed identity guarantee, or major change is authorized. The scratch reference-ID candidate failed tuple preservation; no fallback was implemented.

Later live operator decision supersedes the earlier parked status: Resume the existing row; keep the no-major constraint. Preserve the frozen source and constructed-handle regression while completing prerequisites. The approved two-module experiment and later bounded getter observation are consumed; they do not approve a production caller migration or persisted format. The getter inventories preserve actual Winner None versus independent TypeInfo NotAnnotated; the probe author-added equality STOP is not a replay failure. New candidate correction and any further qualification attempt still require their own complete reviewed decision. The separate load incident remains unresolved after the bounded four-cell diagnostic; current load observations are not ready-state qualification. No implementation PR or row closure is claimed.

Latest operator decision: approve the cold-reviewed two-module contextual descriptor, consumed-structure and reporting correction, followed by one conditional compilation and runtime, each capped at120000ms. A fresh complete staged-source/manifest cold PASS must precede compilation; runtime requires compile success; first failure stops without repair or retry authority. Source implementation is in progress; no result is claimed. This grant includes no product migration, persisted format, host/load, compatibility or major-version approval. The no-major constraint and both product prerequisites remain.
