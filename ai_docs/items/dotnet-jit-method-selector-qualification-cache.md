# dotnet-jit-method-selector-qualification-cache — dotnet-jit-method-selector-qualification-cache

**row:** `dotnet-jit-method-selector-qualification-cache` · **pri:** `Low` · **size:** `M`

## Problem

The external .NET JIT MethodSet renderer caches a formatted method name without invalidating it when assembly qualification changes between selectors. Mixed qualified/unqualified patterns can silently omit native method listings, defeating diagnostic evidence.

## Anchors

- `ai_docs/runtime.md`: native runtime diagnostic assumptions.
- `src/RoslynMcp.Roslyn/Services/NativeConditionCacheObserver.cs`: native-callsite qualification consumer.
- External owner: `dotnet/runtime`, `src/coreclr/jit/jitconfig.cpp`, MethodSet::contains, lines164-172 in v10.0.13.

## Acceptance

- Track the upstream renderer invalidation fix; do not patch or suppress production MSBuild authority checks to compensate for missing diagnostic output.
- Qualify mixed-selector output against the exact installed runtime before relying on it after an upgrade.
- Until upstream correction is verified, use consistently qualified or consistently unqualified selectors and verify every requested method family appears.
- Preserve source/DLL hashes, runtime configuration, actual test exit status and the distinction between compilation evidence and failure causation.

## Evidence

- Primary source: https://github.com/dotnet/runtime/blob/v10.0.13/src/coreclr/jit/jitconfig.cpp#L164-L172 ; includeAssembly is passed to eePrintMethod but omitted from cache invalidation.
- Current native-cache-jit-158 requested native and observer methods; only the unqualified observer family appeared. This is not proof of native inlining.
- Scratch: %USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/native-cache-jit-disassembly-plan/run158-selector-finding.md and jitconfig-v10.0.13.cpp.
- Upstream runtime is externally owned. No upstream issue/comment was sent; local tracking records this prerequisite diagnostic limitation.
