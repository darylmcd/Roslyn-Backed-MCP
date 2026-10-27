# extract-type-composition-base-ctor-refusal — Refuse extraction when a base constructor can reach the unassigned helper

**row:** `extract-type-composition-base-ctor-refusal` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` — `InjectFieldAndCtorParameter` (assignment now precedes the first constructor statement, but a base constructor still runs first) and the pre-token refusal checks near the constructor-topology validation.
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs` — `ExtractType_CompositionFieldAssignedBeforeConstructorCodeRuns` is the sibling test.

## Acceptance

- [ ] Red first: a derived class whose base type is not `object` and whose base constructor calls a virtual member the derived class overrides (or an interface implementation) that references the extracted helper currently yields a preview that throws NullReferenceException at construction.
- [ ] Refuse, typed and before token storage, when the base type is not `object` and a retained override or interface implementation reachable from a base constructor references the helper. Use semantic-model detection; no compiled-IL interpreter.
- [ ] Refusal leaves no token, workspace-version change or disk effect. If a new public refusal type is added (operator policy 2026-10-15 names a distinct `InvalidOperationException` subtype), record it in an ADR (next free number after 0031) plus a `Changed — BREAKING` migration note.

## Evidence

- Split from `extract-type-composition-assigned-before-use` (bullet 3) so its first-statement assignment fix could merge alone; that fix cannot help here because C# runs the base constructor before any derived constructor statement.
