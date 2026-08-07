---
category: Maintenance
---

- **Maintenance:** Consolidated the pre-apply file-snapshot exists/missing-fallback decision (previously inlined independently in `EditService`, `EditorConfigService`, and `ProjectMutationService`) into a single shared `FileSnapshotCapture` helper. No behavior change; reduces drift risk across the undo/revert byte-fidelity paths.
