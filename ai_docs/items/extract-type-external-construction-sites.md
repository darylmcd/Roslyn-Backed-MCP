# extract-type-external-construction-sites — Report construction sites broken by the added constructor parameter

**row:** `extract-type-external-construction-sites` · **pri:** `Medium` · **size:** `S` · **deps:** `extract-type-static-source-composition,type-extraction-service-refusals-public-message`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:899-946` — external-consumer scan covers extracted members only, never source constructors.

## Acceptance

- [x] Shipped (PR #2064): a `new Source()` in a DEPENDENT project that breaks after apply is refused before token storage (public message, CS ids with file:line).
- [ ] Remaining: same-project construction sites in other documents are refused with a structured list (file, line) matching the blocking-dependency shape. Today they already get the public CS-id PublicInvalidOperationException, so this replaces an existing public message (major); BlockingDependencyDto has no file/line fields (DTO change).
- [ ] Moves that add no constructor parameter (see `extract-type-static-source-composition`) are unaffected.

## Evidence

- 2026-10-27: every instance extraction adds a required constructor parameter; no scan reports external construction sites.

## Context

- Found during the archive diagnostic; not covered by the archive.

## Notes

2026-11-11 remediation: PR #2064 covered dependent projects (test ExtractType_InstanceMove_ConstructionSiteInDependentProject_IsRefused). Remaining = same-project structured refusal: major. Next-major trigger: ships with type-extraction-service-refusals-public-message (ADR + migration note per docs/release-policy.md Compatibility Policy), or an operator decision that replacing one refusal with another is not breaking.
