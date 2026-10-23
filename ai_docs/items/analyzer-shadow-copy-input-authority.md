# analyzer-shadow-copy-input-authority — Authorize and freeze analyzer inputs before owned shadow-loader IO

**row:** `analyzer-shadow-copy-input-authority` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs`
- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs`
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowInputAuthorityTests.cs`
- `tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs`

## Acceptance

- [ ] An unavailable compiler epoch retains native analyzer references and an empty isolation lease without our own dependency enumeration, metadata reads or copies.
- [ ] Certified analyzer isolation prepares asynchronously under current input authority: authorize mutable directory enumeration and the exact dependency catalog before explicit bytes; reuse independently issued SDK/package provenance without broad cache grants.
- [ ] Bind analyzer and dependency bytes to the admitted compiler epoch; use content-hash identity and owned shadows so later synchronous Roslyn callbacks perform no fresh source IO or stale authorization callbacks.
- [ ] Preserve lease/process ownership, cancellation, cleanup and assembly-location behavior; observe denied/locked input and post-admission revocation controls at the actual shadow-copy boundary.

## Evidence

- Source inspection: WorkspaceSessionLoader always retargets analyzer references after completing even an unavailable epoch. AnalyzerReferenceIsolation.Create calls RegisterAnalyzerDirectory, which eagerly enumerates sibling DLLs and reads assembly names; later LoadFromPath and Resolve call File.Copy without current path policy.
- Parent approved the required async frozen-input/catalog correction. Tests-first AnalyzerShadowInputAuthorityTests is saved; causal runtime and complete implementation remain pending. No completion claim.

Independent cold review: materialized shadow handles currently close before lazy LoadFromAssemblyPath, permitting replacement of admitted main/dependency bytes or rename-and-swap of the shadow directory. Tests-only AnalyzerShadowSnapshotLifetimeTests adds four actual lazy execution controls (main/dependency times overwrite/directory swap), exact source-byte and Assembly.Location assertions. Bind owned shadow identities throughout the loader lifetime, preserving synchronous callbacks without source IO. Designer owns complete correction after parent causal runtime; no passing claim yet.

Checkpoint: restore-truth-linked-sdk-shadow1 reported 9 pass / 3 fail / 12 total: both old analyzer callbacks causally failed (denied copy and admitted locked-source assembly-name read); linked mutation also failed in the shared image mechanism. Checkpoint: restore-analyzer-ownership-old1 reported 17 pass / 6 fail / 23 total: analyzer binding7/catalog3, linked2, lazy2, branch2, sibling1 passed; all four shadow main/dependency overwrite/route-replacement controls executed unadmitted value 99 instead of 7, and separate sweeper2 deleted external sentinels. Current source (unverified): required async authorized catalog; exact package-file proof; no source IO in synchronous callbacks; content-keyed process reuse; full requested assembly identity/CLR-compatible version selection with ambiguity refusal; SHA-verified owned file handles and physical directory-route leases retained until loader release. File execution callbacks and release share one gate. All acceptance remains open; current/floor/native broad gates pending. Separate sweep mechanism tracked by its companion row.

## Exact analyzer catalog identity evidence (2026-10-16)

| Mechanism | Current-session evidence / disposition |
|---|---|
| Defect | AnalyzerInputCatalog.ContentIdentity encoded framed strings through default UTF8 replacement, collapsing two distinct code-unit path pairs into one catalog identity. This unit proof is independent of file-system or native transport admissibility. |
| Complete repair | NativeIdentityEncoding writes explicit lengths and ushort code units; ContentIdentity and both AnalyzerReferenceIsolation callers require cancellation. Native globals, registry signatures and ruleset coordinate hashing reuse the same exact encoder. |
| Causal | restore-encoding-old-csc-fixed1/encoding-csc.trx:5 encoding failures, including both catalog pairs; framing and native key-case controls pass. |
| Fresh verification | restore-exact-encoding-fixed1/encoding-csc-ruleset.trx:19 passed,0 failed,0 skipped; exact encoding8, Csc4 and ruleset7. Catalog framing remains covered; image-byte and source-content hashes are unchanged. |
| Anchors | src/RoslynMcp.Roslyn/Services/AnalyzerInputCatalog.cs:33; src/RoslynMcp.Roslyn/Services/NativeIdentityEncoding.cs; src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs; tests/RoslynMcp.Tests/AnalyzerContextEncodingTests.cs. |
| Status | Keep OPEN pending complete initiative gates, independent review and merge. |
