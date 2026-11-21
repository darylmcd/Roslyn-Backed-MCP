# rebase-tests-duplicate-reference-workspace-helper — Share CreateReferenceWorkspaceAsync between the rebase tests

**row:** `rebase-tests-duplicate-reference-workspace-helper` · **pri:** `Low` · **size:** `S` · **verified:** `6f04249ec1c1681814a65e21bbbcc07a9dde2b61`

## Anchors

- `tests/RoslynMcp.Tests/RebaseDroppedIntentRefusalTests.cs`
- `tests/RoslynMcp.Tests/PreviewTokenCrossCouplingTests.cs`

## Acceptance

- [ ] One shared CreateReferenceWorkspaceAsync used by both test classes, no behaviour change (both classes stay green).

## Evidence

- Worker report of rebase-changed-document-silent-drop (#2190) sketch; cold review follow-up 5.
