# PR1806 unique-content reconciliation

<!-- scope: in-repo -->
<!-- purpose: Preserve unique retained intent and bind every original PR path to a current owner before closing the PR vehicle. -->

| Contract | Verified disposition |
|---|---|
| Retained head | `d333c2e96a1e23a31286cda53b920dbee5d6b7be` |
| Retained branch | `codex/restore-reviewed-graph`; keep local and remote references. |
| Immutable archive | `archive/pr1806-restore-reviewed-graph-20261025`, independently verified to peel to the retained head. |
| Audited current main | `fc5bb88676214a942daa727c7ca88ac8dd34b61c` |
| True comparison base | `d2c6ac748928d5bcdccfaae1b95efc6487d4dc98` |
| Original scope | 794 paths; 74,578 added and 9,648 removed lines. |
| Three-way provenance | 447 branch-only; 291 changed only on retained side; 55 changed on both sides; one obsolete item absent on both sides. Zero surviving files are byte-identical; this does not imply zero carried behavior. |
| Approval | Two independent cold reviewers passed source-only disposition and tracking after corrections. No retained implementation, architecture, runtime receipt or release is approved by this audit. |
| Canonical writer | File new rows through `~/.claude/scripts/backlog.mjs`; do not restore the retained backlog or retired orchestration state wholesale. |
| Filing receipt | 38 PR1806 rows plus the requested docs-CI row filed canonically; 379 open rows before final metadata/blocker closures; backlog lint 0 ERROR/0 WARN. |
| Closure | Operator authorized closing the superseded PR vehicle after canonical filing and publication. Preserve branch/tag, existing remainders, extraction initiative, paused15 and unrelated worktrees. |

## Machine-input coverage

| Artifact | Completeness |
|---|---|
| [Path owners](2026-10-25-pr1806-reconciliation/coverage.tsv) | Exact original 794-path set; no duplicates, omissions or unknown owners. |
| [Both-changed paths](2026-10-25-pr1806-reconciliation/divergence.md) | All 55 divergent paths distinguish later main changes from retained intent. |
| [Retained finding details](2026-10-25-pr1806-reconciliation/retained-items.tsv) | All 101 absent-current item details retain immutable original paths: 8 existing-owner, 31 proposed-current-owner, 62 prospective classifications. The prospective set contains 58 solely held-architecture entries and 4 mixed cases. |

Resolve original paths against the full retained commit above. Owner labels beginning `carried-` record observed source dispositions; `preserve-extraction-and-paused15` preserves separate initiative routing. Other owners identify existing or newly filed backlog rows.

## Carried slices

| PR | Merge | Actual carried scope |
|---|---|---|
| [1823](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1823) | `c3274dcead5abc69d6330022335f1126eb3a18e1` | Bounded native fork admission and pool/input/output ownership; no retained compiler certificate, directory lease or source-custody implementation. |
| [1825](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1825) | `e17fae0da50186b00799e9d8b21ad4861f2cff3b` | Documentation closes delivered source scope and transfers unfinished graph obligations. |
| [1827](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1827) | `58ba42078031fbf5f765594c77006efa6cb6982e` | Documentation isolates physical undo capture; no source implementation inferred. |
| [1828](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/1828) | `7f877e84d76eaead701a5e1bd33d297f21ac1497` | Changed document bytes, rendered project-reference XML, metadata-preserving source rebase and physical undo. |
| Other current source | Current tree | Preserve async source-session shutdown/removal collectors, source restore planning, active-assets/global-property/JSON guards and later explicit reload/refusal fixes. Shared filenames or historical branch receipts do not prove equivalence. |

## New owners

Each current finding is source-supported or explicitly an investigation. Require its own causal runtime proof and independent remediation review before claiming a fix.

