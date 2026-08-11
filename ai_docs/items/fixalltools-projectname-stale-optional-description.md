# fixalltools-projectname-stale-optional-description — fix_all's projectName MCP description is stale after the blank-projectName guard

**row:** `fixalltools-projectname-stale-optional-description` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/FixAllTools.cs:29`

## Acceptance

- [ ] `projectName`'s `[Description(...)]` on `FixAllTools.cs:29` changes from `"Optional: project name when scope is 'project'"` to something matching the now-required semantics (e.g. `"Required when scope is 'project': the project name"`), mirroring the `filePath` parameter's existing "Required when scope is 'document'" wording on line 28.

## Evidence

Traced during spec-compliance review of `fixall-blank-projectname-silent-wrong-target`: that row added a guard in `FixAllService.ResolveTargets` that throws `ArgumentException` when `scope == "project"` and `projectName` is null/whitespace, making the parameter effectively required for that scope. `FixAllTools.cs:29`'s MCP-facing `[Description]` still reads "Optional", which is now factually wrong — an MCP client reading it will omit the parameter and immediately hit the new `ArgumentException`.

## Context

Spin-off from the `fixall-blank-projectname-silent-wrong-target` row's spec-compliance review (top-n-remediation run 20260810T233007Z).
