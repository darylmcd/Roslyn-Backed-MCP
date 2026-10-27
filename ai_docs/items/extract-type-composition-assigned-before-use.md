# extract-type-composition-assigned-before-use — Assign the extracted helper before constructor code uses it

**row:** `extract-type-composition-assigned-before-use` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:841` — assignment appended at the end of the constructor body.
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:553-556` — refuses only direct extracted references lexically inside a constructor.
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:824-837` — expression-bodied constructor conversion.

## Acceptance

- [ ] Red first: `public C1(){ _v = Init(); } int Init() => Compute(); int Compute() => 7;`, extract `Compute`: current output `{ _v = Init(); _c1Helper = c1Helper; }` throws NullReferenceException; after the fix `_v == 7`.
- [ ] Insert the assignment before the first original statement of every root constructor (block and converted expression-bodied); `this(...)` chains keep forwarding only.
- [ ] Refuse (typed, before token storage) when the base type is not `object` and a retained override or interface implementation reachable from a base constructor references the helper.
- [ ] Existing constructor-topology and trivia-preservation tests stay green.

## Evidence

- 2026-10-27: main's output for the C1 fixture, compiled and run on .NET 10, threw NullReferenceException.

## Context

- Operator policy (2026-10-15 decision, `ai_docs/plans/20261007T211247Z_backlog-remediate/operator-decisions-20261015.md`; refined 2026-10-16 in the deleted extraction archive): refuse before any token, workspace or disk effect when supported semantic analysis proves loss, or cannot verify preservation, using a typed public `InvalidOperationException` subtype (the archive named it `ExtractionPreservationUnverifiedException`) distinct from proven-loss refusals. Keep the existing namespace; no blanket keyword bans; no automatic relocation. Adding that public refusal type is breaking under contract-care: record it in ADR 0032 (0029 is taken) plus a `Changed — BREAKING` migration note.
- Rejected approach from the archive: an owned compiled-IL interpreter (`CompiledExecution*`, `Microsoft.ILVerification`, manifest hashing) to prove base-constructor behaviour; it never integrated. Use semantic-model detection plus typed refusal.
