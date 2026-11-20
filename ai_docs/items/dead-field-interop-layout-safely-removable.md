# dead-field-interop-layout-safely-removable — Never offer layout-significant struct fields as removable

**row:** `dead-field-interop-layout-safely-removable` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`
- `src/RoslynMcp.Roslyn/Helpers/NoFollowDirectoryDeleter.cs:463`

## Acceptance

- [ ] Fields of types with explicit or sequential StructLayout (and fields populated only by native interop) report `safelyRemovable:false` with a `removalBlockedBy` reason.
- [ ] remove_dead_code_preview refuses or warns for the same fields.
- [ ] Red first on `NoFollowDirectoryDeleter.FileAttributeTagInfo.ReparseTag`.

## Evidence

- G1-F2 and Phase 6i: five interop fields returned confidence high / safelyRemovable true; the removal preview carried no warning.
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
