---
category: Changed
---

- **Changed:** `get_code_actions` / `apply_code_action` and `fix_all` previews now compute their solution diffs through the shared `SolutionDiffHelper.ComputeChangesAsync` instead of per-service copies. `CodeActionService` and `FixAllService` each carried a byte-identical 27-LOC `ComputeChangesAsync` (changed-documents only) that duplicated — and lagged — the canonical helper next to `DiffGenerator`. Consolidating onto `SolutionDiffHelper` removes the duplication and brings both preview paths up to the helper's behavior: added/removed documents now appear in previews, the 64 KB total-diff truncation cap (FLAG-6A) applies, and no-op (empty) diffs are skipped (gh #739). The existing code-action + fix-all preview tests stay green. Closes `solution-diff-projection-dedup`.
