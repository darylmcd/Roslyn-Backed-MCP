# extract-type-static-initialization-order — Refuse moves that reorder static initializer side effects

**row:** `extract-type-static-initialization-order` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:58-61` — member partition; no initializer or side-effect analysis.

## Acceptance

- [ ] Red first: `static List<string> Log = new(); static readonly int First = Note("first"); static readonly int Second = Note("second"); static int Note(string s){ Log.Add(s); return 0; }`, extract `Log, Note, Second`, then read `First`: log order becomes `second,first` (was `first,second`) because moved initializers run in the helper type initializer.
- [ ] Constant and side-effect-free initializers stay eligible; no keyword ban.
- [ ] Refusal is typed and happens before token storage.

## Evidence

- 2026-10-27 runtime probe on main's rendered output logged `second,first`.

## Context

- Operator policy (2026-10-15 decision, `ai_docs/plans/20261007T211247Z_backlog-remediate/operator-decisions-20261015.md`; refined 2026-10-16 in the deleted extraction archive): refuse before any token, workspace or disk effect when supported semantic analysis proves loss, or cannot verify preservation, using a typed public `InvalidOperationException` subtype (the archive named it `ExtractionPreservationUnverifiedException`) distinct from proven-loss refusals. Keep the existing namespace; no blanket keyword bans; no automatic relocation. Adding that public refusal type is breaking under contract-care: record it in ADR 0032 (0029 is taken) plus a `Changed — BREAKING` migration note.
