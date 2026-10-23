# workspace-reload-preserve-global-properties — Preserve configured MSBuild settings on reload

**row:** `workspace-reload-preserve-global-properties` · **pri:** `Medium` · **size:** `S` · **deps:** `restore-assets-json-kind-guards`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:508` — Reload enters the shared session loader.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1172` — The helper substitutes null for the configured globals.
- (new) `tests/RoslynMcp.Tests/Workspace/WorkspaceReloadGlobalPropertiesTests.cs`

## Acceptance

- [ ] Public reload preserves the loaded session's MSBuild global properties and resulting configured parse/compilation behavior, including every executable use of the shared reload helper.
- [ ] Observe the old reload lose configured symbols or compilation behavior; assert actual compiler state before and after reload, with default-load and cancellation controls.
- [ ] Preserve load-lock/swap-on-success behavior and diagnostics redaction; obtain independent review, scoped/hosted checks and the serialized delivery gate before closure.

## Scope

- One production file, one test file and one fragment; no assets precedence, restore planning or native-authority machinery.
- First independent context-preservation slice after the JSON-kind fix; metadata evaluation context is the dependent slice.

## Evidence

- Fresh cold review at 5d52ec418befd9706bea63748eb5109fb6eb87e8 traced public ReloadAsync to the helper forwarding globalProperties:null.
- Semantic references found one executable caller of that helper; other references were documentation.
- No regression test or implementation has run for this row yet. Keep the aggregate restore row open.
