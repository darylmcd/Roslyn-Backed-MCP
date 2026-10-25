# workspace-native-reference-global-context-mismatch — Preserve native reference evaluation context at workspace load

**row:** `workspace-native-reference-global-context-mismatch` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs:38` — CreateAndOpenAsync; effectiveProperties at55, MSBuildWorkspace.Create at62–64, OpenSolutionAsync at88 and OpenProjectAsync at93.
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs:95` — ordinary native graph construction through111; private marker removal before native project factory at121–136.
- `tests/RoslynMcp.Tests/Workspace/WorkspaceReloadGlobalPropertiesTests.cs` — existing global-property load controls; verify current test owner before adding regressions.

## Acceptance

- [ ] Observe RED through the public loader when ProjectReference AdditionalProperties or SetConfiguration changes actual child Compile membership; compare with native graph evaluation and confirm ordinary non-graph evaluation during diagnosis. Cover solution per-project Configuration mapping with the same mismatch mechanism and retain multi-TFM/default/global controls.
- [ ] Determine the owned integration or upstream cause before implementing. Preserve explicit caller globals, reference-derived globals, solution mappings and target-framework semantics without inferring contexts from ProjectId, Name, OutputPath or parse symbols.
- [ ] Preserve the public remove-edge, retained-child, later-create lifecycle; do not replay obsolete historical contexts for a newly targeted operation.
- [ ] Preserve source/fork fidelity, original bytes, diagnostics, cancellation and disposal. Do not weaken tests, suppress disagreement or introduce a parallel loader, BuildHost, certificate or registry.
- [ ] Prove any upstream limitation before requesting a product decision. Re-plan or split if confirmed integration work exceeds the two known production owners.

## Evidence

- Current-session wrapped public probes compared actual WorkspaceManager.LoadAsync Documents with native ProjectGraph evaluation; the observed disagreement is proven, its exact owned/upstream cause is not.
- `native-default-policy-disconnected-probe-isolated.log`: native child ProbeMode=reference-context disables default items; workspace child still contains default-glob OnlyDefault.cs.
- `native-default-policy-disconnected-probe-config.log`: native child Configuration=RefContext and ProbeMode=reference-context disables defaults; workspace child includes OnlyDefault.cs.
- `native-default-policy-solution-config-probe.log`: solution Debug maps Child to Release; native child disables defaults while workspace child includes OnlyDefault.cs and Debug output.
- Receipts and exact fixtures are preserved under `<user>/.codex/scratch/roslyn-remediate-20261023-split/`; handoff `native-default-policy-load-mismatch-row-handoff.md` records controls and limits.

## Context

- Independent, nonblocking follow-up authorized by the operator; restore-callers-missing-packages-path closure does not depend on this row.
- Existing branch `codex/restore-fork-reviewed-project-graph` and scratch v3 plan are references, not approved implementation.
- Do not count ignored SetTargetFramework on a single-framework project as a discrepancy; the native graph also ignores it. The multi-TFM case is a successful control.
