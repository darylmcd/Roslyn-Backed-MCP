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
