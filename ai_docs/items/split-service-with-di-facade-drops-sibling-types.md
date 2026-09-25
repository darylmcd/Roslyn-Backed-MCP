# split-service-with-di-facade-drops-sibling-types — Stop split_service_with_di_preview from deleting sibling types

**row:** `split-service-with-di-facade-drops-sibling-types` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs:843`

## Acceptance

- [ ] Splitting a service whose file also declares other types/interfaces keeps those declarations, base list and constants intact
- [ ] Preview output compiles (compile_check 0 new errors) for a multi-type source file
- [ ] Regression test covers a file with an interface + 2 classes

## Evidence

- split_service_with_di_preview on G4Fixture.cs → facade diff deletes IG4Shape, G4Square, G4Circle, G4Point, G4Consumer and drops ': IG4Fixture' + const (not applied — data loss). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- Facade is built as SyntaxFactory.CompilationUnit().WithUsings(usings) + WrapInNamespace(classDecl) and NormalizeWhitespace()'d, then written over the original file.

## Remediation attempt 2026-09-24 (plan `20260924T162035Z_backlog-remediate`) — held at review cap

- Branch `remediation/split-service-with-di-facade-drops-sibling-types` (head `b4f9b43c`, PR #1616 closed unmerged) carries a reviewed-but-held fix: the facade now replaces only the source type's declaration node, so sibling types, base list and constants survive; retained ctor-assigned fields are injected through the facade constructor; primary/chained constructors, constructor logic beyond `field = param` copies, mutable fields shared by moved + kept members, and parameter-name collisions are refused.
- Open findings from the cycle-2 cold review (fix these, then re-review):
  - HIGH: a constructor copy into a field that HAS an initializer (`private readonly int _retries = 3; ctor: _retries = retries;`) is accepted by `CollectInjectableConstructorAssignments` but never injected (`retainedCtorFields` filters `Initializer is null`) — the ctor value is silently lost. Refuse it (or inject regardless of initializer) and add a refusal case.
  - MEDIUM: a non-private non-readonly field used only by a moved method is copied into the partition and kept on the facade (not droppable), duplicating state; `sharedMutableField` only checks names referenced by kept members.
  - LOW: multi-declarator fields over-inject uninitialized declarators the ctor never assigned; multiple instance-ctor overloads collapse into one facade ctor (undocumented).
