---
category: Maintenance
---

- **Maintenance:** added the `PreviewKind` members for the multi-file-edit, file-create, file-delete, file-move, code-action and fix-all producer families, registered all six in the centralized preview-kind to apply-route map (including the two gate-forced `ServerSurfaceCatalog.PreviewApplyRoutes` companions), and declared the kind-carrying `IPreviewStore.Store` overload for the non-`changes` call shape. No runtime behavior change on its own — this is the substrate the remaining apply-route bindings build on.
