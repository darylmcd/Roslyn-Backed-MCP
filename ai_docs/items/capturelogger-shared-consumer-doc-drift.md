# capturelogger-shared-consumer-doc-drift — Keep CaptureLogger documentation aligned with shared consumers

**row:** `capturelogger-shared-consumer-doc-drift` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/MsBuildEvaluationServiceTests.cs:51`
- `tests/RoslynMcp.Tests/SuppressionServiceTests.cs:217`

## Acceptance

- [ ] Replace the fixed consumer enumeration with durable consumer-agnostic wording, or include every current `CaptureLogger<T>` consumer.
- [ ] Keep the helper's shared test-infrastructure purpose clear and preserve all existing logger assertions.

## Evidence

- Cold code-quality review of PR #1075 found that adding `CaptureLogger<SuppressionService>` made the helper summary's exhaustive Scaffolding/EditorConfig/MsBuild list stale.

## Context

This is documentation-only test debt. Do not duplicate or relocate the shared helper as part of the fix.
