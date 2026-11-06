# preview-persist-dir-unvalidated — The preview persist directory is not rejected when it lies inside a workspace

**row:** `preview-persist-dir-unvalidated` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `tests/RoslynMcp.Tests/ServiceCollectionExtensionsTests.cs`

## Acceptance

- [ ] A persist dir inside a loaded or configured workspace root is refused with a typed startup error; red regression first.

## Evidence

- Program.cs ~251; PR 1997 review: a shared persist dir is the surface a forged composite token needs.
