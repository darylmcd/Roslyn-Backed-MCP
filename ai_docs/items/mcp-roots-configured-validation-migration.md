# mcp-roots-configured-validation-migration — Replace Roots as the path security source

**row:** `mcp-roots-configured-validation-migration` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SecurityOptions.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] Server configuration supplies the canonical sanctioned-root allowlist used by file-path validation.
- [ ] Client Roots may narrow the configured allowlist during the compatibility window but cannot widen it or become the sole security boundary.
- [ ] Canonicalization, symlink/junction traversal, empty configuration, and one-level worktree widening retain fail-closed coverage.
- [ ] The legacy MCP9005 Roots suppression is removed from `ClientRootPathValidator.cs`.

## Evidence

- ModelContextProtocol 2.1 deprecates Roots and recommends server configuration or explicit parameters.
- `ClientRootPathValidator` currently treats client Roots as the only optional allowlist, so deleting Roots use before a configured replacement would weaken the security boundary.
