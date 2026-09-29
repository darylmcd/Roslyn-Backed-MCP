| Field | Content |
|---|---|
| Route | deepen |
| Diagnosis | `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:202-206` captures `DataFlowsOut` with `GetSymbolType`, which returns the local's declared `Type` at lines 480-485. `BuildMethodAndCallSite` renders that annotation at line 257 although its synthesized `return` at lines 278-283 may have a narrower nullable flow state; the cited `string?`/CS8603 failure follows. |
| Approach | In `ExtractMethodService.PreviewExtractMethodAsync` / `BuildMethodAndCallSite`, infer the return annotation from the synthesized return identifier's semantic `GetTypeInfo(...).Nullability.FlowState` in a candidate document containing the moved statements, then render the final return type; retain the declared symbol type when flow state is unavailable and leave parameter types unchanged. Use the existing semantic-model/document pattern in `ResolveDocumentAsync` (lines 92-111). Preserve nullable returns when the returned value can be null. |
| Scope | 1 production file: `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`; 1 test file: `tests/RoslynMcp.Tests/ExtractMethodTests.cs`; 1 changelog fragment: `changelog.d/extract-method-nullable-return-flow-state.md`. No deletions. |
| Tool policy | edit-only |
| Estimated context cost | 35000 |
| Risks | The generated method must use its own flow state after copied branches and parameters, including nullable-output and disabled-nullable cases. No refactor fanout: local return-type inference only; `fanoutEstimate: null`. |
| Validation | Add an isolated nullable-enabled source fixture in `ExtractMethodTests` that reproduces the `string?` declaration with a non-null returned expression; assert preview emits `string`, apply succeeds, and compile diagnostics exclude CS8603. Add a maybe-null control. Run focused ExtractMethod tests, `dotnet build RoslynMcp.slnx -c Release -p:TreatWarningsAsErrors=true`, and `just ci` per `CI_POLICY.md` / addenda. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | Extracted methods now use the returned value's nullable flow state for their return type. |
| Backlog sync | Close rows: [extract-method-nullable-return-flow-state]. Mark obsolete: []. |
