# extract-type-preserve-untouched-trivia — Avoid whole-file formatting

**row:** `extract-type-preserve-untouched-trivia` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:135-138`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] `extract_type_preview` formats only synthesized or changed syntax, leaving unchanged source regions byte-identical.
- [ ] A red-first regression includes unusual whitespace outside the extraction and compares the untouched bytes after preview/apply.

## Evidence

- Parent `extract-type-breaks-interfaces-and-publicizes-fields`: source-root `NormalizeWhitespace` rewrites unrelated source text.
- 2026-10-27 worktree cleanup: the unfinished 2026-10-16 semantic-preservation program (127 paths, ~12k lines: compiled-IL execution analysis with a new `Microsoft.ILVerification` dependency, `ExtractionSemanticContext.*`, a 15-file `TypeExtractionService` split, BREAKING `ExtractionPreservationUnverifiedException`, ADR numbered 0029 colliding with main, two plans and seven proposed rows not filed on main) is archived at `a3a7dd94` on remote branch `codex/extraction-semantic-preservation-archive`; no gate, hosted CI or cold review. Main still runs whole-root `NormalizeWhitespace` at `TypeExtractionService.cs:138`. Operator choice pending: ship the narrow untouched-trivia acceptance, or port the archived program as its own ADR-backed series.

## Context

- Separate regression mechanism from interface contract and accessibility handling.

2026-10-09 planning-quality finding: this initiative performance cell contained copied alias-resolution text belonging to workspace-project-alias-lookup. Corrected through audited stanza-amend; source approach, runtime scope, acceptance and estimate unchanged. This existing row tracks implementation; no runtime completion claimed.

2026-10-10 execution hold, immutable product base7322a2d1810743427185add91f4e44442ebfb1a9: complete cold re-vet requires90000 context estimate for generated composition, original-binding protection, namespace/import/nullable/file-local-symbol context and persisted multi-document proof. Exact implementation scope remains TypeExtractionService.cs, TypeExtractionTests.cs and own fragment. Current global1f3a0bd native exec-args exits1 at cap80000; global owning row plan-rule5-indivisible-scope-admission evidence filed in https://github.com/darylmcd/claude-config/pull/748. Preserve scope/estimate; no partial repair or closed-row claim.

Historical checkpoint (superseded by the 2026-10-15 policy approval below): separate operator boundary decision was pending for targeted typed refusal of proven unrescued anonymous/ref/global-dynamic capture versus target-namespace feature. Representable imported/named-type and nested-dynamic cases must still be supported; no blanket keyword ban or automatic relocation. Conditional complete source-only stanza is a held design, not acceptance or execution admission.

Retained uncommitted worktree D:/Roslyn-Backed-MCP/.worktrees/extract-type-preserve-untouched-trivia at unchanged base; SHA256-bound snapshots in <session-scratch>/extract-held-capture-complete-checkpoint.json. Last file-symbol/header-order edit is untested; previous scoped gate5failed8passed13total includes persisted LOCAL/DEBUG result9 instead7. Final semantic validation, moved capture corpus, ratchet/scoped/keyword matrix, fresh full producer and cold implementation review remain required. No PR/merge/current full proof; keep this row open and resume exact WIP only after prerequisites and fresh source vetting.

## Operator decision 2026-10-15

| Field | Record |
|---|---|
| Date | 2026-10-15 |
| Authority | Live user approval of all three recommendations in this chat; record decisions while preserving implementation and validation holds. |
| Approved policy | Approve targeted typed pre-effect refusal only when semantic evidence proves extraction cannot preserve semantics after supported rescue. Keep the existing namespace; defer the target-namespace feature. Retain representable named/imported-type and nested intrinsic dynamic support; no blanket keyword bans or automatic relocation. |
| Remaining holds | Keep deferred and open. Preserve complete 90000 estimate, scope-admission blocker, retained WIP and failed/untested evidence. Native replan/review, semantic rescue and binding checks, original byte-preservation corpus, persisted apply proof and fresh full validation remain required. Refusal must leave tokens, workspace version and disk unchanged. |
| Authorization boundary | Policy decision recorded only; no execution admission, release, merge or completion claim. |
| Plan supplement | ../plans/20261007T211247Z_backlog-remediate/operator-decisions-20261015.md |
