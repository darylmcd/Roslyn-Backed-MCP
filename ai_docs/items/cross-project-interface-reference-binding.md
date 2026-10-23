# cross-project-interface-reference-binding — Bind generated interface references to their declared namespace

**row:** `cross-project-interface-reference-binding` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:177`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:287`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:815`
- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:94`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkGraphReplayTests.cs:304`

## Acceptance

- Bind generated base lists, constructor parameter types and DI registration arguments to the actual generated interface namespace/symbol.
- Preserve source trivia and unrelated bindings; do not add broad imports that can rebind existing names.
- Cover distinct, nested, global and escaped namespaces where supported; preserve actual type arguments rather than guessing from display text.
- Observe existing compiler/symbol-binding regressions fail before the correction and pass afterward.
- Keep real cross-project fork success, project-reference, source-byte and preview ownership assertions unchanged.

## Evidence

- Current source creates the interface in the target namespace but references its bare identifier at the four anchors; no qualifying import accompanies those references.
- Parent run57 reports OrdinaryCrossProjectFork_PreservesSiblingAndSourceAuthority failure: compile-error, one error, empty summary diagnostics. Actual CompileResult diagnostics are omitted by the assertion; diagnostic-only patch prepared to establish exact native error.
- Source candidate is SampleLib.Counter : ICounter versus generated Contracts.ICounter. Do not treat predicted CS0246 as runtime evidence until observed.
- Forced companion exposed by restored added-project fixture coverage; no new batch selection or generic extraction expansion authorized.

Current-session native evidence: run59 ordinary private fork compiled with CS0246 for ICounter in Counter.cs (170ms compile, all4 projects completed, not cancellation). Shared generated-symbol qualification fixed that case in run60. Run60 exposed direct consumer-reference omissions, same-namespace alias collisions and duplicate header separator; complete consumer/binding/composition changes installed. Adhoc run70 namespace merge/no-op controls PASS2; broader native run71 pending. Do not treat source implementation or focused diagnostics as shipping acceptance.

Current-session native matrix91 passed all8 cases after exact source-declaration identity plus imported-to-source correspondence; prior88/89 failed both inversion source-constructor cases. Immutable discovery retained. Helper controls cover project order, primary/record constructors, unrelated assembly and nested parameter exclusion. Error-symbol regression92 reproduced NullReferenceException, fixed guard passes96. Windows broader89 visibility/composition/ordinaryfork passed. Linux90 case-distinct fixture exposed SDK implicit Compile case folding; explicit Compile fixture is under native93 verification. No full gate or ship yet.
