# type-extraction-method-type-parameter-flagged-as-dangling — Generic methods using their own type parameter are refused

**row:** `type-extraction-method-type-parameter-flagged-as-dangling` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Extracting a generic method that uses only its own type parameter succeeds and compiles; a method using a containing-type type parameter is still refused; red regression first.

## Evidence

- TypeExtractionService.cs ~1553 (CollectExtractTypeBlockingDependencies + IsDeclaredInOrUnderType); PR 1990 had to use Pad<T>(int) with an unused type parameter in its generic test because Echo<T>(T) is refused today.
