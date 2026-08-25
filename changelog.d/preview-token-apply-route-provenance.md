---
category: Added
---

- **Added:** Producer/workflow discriminator (`PreviewKind`) on the shared preview-token store. Each preview token can now record which producer created it, surfaced non-consumingly via `IPreviewStore.PeekKind`, so apply routes can verify token provenance before mutating the workspace. The discriminator is optional-with-default (`PreviewKind.Unspecified` = permissive), so existing preview producers and out-of-tree `IPreviewStore` implementations are unaffected; `RefactoringService`'s rename / format-document / format-range / organize-usings / code-fix previews are tagged as the first real producers. Route enforcement follows in a companion change.
