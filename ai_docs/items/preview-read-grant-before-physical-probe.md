# preview-read-grant-before-physical-probe — Authorize physical preview paths before inspection

**row:** `preview-read-grant-before-physical-probe` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffReadContext.cs`: PrepareAsync; ReadBytesAsync.
- `tests/RoslynMcp.Tests/PreviewReadGrantOrderingTests.cs`: PhysicalPreview_AuthorizesBeforeAliasInspection_AndStillRejectsAuthorizedAlias.

## Acceptance

- [ ] Obtain the current path grant before every filesystem alias inspection at both preview entry points.
- [ ] Retain physical alias refusal and held-input reading after authorization.
- [ ] Prove denied linked-parent requests return the exact policy denial before inspection; authorized aliases still refuse, for both entry points.
- [ ] Observe the regression on prior ordering and pass after the bounded repair.

## Evidence

- Cold review found EnsurePhysicalPath before AuthorizePathsAsync at both shared preview entry points.
- Same-mechanism audit checked planner, fork preparation and composite callers; those authorize their inventories before the inspected reads.
- Fix belongs to the active restore prerequisite; do not defer an exposed defect or weaken physical authority.

## Current-session qualification 2026-10-17

- Four real linked-parent cases failed on old ordering in causal173; denied requests never reached policy, and allowed alias refusal occurred with zero policy calls.
- Reordered both entry points to authorize first while retaining physical checks and held reads; fixed174 all four passed.
- Expanded Windows179 and Linux180 both passed90 tests, including all four ordering cases. Full aggregate qualification remains outstanding.
