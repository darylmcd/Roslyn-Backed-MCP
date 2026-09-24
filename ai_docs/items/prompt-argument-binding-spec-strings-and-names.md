# prompt-argument-binding-spec-strings-and-names — Accept spec-compliant string prompt arguments and name the offending argument

**row:** `prompt-argument-binding-spec-strings-and-names` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Middleware/PromptBindingStageAdapter.cs:81`

## Acceptance

- [ ] prompts/get consumer_impact {line:"19",column:"21"} renders (MCP types prompt argument values as strings)
- [ ] All 5 int-arg prompts (consumer_impact, explain_error, guided_extract_method, refactor_and_validate, suggest_refactoring) render with string args
- [ ] Missing/invalid-arg errors name the parameter (e.g. "Missing required argument 'filePath' for prompt 'review_file'")

## Evidence

- prompts/get consumer_impact {line:"19",column:"21"} → -32602 Invalid parameters; same with JSON numbers → OK. prompts/get review_file {} → message never names filePath (PromptBindingStageAdapter.cs:73/85/90). — see `ai_docs/audits/20260924-1305/report.md` (check C3) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- JsonSerializer.Deserialize(value.GetRawText(), parameter.ParameterType) is strict; spec clients send strings. InvalidParameters(promptName) discards parameter.Name.
