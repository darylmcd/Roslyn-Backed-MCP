# extract-type-nested-namespace-identity — Preserve the full extracted type namespace

**row:** `extract-type-nested-namespace-identity` · **pri:** `Medium` · **size:** `S` · **deps:** `release-policy-correction-rule-adr`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:1187-1196`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] A public extraction from a nested namespace emits the extracted type under the original full namespace. The serialized preview and applied solution compile, and generated references bind to that type.

## Evidence

- Cold namespace plan review at `34a87d9c`. The extracted file emitter copies only the innermost namespace name. Reuse the full containing namespace identity and preserve receiving-language syntax.

## Notes

5.1.1 screen at 9e7a9439 re-established stable extract_type_preview and reproduced old nested namespace loss. The candidate failed cold re-review on valid conditional C# branches that contain sibling declarations or split using syntax beside a namespace header. It can duplicate sibling declarations or emit malformed code in another configuration. Candidate 00a223ea and subsequent draft revisions are investigation only. Reopen after a complete compiler-backed projection is shown to preserve previously accepted preview/apply behavior and both supported wire eras. If complete repair changes the stable contract, use the next operator-approved major with ADR, migration note and dual-era raw regression. Preserve structured blockingDependencies names and leave public refusal messages to their dedicated Defer row.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
