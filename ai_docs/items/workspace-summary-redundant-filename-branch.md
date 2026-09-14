# workspace-summary-redundant-filename-branch

**row:** `workspace-summary-redundant-filename-branch` · **pri:** `Low` · **size:** `S` · **deps:** —

## Anchors

- src/RoslynMcp.Core/Models/WorkspaceStatusSummaryDto.cs
- tests/RoslynMcp.Tests/WorkspaceStatusSummaryDtoTests.cs

## Acceptance

- [ ] Remove the redundant extension branch in GetSolutionOrProjectFileName while preserving null/whitespace handling and platform-native basename behavior for all extensions.

## Evidence

GetSolutionOrProjectFileName checks .sln/.slnx/.csproj extensions but returns the same file value from both branches. Observed during 2026-09-14 direct-remediation review.
