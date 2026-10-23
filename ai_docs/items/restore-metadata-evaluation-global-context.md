# restore-metadata-evaluation-global-context — Evaluate metadata with the operation's MSBuild settings

**row:** `restore-metadata-evaluation-global-context` · **pri:** `Medium` · **size:** `M` · **deps:** `workspace-reload-preserve-global-properties`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1272` — Drift detection drops the globals used to load the session.
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:209` — Drift and assets evaluation need explicit context.
- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs:21` — Temporary evaluation currently uses a bare ProjectCollection.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:98` — Target-framework metadata evaluation must preserve loaded context.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:241` — Output-type evaluation is the other helper caller.
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`
- `tests/RoslynMcp.Tests/ProjectOutputTypeTests.cs`
- (new) `tests/RoslynMcp.Tests/Workspace/WorkspaceReloadGlobalPropertiesTests.cs`

## Acceptance

- [ ] Metadata evaluation uses explicit MSBuild globals appropriate to the actual operation: loaded-workspace drift and project metadata use loaded settings; readers accompanying default restore commands use explicit default settings. Update every helper caller through required arguments.
- [ ] With Release and Debug using distinct custom assets paths and no default assets present, configured drift/absence detection and target-framework/output-type reporting use Release rather than a bare Debug evaluation. Observe the old behavior fail before repair.
- [ ] Preserve default-first assets precedence in this slice, the landed kind/diagnostic guards, configured reload behavior and redaction; obtain independent review, scoped/hosted checks and the serialized delivery gate before closure.

## Scope

- Exactly four production files, at most three test files and one fragment.
- One missing-context mechanism at metadata evaluation boundaries; no optional/defaulted compatibility parameters or duplicated evaluators.
- Active-path precedence remains in dependent restore-active-assets-layout-authority. Keep reader and execution settings matched if the later restore caller plan changes command settings.

## Evidence

- Fresh semantic review at 5d52ec41 found three EvaluateProperties call sites: two parser calls and the detector call. Five direct output-type test callers also require explicit context updates.
- WorkspaceManager loads supplied globals but calls drift detection with only project statuses; the helper creates a bare ProjectCollection.
- MSBuildWorkspace.Properties is available in the installed 5.9.0 API; no native certificate, BuildHost, public DTO extension or physical lease is required.
- No regression test or implementation has run for this row yet.
