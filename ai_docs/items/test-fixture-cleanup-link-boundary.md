# test-fixture-cleanup-link-boundary — Keep fixture cleanup inside its owned tree

**row:** `test-fixture-cleanup-link-boundary` · **pri:** `High` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs:31-55`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs:112-122`
- `tests/RoslynMcp.Tests/TestFixtureFileSystemTests.cs:66-102`

## Acceptance

- [ ] Fixture cleanup never clears attributes or deletes targets outside its owned tree through directory junctions, directory/file symlinks or cyclic links; remove owned links without traversing their targets.
- [ ] Regular owned read-only files remain cleanable. Preserve bounded retry and actionable terminal failure for genuinely locked owned files.
- [ ] Red-first helper tests preserve simulated external read-only attributes, contents and directories across cleanup; include native Windows junctions and supported symlinks on Linux, with explicit feature-unavailability evidence.
- [ ] A cyclic link cannot cause unbounded enumeration or depend on every caller manually unlinking it first. Re-derive all same-mechanism cleanup traversal sites and callers; retain live-owner protections independently.

## Evidence

- Immutable0442d5fa0d4be457f0f9a5f0de50996ada720750: DeleteDirectoryIfExists49 unconditionally calls `ClearReadOnlyAttributes(path);` before recursive deletion. Helper114 uses `Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)`; helper119 executes `File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);` without a link/ownership boundary.
- Current-session controlled Windows probe creates inside/linked as a junction to a separately owned simulated outside directory. The exact enumeration/attribute primitives from the helper enumerate inside/linked/owned-marker.txt and change outside/owned-marker.txt from ReadOnly to Normal. No product source edited or arbitrary external/user files touched.
- Native probe exit0; marker attributes restored and all owned probe directories removed using non-recursive explicit operations. Retained evidence: <session-scratch>/fixture-link-boundary-proof.json. This reproduces the traversal primitive; no compiled helper suite was run for this filing.
- Separate regression mechanism from test-temp-reaper-live-owner (abandonment identity) and test-try-delete-directory-copies (consolidating swallowing copies). Alias fixtures link only to owned targets and explicitly unlink their cycles before common cleanup; this boundary defect predates that source change.

## Context

- Prefer explicit no-follow traversal over resolving a link and later trusting the target; assess link-swap and unavailable identity behavior without weakening failures or deleting peer data.
- Reuse the existing fixture test class and native junction helper. Keep this row outside the current plan selection; this filing does not claim a fix.
