# native-reference-free-normalization-duplicate-proof — Fuse the producer-free native publication phase

**row:** `native-reference-free-normalization-duplicate-proof` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs:151`: NormalizeCoreAsync.
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs:222`: unconditional publication input assertion before BindNormalized.
- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs:305`: AssertLoadEpochAsync; grants, hashes, analyzer proof, complete native membership, final grants/hashes/SDK truth.
- `tests/RoslynMcp.Tests/NativeReferenceFreePublicationTests.cs:20`: actual certified SDK loader, normalization and current-boundary controls.
- `tests/RoslynMcp.Tests/NativeCompilerLoadContextTests.cs:18`: existing multi-project compiler context/reference authority coverage.

## Root cause

| Path | Defect |
|---|---|
| No native ProjectReferences | NormalizeCoreAsync performs a full AssertLoadEpochAsync, constructs only empty managed reference arrays, then performs the identical full check through AssertPublicationInputsCurrentAsync. |
| Cost | Each redundant phase performs a complete fresh native graph evaluation and two complete frozen-input hash/grant checks; package-heavy staged loads repeat this during normalization. |
| Boundary | The intervening prefix contains no native producer, explicit input read, policy callback, await or publication. NativeSolution and reference collections are immutable. |

## Acceptance

- [ ] Move the entry proof to the first actual reference-producing project; retain the unconditional final publication proof.
- [ ] Retain all nonempty producer pre/post checks, parsing receipts, compiler-reference metadata reconciliation, current grants and fresh hashes.
- [ ] Do not cache physical bytes or permissions, reuse proofs across IO, change deadlines or alter native load equivalence.
- [ ] Keep cancellation and unavailable/already-issued normalization behavior intact.
- [ ] Verify a real no-reference SDK normalization produces usable compiler semantics and admitted image authority with one complete before/after grant pair.
- [ ] Refuse source drift at both grants, newly matching Compile membership, revoked final grants, and stale already-issued normalization.
- [ ] Observe old duplicate-phase measurement fail and each safety guard mutant fail its corresponding native regression; restore guarded source exactly.
- [ ] Run existing one/multi-project reference controls and required complete gates before closure.

## Evidence

| Artifact under `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume` | Observed status |
|---|---|
| `normalization-entry-fusion-proposal/proposal.patch` | Surgical draft; no timing probes added. |
| `normalization-entry-fusion-proposal/tests.patch` | Six real certified-loader cases; semantic/image and repeated-use authority controls supplement phase measurement. |
| Parent before41 run | Stable row observed expected2 / actual4 source-grant boundary checks on old production; remaining safety rows still running at payload creation. |
| Fixed and safety mutant runs | Pending; no passing claim. |

## Phase invariant

- Before the first reference producer, normalize only reads immutable project/reference collections, checks cancellation and populates a private builder with empty arrays.
- The first producer remains behind the full entry proof. Every producer retains its existing post-check.
- With no producer, the final helper has no parsing receipts and performs the same complete epoch assertion immediately before publication.
- Current policy callbacks remain at the retained complete boundary. Reduced redundant callback count is not cached authorization; drift or denial at either retained grant must refuse.
- The fix belongs to the active restore prerequisite. Do not advance the paused 15-item batch or close the row from a source-only review.

Current-session evidence: normalization-before41 stable control failed expected2 source grants versus actual4; other safety rows passed except new test incorrectly expected exact OperationCanceledException from SemaphoreSlim.WaitAsync. Corrected that new test to exact TaskCanceledException plus original-token equality. normalization-after42 passed31/31 including native producer and ambient-property controls. Guard mutant membership-b9f7b20139764363ad1cd6855a873079 failed new-compile as expected (no refusal); final-grants-21964784198d4bc29d74535c6469c056 failed late-source and revoked-final-grant as expected, plus stable boundary count. Both mutants restored exact source bytes. Combined acceptance remains pending.

Final-publication guard mutant2934cdc1fb0d4c70889cbb080801b233:5expectedfail1pass6; source,late-source,new-compile and revoked-final-grant all wrongly returned without required refusal. Stable boundary count also failed; cancellation remained enforced. Original source restored exactly. No production guard removal retained.
