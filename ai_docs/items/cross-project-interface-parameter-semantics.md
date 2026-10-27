# cross-project-interface-parameter-semantics — Preserve emitted interface parameter semantics

**row:** `cross-project-interface-parameter-semantics` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:619` (`CreateParameterList`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`
- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:197-202`, `:419-428` — the in-project emitter drops modifiers too; `GetDefaultValueText` mis-renders defaults.

## Acceptance

- [ ] Preserve semantic ref/out/in/params modifiers and optional defaults when generating interface parameter declarations, covering all applicable emitted member kinds.
- [ ] Use actual public extraction and dependency-inversion compilation controls; preserve valid ordinary parameters and reject only concretely unrepresentable emitted forms before token publication.
- [ ] Keep declaration/method generic identity and signature dependency ownership in their separate rows.
- [ ] Both emitters build parameters from `IParameterSymbol` via `SyntaxGenerator.ParameterDeclaration` (ref/out/in/ref readonly/params/params collections/scoped; defaults as compiler literals); append `__arglist` for vararg methods; delete `GetDefaultValueText`.
- [ ] Defaults encoded by `[Optional]`, `[DefaultParameterValue]`, `[DateTimeConstant]` or `[DecimalConstant]` get no initializer; copy the attribute instead (C# forbids both; DateTimeConstant has no literal).
- [ ] Matrix on all four routes through preview, apply and reload: `Modify(ref int @event, out int r, in int i)`, `Read(ref readonly int i)`, `Scoped(scoped Span<int> s, scoped ref int i)`, `NumericDefaults(double a = double.PositiveInfinity, double b = double.NegativeInfinity, double c = double.NaN)`, float/decimal/char/escaped-string/enum-member defaults, `MetadataDefaults([Optional, DateTimeConstant(123)] DateTime d)`, `Write(int v, __arglist)`. Verify scoped and params-collection output against the pinned Roslyn.

## Evidence

- Unblocked 2026-10-27: PR1854 merged (`998b0e95`) and PR1857 moved hosted Linux to three shards; the earlier PARKED hold no longer applies.


- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Independent cold audit found CreateParameterList618-624 emits only name/type, dropping method parameter modifiers/defaults. This is distinct from undeclared generic parameters and target-assembly dependency closure.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.


- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
- 2026-10-27: dependency `interface-extraction-nullable-context-projection` closed by PR1854 (hosted Linux budget fixed by PR1857). PR1840 closed as superseded; its design for this row is inlined here and its archive branches were deleted (head still at GitHub `refs/pull/1840/head`). Re-cut from current main; do not resume PR1840.
- 2026-10-27 diagnostic: CPRS:516-521 emits name and type only (`Modify(ref int x, out int y, in int z)` becomes `Modify(int x, int y, int z)`, CS0535); IES:197-202 drops modifiers; IES:419-428 renders char bare, enums as numbers (CS1750), float without `f` (CS0664), decimal without `m`, infinity as `∞`, culture-formatted numbers and unescaped strings; `__arglist` is dropped (CS0535).
