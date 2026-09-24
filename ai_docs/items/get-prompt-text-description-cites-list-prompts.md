# get-prompt-text-description-cites-list-prompts — Fix get_prompt_text's description to reference prompts/list

**row:** `get-prompt-text-description-cites-list-prompts` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs:57`

## Acceptance

- [ ] Descriptions at :47 and :57 name prompts/list or roslyn://server/catalog/prompts/{offset}/{limit}

## Evidence

- promptName description: 'Use list_prompts on the resources channel…'. — see `ai_docs/audits/20260924-1305/report.md` (check C3) and `ai_docs/audits/20260924-1305/findings.json`
