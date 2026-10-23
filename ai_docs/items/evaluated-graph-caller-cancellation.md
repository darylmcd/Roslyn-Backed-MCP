# evaluated-graph-caller-cancellation — Preserve caller cancellation through native graph wrappers

**row:** `evaluated-graph-caller-cancellation` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EvaluatedBuildGraph.cs:45`: Source(Solution) substitutes CancellationToken.None.
- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:175`: async caller loses cancellation after selecting-path authorization.
- `src/RoslynMcp.Roslyn/Services/ForkGraphPreparation.cs:87`: same forwarding mechanism.
- `src/RoslynMcp.Roslyn/Services/RestoredCompilerInputAuthority.cs:141`: same forwarding mechanism.
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs`: native graph wrapper.
- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`: native graph wrapper.
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`: native graph wrapper.
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.NativeProtocols.cs`: native graph wrapper.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs`: semantic project wrappers and their analysis/reference classifiers.
- `tests/RoslynMcp.Tests/EvaluatedGraphCancellationTests.cs`.

## Acceptance

- [ ] Require an explicit token at native graph wrapper boundaries and forward the actual caller token wherever available; update every semantic reference, without an optional compatibility overload.
- [ ] Preserve synchronous explicit noncancellable intent only where the actual caller has no cancellation contract.
- [ ] After an actual selecting-path grant cancels normally, refuse with the exact caller cancellation before attempting graph evaluation.
- [ ] Retain an uncanceled malformed-native-definition control, unchanged physical bytes and exact fixture inventory.
- [ ] Observe the regression fail on old implementation and pass on current source; cold review the full propagation diff.

## Evidence

- Semantic Source overload references: 32, including three direct async losses and sync forwarding wrappers.
- Scratch graph-cancellation-old19 compiles the actual new test plus real cleanup fixtures against the unchanged causal19 product assemblies; initial native ProjectCollection.LoadProject independently proves InvalidProjectFileException before arming.
- graph-cancellation-old19-tests2.log: 2 tests, uncanceled control passes; canceled row fails because observed exception is null after exact grant/token/canceled-state/unchanged-bytes and member-list assertions. The old detector returns false after native graph failure instead of propagating cancellation.
- Scratch compilation has CS1701 net9 MSTest versus net10 ref-pack compatibility warning; native runtime executed both tests. This is not a full current-source build.
