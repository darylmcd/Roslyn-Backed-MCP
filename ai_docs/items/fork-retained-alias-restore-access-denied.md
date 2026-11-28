# fork-retained-alias-restore-access-denied — Make the retained-source alias test restore its fixture reliably on Windows

**row:** `fork-retained-alias-restore-access-denied` · **pri:** `Medium` · **size:** `S` · **verified:** `b66239c2e4c60f505f37c79a6e9aaa4389bb2218`

## Anchors

- `tests/RoslynMcp.Tests/Workspace/ForkRetainedSourceDeletionTests.cs:321`

## Acceptance

- [ ] Identify the owner of the filesystem state or handle that prevents the Windows fixture rename.
- [ ] Restore the fixture deterministically while preserving the existing retained-source authority, refusal and cleanup assertions.
- [ ] Run the focused case and the full unfiltered Windows gate on the fix.

## Evidence

- Reach L0; origin pre-existing; severity P1; routes I4/V4. When the Windows integration test restores its fixture directory after the expected refusals, `Directory.Move` fails with `System.IO.IOException: Access to the path '…-child-held' is denied.` The test aborts before its final authority and destination assertions.
- Reproduced by `just machine-validate ci` on unchanged base `b66239c2e4c60f505f37c79a6e9aaa4389bb2218` and PR #2239 head `1564af56a8357772f2c2d169074ba0211e318d22`. PR #2240 head `9e31770fa3f7d6d881498cb82d258ea19ca870fc` independently reported the same line and exception. Handle ownership has not been established.
