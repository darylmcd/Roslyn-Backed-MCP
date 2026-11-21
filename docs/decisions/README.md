# Decisions

Architecture decision records (ADRs) for breaking, compatibility, and security decisions on the public surface. Breaking changes require an ADR here plus a `CHANGELOG.md` migration note (see [release-policy.md](../release-policy.md)).

Numbering runs 0001 to 0042 with 34 ADR files. Gaps 0014, 0017 and 0020 to 0025 are intentional: those numbers were never published.

| File | Purpose | Status |
|------|---------|--------|
| [0001](0001-locationdto-nested-field-migration.md) | Additive nested `Location` on `SymbolDto`/`DiagnosticDto`/`TypeUsageDto`, deprecation window, migration stages | Accepted 2026-08-06; section 2 amended by 0011 |
| [0002](0002-configured-sanctioned-root-boundary.md) | Server-owned sanctioned-root boundary, canonical path resolution, migration from client Roots | Accepted 2026-08-13 |
| [0003](0003-sdk-2x-wire-compatibility.md) | SDK 1.4.1 to 2.1.0 lineage, dual-protocol wire contract, correction classification | Accepted 2026-08-14; correction classification amended by 0041 |
| [0004](0004-public-command-diagnostic-path-projection.md) | Secret-safe projection of child-process diagnostic paths (Windows, UNC, POSIX) | Accepted 2026-08-22 |
| [0005](0005-stable-profile-preview-apply-closure.md) | Stable-profile preview/apply closure, registration invariants, token redemption | Accepted 2026-08-22 |
| [0006](0006-modelcontextprotocol-2-2-servicing.md) | ModelContextProtocol 2.1.0 to 2.2.0 servicing, stdio-only, non-breaking | Accepted 2026-08-24 |
| [0007](0007-tasks-extension-compatibility.md) | Tasks extension: exact version-pair adoption, per-request opt-in, synchronous fallback, finite retention | Accepted 2026-09-04; version pair amended by 0031 (now 2.3.0) |
| [0008](0008-workspace-id-optional-adoption.md) | Optional `workspaceId` adoption, NO-GO expansion decision, recheck trigger | Accepted 2026-09-04 |
| [0009](0009-tool-surface-policy.md) | Tool consolidation risk buckets and compatibility aliases | Accepted 2026-09-04 |
| [0010](0010-validation-verdict-completeness.md) | Incomplete compilation verdicts and retryable test-phase timeouts | Accepted 2026-09-15 |
| [0011](0011-v5-major-release-contract.md) | The 5.0.0 contract: alias removal, nullable `LocationDto` end span, error categories; indexes the nine earlier breaking ADRs | Accepted 2026-10-28 |
| [0012](0012-editorconfig-write-workspace-boundary.md) | Editorconfig writes bounded to the loaded workspace; ancestor-config migration | Accepted; shipped 5.0.0 |
| [0013](0013-workspace-load-default-auto-restore.md) | Tri-state `autoRestore` for `workspace_load` | Accepted 2026-10-01 |
| [0015](0015-dispatch-public-argument-diagnostics.md) | Safe actionable argument refusals, candidate-path removal | Accepted 2026-10-06; shipped 5.0.0 |
| [0016](0016-malformed-refactoring-input-refusals.md) | Malformed refactoring input refusals and envelope corrections | Accepted 2026-10-06; shipped 5.0.0 |
| [0018](0018-extraction-public-argument-diagnostics.md) | Extraction coordinate bounds, safe refusals, shared value-expression selection | Accepted 2026-10-07; shipped 5.0.0 |
| [0019](0019-suppression-public-argument-diagnostics.md) | Suppression severity corrections, safe parameter guidance, retained BCL identities | Accepted 2026-10-07; shipped 5.0.0 |
| [0026](0026-change-signature-struct-construction-preservation.md) | Marker construction preserves runtime-zero and serializer paths across struct arity changes | Accepted 2026-10-15 |
| [0027](0027-validation-scope-and-test-run-diagnostics.md) | Explicit empty validation scope, safe pagination endpoints, actionable test-run diagnostics | Accepted 2026-10-15; shipped 5.0.0 |
| [0028](0028-editorconfig-assignment-grammar-refusals.md) | Refuse non-single-line editorconfig assignments; `add_pragma_suppression` takes one id | Accepted 2026-10-20; shipped 5.0.0 |
| [0029](0029-fork-source-session-lifecycle.md) | Retain fork source sessions, refuse busy close, drain fork operations before provider teardown | Accepted 2026-10-24; shipped 5.0.0 |
| [0030](0030-retained-fork-storage-and-lifetime.md) | Authorized sibling storage, native working-directory preflight, ancestor lifetime ownership for retained forks | Accepted 2026-10-24 |
| [0031](0031-modelcontextprotocol-2-3-servicing.md) | ModelContextProtocol 2.2.0 to 2.3.0 servicing, stdio-only, ADR 0007 pair amendment | Accepted 2026-10-26 |
| [0032](0032-persisted-composite-token-workspace-identity.md) | Bind persisted composite tokens to the loaded solution path (format 2) | Accepted 2026-11-06 |
| [0033](0033-composite-preview-source-fingerprints.md) | Composite target drift detection and exclusive new-file publish (format 4) | Accepted 2026-11-07, amended 2026-11-11 |
| [0034](0034-interface-nullable-contract-postcondition.md) | Verify generated interface nullable contracts in extraction previews | Accepted 2026-11-07 |
| [0035](0035-interface-signature-reference-closure.md) | Valid acyclic references for emitted interface signature owners; refuse incompatible metadata | Accepted 2026-11-07 |
| [0036](0036-cyclic-interface-signature-refusal.md) | Refuse interface signature dependencies that would create a project-reference cycle | Accepted 2026-11-08 |
| [0037](0037-interpolated-handler-contract-projection.md) | Preserve handler argument attributes only when source and contract calls bind the same constructors | Accepted 2026-11-08 |
| [0038](0038-interface-preview-compilation.md) | Reject new compiler errors in final interface previews; compose extract-and-wire on one solution | Accepted 2026-11-08 |
| [0039](0039-dotnet-11-sdk-floor.md) | Raise the source-build SDK floor to 11.0.100 for Roslyn 5.12 and MSBuild 18.x; keep `net10.0` | Accepted 2026-11-18 |
| [0040](0040-extensions-11-on-net10.md) | Move the Microsoft.Extensions runtime family to 11.x on `net10.0` | Accepted 2026-11-18 |
| [0041](0041-stable-corrections-and-security-exception.md) | Corrections and minimal leaked-input removals ship in a 5.x minor; agent qualifies with wire evidence | Accepted 2026-11-20 |
| [0042](0042-default-on-bounded-file-log-sink.md) | Default-on bounded file log sink; `disabled` opt-out; `server_info.observability` reports the log path | Accepted 2026-11-20 |
