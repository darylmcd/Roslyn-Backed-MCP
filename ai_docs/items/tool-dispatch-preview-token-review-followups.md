## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:120` — the `IPreviewStore` overload's inlined peek-or-throw
- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:87` — XML doc naming an unfiled row
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:185` — `PeekChangedPaths` project enumeration

## Acceptance

- The peek + `PreviewTokenStaleException` throw + interpolated message exists once, in a private static helper called by both `ApplyByTokenAsync` overloads.
- The `ToolDispatch.cs:87` XML doc either names this row id or is reworded so the claim is verifiable.
- `PeekChangedPaths` enumerates `GetRemovedProjects()` documents symmetrically with `GetAddedProjects()`, or carries a comment explaining why project removal cannot reach the store's persistence path.

## Evidence

All three surfaced in the Step 8b code-quality review of PR #1294 (`preview-apply-token-write-path-toctou`) and were left unfixed there deliberately: they are medium/low severity, which is advisory under the Step 8b gate, and the fix cycle was spent on the HIGH finding (revalidation re-deriving a narrower boundary than load time).

The duplication at `:120` was INTRODUCED by that diff — the overload previously delegated (`=> ApplyByTokenAsync(gate, previewStore.PeekWorkspaceId, ...)`) and now inlines a byte-identical copy.

The documents-removed-with-a-project gap at `PreviewStore.cs:185` means such documents are silently omitted from the revalidated write set rather than surfaced — worth confirming it cannot become a revalidation bypass.

Source: code-quality review of PR #1294, sweep 20260819T180531Z.

## Amendment — redemption drops the client-roots narrowing dimension (PR #1294 re-review)

`ToolDispatch.RevalidateChangedPathsAsync` (`ToolDispatch.cs:181`) calls `ValidatePathAgainstRootsAsync` with `server: null`, so `LegacyClientRootsNarrowingAdapter.TryGetNarrowingRootsAsync` (`LegacyClientRootsNarrowingAdapter.cs:21`) short-circuits to `null`. The redemption-time check therefore omits the client-roots NARROWING dimension that the preview-time check applied.

For a client advertising MCP Roots, a swap that redirects within the configured roots but outside the client roots survives redemption. This is **not worse than base** (base performed no redemption check at all), which is why PR #1294 landed with it — but it means the TOCTOU fix is partial for that client class, and the `RevalidateChangedPathsAsync` XML doc currently claims parity with the admitting boundary without disclosing the omission.

### Additional acceptance

- Redemption-time revalidation either applies the same client-roots narrowing boundary the preview-time check applied (thread the `McpServer` through), OR names the omission explicitly as a residual gap in the `RevalidateChangedPathsAsync` XML doc.

### Additional anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs:181`
- `src/RoslynMcp.Host.Stdio/Security/LegacyClientRootsNarrowingAdapter.cs:21`

Amended here rather than filed as a sibling because this row already plans to edit the same helper.

Source: code-quality re-review of PR #1294, sweep 20260819T180531Z.
