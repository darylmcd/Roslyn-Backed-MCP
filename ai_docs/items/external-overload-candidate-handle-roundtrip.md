# external-overload-candidate-handle-roundtrip - Preserve external overload identity in candidate handles

**row:** `external-overload-candidate-handle-roundtrip` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolHandleSerializer.cs`
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs`
- `tests/RoslynMcp.Tests/SymbolDisambiguationMrtrWireTests.cs`

## Acceptance

- [ ] Reproduce external overloaded metadata lookup followed by immediate candidate-symbolHandle reference/definition lookup in the same ready workspace.
- [ ] Preserve enough overload identity for every advertised candidate handle to roundtrip. Do not suppress ambiguity or require callers to guess a source location.
- [ ] Cover System.Diagnostics.Process.Kill() and Kill(bool), external/source symbols and stable safe not-found behavior without changing published categories.

## Evidence

- 2026-10-21 verified live roslyn-mcp 4.3.0+85b6e3dc559ac05c0bceb8fcbaf390c99a5f31a6, .NET10.0.13; ready DevBuildRunner.slnx workspace 6bed727d7b4349a98e4231116e8f6b1a.
- find_references with metadataName=System.Diagnostics.Process.Kill, summary=true, limit=100 returned ambiguous=true,count=2 and Kill(bool) candidate handle:

```text
eyJNZXRhZGF0YU5hbWUiOm51bGwsIkRpc3BsYXlOYW1lIjoiU3lzdGVtLkRpYWdub3N0aWNzLlByb2Nlc3MuS2lsbChib29sKSIsIkZpbGVQYXRoIjpudWxsLCJMaW5lIjpudWxsLCJDb2x1bW4iOm51bGx9
```

- Immediate find_references with that symbolHandle in the same workspace returned error=true, category=NotFound, exceptionType=KeyNotFoundException, message=The requested item was not found. Ensure the workspace is loaded and the identifier is correct.
- Source locator tests/DevBuildRunner.Tests/OnboardingFixtures.cs:62:90 in the same workspace resolved the exact Kill(bool) and all13 references. Tool remained live; source-locator semantic navigation worked. Root cause of metadata signature serialization/resolution remains to rederive.
