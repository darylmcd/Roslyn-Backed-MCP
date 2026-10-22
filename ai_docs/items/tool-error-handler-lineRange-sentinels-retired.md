# tool-error-handler-lineRange-sentinels-retired — Retire ToolErrorHandler message-sniffing sentinels for lineRange and startLine must be <=

**row:** `tool-error-handler-lineRange-sentinels-retired` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:608-620`
- `src/RoslynMcp.Host.Stdio/Tools/SourceTextRequestProjection.cs:43`

## Acceptance

- [ ] Throwers that rely on the sentinels publish PublicArgumentException with the real wire parameter name; the sentinel branches are deleted and no behavior changes on the wire.

## Evidence

- Legacy heuristics coupled to message text in other services; still live for SourceTextRequestProjection after the format-range conversion.
