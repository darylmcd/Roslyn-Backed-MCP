# change-signature-linked-document-callsite-entries — Aggregate callsite entries by physical file

**row:** `change-signature-linked-document-callsite-entries` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:594`
- `src/RoslynMcp.Roslyn/Services/BulkRefactoringService.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Change-signature callsite updates for a linked or multi-targeted document (same physical path, different DocumentIds) are one entry per physical file, using the shared CallsiteUpdateProjection that BulkRefactoringService and ParameterObjectService use.
- [ ] Red-first linked-document test; decide and document whether totalCallsites counts a linked document once or per DocumentId (BulkRefactoringService and ParameterObjectService sum per DocumentId today).

## Evidence

- Cluster-C implementation review: the builder projects callCounts per DocumentId into CallsiteUpdateDto, so linked documents yield duplicate-FilePath entries. Static finding; no runtime reproduction.
