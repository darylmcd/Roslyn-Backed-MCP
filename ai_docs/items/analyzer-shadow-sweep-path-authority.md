# analyzer-shadow-sweep-path-authority — Reject reparse traversal before analyzer shadow sweeping

**row:** `analyzer-shadow-sweep-path-authority` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`
- `src/RoslynMcp.Roslyn/Helpers/AnalyzerSweepDirectory.cs`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryLease.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowSweepAuthorityTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs`

## Acceptance

- [ ] Validate the shared root and workspace-directory traversal without following reparse targets before enumeration or deletion.
- [ ] Preserve external children and bytes when an apparent workspace directory is a real Windows junction, including replacement of a formerly owned directory.
- [ ] Keep process ownership locks, active lease protection, stale-age checks and owned stale-root reclamation.
- [ ] Observe regression controls fail on prior behavior and pass after the complete traversal correction.

## Evidence

- Read-only inspection: SweepAbandonedRoots enumerates each workspaceDirectory before checking its physical identity; a junction redirects child enumeration to an external target, and recursive deletion receives those external child paths.
- Tests-only AnalyzerShadowSweepAuthorityTests installs two actual Windows junction controls under an isolated shared parent. Parent owns runtime; no causal result or completion claim yet.

Parent causal restore-analyzer-ownership-old1 executed both real Windows junction controls; both deleted the external stale-child sentinel. Complete correction in progress: pinned namespace traversal and bounded nonrecursive removal; shared helper must preserve supported Unix platforms through descriptor-relative nofollow operations, not a no-op directory handle or platform skip. No corrected runtime claim.

Source checkpoint: AnalyzerSweepDirectory provides Windows full-route pins and Linux/macOS descriptor-relative openat/fdopendir/readdir/unlinkat traversal without recursive path deletion. Sweeper and lease cleanup share it; lease captures original parent/root identity before loader release. Added parent-replacement and lease-ancestor replacement controls; Unix uses real symlinks, Windows real junctions. Primary OS headers verified platform flag/layout differences. Source-only, diff check clean; corrected Windows/Linux/macOS runtime remains pending.

Parent-serialized bounded evidence (2026-10-16): original Windows junction controls both deleted external stale-child sentinels; localized pathname-only namespace factory failed all three stronger replacement cases and restored exact helper SHA A9A3C2EAB3B1EA373B03AE6055CC7B8AD50A8EFE2FEE434F28455AAB91A8BC7E. Corrected Windows B five controls passed after FILE_LIST_DIRECTORY activated no-delete route sharing. Actual Ubuntu SDK 10.0.400 current prefix-fixed six controls passed in normal TMPDIR (91ms) and independently configured symlink TMPDIR (97ms), exact copied DLL hashes verified by parent. Existing stale-process-copy reclamation also passed separately on Linux (45ms). Logs: /home/<user>/.codex/scratch/roslyn-correctness15/analyzer-sweep-linux-prefix-fixed-normal.log, analyzer-sweep-linux-prefix-fixed-alias.log, analyzer-stale-positive-linux-normal.log. POSIX moved-leaf oracle preserves external bytes and removes known owned contents while retaining only the empty renamed directory, without discovering its unowned new pathname. No full aggregate, macOS runtime, merge, acceptance closure or final-head-wide claim; row remains open.
