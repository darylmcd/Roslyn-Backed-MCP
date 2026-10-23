# hash-only-input-materialization — hash-only-input-materialization

**row:** `hash-only-input-materialization` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/FileContentDigest.cs:7`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalInputReader.cs:23`
- `src/RoslynMcp.Roslyn/Services/CompilerArgumentInputContext.cs:660`
- `src/RoslynMcp.Roslyn/Services/InstalledPackageAuthority.cs:122`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs:310`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs:193`
- `tests/RoslynMcp.Tests/FileContentDigestTests.cs:11`
- `tests/RoslynMcp.Tests/InstalledPackageAuthorityTests.cs:103`
- `tests/RoslynMcp.Tests/NativeCompilerContextCertificateTests.cs:150`

## Acceptance

- [ ] Stream hash-only package/compiler input reads through the same held FileShare.Read authority; retain complete EOF hashing, exact current grants, physical kind checks, membership and final closure checks.
- [ ] Migrate every required digest-provider caller, including injected filesystem and virtual providers; preserve bytes for parsing/archive/native image inspection.
- [ ] Prove exact/empty immutable digests, missing versus unreadable inputs, original sharing, cancellation, virtual changed/missing inputs and SnapshotOnly no-IO/refusal.
- [x] Observe baseline/candidate materialization reduction with exact preserved successful fresh-read counts and unchanged native package/compiler assertions.
- [ ] Pass normal uninstrumented focused and required aggregate gates; qualify supported Windows/Linux paths.

## Evidence

| Evidence | Result |
|---|---|
| Actual fork allocation215 | Compiler receipt accumulator1,380,987,968 + package payload accumulator802,715,840 weighted bytes;20.30% of10,755,707,320 in60seconds. No missing stacks/lost events. |
| Causal228 package workload | Both variants passed;9 fresh reads retained (candidate5 byte +4 digest). Materialized22,934,647 ->6,900,504 bytes;16,034,143 streamed bytes exactly equal the reduction. |
| Causal228 compiler workload | Both variants passed;615 fresh reads retained (candidate349 byte +266 digest). Materialized64,526,009 ->19,644,205 bytes;44,881,804 streamed bytes exactly equal the reduction. |
| Source restoration | Diagnostic restoration receipt exit0; no concurrent drift. Normal229 qualification pending. |
| Measurement limits | Counts prove removed full-buffer materialization with preserved successful reads; no latency, deadline recovery, or full-gate claim. |

## Scope

- High/L: required provider migration spans38 files, including mechanical shared missing-reader rename and explicit virtual/injected adapters.
- Keep archive/signature/JSON/source/PE parsing bytes; no cross-boundary cache, missing-read fallback, freshness removal or changed runtime bounds.
- Related row: `installed-package-payload-duplicate-digest` removes repeated hashing of the same owned private buffer; this row removes full-buffer allocation at hash-only reads.
- Receipts: `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/streaming-owned-input-digest-proposal/materialization-count-ae21f5f3aed749e19a02a1136e3e7ffa/counts.json` and `restoration.json`.

2026-10-17 causal228: unchanged package and native compiler workloads both passed baseline/candidate. Successful fresh reads remain exactly9 and615; candidate splits5+4 and349+266 byte/digest reads. Materialized bytes drop22,934,647→6,900,504 and64,526,009→19,644,205; streamed16,034,143 and44,881,804 bytes exactly equal those reductions. Guarded restoration exit0/no concurrent drift. Counts prove removed full-buffer materialization, not latency/deadline recovery; normal229 and full qualification pending. Receipt: streaming-owned-input-digest-proposal/materialization-count-ae21f5f3aed749e19a02a1136e3e7ffa/counts.json under session scratch. Related installed-package-payload-duplicate-digest remains separately tracked for private-buffer hash reuse.
