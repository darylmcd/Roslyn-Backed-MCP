# cache-graph-identity-framing — Frame graph cache identity unambiguously

**row:** `cache-graph-identity-framing` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:189`

## Acceptance

- [ ] Reproduce valid Unix path delimiter collisions; preserve graph-order independence and all represented fields.
- [ ] Use unambiguous exact-string framing with explicit Unicode behavior; preserve independent cache validation rather than trusting a hash alone.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. ComputeMsbuildGraphHash concatenates project paths, tab, reference paths, pipe and newline, then UTF8 encodes. Those characters can occur in Unix paths.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
