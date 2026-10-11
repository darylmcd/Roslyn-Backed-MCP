# editorconfig-section-source-path-matching — Match editorconfig sections against the source path

**row:** `editorconfig-section-source-path-matching` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:161`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`

## Acceptance

- [ ] Disk option overlay applies each section only when its glob matches the requested source file relative to that .editorconfig file.
- [ ] Regression covers a path-qualified C# glob that should match one source directory but not another, while ordinary [*.cs] and [*] sections still apply.

## Evidence

- `SectionMatchesCSharp` accepts any section ending in `.cs` for every C# file, regardless of its path. `ParseEditorconfigCsKeys` uses that predicate for GetOptionsAsync overlay, so a path-qualified section can leak options into unrelated documents. This differs from the set-option duplicate-section writer issue.

### Source-path matcher corpus
Decidable inputs: literal header, absolute config directory and absolute source path. Pairs below use source paths relative to that fixed config directory. No filesystem existence, shell evaluation or inferred intent enters the predicate. Red=13 rejects unrelated sections; green=12 retains matching sections.
red: ["[src/*.cs]","other/A.cs"] => false
red: ["[src/*.cs]","src/nested/A.cs"] => false
red: ["[*.{cs,csx,cake}]","src/A.vb"] => false
red: ["[A?.cs]","A12.cs"] => false
red: ["[A[!12].cs]","A1.cs"] => false
red: ["[A{1..3}.cs]","A4.cs"] => false
red: ["[*.csx]","A.cs"] => false
red: ["[*.cake]","A.cs"] => false
red: ["[*.vb]","A.cs"] => false
red: ["not-a-section","A.cs"] => false
red: ["[src/*.cs]","Src/A.cs"] => false
red: ["[*.CS]","A.cs"] => false
red: ["[file\\*.cs]","file/A.cs"] => false
green: ["[src/*.cs]","src/A.cs"] => true
green: ["[*.cs]","src/A.cs"] => true
green: ["[*]","src/A.cs"] => true
green: ["[/src/*.cs]","src/A.cs"] => true
green: ["[src/**.cs]","src/nested/A.cs"] => true
green: ["[**/*.cs]","src/nested/A.cs"] => true
green: ["[*.{cs,csx,cake}]","src/A.cs"] => true
green: ["[A?.cs]","A1.cs"] => true
green: ["[A[12].cs]","A1.cs"] => true
green: ["[A{1..3}.cs]","A2.cs"] => true
green: ["[A*]","A.cs"] => true
green: ["[{src,other}/A*]","src/A.cs"] => true
Measured baseline: exact current SectionMatchesCSharp lines 174-217 compiled by PowerShell Add-Type and invoked on all tuples at head 6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4. True-as-match TP=10 FP=11 FN=2 TN=2; rejection-as-positive TP=2 FP=2 FN=11 TN=10. Required repaired true-as-match TP=12 FP=0 FN=0 TN=13. Extend before implementation approval with escaped literal wildcard, whitespace within names, malformed patterns, zero-depth ** semantics from installed Roslyn, and absolute backslash-source forms; remeasure expanded denominators.
Official implementation inspected: https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/CommandLine/AnalyzerConfig.SectionNameMatching.cs (ordinal regex, slash-prefix rules, escapes, classes, nested alternatives, numeric ranges); https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/CommandLine/AnalyzerConfigSet.cs (normalized source separators/drive letters, ordinal containment, root/later-section precedence, severity separation); https://spec.editorconfig.org/ . Installed 5.9.0 XML confirms public Parse/GetOptionsForSourcePath; NamedSections/matcher are internal, not a supported reuse seam. Match provider semantics when specification and installed Roslyn differ; do not introduce a second grammar.

## Current operator-policy hold 2026-10-10

| Item | Evidence / obligation |
|---|---|
| Disposition | Selected plan deferred: corrected last-wins reader exposes writer first-assignment/source-section provenance defects. Do not execute reader-only40000 scope. |
| Proposed writer | Public-provider unique per-assignment markers select all requested-key assignments applicable to requested source; value-span edits preserve original key/separator/trivia/comment/encoding/undo and other-only overrides. Original34-case reader corpus retained. |
| Unanswered decisions | Effects on other files sharing matching assignments; new-key placement/parallel default versus section reuse; global-config write domain versus new local-only refusal. Silence authorizes no decision. |
| Contract/scope | ADR/migration/release disposition and catalog-description companion probe required; proposed70000 three-code/three-row-detail/two-test plus docs scope is unapproved. Re-vet, native-amend and cold-review after decisions. |
| Verification | Eight public-API provenance/preamble probes establish feasibility only; service/wire/full validation remains owed. Complete proposal is durable in selected checkpoint; no implementation or global-tooling blocker claimed. |

## Operator policy decision — existing assignments

