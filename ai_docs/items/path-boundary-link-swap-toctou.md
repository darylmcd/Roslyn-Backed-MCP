# path-boundary-link-swap-toctou — Close validation-to-use link-swap race

**row:** `path-boundary-link-swap-toctou` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`
- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs`
- `src/RoslynMcp.Host.Stdio/Tools/EditTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`

## Acceptance

- [ ] Design the boundary API so downstream filesystem consumers use the validated canonical target or perform a final revalidation immediately before mutation.
- [ ] Apply the primitive first to the highest-risk direct file/project write tools without weakening configured-root or sibling-worktree behavior.
- [ ] Keep the server-owned boundary authoritative after validation and during use.
- [ ] Add one deterministic link/junction swap regression proving a path cannot be redirected outside the configured boundary between validation and write.

## Evidence

- Validation currently canonicalizes only for comparison and returns no canonical target; downstream tools retain the original mutable link path.
## Amendment — 2026-08-13 (backlog-sweep 20260813T172325Z; PR #1230 HELD, NOT merged — row stays OPEN)

An attempted fix reached PR #1230 and was **held at the Step 8b fix-cycle cap**. Do not merge that PR as-is; it needs re-planning as a fresh initiative.

**What the attempt established (worth keeping):** `ClientRootPathValidator.ValidatePathAgainstRootsAsync` can return the boundary-canonical target it already computes, and `EditTools.ApplyTextEdit` can forward it to `IEditService.ApplyTextEditsAsync` as an optional `canonicalWritePath`. That wiring was proven correct by an empirically-verified inverting test (deleting the argument makes the test fail with `actual: null`; restoring it passes).

**Why it did not close this row — two independent blockers:**

1. **Acceptance items 3-4 are unreachable from this seam.** `EditService.ApplyTextEditsCoreAsync` calls `_workspace.TryApplyChanges` BEFORE `PersistDocumentTextToDiskAsync`, and MSBuildWorkspace flushes changed documents to disk itself through the un-canonicalized `Document.FilePath` (in-repo comment in `WorkspaceManager.cs` confirms it). So the swapped target still receives an earlier Roslyn-level write and the boundary-escape property is not achieved. Tracked as `workspace-load-path-canonicalization`.

2. **The attempted fix introduced a NEW data-loss defect** (high-severity, confirmed against source). The write target is resolved physically by `ConfiguredRootBoundary.ResolvePathCore`, which deliberately does NOT lexically collapse `..`; the document lookup is resolved lexically by `SymbolResolver.FindDocument` via `Path.GetFullPath`. For a request path shaped `<root>/real/sub/link/../Program.cs` the two resolve to DIFFERENT physical files, so document A's edited text is written into file B — while the undo snapshot and change-tracker entry both record A, leaving B unrecoverable through the tool's own undo.

**Shape for the re-plan:** honor `canonicalWritePath` only when `ClientRootPathValidator.ResolvePath(Path.GetFullPath(filePath))` equals it, else fail closed (consistent with the repo's existing fail-closed stance on ambiguous paths), plus a regression for the link + `..` divergence. Note that `EditService.cs` was decomposed by PR #1241 after PR #1230 was cut, so that branch will not rebase cleanly.

**Sibling exposures split out of this row:** `suppression-tools-missing-root-boundary-validation` (High — those tools validate nothing at all), `preview-apply-token-write-path-toctou`, `undo-revert-uncanonicalized-restore-path`.
## Amendment — 2026-08-14 (PR #1230 remediated; row STILL OPEN)

The held PR was rebased onto v3.0.0 and its blocking defect fixed. It is no longer a hazard — but it does **not** close this row.

**Fixed in #1230:** the physical-vs-lexical divergence. `EditTools.EnsurePinnedTargetMatchesResolvedDocument` refuses a request whose lexical and physical resolution disagree (a `..` following a link), rather than writing the edited document's text into a different file with the undo snapshot pointing at the original. Fail-closed, using `ConfiguredRootBoundary.PathsEqual` so the check cannot be more permissive than the boundary it defends. Inversion verified empirically: neutering the guard fails the regression.

**Why acceptance items 3-4 are still unmet:** unchanged from the previous amendment — `MSBuildWorkspace.TryApplyChanges` flushes changed documents to disk through the un-canonicalized `Document.FilePath` *before* the pinned write runs. Tracked by `workspace-load-path-canonicalization`.

**Carried forward from the cold review of the fix (no HIGH findings; verdict was merge):**

1. **The invariant is enforced in the wrong layer (medium).** The guard lives in `EditTools`, but the dangerous capability — write to an arbitrary path, bypassing `document.FilePath` — is a defaulted parameter on the **public** `IEditService.ApplyTextEditsAsync`, whose doc never states the precondition "the pin MUST denote the same physical file the document resolves to". The two tracked follow-ups (`preview-apply-token-write-path-toctou`, `suppression-tools-missing-root-boundary-validation`) will add pin-passing callers; one that forgets the host-layer guard reintroduces the wrong-file write. **Correct home is `EditService.PersistDocumentTextToDiskAsync`**, which holds the actual document and can compare `ResolvePath(document.FilePath)` directly instead of via a proxy. Blocked on the layering constraint: `RoslynMcp.Roslyn` references only `RoslynMcp.Core`, so `ConfiguredRootBoundary` is unreachable there — solving this and `preview-apply-token-write-path-toctou` likely means promoting the boundary primitive to a shared layer, once.

2. **The guard rests on an undocumented Roslyn behavior (low, now commented).** It compares the physical resolution of the lexically-normalized REQUEST path as a stand-in for the document's physical path. Those coincide only because Roslyn's MSBuild loader normalizes item paths through `Path.GetFullPath` before they become `Document.FilePath`. Nothing tests that assumption; if it stops holding the proxy silently weakens.

3. **Fail-open branch is now stricter than it was (low, untested).** `ValidatePath`'s zero-roots branch now calls `ResolvePath(path)`, which throws `ArgumentException` on drive-relative input and `IOException` on link cycles / ambiguous partially-qualified link targets. The explicit compatibility escape hatch therefore rejects some inputs it previously accepted. Only the happy path is covered.

4. **Test placement (low).** `EnsurePinnedTarget_*` exercise `EditTools` but live in `ClientRootPathValidatorTests.cs`.
