# compilation-cache-first-call-single-flight — Start only the winning compilation task

**row:** `compilation-cache-first-call-single-flight` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompilationCache.cs`
- `tests/RoslynMcp.Tests/CompilationCacheTests.cs`

## Acceptance

- [ ] Concurrent first callers install a lazy/shared task before compilation work starts, so only the dictionary winner invokes Roslyn for a workspace/project/version key.
- [ ] Apply the same single-flight discipline to analyzer-bound construction.
- [ ] Preserve per-caller cancellation, version replacement, and fault/cancellation eviction semantics.
- [ ] Add one deterministic barrier/counting regression proving a burst starts one raw compilation and one analyzer-bound build per key.

## Evidence

- Both cache miss paths start work before `AddOrUpdate`; losing racers run duplicate expensive builds even though comments claim a single compilation pass.