| Decision | Recorded instruction |
|---|---|
| Approved | Update applicable broad C# defaults and every later override matching the requested source; preserve assignments matching only other sources. Shared matching sections may change other files. |
| Live answer | `Use the recommended policy` to the matching-default/override question; public-contract rationale and migration note authorized. |
| Unanswered separate policies | New-key placement (canonical default before existing sections versus matching-section reuse); selected `is_global` write domain (complete global semantics versus explicit prewrite refusal with breaking migration). |
| Implementation state | Unstarted. Reader-only40000 scope remains inadmissible for the complete repair; writer companion must be natively replanned and cold-reviewed after the separate choices. Preserve34-case corpus and source-only70000 proposal; no estimate/admission claim for a wider global design. |
| Run boundary | Ten distinct initial starts already used; do not disguise cap-held work as a global dependency. |

The earlier matching-assignment question is answered. Do not infer approval of new-key placement or global refusal from that answer.

## Operator policy decisions — superseding the unanswered-policy checkpoint

| Policy | Approved instruction |
|---|---|
| Existing assignments | Update applicable broad C# defaults and all later overrides matching the requested source; preserve other-only assignments. Shared-section impact, public rationale and migration note remain approved. |
| New key without applicable assignment | Insert the canonical C# default before existing sections, preserving later overrides. Live answer: `Insert the default before existing sections (recommended)`. |
| Selected global config | Support global writes with a re-vetted design. Live answer: `Support global writes with a re-vetted design (recommended)`. Do not substitute blanket prewrite refusal. |
| Re-vet required | Derive global provider precedence, domain selection, applicable assignments and new-key placement from actual public-provider behavior. Preserve full config, original bytes/encoding/undo, and final effective-value proof. Retain the original34-case reader corpus and all writer regression obligations. |
| Admission | Original reader-only40000 scope is incomplete; source-only70000 proposal did not cover approved global semantics. Re-derive complete files, estimate, ADR/migration and companion-row dispositions; use native replan and fresh cold review. No expanded admission or implementation claim. |
| Current sequencing | Record all three approvals now. Wait for active constructor validation/delivery to settle before plan-wide replan epoch changes. Ten initial starts used; implementation remains unstarted for this run. |

All three operator policy choices are answered. Earlier unanswered-policy statements are historical and superseded by this table.

## Approved global design — source-only re-vet checkpoint

| Field | Evidence / pending work |
|---|---|
| Artifact | `C:/Users/daryl/.codex/scratch/roslyn-20261009-resume/editorconfig-approved-global-design.result`; Root read all20780 bytes, parsed exact result sentinel/JSON; proposed stanza4930 UTF-8 bytes. |
| Provenance | Parent44a941c91a27a0265b48a91a5a465e90df8fd31a; editorconfig source/tests unchanged from337fc2cde10eca3f591cc016c146dee7b893e26a. Independent deepener used live read-side MCP and closed only its own workspace with drainProcessesfalse. No fresh tests/builds/corpus measurements. |
| Existing approvals | Applicable existing matching assignments, ordinary new-key C# default before existing sections, and complete selected-global write support remain approved. |
| Newly derived global placement | Pinned Roslyn5.9 provider accepts only absolute named global sections; glob sections are invalid. New global key needs a separate choice: global preamble default affects all project sources, or exact requested-source section affects one source. Root asked asynchronously; answer pending. Ordinary C# default approval is unchanged. |
| Provider source | `https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Compilers/Core/Portable/CommandLine/AnalyzerConfigSet.cs`:444-460 validity; :165-176 exact path matching; :510-566 global-level conflicts. Root independently read the pinned source. |
| Complete behavior | Public-provider full-file provenance and original-key final-value proof; preserve original byte spans/encoding/EOL/EOF/undo; update applicable existing assignments; never silently edit another config or change global_level to defeat external precedence. Specific unrepresentable conflicts must refuse atomically, without blanket global refusal. Preserve literal34-case reader corpus and full writer/input-validation/dual-wire-era obligations. |
| Scope ownership correction | Source result includes Root-owned metadata among proposed file arrays; this grants no executor write permission. Actual proposed executor: five code files, two test files, four ADR/index/product-contract/fragment docs. Root owns item/corpus/policy-supersession/stanza updates before admission and backlog closure/deletion during reconciliation. Separate these before native application/cold review. |
| Comment clarification | Proposed helper-comment repairs refer to scoped EditorConfigService comments. Independent stale SourceFileEncoding comment is tracked separately as sourcefileencoding-editorconfig-transaction-comment; it is not an unlisted executor edit. |
| Estimate / admission | Conservative complete source-derived estimate90000, above installed Rule5 marker80000. No native90000 execution admission attempt, no replan and no expanded scope approval yet. Re-vet estimate/files after global-placement answer; retain honest full scope and tracked remainder. |
| Sequencing | Active constructor validation epoch remains intact. Native replan and fresh cold review occur only after that lifecycle settles. Ten initial starts used; editorconfig implementation remains unstarted. |

Earlier claims that the original three policy answers were pending are historical. The newly derived global new-key placement is a distinct decision, not a revocation of global write support.

