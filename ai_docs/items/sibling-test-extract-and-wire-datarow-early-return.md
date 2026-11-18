# sibling-test-extract-and-wire-datarow-early-return — Assert the extract-and-wire row of the linked re-pin test

**row:** `sibling-test-extract-and-wire-datarow-early-return` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `tests/RoslynMcp.Tests/InterfaceSameNameSiblingTests.cs`:755 — `if (route == "extract-and-wire") return;` in `A_Re_Pinned_Consumer_Linked_Into_A_Project_That_Does_Not_Reference_The_Interface_Gains_No_Reference`.

## Acceptance

- [ ] The extract-and-wire DataRow asserts the same facts as the other routes (ZLinked gains no Contracts reference; `UsesBoth.Item` keeps `SampleLib.Legacy.IItemService` in both projects), through the applied solution or the composite edit set.
- [ ] Mutation check: reconcile every group on the extract-and-wire route and see that row fail.

## Evidence

- iface-extraction-b report (PR 2148), round 5 row sketch: "the extract-and-wire DataRow ... returns before the FieldType and project-reference checks (the composite store has no pending solution)".
- Verified at 12bc2c8b: line 755.
