# change-signature-service-refusals-public-message — Publish safe change-signature refusals

**row:** `change-signature-service-refusals-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewMetadataNameShapeTests.cs`

## Acceptance

- [ ] Classify every caller refusal in ChangeSignatureService as safe public argument/operation detail or explicit redacted detail; retain true internal invariants.
- [ ] Duplicate/unknown/identity permutations, operation and position bounds, required inputs, mixed arguments, no-change and metadata-only refusals give actionable envelopes with real parameter names.
- [ ] Preserve published categories and BCL exceptionType; never publish raw request text, absolute paths or lower-layer messages.
- [ ] Update exact-type assertions and verify hostile-input redaction through ToolErrorHandler; keep valid preview/apply coverage.

## Evidence

- Source checked at 89ccceb3c58f8dbfc02f856358f287535253c7d1: plain ArgumentException constructions at ChangeSignatureService.cs:38,55,105,152-166,229,293-300,332-360,422-461; plain caller InvalidOperationException refusals at :96,304,379,523.
- Duplicate reorder token at :461 is an independently triggerable change-signature refusal; ToolErrorHandler requires IPublicMessageException for authored public diagnostics.

## Context

- Split from change-signature-refusals-public-message; independent of formatter range publication. Current Core PublicArgumentException, ArgumentErrors and Host boundary code govern the contract; the historical Family design pointer no longer exists.
- Optional positional-binding preservation is a separate algorithm defect tracked by change-signature-omitted-optional-binding.
