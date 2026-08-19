# parameter-object-dto-gate-tidy — tidy the DTO type-validity gate

**row:** `parameter-object-dto-gate-tidy` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1040`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1046`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1066`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:81`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] The `ProtectedOrInternal` rank is a named constant alongside `PublicAccessibilityRank` (4) and `InternalAccessibilityRank` (2), replacing the bare literal `3`.
- [ ] One shared `MissingProjectReferenceMessage(...)` formatter produces the missing-project-reference wording for BOTH refusal sites, so `EnforceCrossProjectReferences` and the new gate cannot drift.
- [ ] A null DTO-project compilation yields a named refusal instead of the `dtoCompilation!` null-forgiveness (which turns a legitimate refusal path into an NRE for a project that does not support compilation).
- [ ] `EnforceGroupedParameterTypesAreDtoCompatibleAsync` runs BEFORE `CollectCallSiteBindingsAsync`, so an obviously-invalid parameter type refuses without first paying a solution-wide find-references scan. (Only the cross-project arm needs the DTO compilation, not the call sites.)
- [ ] Regressions pin the two BEHAVIOR-CHANGING bullets above: one asserting a null DTO-project compilation refuses (rather than the guard degrading to "destination is free"), one asserting an invalid parameter type refuses without a call-site scan having run. The rank-constant and shared-formatter bullets are behavior-preserving and need no new test.

## Evidence

All four are visible in the `EnforceGroupedParameterTypesAreDtoCompatibleAsync` block added by PR #1269: bare literal `3` between two named rank constants, hand-copied "Missing references: A -> B" wording duplicated from `EnforceCrossProjectReferences`, a `!` on an awaited `GetCompilationAsync`, and the gate's placement at `:81` after the call-site scan.

## Context

Four low-severity findings from PR #1269 (`parameter-object-generic-dto-type-validity`) code-quality review. Advisory — none blocked that PR. The ordering item is a cheap perf win, not just hygiene.
