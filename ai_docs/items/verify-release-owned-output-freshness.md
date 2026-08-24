# verify-release-owned-output-freshness — Exclude stale files from release artifacts

**row:** `verify-release-owned-output-freshness` · **pri:** `High` · **size:** `S`

## Anchors

- `eng/verify-release.ps1`
- `tests/RoslynMcp.Tests/VerifyReleaseChildScriptTests.cs`

## Acceptance

- [ ] Resolve and containment-check the verifier-owned publish and manifest directories before deleting and recreating them for every non-shard-only run.
- [ ] Never recursively delete `OutputRoot` itself or any path outside the repository-owned artifact boundary.
- [ ] Generate the SHA-256 manifest only from files produced by the current publish invocation.
- [ ] One seeded-stale-file regression proves neither the publish upload set nor the manifest includes content from an earlier run.

## Evidence

The verifier currently creates existing publish and manifest directories with `-Force` but does not empty them. A local repeated `just ci` can therefore hash and ship an unrelated file left by an earlier run even though fresh hosted runners hide the defect.
2026-08-24 release-integrity review: also reject a successful dotnet publish that produces zero files and reject an empty SHA-256 manifest. Seed the fake publisher with both empty-success and expected-host-output cases so upload success always proves a nonempty current-run deliverable.
