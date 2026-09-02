# scaffold-sampling-mrtr-replay-cost — avoid repeating expensive scaffold preparation across MRTR replay

**row:** `scaffold-sampling-mrtr-replay-cost` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`
- `src/RoslynMcp.Core/Services/IScaffoldingService.cs`
- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`
- `tests/RoslynMcp.Tests/SamplingMrtrWireTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] Separate sampling-context preparation from final scaffold preview so the MRTR retry does not repeat project resolution, compilation, or sibling-test discovery.
- [ ] Preserve stateless operation with bounded, opaque, secret-safe request state; do not cache client payloads or absolute paths in process-static state.
- [ ] A production-tool wire regression proves one expensive preparation, one client sampling request, and one completed preview across the input-required/retry exchange.

## Evidence

The production `scaffold_test_preview` MRTR regression correctly observes two service/provider entries because the protocol replays the entire tool call. `SingleTestScaffolder` derives the sampling context only after project resolution, compilation, and sibling inference, so those expensive steps currently repeat before the retry can consume the response.

## Amendment — 2026-09-02 (cold plan-deepener; verified against live source, no code shipped)

Row is **shovel-ready** — all five anchors resolve. Mechanism confirmed and narrower than the row implies.

**Root cause.** `SingleTestScaffolder.PreviewAsync` orders the work wrong: target/method resolution plus test-project compilation (`SingleTestScaffolder.cs:63`, inner `GetCompilationAsync` at `:189`), a second `GetCompilationAsync` (`:81-83`), and semantic sibling-pattern inference (`:84`) all run BEFORE the sampling request at `:86`. That request terminates leg 1 by throwing `InputRequiredException` from `RequestScopedInputAdapter.cs:207`, and MRTR replays the whole `tools/call`, so every step runs twice. `SamplingMrtrWireTests.cs:122-127` already pins the replay (`AttemptCount == 2`, `CompletedCount == 1`) as CORRECT protocol behavior — do not "fix" the replay.

**Inverse waste also present:** prompt-only preparation (the recursive `*Tests.cs` enumerate-and-parse in `CollectSiblingTestMethodNames`, `:345-376`, built at `:128-133`) is constructed on BOTH legs but consumed only on leg 1.

**Approach — two-phase provider contract + hoist, no cross-request storage.** Add `bool TryConsumePendingSuggestion(out …)` to `ITestNameSuggestionProvider` (`IScaffoldingService.cs:59-62`) as a **default-implemented** member returning `false` (keeps both existing test fakes compiling untouched). Extract the retry-leg half of `RequestSampling` (`RequestScopedInputAdapter.cs:190-205`) into an internal `TryConsumeSampling` so the capability + protocol gate at `:190-193` stays in one place. Move the sampling call from `SingleTestScaffolder.cs:86` to ahead of `:63`; leg 1 builds the context from the syntactic name only and lets `InputRequiredException` escape, leg 2 consumes the answer without rebuilding the prompt context, so `:63-100` run exactly once.

**Bounded/secret-safe state is satisfied by construction:** prepared context lives only on the stack of the single leg that consumes it — no store, no key, no TTL. `requestState` keeps carrying only `RequestStateCodec`'s fixed 54-char opaque token (`RequestStateCodec.cs:14,29`).

**Scope — exactly at the Rule 3 base cap of 4, no exemption claimed:** `src/RoslynMcp.Core/Services/IScaffoldingService.cs`, `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs`, `src/RoslynMcp.Host.Stdio/Elicitation/RequestScopedInputAdapter.cs`, `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`. Tests 2 extended: `SamplingMrtrWireTests.cs`, `ScaffoldingIntegrationTests.cs`.

**Accepted trade-off:** leg 1 loses the symbol-derived `TargetMethodSignature`/`TargetNamespace` from the sampling prompt; both are already `string?` and `BuildSamplingPrompt` (`ScaffoldingTools.cs:220-227`) degrades gracefully. **Rejected alternative:** a `requestState`-keyed server-side prepared-context cache — it would be a process-static cache of absolute paths needing eviction/TTL, contradicting both the acceptance constraint and the no-session-cache guarantee at `RequestScopedInputAdapter.cs:50-54`.

**Scheduling:** may touch `src/RoslynMcp.Host.Stdio/Tools/ScaffoldingTools.cs`, shared with the `method-description-diet` / `param-description-dedupe` slice families — file overlap only, no build-order edge; do not co-schedule in one parallel wave.
