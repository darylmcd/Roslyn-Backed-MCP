# server-surface-catalog-private-field-naming — Align private catalog fields with repo style

**row:** `server-surface-catalog-private-field-naming` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`

## Acceptance

- [ ] Rename every private static field and constant in the catalog partials to the repository's underscore-prefixed convention.
- [ ] Update all internal references without changing serialized catalog content.
- [ ] Run the touched-file style formatter at warning severity with zero catalog violations.

## Evidence

- Existing private members such as `V231ReleaseVersion`, `V231SourceCommit`, `PriorCatalogSnapshotResourceName`, and lazy catalog fields violate IDE1006 and make touched-file style validation fail.
