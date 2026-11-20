# find-overloads-default-project-resolution — Resolve find_overloads metadata types deterministically

**row:** `find-overloads-default-project-resolution` · **pri:** `Medium` · **size:** `S` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`

## Acceptance

- [ ] Without projectName the tool resolves the type in a project that has it, or says which project it used and why nothing matched.
- [ ] A missing member is distinguishable from an empty overload set.

## Evidence

- G2-F2: 0 overloads by default, 13 with projectName=RoslynMcp.Roslyn.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
