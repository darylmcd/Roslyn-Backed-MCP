# analyzer-shadow-sweep-path-authority — Keep analyzer sweeping inside owned no-follow paths

**row:** `analyzer-shadow-sweep-path-authority` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:172`
- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:203`
- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:217`

## Acceptance

- [ ] Validate shared-root and workspace-directory traversal without following replacement reparse targets before enumeration/deletion.
- [ ] Cover workspace-directory Windows junctions and supported symlinks; external stale child bytes and attributes remain unchanged. Preserve live owner/lease protection and ordinary stale-root reclamation.
- [ ] Use the existing cleanup boundary and smallest complete no-follow correction; the absent PhysicalDirectoryLease/Scope architecture is not a prerequisite.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. Current SweepAbandonedRoots enumerates children beneath workspaceDirectory before checking reparse traversal, then recursively deletes stale child directories. A workspace-directory junction exposes external children to that loop; retained independent controls describe this exact boundary.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
