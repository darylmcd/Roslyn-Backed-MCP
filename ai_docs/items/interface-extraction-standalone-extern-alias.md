# interface-extraction-standalone-extern-alias — Preserve standalone interface alias directives

**row:** `interface-extraction-standalone-extern-alias` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-signature-reference-closure`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs:189`
- `src/RoslynMcp.Roslyn/Helpers/UsingDirectiveSynthesizer.cs`
- `tests/RoslynMcp.Tests/ExtractInterfaceSemanticUsingsTests.cs`

## Acceptance

- [ ] Standalone extraction from a source whose signature uses an aliased reference emits only the required `extern alias` directives from compilation-unit and containing namespace scopes (including nested ancestors), preserving symbol binding and compiling the generated interface without changing unrelated reference aliases.

## Evidence

- At `0121d9ab`, `BuildInterfaceFile` creates three compilation-unit layouts with usings but drops required compilation-unit and containing-namespace extern directives; the shared projector retains `ext::Shape` syntax, so an alias-only standalone preview can store an uncompilable token. Found while implementing the dependency-reference slice; not shipped in that PR.

## Context

- Follow-up to `cross-project-interface-signature-reference-closure`. Red test: an aliased Shared project reference and `extern alias ext;` source member return `ext::Shared.Shape`; unchanged code omits the directive in the generated file and reports CS0430/CS0432. Repeat for file-scoped, block and nested namespace extern scopes. Extract reusable alias discovery into UsingDirectiveSynthesizer only when both emitters consume it; avoid a dead helper in the first slice.