## Operator decision — new keys in global configs

| Decision | Recorded instruction |
|---|---|
| Approved | Insert a global preamble default before existing named sections. It applies to all project sources; later exact-source sections retain precedence. |
| Live answer | `Global preamble default (recommended)` to the separate global-placement question. |
| Ordinary config | Preserve the approved canonical C# default section before existing sections. |
| Existing assignments | Preserve the approved update of every applicable existing matching assignment; retain other-only assignments and disclose shared effects. |
| Ownership | Keep writes within the selected config; no silent edits of other configs or global_level escalation. Complete original-provider final-effective-value proof and specific atomic refusal of genuinely unrepresentable external precedence remain required. |
| Remaining work | Final source-only scope/estimate re-vet with Root metadata separated from executor files; then native replan, actual admission and fresh cold review after the active constructor lifecycle settles. No implementation or admission granted by this note. |

All current operator policy questions are answered. Earlier global-placement pending statements are historical and superseded.

# Final approved editorconfig proposal — preadmission record

| Field | Recorded disposition |
|---|---|
| Source-only artifact | C:/Users/daryl/.codex/scratch/roslyn-20261009-resume/editorconfig-approved-global-final-design.result;22582bytes;SHA2568d03a529edac5bc317c771a096a46f68b0da15266d87359bda894de8e39f76aa. Root read whole artifact and parsed sentinel/JSON. |
| Proposed executor scope | 5code/2tests/4docs; fanout5; complete estimate90000. Root metadata stays outside executor arrays. Proposed stanza5034UTF8 bytes. |
| Policies | Every current policy choice approved, including global new-key project-wide preamble before named sections; ordinary C# default-before-sections unchanged. |
| Admission | No native90000 attempt/replan/cold approval/implementation. Preserve active constructor epoch; apply only after its lifecycle settles with native approval/history handling and fresh cold review. Do not lower estimate, omit global/input-validation/byte/undo obligations or claim historical corpus measurements fresh. |

## Preserved reader corpus — historical measurements

Preserved exact existing 34-case Validation row below. Its measurements are historical, not remeasured here; fresh service/corpus evidence is mandatory.
| Validation | Inputs: literal header, absolute config directory/source path; tuples give relative source paths. Red=13 false: [["[src/*.cs]","other/A.cs"],["[src/*.cs]","src/nested/A.cs"],["[*.{cs,csx,cake}]","src/A.vb"],["[A?.cs]","A12.cs"],["[A[!12].cs]","A1.cs"],["[A{1..3}.cs]","A4.cs"],["[*.csx]","A.cs"],["[*.cake]","A.cs"],["[*.vb]","A.cs"],["not-a-section","A.cs"],["[src/*.cs]","Src/A.cs"],["[*.CS]","A.cs"],["[file\\*.cs]","file/A.cs"]]. Green=12 true: [["[src/*.cs]","src/A.cs"],["[*.cs]","src/A.cs"],["[*]","src/A.cs"],["[/src/*.cs]","src/A.cs"],["[src/**.cs]","src/nested/A.cs"],["[**/*.cs]","src/nested/A.cs"],["[*.{cs,csx,cake}]","src/A.cs"],["[A?.cs]","A1.cs"],["[A[12].cs]","A1.cs"],["[A{1..3}.cs]","A2.cs"],["[A*]","A.cs"],["[{src,other}/A*]","src/A.cs"]]. Measured b6fea40 baseline original25 TP10 FP11 FN2 TN2; installed Roslyn5.9 TP12 FP0 FN0 TN13. Extended7: false=[["[A\\*.cs]","A1.cs"],["[my file.cs]","myfile.cs"],["[A[12.cs]","A1.cs"],["[**/*.cs]","A.cs"]]; true=[["[A\\*.cs]","A*.cs"],["[my file.cs]","my file.cs"],["[**.cs]","A.cs"]]. Windows inputs JSON=[{"configPath":"C:\\r\\.editorconfig","sourcePath":"C:\\r\\src\\A.cs","header":"[src/*.cs]","expected":true},{"configPath":"C:\\r\\.editorconfig","sourcePath":"C:\\r\\other\\A.cs","header":"[src/*.cs]","expected":false}]. Measured all34 baseline TP14 FP16 FN2 TN2; public-provider TP16 FP0 FN0 TN18; disagreements=0/34. In-process predicate+provider corpus probe, not repaired-service/test-suite proof. Preserve literal pairs in own stanza/tests before close. Verify paths/case/nesting; arbitrary/known/raw-severity/Unicode/collision keys; =/:, malformed headers, repeated sections/assignments; ancestor/child/root/unset; global_level/ties/is_global=false; stale snapshot change/removal; separate global fallback; released handles (explicit unsupported-platform Inconclusive); broad-C# writer. Observe service regression red then green; compile_check, EditorConfigServiceTests, serialized exact-head just ci and hosted CI_POLICY checks. |
