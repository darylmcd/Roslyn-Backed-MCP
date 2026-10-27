# analyzer-shadow-sweep-live-child-authority — Preserve active foreign-host analyzer lease children

**row:** `analyzer-shadow-sweep-live-child-authority` · **pri:** `High` · **size:** `S`

## Anchors

- src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:280-290 (age check and recursive child deletion)
- src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:518 (process-local live-root registry)
- src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:677-691,758-775 (catalog then late dependency copy/load)
- (new) tests/RoslynMcp.Tests/AnalyzerShadowForeignLeaseAuthorityTests.cs

## Acceptance

- [ ] Establish a deterministic valid producer/foreign-sweeper interleaving for a long-lived active lease and late dependency copy; observe old-behavior loss before calling a correction fixed. Distinguish source proof from runtime reproduction.
- [ ] Require valid whole-child cleanup authority rather than assuming an already mapped sibling prevents partial recursive deletion. Preserve active copies/loads and genuine disposed/abandoned child reclamation across hosts.
- [ ] Retain the separately delivered process/workspace parent initialization protection, original stale-age policy and same-process live-root protection. No timer padding, blanket cleanup disablement, retries, swallowed failures or weakened assertions.

## Evidence

- Fresh narrow semantic/BCL review at49e0b03a confirms ordinary leases have no age expiry and can load dependencies lazily after initial cataloging. A foreign sweep sees only its own live-root registry, checks age, then deletes recursively without the producer load gate.
- A copy completed after the age snapshot can be removed before LoadFromAssemblyPath. The Windows recursive-delete implementation continues siblings after recording locked-file failures, so an old mapped sibling does not establish subtree-wide safety.
- This is source-grounded, not runtime-reproduced or attributed to a current CI failure. The child loop was unchanged by1855; its parent-initialization correction remains correctly scoped. Existing analyzer-shadow-sweep-path-authority owns reparse traversal, and automatic-reload-analyzer-reader-lifetime owns reload reader ownership; neither covers this foreign-sweeper authority.
