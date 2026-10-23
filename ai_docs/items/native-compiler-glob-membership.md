# native-compiler-glob-membership — Freeze native input membership through certificate use

**row:** `native-compiler-glob-membership` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NativeCompilerContextCertificate.cs`: AssertCurrentAsync and Epoch capture.
- `src/RoslynMcp.Roslyn/Services/NativeConditionEvidence.cs`: native filesystem observations.
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.NativeProtocols.cs`: prepared execution certificate admission.

## Acceptance

- [ ] Preserve native item and import glob membership, including zero-match selectors and include/exclude/remove semantics, in issued authority.
- [ ] Revalidate current matching membership before queued execution and certificate reuse; authorize traversal before new explicit enumeration or external reads.
- [ ] Reject matching additions, removals, condition changes and revoked grants while retaining known-file hash and exact compiler-context checks.
- [ ] Exercise direct native certificate drift and a real queued-execution race; verify unrelated nonmatching and excluded files remain accepted.
- [ ] Observe old behavior fail the regression and run focused plus full required gates on the fixed head.

## Evidence

- Restore prerequisite live review found AssertCurrentAsync checks frozen known input hashes, SDK conditions, analyzer inputs and compiler argument receipts, but not newly matching project globs.
- Directory PhysicalInputIdentity records existence and kind without child membership; newly added matching source files can escape that frozen input set.
- This prerequisite closes the discovered authority hole before any loaded-native-graph reuse. It does not advance the paused 15-item batch.

### Current-session membership and issuance evidence

- Freeze ordered native items/metadata, import/definition roles, exact contexts, registry identity and condition witnesses; re-evaluate through trusted MSBuild before certificate reuse, publication and queued execution. Authorize our explicit IO independently.
- Explicit restore preserves original whole-invocation authored membership and typed condition identities. Only selected producer outputs and independently verified package files may transition; parent-directory existence exceptions never grant descendant access.
- Carry the same validated manifest and typed restore proof into the exact issued plan receipt; do not establish a new baseline after awaited policy callbacks. Keep SDK/package-proven condition inputs out of mutable-only file validation.
- Retain the first native workspace only when complete admissions before/after the load match; admitted output transitions use the strict final load. Never reuse cached permissions, physical bytes or failed/cancelled admissions.

| Evidence under `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume` | Result | Limit |
|---|---|---|
| native-membership-causal-b6d108d3088e4536b054a691cb5432a3/diagnostic.trx | 9 expected failures / 9 passing controls after guarded removal of new proof | Reconstructed old mechanism, not complete historic source |
| test-preparation-issuance-causal-fa0c4196d33647b5b7ffb7bbfa1cc70f/diagnostic.trx | 2 late-policy failures / 8 passing controls | New baseline adoption reproduced |
| test-preparation-closure-causal-ac53d2885f37417ea39533fae36cbc70/diagnostic.trx | 1 expected failure | Unselected application source addition reproduced |
| test-preparation-directory-causal-3d5c88765dc8470d8f465ddc215efc6c/diagnostic.trx | 2 expected failures / 8 passing controls | Typed directory proof cannot become file-only execution input |
| restore-focused20.trx | Membership selector9, queued race1 and unselected project1 passed | Aggregate96/99; three original deadline failures remain unresolved at this snapshot |

- Focused22 with fresh-hash buffering is running/pending at payload creation. Keep this row OPEN until current fixed-head focused/full/hosted evidence and merge; do not advance the paused batch.

Additional current-session review: ordinary membership hashes native items and selected context fields but omits non-global evaluated compiler properties. An ambient property can change DefineConstants or LangVersion without changing known bytes or item membership. Extend this same row to bind complete evaluated properties and prove real normalized-loader parse-option drift refuses old certificate reuse; retain explicit override and unchanged controls. Scratch proposal exists; no causal tests or fixed-head verification claimed yet. Restore-transition semantics are under separate review against actual issuance paths; do not infer a complete arbitrary native property-function sandbox.

Evaluated-property fix evidence: membership-properties-before35.trx observed 4 expected failures and 3 passing controls. Both real normalized SDK loads proved changed DefineConstants/LangVersion parse options while the old certificate accepted drift. Full evaluated property identity, exact encoding, and duplicate context checks are installed. membership-properties-after36.trx passed all 34 selected tests, including graph/property encoding, real published certificate/override/restored-environment controls, installed package authority and physical hash controls. Full required gates remain pending. Restore-transition review retracted the speculative stale-authority claim: explicit restore intentionally drops the old certificate and freezes newly evaluated command membership; no environment sandbox or counterfactual replay is needed for this finding.

Current-session causal26 fixture review found process-global environment mutation without MSTest isolation in CompilerArgumentOwnedRetainedTests (two rows), CompilerArgumentRulesetTests (included environment row), and ValidationBundleToolsTests (three ResolveFork configuration methods). AssemblyInfo enables class-level parallelism; NativeBuildMembership captures all evaluated properties, so unique variable names do not isolate native graph captures. Old csx initial-load failure temporally overlaps the retained-parser environment actor, but exact historical causation remains unproven. Correct all instances of this fixture ownership mechanism and add deterministic ambient-property membership coverage before shipping. Current causal27 semantic/parser/move controls passed 26/26, including actual Regular/Certified csx; this is not proof of the old failure cause or full CI.
