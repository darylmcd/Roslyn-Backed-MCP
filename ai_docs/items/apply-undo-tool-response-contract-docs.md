# apply-undo-tool-response-contract-docs — Document apply and undo response variants

**row:** `apply-undo-tool-response-contract-docs` · **pri:** `Low` · **size:** `M` · **deps:** `apply-undo-workflow-service-extraction`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs`
- `docs/tool-reference.md`

## Acceptance

- [ ] Public documentation enumerates the five `apply_with_verify` and three sequence-revert response variants.
- [ ] Each variant names its status/reason discriminator, required properties, explicit-null properties, and recovery action.
- [ ] Documentation matches the stable wire contract verified by endpoint tests.

## Evidence

- Cold review found that descriptions do not fully document the multi-shape response contracts.

## Dependencies

- `apply-undo-workflow-service-extraction`
## Validation

- Verify documented shapes against `tests/RoslynMcp.Tests/Top10V2RegressionTests.cs` and `tests/RoslynMcp.Tests/UndoServiceTests.cs`.
