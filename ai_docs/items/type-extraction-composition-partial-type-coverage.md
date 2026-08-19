## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:39` — `PreviewExtractTypeAsync` single-document syntax-root resolution
- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:489` — `InjectFieldAndCtorParameter` enumerates only `typeDecl.Members`

## Acceptance

- Extraction against a `partial` source type whose constructors live in another part either wires the composition field across all parts, or refuses with an explicit multi-part topology message.
- No path synthesizes a constructor in one part while leaving another part's constructors failing to assign the readonly field.
- Regression test covering a two-part `partial` type with the constructor in the non-extracted part.

## Evidence

Traced in code during PR #1281 review. `PreviewExtractTypeAsync` resolves `typeDecl` from a single document's syntax root and `InjectFieldAndCtorParameter` enumerates only that part's members, so `instanceIndexes` is empty in the extracted part. A constructor is synthesized there while the other part's constructors never assign the readonly field — the same silent-null failure the parent row targeted, on a shape it did not cover.

Source: code-quality review of PR #1281 (initiative `type-extraction-composition-constructor-coverage`, sweep 20260819T180531Z).
