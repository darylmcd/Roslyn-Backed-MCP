# preview-valid-member-replacement-invalidoperation — Restore valid member replacement previews

**row:** `preview-valid-member-replacement-invalidoperation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs` — preview edit construction and syntax validation.
- `src/RoslynMcp.Host.Stdio/Tools/MultiFileEditTools.cs` — preview_multi_file_edit refusal publication.
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs` — real valid-source replacement regression.

## Acceptance

- [ ] Reproduce the valid existing-member replacement batch below with a self-contained C# fixture; establish the actual throwing mechanism before changing guards.
- [ ] Return a complete diff/token and apply the intended valid edit through the documented route; preserve source on refusal, coordinate/syntax validation, exact workspace identity and stale-token rejection.
- [ ] Observe the runtime regression red before repair and green afterward; cover the reduced batch and whole-document equivalent without weakening malformed-source refusals.
- [ ] Compare the demonstrated mechanism with preview-existing-document-insertion-refused; consolidate only if the same mechanism is established.

## Evidence

| Fact | Current observation |
|---|---|
| Installed server | 4.3.0+85b6e3dc559ac05c0bceb8fcbaf390c99a5f31a6; .NET10.0.13; ready workspace7projects, zero load/compile diagnostics. |
| Valid replacement | Existing nested fake's `public string? ChangedField;` becomes `public string? ChangedField, StopChangedField;` plus a nullable statistics field; its valid body chooses the changed field by operation, constructs the same statistics record and retains it. |
| Reduced request | One file, two nonoverlapping line ranges20:1..21:1 and33:1..36:1; normal syntax checking enabled. Whole-document equivalent also refused. |
| Refusal | isError=true; category InvalidOperation; exceptionType InvalidOperationException; message: The operation is not valid in the current state. Check the tool contract and retry. |
| Reproduction limit | Cited raw envelope records an automatic stale reload. Reduced and whole-document refusals are recorded in the same note; independent no-reload reproduction remains unverified. |
| Counterexample | Insertion-only additional test preview and production-file preview/apply succeed through the same installed tool. Existing source compiles. |
| Local raw evidence | <user-profile>/.codex/scratch/pathscope-remediate-20261023-2031/roslyn-native-test-preview-error.md; consumer source remains local. |

## Context

- Observed tool defect filing only; no implementation takeover or diagnostic/guard bypass.
- Keep consumer source and machine paths out of regression fixtures; reconstruct a minimal standalone sample.
- Installed tool may differ from current repository main; verify both before deciding the fix or release boundary.
- Preserve public contract-care; any breaking refusal-category change needs the normal ADR/migration process.
- backlog: sync ai_docs/backlog.md.
