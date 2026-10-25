# cache-graph-identity-framing — Frame graph cache identity unambiguously

**row:** `cache-graph-identity-framing` · **pri:** `Medium` · **size:** `S` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:189`

## Acceptance

- [ ] Reproduce valid Unix path delimiter collisions; preserve graph-order independence and all represented fields.
- [ ] Use unambiguous exact-string framing with explicit Unicode behavior; preserve independent cache validation rather than trusting a hash alone.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. ComputeMsbuildGraphHash concatenates project paths, tab, reference paths, pipe and newline, then UTF8 encodes. Those characters can occur in Unix paths.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
