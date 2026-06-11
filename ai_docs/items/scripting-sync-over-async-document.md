# scripting-sync-over-async-document — mark the deliberate GetAwaiter().GetResult() fence

**row:** `scripting-sync-over-async-document` · **pri:** `Low` · **size:** `M` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- `src/RoslynMcp.Roslyn/Services/ScriptingService.cs:152`
- `src/RoslynMcp.Roslyn/Services/ScriptExecutionSupervisor.cs`

## Acceptance

- [ ] Comment added at the call site explaining the deliberate fence (dedicated worker → deadlock impossible); optionally a pragma/attribute marking it an approved exception
- [ ] Regression (doc-only, no behavior change): a grep/architecture assertion that the sole `GetAwaiter().GetResult()` in scripting carries the approved-exception marker

## Evidence

- 2026-06-04 discovery-sweep refactor audit (FLAG-5C), Standing Directive #3.

## Context

`ScriptingService.ExecuteScript` blocks on `GetAwaiter().GetResult()` — intentional and safe (it runs on a dedicated non-threadpool worker thread fenced by `ScriptExecutionSupervisor`, because `CSharpScript` does not honor the `CancellationToken`), but the call site has no explanatory comment, so the pattern reads as a sync-over-async smell and risks being cargo-culted onto threadpool paths where it would deadlock.
