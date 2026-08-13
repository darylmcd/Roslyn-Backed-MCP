---
category: Fixed
---

- **Fixed:** narrowed a validation-to-use link-swap (TOCTOU) race in `apply_text_edit` — `ClientRootPathValidator.ValidatePathAgainstRootsAsync` now returns the boundary-canonicalized target it approved, and the edit-service write path persists to that pinned location instead of re-walking the client-supplied path (which could be redirected outside the configured sanctioned-root boundary by a symlink/junction swap between validation and write). Added a deterministic link-swap regression test. Two related exposures remain tracked follow-ups: the preview/apply-token write tools (`apply_multi_file_edit` plus the `ToolDispatch.ApplyByTokenAsync`-routed tools), and `MSBuildWorkspace.TryApplyChanges`, which flushes changed documents to disk itself using the un-canonicalized `Document.FilePath`.
