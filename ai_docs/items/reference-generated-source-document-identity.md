# reference-generated-source-document-identity — Recognize source-generated reference documents by identity

**row:** `reference-generated-source-document-identity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ReferenceService.cs:135`

## Acceptance

- [ ] Prove generated-location classification with a native generator whose hint does not end in .g.cs or .generated.cs. Preserve checked-in/build-generated conventions and physical declarations. Screen stable reference payload behavior before selection.

## Evidence

- At 9e7a9439 ReferenceService says Solution.GetDocument(tree) cannot return source-generated documents. [Pinned Roslyn 5.9.0 source](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/Core/Portable/Workspace/Solution/Solution.cs) recognizes SourceGeneratedDocumentState. A live GeneratedRegex declaration probe returned generated locations. The null-or-suffix heuristic can miss other generator hint names.
