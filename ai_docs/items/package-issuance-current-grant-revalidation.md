# package-issuance-current-grant-revalidation — Revalidate current package grants before every issuance read

**row:** `package-issuance-current-grant-revalidation` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InstalledPackageAuthority.cs:145`
- `src/RoslynMcp.Roslyn/Services/InstalledPackageAuthority.cs:207`
- `tests/RoslynMcp.Tests/InstalledPackageIssuanceGrantTests.cs`

## Acceptance

- Retain initial archive/hash/metadata batch preflight; require the current exact grant immediately before every explicit archive, hash, installation metadata and payload read.
- Reauthorize each input before the final ordered installation identity recheck; preserve full hashes, held physical identities, input order and all existing checks.
- Share the ordered authorization/identity protocol with package proof revalidation; do not cache request permissions or add optional compatibility parameters.
- Preserve typed policy refusal provenance and redaction; deny before reading a now-locked revoked path.
- Cover hash and metadata grants revoked after initial batch approval, archive grant revoked during later payload authorization, and stable complete issuance.
- Assert physical-only preflight remains valid under test blockers; observe original behavior fail and corrected behavior pass without broadening exception expectations.
- Preserve exact bootstrap-cache provenance exemptions and required current grants on all other retained package files.

## Evidence

| Observation | Evidence |
|---|---|
| Initial approval becomes stale before later explicit reads | CreateCoreAsync authorizes archive/hash/metadata together at line145, then reads/parses archive across awaits before RequiredBytesAsync(hashPath/metadataPath). Those readers do not independently authorize. |
| Final reread omits current grants | Line207 calls PhysicalInputIdentity.AssertCurrentAsync(current,ct) after intervening awaited payload authorization callbacks; an earlier grant may have been revoked. |
| Existing current-proof protocol demonstrates required behavior | AssertPackageFilesCurrentAsync lines84-91 authorizes each input before its full identity read. |
| Distinct mechanism | Temporal grant provenance, not the held-coordinate acquisition defect in physical-input-held-read-authority. |
| Prepared correction | Scratch package-grant-revalidation100/proposal.patch retains all preflight/identity checks and uses a required authorization callback for each byte read plus a shared ordered final identity protocol. |
| Prepared controls | InstalledPackageIssuanceGrantTests has hash/metadata revocation rows, final archive revocation row, and stable issuance row using real NuGet installation fixtures. |
| Verification limit | Patch apply-check passed; causal baseline and candidate runtime remain pending. No passing runtime or shipping claim. |

## Authority Scope

- An issued proof binds content and installation provenance; it is not a durable permission grant.
- Final selecting-assets authorization does not make all package grants atomic. Non-bootstrap package files retain RequiresCurrentGrant obligations for subsequent use.
- No product edit, backlog mutation or batch advancement performed while preparing this payload.

2026-10-17 causal102: original production failed all three revoked-grant controls with content IO reached; stable issuance passed. Installed shared required per-read current authorization and ordered per-input final revalidation without removing initial batch preflight. authority103 Windows: 39 passed, 5 platform/capability skips, 0 failed (44 total); exact compiled assemblies namespace104 Linux: 38 passed, 2 Windows-only skips, 0 failed (40 total), including all four issuance controls and image authority cases. Supersedes prepared-only notes above. Full gate and shipping remain pending.
