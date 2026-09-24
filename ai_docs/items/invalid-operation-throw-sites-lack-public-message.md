# invalid-operation-throw-sites-lack-public-message — Carry public messages for expected InvalidOperation refusals

**row:** `invalid-operation-throw-sites-lack-public-message` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:152`
- `src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs:674`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:94`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs:1197`

## Acceptance

- [ ] set_project_property_preview(OutputPath) returns 'Property OutputPath is not supported. Allowed properties: …'
- [ ] move_type_to_project_preview cycle refusal names the cycle
- [ ] format_range_preview refusal and change_signature reorder errors state the reason
- [ ] get_prompt_text unknown prompt lists available prompts (PromptShimTools.cs:67)
- [ ] build_project/test_run with an unknown projectName name the loaded projects and never advise workspace_reload

## Evidence

- 12 refusal paths across Phases 6/10/12/13 returned "The operation is not valid in the current state. Check the tool contract and retry." (set_project_property_preview, move_type_to_project_preview, create_file_preview existing file, scaffold_first_test_file_preview, format_range_preview, change_signature reorder, get_prompt_text unknown prompt, catalog-diff pair). Also misleading advice: build_project/test_run(projectName=NoSuchProject) and rename_preview(fabricated symbolHandle) → "The operation is not valid for the current workspace state. Call workspace_reload if the state is stale, then retry." (real cause: project/symbol not found, WorkspaceProjectResolver.cs:21). — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- Sibling of argument-exception-throw-sites-lack-public-message; same redaction contract. Consider one shared IPublicMessageException marker covering both.
