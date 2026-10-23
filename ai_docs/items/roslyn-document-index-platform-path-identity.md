# roslyn-document-index-platform-path-identity — roslyn-document-index-platform-path-identity

**row:** `roslyn-document-index-platform-path-identity` · **pri:** `High` · **size:** `L`

## Problem

| Field | Evidence |
|---|---|
| Priority / size | High / L; five production files and one new test file, plus existing native regression coverage. |
| Root mechanism | Roslyn GetDocumentIdsWithFilePath uses a case-insensitive index on Unix. Unfiltered IDs conflate distinct physical files, misroute diagnostics/code fixes and falsely reject valid registration composition as conflicting linked edits. |
| Direct anchors | BuildService.FindDocuments; DiagnosticDocumentLookup.FindInDocumentAsync; SupportedFixEnumerationService.ResolveDocument; ExtractAndWireOrchestrator.PreviewExtractAndWireInterfaceAsync registration candidate loop. |
| Same-mechanism fallbacks | BuildService canonical fallback, diagnostic candidate distinctness and composite span keys; DiagnosticDocumentLookup.MatchesLocation cached/full-scan canonical path comparison. |
| Causal runtime evidence | Parent Linux93: native explicit-Compile fixture establishes both distinct physical file paths, then ExtractAndWireOrchestrator107 refuses with Linked registration documents disagree; line94 consumes case-folded IDs. |
| Required identity | Use FileSystemPath.Comparer for filesystem paths; preserve diagnostic-ID case-insensitivity independently. SourceTree identity remains preferred where available. |

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DocumentPathLookup.cs:9` — shared platform-exact filter over Roslyn indexed document IDs.
- `src/RoslynMcp.Roslyn/Services/BuildService.cs:246` — document lookup; `:180` — diagnostic path grouping and span aggregation; `:269` — normalized fallback.
- `src/RoslynMcp.Roslyn/Services/DiagnosticDocumentLookup.cs:60` — indexed lookup; `:232` — cached/full-scan canonical path matching.
- `src/RoslynMcp.Roslyn/Services/SupportedFixEnumerationService.cs:128` — path fallback after exact SourceTree identity.
- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:94` — physical registration document group.
- `tests/RoslynMcp.Tests/DocumentPathLookupTests.cs:9` — platform groups, linked-content retention and diagnostic selection controls.
- `tests/RoslynMcp.Tests/ExtractAndWireCompositionTests.cs:25` — existing linked-conflict refusal and immutable snapshot controls.

## Acceptance

- [ ] Centralize a platform-exact filter over the Roslyn path index and migrate all four production callers.
- [ ] Preserve Windows case-insensitive physical grouping; keep Unix case-distinct files separate, including absent case-only query results.
- [ ] Fix same-mechanism fallback/aggregation comparisons so subsequent scans cannot reintroduce path conflation.
- [ ] Preserve every actual same-path linked document, including unequal text, and retain ExtractAndWireCompositionTests linked-conflict refusal unchanged.
- [ ] Pass DocumentPathLookupTests platform, linked-content, and cached-diagnostic controls on Windows and Linux; verify Linux93 native case-distinct composition succeeds with unchanged semantic and disk assertions.
- [ ] Run required exact-head gates; do not weaken grants, current-snapshot checks, linked-content equality or source identity assertions.

## Current evidence

| Evidence | State |
|---|---|
| Scratch proposal | document-platform-path-proposal/proposal.patch, tests.patch, guards.json; git apply --check passes. |
| Verification | Runtime pending parent-owned serialized execution. No agent build/test or product edit. |
| Scope | Exposed restore-prerequisite defect; does not advance the paused 15-item batch. Keep OPEN until verified and landed. |
