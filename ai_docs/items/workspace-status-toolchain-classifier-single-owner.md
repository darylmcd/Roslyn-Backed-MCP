# workspace-status-toolchain-classifier-single-owner

**row:** `workspace-status-toolchain-classifier-single-owner` · **pri:** `Low` · **size:** `M` · **deps:** —

## Anchors

- src/RoslynMcp.Core/Models/WorkspaceStatusSummaryDto.cs
- src/RoslynMcp.Roslyn/Helpers/WorkspaceDiagnosticSeverityClassifier.cs
- tests/RoslynMcp.Tests/WorkspaceStatusSummaryDtoTests.cs

## Acceptance

- [ ] Share a Core classifier consumed by both paths; table-test the same positive and negative messages through both entry points.

## Evidence

The summary DTO explicitly duplicates the Roslyn helper substring scan because Core cannot depend upward.
Observed during direct remediation on 2026-09-14.
