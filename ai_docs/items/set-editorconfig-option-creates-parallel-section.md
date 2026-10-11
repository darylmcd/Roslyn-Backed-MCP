# set-editorconfig-option-creates-parallel-section — Reuse an existing matching section in set_editorconfig_option

**row:** `set-editorconfig-option-creates-parallel-section` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:397`

## Acceptance

- [ ] Setting a C# key on an .editorconfig that already has a C#-files section edits that section instead of appending a parallel multi-extension glob section
- [ ] get_editorconfig_options reflects the value

## Evidence

- set_editorconfig_option added [*.{cs,csx,cake}] next to the existing [*.cs] section (Phase 6/7). Related prior #735 (duplicate key) — no duplicate key this run. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Approved policy supersession (2026-10-11; implementation unstarted)

| Case | Operator-approved behavior | Closure requirement |
|---|---|---|
| Existing applicable assignment | Update applicable broad C# defaults and every later override matching the requested source; preserve other-only assignments. | Prove effective requested-source value and preservation through service and both tool wire eras. |
| New ordinary-config key with no applicable assignment | Insert a canonical C# default section before existing sections; preserve later overrides. This supersedes this row's matching-section reuse acceptance for that case. | Prove early placement, effective value, preserved overrides and byte/transaction guarantees; never close by claiming the superseded reuse behavior was implemented. |
| New global-config key | Insert the approved project-wide preamble default before named sections, including non-C# project sources. | Prove original-provider final effective value and preserved exact-path overrides. |

Ownership: editorconfig-section-source-path-matching complete source-only proposal; Root owns this policy disposition. All choices are live user approvals. This note is a design decision, not implementation, admission, validation or closure. Keep this row open until the superseding behavior is implemented and verified; reconcile the acceptance explicitly before closure.
