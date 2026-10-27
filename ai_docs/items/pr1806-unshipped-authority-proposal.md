# pr1806-unshipped-authority-proposal — Decide the retained preview/native/physical authority proposal

**row:** `pr1806-unshipped-authority-proposal` · **pri:** `Defer` · **size:** `—`

## Anchors

- `docs/decisions/0029-fork-source-session-lifecycle.md`
- `ai_docs/items/restore-fork-reviewed-project-graph.md`
- `https://github.com/darylmcd/Roslyn-Backed-MCP/blob/d333c2e96a1e23a31286cda53b920dbee5d6b7be/docs/decisions/0029-explicit-preview-snapshot-authority.md`

## Acceptance

- [ ] Keep the preserved branch and immutable tag; closing the PR vehicle grants no implementation or release approval.
- [ ] Unpark only after an explicit operator decision on each architectural family: explicit preview original-snapshot/state contracts; operation-owned physical mutation/undo/recovery; held filesystem and cleanup authority; compiler/analyzer/package certificates and native output/condition protocols; typed command/test phases and report ownership; invocation-scoped path policy and graph staging.
- [ ] Use the compact PR1806 reconciliation inventory and original branch item paths to rederive every prospective-only finding if that family is approved. Do not copy branch code, revive retired engine admission, or classify absent proposal mechanisms as current-main bugs.
- [ ] Keep the bounded restore graph row, extraction initiative and paused15 independent. Decompose approved implementation into independently correct dependency-chained rows; preserve all original obligations.
- [ ] Resolve the duplicate ADR0029 identity and existing next-major breaking-contract hold before publishing any adopted contract. Scratch owned-BuildHost evidence is not a deployed implementation or authorization.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. The retained 181 production paths include absent NativeCompilerContextCertificate, PhysicalDirectoryScope, PhysicalGraphStage, WorkspaceManager.Recovery and their required interface migrations. Main uses a different bounded fork admission path. Its existing graph row explicitly excludes these mechanisms. This row owns a product decision and prospective-only findings, not one enormous implementation.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
- 2026-10-27 diagnostic, self-contained summary (closed PR1806 head `d333c2e9` stays at GitHub `refs/pull/1806/head`; its archive branch, tag and the earlier stash draft were deleted): PR1806 proposed that every preview, compile, filesystem write, restore and test run carry certified authority — explicit preview snapshots, a native compiler certificate from an owned Csc run (reflecting on private Roslyn fields `_initialModules`/`_lazyData`), held-handle no-follow directory scopes, custody-based crash recovery requiring OS rename/delete permissions and a `.roslynmcp-custody` parent, an injected path policy with forks under OS temp, and typed command/test phases. About 181 production paths, 74k lines, next-major and source-breaking; its ADR number 0029 collides with `0029-fork-source-session-lifecycle`; never qualified.
- Every concrete defect it found is owned by a bounded row: the 38 rows citing PR1806, `preview-store-explicit-original-snapshot` (explicit originals), `project-reference-reload-embed-metadata`, `compile-check-diagnostic-suppressor-parity`, `analyzer-shadow-sweep-path-authority`, `analyzer-shadow-sweep-live-child-authority`, `physical-compensation-external-edit-ownership`, and the `test-trx-*` / `test-run-global-json-fail-closed-authority` / `mtp-resolved-engine-version-selection` rows.
- Recommendation: reject the architecture (private-ABI reflection, owned BuildHost, new OS-permission requirement) and close this row; nothing unowned remains.
