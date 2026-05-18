# Backlog sweep plan — 20260518T221744Z

**Generated:** 2026-05-18T22:17:44Z
**Backlog snapshot:** 2026-05-18T03:09:14Z
**Mode:** `/backlog-sweep:prepare count=15`
**Initiative count:** 15 (all P2 Medium; skipped `firewallanalyzer-p2-polish-aggregate-20260516` aggregator row — intake-tracking only, not a single-initiative target)
**Phase:** skeleton (deepening pending)

## Plan summary

Fifteen P2 Medium initiatives selected from the 24-row claimable backlog after the 20260517T235058Z sweep cleared all 8 P1 High rows. Selection covers refactor-tool correctness (extract_interface duplicate, change_type_namespace, symbol_refactor, migrate_package), response-shape bugs (project_diagnostics, symbol_signature_help, validate_workspace overallStatus, find_property_writes), payload-overflow patterns (find_overrides corlib, analyze_dependencies prompt, review_test_coverage prompt), timeout-handling (validate_workspace 25s, workspace_status verbose), test-runner integration (test_run FQDN drift), and a doc-gap (source_file resource URL encoding).

## Bundle considerations (Rule 1)

Two pairs flagged as bundle candidates — the deepener will verify or split:

- **Initiatives 6 + 13:** `validate-workspace-overallstatus-analyzer-error-with-empty-errordiagnostics` (gh #751) + `validate-workspace-25s-internalvalidationtimeoutexception-on-medium-solution` (gh #759). Both touch `WorkspaceValidationService.cs` but likely different code paths (overallStatus computation vs timeout-exception graceful-failure-envelope). Deepener verifies Rule 1 four conditions.
- **Initiatives 10 + 11:** `analyze-dependencies-prompt-payload-overflow` (gh #755) + `review-test-coverage-prompt-payload-overflow` (gh #756). Both prompt-payload-overflow patterns mirroring the shipped `guided_extract_interface` precedent. Different prompt renderers — Rule 1 four conditions unlikely to hold; expect split.

## Initiatives

### 1. project-diagnostics-totaldiagnostics-collapses-under-severity-filter

_pending plan-deepener_

### 2. symbol-signature-help-returns-bare-null-for-resolvable-method-metadata

_pending plan-deepener_

### 3. extract-interface-preview-duplicate-interface-when-already-implements

_pending plan-deepener_

### 4. change-type-namespace-preview-omits-consumer-using-additions

_pending plan-deepener_

### 5. symbol-refactor-preview-empty-appliedfiles-on-success

_pending plan-deepener_

### 6. validate-workspace-overallstatus-analyzer-error-with-empty-errordiagnostics

_pending plan-deepener (bundle candidate with #13)_

### 7. test-run-fqdn-drift-vs-test-discover

_pending plan-deepener_

### 8. migrate-package-preview-misses-analyzer-only-references

_pending plan-deepener_

### 9. find-overrides-payload-overflow-on-corlib-virtual

_pending plan-deepener_

### 10. analyze-dependencies-prompt-payload-overflow

_pending plan-deepener (bundle candidate with #11)_

### 11. review-test-coverage-prompt-payload-overflow

_pending plan-deepener (bundle candidate with #10)_

### 12. find-property-writes-metadataname-hint-mismatches-input-shape

_pending plan-deepener_

### 13. validate-workspace-25s-internalvalidationtimeoutexception-on-medium-solution

_pending plan-deepener (bundle candidate with #6)_

### 14. workspace-status-verbose-5s-timeout-race-on-ready-workspace

_pending plan-deepener_

### 15. source-file-resource-requires-url-encoded-absolute-path-undocumented

_pending plan-deepener_
