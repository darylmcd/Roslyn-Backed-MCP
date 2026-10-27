# extract-type-value-type-and-record-composition — Refuse instance composition into records and structs

**row:** `extract-type-value-type-and-record-composition` · **pri:** `High` · **size:** `S` · **deps:** `extract-type-static-source-composition`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:708-714` — only primary constructors are refused.
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:132-134` — unconditional field injection.

## Acceptance

- [ ] Red first: `public record R1 { public int A { get; init; } public static int Seed() => 7; }`, extract `Seed`: `new R1(h1){A=1} == new R1(h2){A=1}` turns False (was True), and `with` copies alias the helper.
- [ ] Red first: `struct V1` with an extracted instance member: `default(V1).Twice()` throws NullReferenceException.
- [ ] Refuse (typed, before token) when the source is a record, record struct or struct and the move needs an instance helper.
- [ ] Static-only moves from these types succeed with no injected field.

## Evidence

- 2026-10-27 runtime probe on main's rendered output: record equality False; `default(V1)` NullReferenceException.

## Context

- Operator policy (2026-10-15 decision, `ai_docs/plans/20261007T211247Z_backlog-remediate/operator-decisions-20261015.md`; refined 2026-10-16 in the deleted extraction archive): refuse before any token, workspace or disk effect when supported semantic analysis proves loss, or cannot verify preservation, using a typed public `InvalidOperationException` subtype (the archive named it `ExtractionPreservationUnverifiedException`) distinct from proven-loss refusals. Keep the existing namespace; no blanket keyword bans; no automatic relocation. Adding that public refusal type is breaking under contract-care: record it in ADR 0032 (0029 is taken) plus a `Changed — BREAKING` migration note.
