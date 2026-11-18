# repin-perf-findnode-path-index — Index nodes and linked copies once per re-pin

**row:** `repin-perf-findnode-path-index` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SiblingInterfaceRepin.cs`:127 — `BindsToAsync` per copy and span.
- `src/RoslynMcp.Roslyn/Helpers/SiblingInterfaceRepin.cs`:185 — `DescendantNodes().OfType<IdentifierNameSyntax>().FirstOrDefault(span)` per call.
- `src/RoslynMcp.Roslyn/Helpers/SiblingInterfaceRepin.cs`:196-224 — `RefuseReboundGeneratedReferencesAsync` runs generators for every project on every extraction.

## Acceptance

- [ ] Node lookup by span uses `root.FindNode(span, getInnermostNodeForTie: true)` (or a per-document span index) instead of a full descendant walk per span.
- [ ] One path-to-documents lookup is built per `RepinAsync`; the per-copy loop does not rescan the solution.
- [ ] Source-generated documents are scanned only for projects whose generator output can see the generated interface (the interface project and its dependents), not for every analyzer-bearing project.
- [ ] Existing `InterfaceSameNameSiblingTests` stay green; add a measurement (benchmark category) or document the complexity change in the PR.

## Evidence

- iface-extraction-b report (PR 2148), round 2 sketch 6 and round 3 sketch `repin-perf-findnode-path-index`: "BindsToAsync and the merge loop re-walk the tree and rescan the solution per span and per copy".
- Verified at 12bc2c8b: line 185 walks all identifier nodes per call; line 96 calls the generated-document scan per project.
