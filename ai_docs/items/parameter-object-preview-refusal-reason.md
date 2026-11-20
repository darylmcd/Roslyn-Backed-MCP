# parameter-object-preview-refusal-reason — Deliver the parameter_object_preview refusal reason

**row:** `parameter-object-preview-refusal-reason` · **pri:** `Medium` · **size:** `M` · **verified:** `d3ab02228685f3cc015691f32c0292c6e6526b43`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectRefusals.cs`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.Feasibility.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs`

## Acceptance

- [ ] Reproduce on `RoslynMcp.Roslyn.Helpers.SourceTextSlicer.SliceLines` [startLine, endLine] and find out whether the refusal is correct.
- [ ] A correct refusal reaches the caller with its authored reason (the description promises "Refuses with a reason"); an incorrect one is fixed.
- [ ] Check release impact of publishing refusal text against docs/release-policy.md before changing the wire message.

## Evidence

- P6-F12: 2/2 targets returned "The operation is not valid in the current state."
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).