| Row | Priority / size | Dependency | Objective |
|---|---|---|---|
| [pr1806-unshipped-authority-proposal](../items/pr1806-unshipped-authority-proposal.md) | Defer / — | — | Decide the retained preview/native/physical authority proposal |
| [build-command-captured-evaluation-context](../items/build-command-captured-evaluation-context.md) | Medium / M | — | Build with the loaded evaluation context |
| [test-trx-definition-qualified-identity](../items/test-trx-definition-qualified-identity.md) | Medium / S | — | Resolve failed TRX methods through test definitions |
| [test-trx-verdict-consistency](../items/test-trx-verdict-consistency.md) | Medium / M | — | Reject inconsistent and unsuccessful test-report proof |
| [mtp-resolved-engine-version-selection](../items/mtp-resolved-engine-version-selection.md) | Medium / M | — | Gate MTP filters on every applicable resolved engine |
| [test-run-global-json-fail-closed-authority](../items/test-run-global-json-fail-closed-authority.md) | Medium / M | — | Distinguish invalid runner configuration from no configuration |
| [composite-batch-final-diff-composition](../items/composite-batch-final-diff-composition.md) | Medium / M | — | Review the complete initial-to-final composite delta |
| [extract-and-wire-same-file-composition](../items/extract-and-wire-same-file-composition.md) | Medium / M | — | Compose DI edits on the extracted snapshot |
| [cross-project-interface-reference-binding](../items/cross-project-interface-reference-binding.md) | High / M | — | Bind generated interface uses to the generated symbol |
| [cross-project-interface-generic-type-parameters](../items/cross-project-interface-generic-type-parameters.md) | Medium / M | — | Preserve generic parameters in extracted interfaces |
| [cross-project-interface-signature-dependencies](../items/cross-project-interface-signature-dependencies.md) | High / M | — | Validate emitted interface signature dependencies |
| [extract-and-wire-semantic-registration-rewrite](../items/extract-and-wire-semantic-registration-rewrite.md) | Medium / M | — | Rewrite bound DI registrations semantically |
| [file-move-namespace-text-encoding](../items/file-move-namespace-text-encoding.md) | High / S | — | Preserve encoding during moved namespace rewrites |
| [refactoring-rebase-source-kind-preservation](../items/refactoring-rebase-source-kind-preservation.md) | High / M | — | Preserve reviewed document source kind during replay |
| [automatic-reload-analyzer-reader-lifetime](../items/automatic-reload-analyzer-reader-lifetime.md) | High / M | — | Exclude readers before automatic reload retires providers |
| [gated-command-executor-disposal-drains-active-runs](../items/gated-command-executor-disposal-drains-active-runs.md) | Medium / M | — | Drain owned command runs before disposing gates |
| [watcher-delivery-loss-observation](../items/watcher-delivery-loss-observation.md) | Medium / M | — | Treat lost watcher delivery as unknown freshness |
| [cache-graph-identity-framing](../items/cache-graph-identity-framing.md) | Medium / S | — | Frame graph cache identity unambiguously |
| [workspace-idle-diagnostic-touch-order](../items/workspace-idle-diagnostic-touch-order.md) | Low / S | — | Measure idle time before access updates |
| [workspace-load-registration-compensation](../items/workspace-load-registration-compensation.md) | High / M | — | Compensate failed watcher registration |
| [workspace-native-load-cleanup-gate-release](../items/workspace-native-load-cleanup-gate-release.md) | High / M | — | Release the load gate after resource cleanup failure |
| [test-concurrency-observation-atomicity](../items/test-concurrency-observation-atomicity.md) | Medium / S | — | Make maximum concurrency observation atomic |
| [test-probe-custom-output-paths](../items/test-probe-custom-output-paths.md) | Medium / S | — | Copy the current referenced process-probe outputs |
| [fixture-restore-process-lifetime](../items/fixture-restore-process-lifetime.md) | Medium / M | — | Bound fixture-owned restore processes |
| [test-fixture-primary-cleanup-preservation](../items/test-fixture-primary-cleanup-preservation.md) | Medium / L | — | Preserve primary failures while attempting all owned cleanup |
| [solution-diff-truncation-source-substring-test](../items/solution-diff-truncation-source-substring-test.md) | Low / S | — | Assert real omitted-file preview behavior |
| [compile-check-diagnostic-suppressor-parity](../items/compile-check-diagnostic-suppressor-parity.md) | Medium / S | — | Investigate suppressor parity with native compiler results |
| [workspace-cache-retention-policy](../items/workspace-cache-retention-policy.md) | Low / M | — | Decide bounded persisted cache retention |
| [analyzer-shadow-sweep-path-authority](../items/analyzer-shadow-sweep-path-authority.md) | High / M | — | Keep analyzer sweeping inside owned no-follow paths |
| [central-package-query-evaluated-policy](../items/central-package-query-evaluated-policy.md) | Medium / M | — | Read central package versions from the active evaluated context |
| [central-package-mutation-evaluated-owner](../items/central-package-mutation-evaluated-owner.md) | Medium / M | central-package-query-evaluated-policy | Bind central-package edits to their effective authored owner |
| [restore-package-root-authoritative-read-fail-closed](../items/restore-package-root-authoritative-read-fail-closed.md) | High / M | — | Keep unreadable assets distinct from an absent recorded root |
| [cross-project-interface-parameter-semantics](../items/cross-project-interface-parameter-semantics.md) | High / M | — | Preserve emitted interface parameter semantics |
| [workspace-cache-coordinator-unexpected-failure-propagation](../items/workspace-cache-coordinator-unexpected-failure-propagation.md) | Medium / S | — | Do not hide cache-coordinator programming failures |
| [mstest-formatter-enum-zero-comparison](../items/mstest-formatter-enum-zero-comparison.md) | Medium / M | — | Investigate enum-zero assertion formatter dependency behavior |
| [process-environment-fixture-isolation](../items/process-environment-fixture-isolation.md) | Medium / M | — | Isolate shared process environment fixture actors |
| [physical-compensation-external-edit-ownership](../items/physical-compensation-external-edit-ownership.md) | High / M | — | Preserve external edits during failed persistence compensation |
| [evaluated-test-project-classification](../items/evaluated-test-project-classification.md) | Medium / M | msbuild-evaluation-project-context-cache | Classify test projects from actual evaluated context |

