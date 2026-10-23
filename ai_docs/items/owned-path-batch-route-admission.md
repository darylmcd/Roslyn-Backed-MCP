# owned-path-batch-route-admission — Bound repeated owned path route acquisition

**row:** `owned-path-batch-route-admission` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ForkInvocationPathPolicy.cs`: ForkInvocationAuthority.ValidateCurrentAsync and MapOwnedPath.
- `src/RoslynMcp.Roslyn/Helpers/PhysicalDirectoryScope.cs`: OwnedRoot.AssertPhysicalPath.
- `tests/RoslynMcp.Tests/PhysicalDirectoryFinalRemovalTests.cs`: real native forwarding scope.
- `tests/RoslynMcp.Tests/SolutionFileMutationEscapingLifecycleTests.cs`: unchanged public fork request deadline.

## Acceptance

- [ ] Materialize each pre-callback and post-callback owned mapping pass separately; preserve revocation, exact scope-chain identity, workspace authority and nested mapping round trips.
- [ ] Admit every requested path and each physical leaf/absence; acquire shared directory prefixes once within a pass using existing native descriptor-relative operations.
- [ ] Revalidate held parent-to-child bindings and published root before returning; reject links, special objects and non-directory ancestors.
- [ ] Dispose all pass-scoped pins before callbacks; no cross-callback cache or retained absence truth.
- [ ] Observe actual old structural repeated native operations, then green bounded prefix operations; preserve actual manager-issued callback mutation controls.
- [ ] Qualify the unchanged lifecycle request deadline and existing Windows/POSIX physical authority controls. No gate retries, timeout inflation or relaxed assertions.

## Evidence

- 2026-10-23 causal15 completed 97 tests: 83 passed, 13 failed, 1 skipped; four public lifecycle tests still hit the existing 120-second request timeout.
- Read-only exact frozen testhost stack causal15-stacks1.log111–155 samples AssertLoadEpoch -> ValidateCurrentAsync -> MapOwnedPath -> OwnedRoot.AssertPhysicalPath -> AssertCurrentPath -> OpenRouteWindowsCore.
- Current MapOwnedPath rechecks the whole owned scope chain for every path; each path reopens every ancestor route, repeating shared prefix work. Sampling does not quantify total request-time share.
- Root cause repair is required in the active restore PR, not deferred work.
