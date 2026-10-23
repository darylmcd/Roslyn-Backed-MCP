# process-environment-fixture-isolation — Isolate process environment test actors

**row:** `process-environment-fixture-isolation` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/AssemblyInfo.cs:3`
- `tests/RoslynMcp.Tests/CompilerArgumentOwnedRetainedTests.cs:163`
- `tests/RoslynMcp.Tests/CompilerArgumentRulesetTests.cs:60`
- `tests/RoslynMcp.Tests/ValidationBundleToolsTests.cs:308`
- `tests/RoslynMcp.Tests/NativeMembershipPropertyTests.cs`
- `src/RoslynMcp.Roslyn/Services/NativeBuildMembership.cs:55`

## Acceptance

- [ ] Isolate all in-process environment-mutating test classes under the existing assembly parallelism contract; preserve actors, assertions and restoration.
- [ ] Prove an unreferenced environment variable changes native evaluated-property membership while evaluated items, document paths and authored bytes remain unchanged.
- [ ] Prove stale membership refuses and restoring the captured environment restores currency; restore original process state before fixture retirement.
- [ ] Retain compiler properties in production membership; do not filter environment keys or weaken currentness checks.

## Evidence

- Class-level parallel execution shares process environment; NativeBuildMembership binds all evaluated properties. Unique variable names cannot isolate concurrent native evaluation.
- Causal26 old native csx initial-load failure overlaps the retained-parser environment actor. Exact historical causation remains unproven.
- Same-mechanism inventory identified three unisolated classes, three already isolated classes, and one separate child-process actor.

## Context

- Correct the introduced fixture defect and exposed copies before shipping RESTORE. Keep this row open until merged evidence exists.

Scope: one shared process-global ownership mechanism forces three mutator classes plus one existing isolated native regression class; AssemblyInfo and NativeBuildMembership are read-only contract anchors. Keep one cohesive correction rather than splitting independently unsafe actors. Anchor overlap with load-profile-full-suite and native-test-cleanup-primary-preservation accepted because this change preserves their parallelism contract and existing cleanup behavior.

Independent cold plan and exact implementation source reviews approved. Root applied all four hash-verified drafts: three class-level DoNotParallelize declarations with process-global ownership comments, unchanged original actors/assertions/restoration; native unreferenced-property regression in existing isolated class. Scoped Roslyn compiler check zero warnings/errors. Native build/runtime qualification and merge remain pending; no historical csx causation claim.

Current source29 native focused run completed124=122passed2failed0skipped3m12. All affected environment classes passed: OwnedRetained9, Ruleset7, ValidationBundle43; NativeMembershipProperty9 including new unreferenced environment restoration regression. Unrelated failures are known source-admission generated transition and unverified task fixture final compiler failure before availability premise. Build29 zero warnings/errors14.07s, all1381 source identities matched. This proves current focused controls, not exact historical csx cause, full CI or merged completion.
