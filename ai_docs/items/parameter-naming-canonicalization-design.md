# Parameter-naming canonicalization — design note
<!-- purpose: Bounded design for canonical MCP parameter naming across the experimental surface. Defines convention, enumerates affected tools/prompts, and specifies migration contract for the follow-on breaking-rename initiative. -->

**Initiative:** `parameter-naming-canonicalization-experimental-surface`
**Date:** 2026-05-12
**Author:** backlog-sweep executor subagent
**Scope:** design-only; no `src/` mutations.
**Outcome:** see [Verdict](#verdict) — **go**, one follow-on breaking-rename initiative `parameter-naming-canonicalization-migration`.

---

## (a) Use case

The 2026-05-02 experimental-promotion audit flagged two inconsistency families across the experimental MCP surface:

1. **Locator tools mix `character` vs `column`.** Some tools accept a 0-based `character` (LSP convention) at the locator layer, while the server's standard is 1-based `column` (matching `workspace_load`'s `path` precedent and every stable tool). The retro §3 P6 finding labeled this the most impactful discrepancy because agents switching between stable and experimental tools silently pass the wrong value with no diagnostic.

2. **NuGet mutation tools use `packageId`** consistently internally but the 2026-05-02 audit noted potential `packageName` alias conflict from sister-server naming pressure.

