# interface-extraction-public-accessor-contracts — Preserve ordinary property accessor contracts

**row:** `interface-extraction-public-accessor-contracts` · **pri:** `Medium` · **size:** `M` · **deps:** `interface-member-projection-shared-emitter`

## Anchors

- src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:222-227 (includes nonpublic getter; loses init)
- src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:506,518-534 (getter/setter selection omits public/init distinction)

## Acceptance

- [ ] Preserve only applicable public ordinary-property accessor contracts and init semantics in both emitters and all four public routes; retain public getter-only/setter-only and private-counterpart controls. Preserve receiving policy and semantic nullable/return contracts.
- [ ] Observe native valid-before regression on old output, then serialized preview and applied/reloaded compilation and binding after repair. Preserve unrelated bytes and refuse only actual unrepresentability before stores.
- [ ] Coordinate complete indexer emission through its explicit dependent owner; this row owns ordinary properties and never closes the broader ref-return or unsafe owners on incidental coverage.
- [ ] Emit an accessor only when its `DeclaredAccessibility == Public`; emit `init` when `SetMethod.IsInitOnly`. Controls: `{ get; private set; }` → `{ get; }`; `{ private get; set; }` → `{ set; }`; `{ get; init; }` → `{ get; init; }` (IES:223-228 keeps a private getter and drops init; CPRS:511 turns `private set` into `set`, CS0277).

## Evidence

- Fresh parameter planner and cold reviewer independently verified source omission; no existing accessor owner found. Independent ordinary-property mechanism remains outside the method-parameter slice. No compiler/runtime proof or source repair claimed.
