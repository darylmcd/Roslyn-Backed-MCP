# move-type-to-project-source-dependency-gap — move_type_to_project_preview: a moved type that references a type left in the source project does not compile

**row:** `move-type-to-project-source-dependency-gap` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] A moved declaration (class, enum or delegate) that references a type left in the source project yields a preview that compiles (the tool adds the reference or moves the dependency), or refuses with a named reason; red-first test: the preview solution fails with CS0246 today.
- [ ] Check that the reference direction does not create a project-reference cycle (source already references the target): refuse that case.

## Evidence

- Probe of relocation-support-enum-delegate-move-to-project (slice 2) on the base commit with a class and a delegate: CS0246. The tool is experimental. Related: relocation-moved-declaration-sibling-import (namespace relocation path).
