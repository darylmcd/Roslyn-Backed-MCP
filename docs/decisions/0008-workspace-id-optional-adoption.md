# ADR 0008 — Optional workspaceId adoption gate

- **Status:** Accepted (2026-09-04)
- **Deciders:** repository maintainers
- **Scope:** expansion of optional `workspaceId` beyond the read-only pilot
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

PR #959 made `workspaceId` optional on `go_to_definition`, `find_references`, and
`document_symbols`. The request middleware reports whether a call was explicit, resolved from the
only loaded workspace, or rejected because omission was ambiguous. Surface-schema tests prove the
capability exists; they do not prove clients use it safely.

A read-only census examined real Claude Code plus live and archived Codex transcripts after PR #959
merged. It correlated actual Roslyn MCP calls with their results, excluded this repository's own
sessions, and counted the three pilot tools separately from the later `compile_check` addition.
Aggregate evidence only is retained here; raw transcripts can contain source and local paths and are
not repository artifacts.

| Census boundary | Value |
|---|---:|
| Start, inclusive | 2026-06-09 15:50:23 UTC |
| End, inclusive | 2026-09-04 13:30:00 UTC |
| Transcript files scanned | 1,238 |
| Parsed JSONL records | 667,658 |
| Parse failures | 0 |
| Discovered candidate calls | 3,193 |
| Correlated call/result pairs | 1,937 |
| Excluded host/self-test calls | 412 |
| Eligible pilot calls | 1,137 |
| Eligible pilot sessions | 77 |
| Repository buckets | 6, including one unattributed Codex bucket |

| Pilot tool | Claude | Codex archive | Codex live | Total | Organic omissions |
|---|---:|---:|---:|---:|---:|
| `document_symbols` | 17 | 27 | 14 | 58 | 2 |
| `find_references` | 966 | 73 | 40 | 1,079 | 35 |
| `go_to_definition` | 0 | 0 | 0 | 0 | 0 |
| **Total** | **983** | **100** | **54** | **1,137** | **37** |

| Omitted-call outcome | Count | Interpretation |
|---|---:|---|
| `single-workspace` | 35 | Middleware resolved the only loaded workspace. |
| `fast-fail` | 2 | Two loaded workspaces made omission ambiguous; the client retried explicitly. |

The two fast-fails were organic `find_references` calls in a consumer repository, not manual probes
or this repository's tests. A bounded source check confirmed an omitted successful call carried
`_meta.autoResolution=single-workspace`; a bounded failure check confirmed the ambiguous calls
carried `_meta.autoResolution=fast-fail` and were followed by successful explicit retries.

The pilot repository distribution reconciles to the eligible total: BioRemote 276,
DotNet-Firewall-Analyzer 345, DotNet-Network-Documentation 2, SnipCue 182, TradeWise 178, and 154
Codex calls without reliable repository attribution. The later `compile_check` cohort contained 388
eligible calls and is not used to decide the original three-tool pilot.

## Decision

1. Record **NO-GO** for expanding optional `workspaceId` across the remaining read-only surface.
2. Keep the existing pilot and `compile_check` behavior. Their fail-closed ambiguity handling is
   correct and the census does not justify a breaking rollback.
3. Retire `workspace-id-flip-batch-01` through `workspace-id-flip-batch-11`; closing this evidence
   gate must not make those implementation batches appear actionable.
4. Reconsider expansion only after a new trailing-30-day census observes at least 100 organic
   omitted pilot calls across at least three known consumer repositories and both Claude and Codex
   harness families, with zero multi-workspace fast-fails. A separately approved unambiguous
   multi-workspace selection contract may also trigger reconsideration.
5. Continue describing optionality as a schema capability. Do not claim that it causes clients to
   omit `workspaceId`; current bootstrap guidance often tells clients to retain the identifier.

## Consequences

- The current pilot remains a bounded convenience for single-workspace sessions.
- Clients retain deterministic behavior by supplying `workspaceId`, especially when multiple
  workspaces may coexist.
- The eleven broad flip batches are removed rather than left deceptively dependency-ready.
- A future decision starts from this denominator, extraction method, failure signal, and explicit
  recheck threshold instead of re-litigating the gate from anecdotes.
- Codex repository attribution remains incomplete, so repository-level conclusions use only known
  Claude Code paths. Transcript steering also makes the observed omission rate descriptive rather
  than a causal estimate of schema behavior.

## Validation

- Run the census extractor twice against the fixed time boundary and require identical aggregate
  output and SHA-256 digest.
- Manually inspect at least one real call/result pair for each nonzero resolution bucket.
- Reconcile totals across tool, harness, repository, session, and exclusion dimensions.
- Run `WorkspaceIdOptionalSurfaceTests`.
- Run `eng/verify-ai-docs.ps1` and the complete release gate.

## References

- [Product contract](../product-contract.md)
- [MCP SDK wire compatibility](0003-sdk-2x-wire-compatibility.md)
