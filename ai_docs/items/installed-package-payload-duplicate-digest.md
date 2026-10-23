# installed-package-payload-duplicate-digest — installed-package-payload-duplicate-digest

**row:** `installed-package-payload-duplicate-digest` · **pri:** `Medium` · **size:** `S`

| Field | Value |
|---|---|
| Proposed id | installed-package-payload-duplicate-digest |
| Priority / size | Medium / S |
| Production anchor | src/RoslynMcp.Roslyn/Services/InstalledPackageAuthority.cs::CreateCoreAsync |
| Test anchor | tests/RoslynMcp.Tests/InstalledPackageAuthorityTests.cs::RealCustomInstallation_CurrentGrantPrecedesManifestReadAndRemainsRequired |
| Defect | Archive comparison and issued identity hash the same owned payload byte array twice in one callback-free interval. |
| Acceptance | Compute one payload digest for comparison and identity; retain every fresh read/current grant/archive check/final rehash. Exact payload identity and modified-payload refusal controls pass. Record static work reduction separately from measured timing. |
| Evidence | Current source lines196-207 and Identity helper321; scratch package-payload-private-digest-proposal. Runtime pending. |

2026-10-17 qualification213-214: guarded baseline/candidate count diagnostic removed 2 comparison hashes over 16033776 private bytes; identity hashes unchanged at 9/22934647 bytes and completed fresh-input checks unchanged at 7. Exact payload receipt assertion passed both variants; source restored before installing production digest reuse. Normal Release214 InstalledPackageAuthorityTests 9/9 passed. Receipt: %USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/package-payload-private-digest-proposal/count-9aca78d6fdc640309e6dc7591266e448/counts.json. This proves work reduction, not deadline recovery. Full gate pending.

2026-10-17 Linux216: fresh actual Roslyn/Core restore created native Linux assets and NuGet.Versioning6.3.4 in process-bootstrap cache; premise checked; byte-identical transferred normal216 DLL ran all 9 package controls with zero failures/skips. Earlier Linux214 fixture lacked product assets (7 pass/2 setup failures); corrected provisioning, not test/oracle changes. Receipt package216-linux/results.trx and copy.sha256 under session scratch. Streaming hash-only follow-on remains under review; no deadline/full-gate claim.
