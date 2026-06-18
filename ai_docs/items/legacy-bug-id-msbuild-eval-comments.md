# legacy-bug-id-msbuild-eval-comments — Strip remaining BUG-008 ids from MSBuild eval service

**row:** `legacy-bug-id-msbuild-eval-comments` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/MsBuildEvaluationService.cs:84` (implementation comment)
- `src/RoslynMcp.Core/Services/IMsBuildEvaluationService.cs:23` (XML-doc param remark)

## Acceptance

- [ ] No `BUG-007`/`BUG-008` literal remains anywhere under `src/` (grep clean).
- [ ] The surrounding filter-rationale text (filter-to-avoid-large-output) is preserved and still reads correctly.

## Evidence

- Code-quality review of PR #966 (`legacy-bug-id-tool-descriptions`) found these two internal bug-tracker ids leaking in source comments outside the tool-surface Descriptions that PR cleaned — same leak family, different surface. — 2026-06-18 backlog-sweep execute.

## Context

PR #966 removed `(BUG-007)`/`(BUG-008)` from the `test_discover` and `get_msbuild_properties` `[Description]` strings (the consumer-facing MCP surface). The two anchors above are internal comments/XML-doc, out of that row's tool-surface-only scope, so they were intentionally left untouched and spun off here.
