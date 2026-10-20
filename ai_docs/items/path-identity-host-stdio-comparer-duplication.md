# path-identity-host-stdio-comparer-duplication — Use FileSystemPath in Host.Stdio path helpers instead of hand-rolled OS ternaries

**row:** `path-identity-host-stdio-comparer-duplication` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/SolutionDiscoveryHelper.cs:30`
- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs:15,30`
- `src/RoslynMcp.Host.Stdio/Tools/TestRunPublicProjection.cs:216,367`
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs:707`

## Acceptance

- [ ] Each site uses FileSystemPath (InternalsVisibleTo already grants Host.Stdio access) or the shared helper; TestRunPublicProjection PathsEqual is platform-aware.
- [ ] A Linux case-distinct regression per changed behavior fails on old code.

## Evidence

- Cluster-C sweep: three hand-rolled copies of the FileSystemPath OS decision plus one unconditional PathsEqual. Static finding.