## Existing and held ownership

| Mechanism | Preserved owner or disposition |
|---|---|
| Bounded reviewed fork graph replay | `restore-fork-reviewed-project-graph`; retain its explicit exclusion of owned BuildHost, compiler certificates and directory leases. |
| Original snapshot/store/truncation admission | `preview-store-explicit-internal-state`; preserve its all-producer hold rather than creating an independently actionable composite duplicate. |
| Failed apply displacing undo | Existing composite/direct-caller undo rows remain separate from native proposal ownership. |
| MSBuild loaded query context | `msbuild-evaluation-project-context-cache`; build-command execution and new classification/query consumers retain explicit dependencies where required. |
| Global-property log values | `workspace-msbuild-global-properties-value-redaction`; wider retained parser/executor redesign remains held. |
| No-op cleanup | `test-noop-class-cleanup-retirement`; all 81 no-op-only patches were independently inspected, not treated as new current fixes. |
| TRX script duplication | `eng-trx-parser-duplicated-across-two-scripts`; preserve actual current helper structure. |
| Fixture cleanup families | Preserve existing fork initialization/cleanup, worker-probe and workspace-cache cleanup owners; new inventories exclude or amend those scopes. |
| Historical retirement self-join allegation | Current synchronous Dispose requests asynchronous stop; do not file the superseded synchronous-join premise as a live bug. |
| Historical nonconstant optional default allegation | Current candidate compiler-diagnostic barrier rejects introduced errors; direct syntax interpolation alone does not prove a missing guard. |
| Proposed runner forwarders | Absent required-interface migrations are prospective; distinct current hanging/blocking scenario runners are not duplicate adapters. |
| XML classification versus graph cost | Current XML-only imports/conditions classification gets an investigation; the retained repeated-complete-graph performance premise is absent. |

The Defer decision `pr1806-unshipped-authority-proposal` preserves explicit preview snapshots, operation-owned mutation/undo/recovery, physical input/directory custody, compiler/analyzer/package authority, native output/condition protocols, required command/test phases, invocation path policy and owned BuildHost intent. Unpark only by live operator decision, resolve the duplicate ADR0029 identity and public next-major/migration hold, then decompose approved implementation without discarding original finding obligations.

## Batch qualification and limits

| Subject | Disposition |
|---|---|
| Product runtime evidence | None generated by this reconciliation. Historical branch tests and scratch BuildHost results do not qualify current main. |
| Requested CI follow-up | `docs-ci-sdk-download-dependency` removes SDK-download dependence while preserving complete documentation tests, release checks and public-fork trust; no workflow edit is part of this batch. |
| SDK hold | Probe the requested runtime origin at ten-minute intervals. HTTP409 holds both unchanged PR heads; recorded HTTP200 permits one required-CI rerun per head. No SDK pin or check bypass. |
| Operator merge order | PR1836 checkpoint, then PR1835 metadata; publish/merge this independent reconciliation, then small metadata/blocker closeout. Root owns the final full `just machine-validate ci` on clean final merged main after all trailing documentation. |
| Final gate | Pending at this audit's source-only checkpoint. Record its exact merged head, original exit status and totals in the final closeout PR description. |