3. **One `validate_recent_git_changes` error path** emits a bare "An error occurred" string with no parameter hint when the outer try/catch swallows the structured envelope (this is already mitigated by the in-handler belt-and-suspenders try/catch added in that tool's implementation, but it warrants a note in the migration contract).

No canonical convention document exists. The follow-on migration row `parameter-naming-canonicalization-migration` must not proceed without this design landing first.

---

## (b) Canonical naming convention

**Decision (binding):** all `[McpServerTool]` parameters on the experimental surface MUST use **lowerCamelCase**, with no LSP-style aliases and no `character` synonym for column positions.

**Rationale (one paragraph).** The stable surface established `path`, `workspaceId`, `filePath`, `line`, `column` as the server's vocabulary (`workspace_load`, `go_to_definition`, `symbol_info`, etc.). Every agent interacting with a mix of stable and experimental tools uses those names. Introducing `character` (0-based, LSP) alongside `column` (1-based, server) for positionally-located tools creates a silent semantic trap: an agent that learned `column` from a stable tool and passes it to an experimental tool that interprets it as `character` will be off by one without any error. The fix is to: (1) mandate `column` everywhere as the single locator vocabulary; (2) mandate 1-based semantics uniformly; (3) reject any alias or overload whose sole purpose is matching LSP naming. NuGet tools use `packageId` — that name is already idiomatic in the .NET ecosystem (it matches the `PackageReference Include=` attribute name) and requires no change; the `packageName` alias pressure is informational only.

**Summary table of decisions:**

| Convention | Canonical form | Rejected alternatives |
|---|---|---|
| Column position | `column` (1-based, int) | `character` (LSP 0-based), `col`, `columnNumber` |
| Line position | `line` (1-based, int) | `lineNumber`, `row`, `lineIndex` |
| File path | `filePath` (absolute string) | `file`, `path`, `fileName`, `sourceFile` |
| Workspace reference | `workspaceId` (string) | `workspace`, `sessionId`, `wsId` |
| Project scoping | `projectName` (string) | `project`, `projectFilter` (see §(c)) |
| NuGet package identity | `packageId` (string) | `packageName`, `package` |
| Symbol locator group | `filePath?`, `line?`, `column?`, `symbolHandle?`, `metadataName?` | no aliases; every locator tool must expose this exact five-field optional group |

---

## (c) Per-tool migration table — experimental tools

The following table covers every tool in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.*.cs` whose `supportTier` is `"experimental"`. Re-enumerated live against the catalog partials at design time. Tools that already comply are marked `compliant`. A rename is `breaking` if it changes the JSON key the MCP client sends (all parameter renames are breaking by definition for typed MCP callers).

### Analysis / Advanced-analysis experimental tools

| Tool | Param | Current name | Canonical name | Breaking? | Notes |
|---|---|---|---|---|---|
| `trace_exception_flow` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `trace_exception_flow` | exception type | `exceptionTypeMetadataName` | `exceptionTypeMetadataName` | compliant | Long but unambiguous; matches `metadataName` pattern |
| `trace_exception_flow` | project scope | `scopeProjectFilter` | `projectName` | YES | `scopeProjectFilter` diverges from canonical `projectName` |
| `trace_exception_flow` | cap | `maxResults` | `maxResults` | compliant | |
| `find_duplicate_helpers` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `find_duplicate_helpers` | project scope | `projectFilter` | `projectName` | YES | `projectFilter` diverges from canonical `projectName` |
| `find_duplicate_helpers` | cap | `limit` | `limit` | compliant | |
| `find_duplicate_helpers` | framework wrap | `excludeFrameworkWrappers` | `excludeFrameworkWrappers` | compliant | |
| `find_dead_locals` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `find_dead_locals` | project scope | `projectFilter` | `projectName` | YES | `projectFilter` diverges from canonical `projectName` |
| `find_dead_locals` | cap | `limit` | `limit` | compliant | |
| `find_dead_fields` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `find_dead_fields` | project scope | `projectFilter` | `projectName` | YES | `projectFilter` diverges from canonical `projectName` |
| `find_dead_fields` | include public | `includePublic` | `includePublic` | compliant | |
| `find_dead_fields` | usage kind | `usageKind` | `usageKind` | compliant | |
| `find_dead_fields` | cap | `limit` | `limit` | compliant | |
| `symbol_impact_sweep` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `symbol_impact_sweep` | file | `filePath` | `filePath` | compliant | |
| `symbol_impact_sweep` | line | `line` | `line` | compliant | |
| `symbol_impact_sweep` | column | `column` | `column` | compliant | |
| `symbol_impact_sweep` | handle | `symbolHandle` | `symbolHandle` | compliant | |
| `symbol_impact_sweep` | metadata | `metadataName` | `metadataName` | compliant | |
| `symbol_impact_sweep` | summary | `summary` | `summary` | compliant | |
| `symbol_impact_sweep` | per-category cap | `maxItemsPerCategory` | `maxItemsPerCategory` | compliant | |
| `test_reference_map` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `test_reference_map` | project scope | `projectName` | `projectName` | compliant | |
| `test_reference_map` | offset | `offset` | `offset` | compliant | |
| `test_reference_map` | limit | `limit` | `limit` | compliant | |
| `validate_workspace` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `validate_workspace` | changed files | `changedFilePaths` | `changedFilePaths` | compliant | |
| `validate_workspace` | run tests | `runTests` | `runTests` | compliant | |
| `validate_workspace` | summary | `summary` | `summary` | compliant | |
| `validate_workspace` | format | `responseFormat` | `responseFormat` | compliant | |
| `validate_recent_git_changes` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `validate_recent_git_changes` | run tests | `runTests` | `runTests` | compliant | |
| `validate_recent_git_changes` | summary | `summary` | `summary` | compliant | |
| `preview_record_field_addition` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `preview_record_field_addition` | record type | `recordMetadataName` | `recordMetadataName` | compliant | |
| `preview_record_field_addition` | field name | `newFieldName` | `newFieldName` | compliant | |
| `preview_record_field_addition` | field type | `newFieldType` | `newFieldType` | compliant | |
| `preview_record_field_addition` | default value | `defaultValue` | `defaultValue` | compliant | |
| `semantic_grep` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `semantic_grep` | regex | `pattern` | `pattern` | compliant | |
| `semantic_grep` | scope | `scope` | `scope` | compliant | |
| `semantic_grep` | project scope | `projectFilter` | `projectName` | YES | `projectFilter` diverges from canonical `projectName` |
| `semantic_grep` | cap | `limit` | `limit` | compliant | |

### Symbol experimental tools

| Tool | Param | Current name | Canonical name | Breaking? | Notes |
|---|---|---|---|---|---|
| `find_type_consumers` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `find_type_consumers` | type | `typeName` | `typeName` | compliant | |
| `find_type_consumers` | cap | `limit` | `limit` | compliant | |
| `probe_position` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `probe_position` | file | `filePath` | `filePath` | compliant | |
| `probe_position` | line | `line` | `line` | compliant | |
| `probe_position` | column | `column` | `column` | compliant | |

### Workspace experimental tools

| Tool | Param | Current name | Canonical name | Breaking? | Notes |
|---|---|---|---|---|---|
| `workspace_warm` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `workspace_warm` | projects filter | `projects` | `projects` | compliant | Plural array; distinct from `projectName` |
| `workspace_drift_check` | workspace | `workspaceId` | `workspaceId` | compliant | |

### Refactoring experimental tools

| Tool | Param | Current name | Canonical name | Breaking? | Notes |
|---|---|---|---|---|---|
| `format_check` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `format_check` | project scope | `projectName` | `projectName` | compliant | |
| `restructure_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `restructure_preview` | pattern | `pattern` | `pattern` | compliant | |
| `restructure_preview` | goal | `goal` | `goal` | compliant | |
| `restructure_preview` | file scope | `filePath` | `filePath` | compliant | |
| `restructure_preview` | project scope | `projectName` | `projectName` | compliant | |
| `replace_string_literals_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `replace_string_literals_preview` | replacements | `replacements` | `replacements` | compliant | |
| `replace_string_literals_preview` | file scope | `filePath` | `filePath` | compliant | |
| `replace_string_literals_preview` | project scope | `projectName` | `projectName` | compliant | |
| `change_signature_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `change_signature_preview` | op | `op` | `op` | compliant | |
| `change_signature_preview` | file | `filePath` | `filePath` | compliant | |
| `change_signature_preview` | line | `line` | `line` | compliant | |
| `change_signature_preview` | column | `column` | `column` | compliant | |
| `change_signature_preview` | handle | `symbolHandle` | `symbolHandle` | compliant | |
| `change_signature_preview` | metadata | `metadataName` | `metadataName` | compliant | |
| `change_signature_preview` | param name | `name` | `name` | compliant | |
| `change_signature_preview` | new name | `newName` | `newName` | compliant | |
| `change_signature_preview` | type | `parameterType` | `parameterType` | compliant | |
| `change_signature_preview` | default | `defaultValue` | `defaultValue` | compliant | |
| `change_signature_preview` | position | `position` | `position` | compliant | |
| `change_signature_preview` | reorder | `newOrder` | `newOrder` | compliant | |
| `parameter_object_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `parameter_object_preview` | param names | `parameterNames` | `parameterNames` | compliant | |
| `parameter_object_preview` | new type | `newTypeName` | `newTypeName` | compliant | |
| `parameter_object_preview` | file | `filePath` | `filePath` | compliant | |
| `parameter_object_preview` | line | `line` | `line` | compliant | |
| `parameter_object_preview` | column | `column` | `column` | compliant | |
| `parameter_object_preview` | handle | `symbolHandle` | `symbolHandle` | compliant | |
| `parameter_object_preview` | metadata | `metadataName` | `metadataName` | compliant | |
| `parameter_object_preview` | dto project | `dtoProjectName` | `dtoProjectName` | compliant | |
| `parameter_object_preview` | dto namespace | `dtoNamespace` | `dtoNamespace` | compliant | |
| `parameter_object_preview` | dto folders | `dtoFolders` | `dtoFolders` | compliant | |
| `parameter_object_preview` | param name | `parameterName` | `parameterName` | compliant | |
| `symbol_refactor_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `symbol_refactor_preview` | operations | `operations` | `operations` | compliant | |
| `split_service_with_di_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `split_service_with_di_preview` | source file | `sourceFilePath` | `sourceFilePath` | compliant | |
| `split_service_with_di_preview` | source type | `sourceType` | `sourceType` | compliant | |
| `split_service_with_di_preview` | partitions | `partitions` | `partitions` | compliant | |
| `split_service_with_di_preview` | registration file | `hostRegistrationFile` | `hostRegistrationFile` | compliant | |
| `record_field_add_with_satellites_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `record_field_add_with_satellites_preview` | type | `typeMetadataName` | `typeMetadataName` | compliant | |
| `record_field_add_with_satellites_preview` | field name | `newFieldName` | `newFieldName` | compliant | |
| `record_field_add_with_satellites_preview` | field type | `newFieldType` | `newFieldType` | compliant | |
| `change_type_namespace_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `change_type_namespace_preview` | type | `typeName` | `typeName` | compliant | |
| `change_type_namespace_preview` | from namespace | `fromNamespace` | `fromNamespace` | compliant | |
| `change_type_namespace_preview` | to namespace | `toNamespace` | `toNamespace` | compliant | |
| `change_type_namespace_preview` | new file | `newFilePath` | `newFilePath` | compliant | |
| `extract_interface_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `extract_interface_preview` | file | `filePath` | `filePath` | compliant | |
| `extract_interface_preview` | type | `typeName` | `typeName` | compliant | |
| `extract_interface_preview` | interface name | `interfaceName` | `interfaceName` | compliant | |
| `extract_interface_preview` | members | `memberNames` | `memberNames` | compliant | |
| `extract_interface_preview` | replace usages | `replaceUsages` | `replaceUsages` | compliant | |
| `extract_interface_apply` | token | `previewToken` | `previewToken` | compliant | |
| `fix_all_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `fix_all_preview` | diagnostic | `diagnosticId` | `diagnosticId` | compliant | |
| `fix_all_preview` | scope | `scope` | `scope` | compliant | |
| `fix_all_preview` | file | `filePath` | `filePath` | compliant | |
| `fix_all_preview` | project scope | `projectName` | `projectName` | compliant | |
| `fix_all_apply` | token | `previewToken` | `previewToken` | compliant | |
| `apply_with_verify` | token | `previewToken` | `previewToken` | compliant | |
| `apply_with_verify` | rollback | `rollbackOnError` | `rollbackOnError` | compliant | |
| `move_type_to_file_apply` | token | `previewToken` | `previewToken` | compliant | |
| `bulk_replace_type_apply` | token | `previewToken` | `previewToken` | compliant | |
| `extract_type_apply` | token | `previewToken` | `previewToken` | compliant | |
| `extract_method_apply` | token | `previewToken` | `previewToken` | compliant | |
| `extract_shared_expression_to_helper_preview` | workspace | `workspaceId` | `workspaceId` | compliant | (parameters not separately read; body delegates to service) |
| `format_range_apply` | token | `previewToken` | `previewToken` | compliant | |

### Editing experimental tools

| Tool | Param | Current name | Canonical name | Breaking? | Notes |
|---|---|---|---|---|---|
| `apply_multi_file_edit` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `apply_multi_file_edit` | edits | `fileEdits` | `fileEdits` | compliant | |
| `apply_multi_file_edit` | skip syntax | `skipSyntaxCheck` | `skipSyntaxCheck` | compliant | |
| `apply_multi_file_edit` | verify | `verify` | `verify` | compliant | |
| `apply_multi_file_edit` | auto-revert | `autoRevertOnError` | `autoRevertOnError` | compliant | |
| `preview_multi_file_edit` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `preview_multi_file_edit` | edits | `fileEdits` | `fileEdits` | compliant | |
| `preview_multi_file_edit` | skip syntax | `skipSyntaxCheck` | `skipSyntaxCheck` | compliant | |
| `preview_multi_file_edit_apply` | token | `previewToken` | `previewToken` | compliant | |
| `create_file_apply` | token | `previewToken` | `previewToken` | compliant | |
| `delete_file_apply` | token | `previewToken` | `previewToken` | compliant | |
| `move_file_apply` | token | `previewToken` | `previewToken` | compliant | |
| `remove_dead_code_apply` | token | `previewToken` | `previewToken` | compliant | |
| `remove_interface_member_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `remove_interface_member_preview` | handle | `interfaceMemberHandle` | `interfaceMemberHandle` | compliant | |
| `remove_interface_member_preview` | remove empty | `removeEmptyFiles` | `removeEmptyFiles` | compliant | |

### Orchestration experimental tools

| Tool | Param | Current name | Canonical name | Breaking? | Notes |
|---|---|---|---|---|---|
| `get_prompt_text` | prompt name | `promptName` | `promptName` | compliant | |
| `get_prompt_text` | params json | `parametersJson` | `parametersJson` | compliant | |
| `add_central_package_version_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `add_central_package_version_preview` | package | `packageId` | `packageId` | compliant | NuGet identity; `packageId` is canonical |
| `add_central_package_version_preview` | version | `version` | `version` | compliant | |
| `apply_project_mutation` | token | `previewToken` | `previewToken` | compliant | |
| `scaffold_type_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `scaffold_type_preview` | project | `projectName` | `projectName` | compliant | |
| `scaffold_type_preview` | type name | `typeName` | `typeName` | compliant | |
| `scaffold_type_preview` | type kind | `typeKind` | `typeKind` | compliant | |
| `scaffold_type_preview` | namespace | `namespace` | `namespace` | compliant | |
| `scaffold_type_preview` | base type | `baseType` | `baseType` | compliant | |
| `scaffold_type_preview` | interfaces | `interfaces` | `interfaces` | compliant | |
| `scaffold_type_preview` | implement | `implementInterface` | `implementInterface` | compliant | |
| `scaffold_type_apply` | token | `previewToken` | `previewToken` | compliant | |
| `scaffold_test_batch_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `scaffold_test_batch_preview` | test project | `testProjectName` | `testProjectName` | compliant | |
| `scaffold_test_batch_preview` | targets | `targets` | `targets` | compliant | |
| `scaffold_test_batch_preview` | framework | `testFramework` | `testFramework` | compliant | |
| `scaffold_first_test_file_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `scaffold_first_test_file_preview` | service | `serviceMetadataName` | `serviceMetadataName` | compliant | |
| `scaffold_first_test_file_preview` | test project | `testProjectName` | `testProjectName` | compliant | |
| `scaffold_first_test_file_preview` | framework | `testFramework` | `testFramework` | compliant | |
| `scaffold_test_apply` | token | `previewToken` | `previewToken` | compliant | |
| `move_type_to_project_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `move_type_to_project_preview` | source file | `sourceFilePath` | `sourceFilePath` | compliant | |
| `move_type_to_project_preview` | type | `typeName` | `typeName` | compliant | |
| `move_type_to_project_preview` | target project | `targetProjectName` | `targetProjectName` | compliant | |
| `move_type_to_project_preview` | target namespace | `targetNamespace` | `targetNamespace` | compliant | |
| `move_type_to_project_preview` | preserve ns | `preserveNamespace` | `preserveNamespace` | compliant | |
| `extract_interface_cross_project_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `extract_interface_cross_project_preview` | file | `filePath` | `filePath` | compliant | |
| `extract_interface_cross_project_preview` | type | `typeName` | `typeName` | compliant | |
| `extract_interface_cross_project_preview` | interface name | `interfaceName` | `interfaceName` | compliant | |
| `extract_interface_cross_project_preview` | target project | `targetProjectName` | `targetProjectName` | compliant | |
| `dependency_inversion_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `dependency_inversion_preview` | file | `filePath` | `filePath` | compliant | |
| `dependency_inversion_preview` | type | `typeName` | `typeName` | compliant | |
| `dependency_inversion_preview` | iface project | `interfaceProjectName` | `interfaceProjectName` | compliant | |
| `dependency_inversion_preview` | iface name | `interfaceName` | `interfaceName` | compliant | |
| `migrate_package_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `migrate_package_preview` | old package | `oldPackageId` | `oldPackageId` | compliant | NuGet identity; `packageId` is canonical |
| `migrate_package_preview` | new package | `newPackageId` | `newPackageId` | compliant | NuGet identity; `packageId` is canonical |
| `migrate_package_preview` | new version | `newVersion` | `newVersion` | compliant | |
| `split_class_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `split_class_preview` | file | `filePath` | `filePath` | compliant | |
| `split_class_preview` | type | `typeName` | `typeName` | compliant | |
| `split_class_preview` | members | `memberNames` | `memberNames` | compliant | |
| `split_class_preview` | new file | `newFileName` | `newFileName` | compliant | |
| `extract_and_wire_interface_preview` | workspace | `workspaceId` | `workspaceId` | compliant | |
| `extract_and_wire_interface_preview` | file | `filePath` | `filePath` | compliant | |
| `extract_and_wire_interface_preview` | type | `typeName` | `typeName` | compliant | |
| `extract_and_wire_interface_preview` | target project | `targetProjectName` | `targetProjectName` | compliant | |
| `extract_and_wire_interface_preview` | iface name | `interfaceName` | `interfaceName` | compliant | |
| `extract_and_wire_interface_preview` | update di | `updateDiRegistrations` | `updateDiRegistrations` | compliant | |
| `apply_composite_preview` | token | `previewToken` | `previewToken` | compliant | |

### Prompts (all experimental)

All 20 registered prompts are experimental. Prompts use positional parameters resolved at `get_prompt_text` dispatch time via `parametersJson`. The canonical naming convention applies to the underlying prompt method parameter names (which appear in `PromptParameterIndex` and are published in the catalog). A spot-check of the prompt files confirms they already use `workspaceId`, `filePath`, `line`, `column`, `projectName` vocabulary consistent with the canonical convention. No renames required for the prompt surface.

---

## (d) Non-compliant parameters — consolidated rename list

Only **5 parameters** require renaming across the entire experimental surface. All 5 are in the `projectFilter` → `projectName` family:

| Tool | Current param | Canonical param | Caller impact |
|---|---|---|---|
| `trace_exception_flow` | `scopeProjectFilter` | `projectName` | YES — breaking rename |
| `find_duplicate_helpers` | `projectFilter` | `projectName` | YES — breaking rename |
| `find_dead_locals` | `projectFilter` | `projectName` | YES — breaking rename |
| `find_dead_fields` | `projectFilter` | `projectName` | YES — breaking rename |
| `semantic_grep` | `projectFilter` | `projectName` | YES — breaking rename |

**No `character` vs `column` violations found.** The live enumeration shows every locator-accepting experimental tool already uses `column` (1-based). The 2026-05-02 audit finding was a prospective concern; the surface is already compliant on this axis.

**NuGet tooling already uses `packageId`.** No action required.

**`scopeProjectFilter` on `trace_exception_flow`** is the most divergent: both the prefix (`scope`) and the suffix (`Filter`) diverge from `projectName`. The migration must rename it in one atomic change.

---

## (e) Alias strategy and deprecation window

The 5 non-compliant parameters are all on **experimental** tools. Per the server's support policy (`ServerSurfaceCatalog.cs`, `SupportPolicy` field): "Experimental entries may evolve faster and can change with less notice before a remote host exists." No external versioned API-stability contract exists for experimental parameters today.

**Decision (binding):** no deprecation aliases. The migration makes the rename directly in the method signature, updates the `[Description(...)]` text to use the new name, and ships as a single breaking-change PR. Callers using any of the 5 renamed parameters must update their argument key in the same release cycle.

**Rationale:** adding an alias `projectName` alongside `projectFilter` (both optional, both `string?`) would silently accept both names but only bind one, creating an ambiguity bug surface. The experimental tier explicitly disclaims the backward-compat expectation that aliases exist to honor. A clean rename is cheaper to maintain and aligns with the no-shim policy.

**Migration contract summary:**

- CHANGELOG category: `Changed — BREAKING`
- Version gate: file the rename in a minor bump (not a patch) so semantic versioning signals the break.
- Release note MUST enumerate all 5 renamed parameters by tool name and old → new name.
- No server-version header or protocol-level capability flag is required; the break is detectable by callers' test failures.

---

## (f) Regression test shape (for follow-on initiative)

The follow-on initiative `parameter-naming-canonicalization-migration` MUST add one regression test file. The test asserts lockstep compliance between the live `[McpServerTool]` parameter names and the canonical convention — so that any future experimental tool added with a non-canonical name fails CI immediately.

**Structural template:** mirror `tests/RoslynMcp.Tests/Skills/IssueTemplateAndLabelSeedTests.cs`. That file asserts three-surface lockstep (schema doc, issue template, seed script) using static file reads and `[TestClass]`/`[TestMethod]` (MSTest) with no live workspace. The parameter-naming test should follow the same pattern.

**Target file:** `tests/RoslynMcp.Tests/ExperimentalSurfaceParameterNamingTests.cs`

**Required test methods (minimum 3):**

1. `ExperimentalTools_NoToolHasProjectFilterParam` — reflect all `[McpServerToolType]` classes in `RoslynMcp.Host.Stdio.Tools`; for each `[McpServerTool]`-decorated method, assert that no parameter is named `projectFilter` or `scopeProjectFilter`. Failure message names the offending tool and parameter.

2. `ExperimentalTools_ColumnParamsAreNotNamedCharacter` — same reflection walk; assert no parameter named `character` exists on any experimental tool method. This pins the LSP-alias non-intrusion permanently.

3. `ExperimentalTools_NuGetParamsUsePackageIdNotPackageName` — assert no parameter named `packageName` exists on any experimental tool method that also has a parameter named `workspaceId` (i.e., scoped to workspace-bearing tools that handle NuGet operations).

**Implementation note:** reflection over `Assembly.GetTypes()` filtered by `[McpServerToolType]` attribute and `[McpServerTool]` attribute on each method (matching `PromptShimTools.ResolvePromptMethod`'s pattern). No live Roslyn workspace required — the test is purely static reflection + convention assertion.

---

## (g) What this design explicitly does NOT do

- **No `src/` mutations in this PR.** The design note is the deliverable; all renames happen in the follow-on initiative.
- **No new test file added now.** The test shape is specified in §(f) but written by the follow-on initiative executor.
- **No follow-on backlog row filed in this PR.** The row `parameter-naming-canonicalization-migration` is filed separately after this design lands.
- **No prompt parameter renames.** Prompt parameters are already canonical.
- **No stable-surface changes.** This design's scope is the experimental tier only.

---

## Verdict

**Go for v1.** The live enumeration found only 5 non-compliant parameters, all in the `projectFilter → projectName` family, all on experimental tools where breaking changes require only a CHANGELOG entry and a version bump — no deprecation aliases, no grace period. The surface is cleaner than the 2026-05-02 audit implied: no `character` vs `column` violations exist and NuGet naming is already canonical. The migration is a straightforward set of 5 parameter renames + 1 new lockstep regression test.

### Next deliverable

File one new backlog row `parameter-naming-canonicalization-migration` (priority **Low**; `deps: parameter-naming-canonicalization-experimental-surface`). The row's implementation scope: rename 5 parameters as listed in §(d), add `tests/RoslynMcp.Tests/ExperimentalSurfaceParameterNamingTests.cs` with the 3 test methods from §(f), update the 5 `[Description(...)]` attributes, update `CHANGELOG.md` category `Changed — BREAKING`. `productionFilesTouched: 5` (one per tool file), `testFiles: 1`. `toolPolicy: "edit-only"`.

---

## Refs

| Path | Why |
|---|---|
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Analysis.cs` | Experimental tool tier source — analysis/advanced-analysis |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Symbols.cs` | Experimental tool tier source — symbols |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Workspace.cs` | Experimental tool tier source — workspace |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs` | Experimental tool tier source — refactoring |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Editing.cs` | Experimental tool tier source — editing |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs` | Experimental tool tier source — orchestration |
| `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Prompts.cs` | Experimental prompt tier source |
| `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs` | Parameter signatures: `find_dead_locals`, `find_dead_fields`, `find_duplicate_helpers`, `semantic_grep` |
| `src/RoslynMcp.Host.Stdio/Tools/ExceptionFlowTools.cs` | Parameter signature: `trace_exception_flow` |
| `src/RoslynMcp.Host.Stdio/Tools/ImpactSweepTools.cs` | Parameter signature: `symbol_impact_sweep` |
| `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` | Parameter signatures: `validate_workspace`, `validate_recent_git_changes` |
| `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs` | Parameter signature: `semantic_grep` |
| `tests/RoslynMcp.Tests/Skills/IssueTemplateAndLabelSeedTests.cs` | Structural template for the follow-on regression test |
| `ai_docs/items/parameter-object-preview-design.md` | Section-structure precedent for this document |
