# condition-preparation-preserve-native-xml-semantics — condition-preparation-preserve-native-xml-semantics

**row:** `condition-preparation-preserve-native-xml-semantics` -- **pri:** `High` -- **size:** `S` -- **deps:** ---

## Problem
- Native condition preparation reloads a cloned serialization before instrumentation.
- Independent evaluation of the same authored project yields `Value = A < B & C`; current instrumented evaluation yields an empty value for both same-line and multiline XML.
- The fixture uses a CDATA property value inside a true Exists condition. Inspected native DeepClone invokes ProjectPropertyElement.Clone; its inherited CopyFrom copies only a sole Text child and omits CDATA. ProjectPropertyElement has no overriding CopyFrom.
- Preserve native XML semantics rather than weakening the independent oracle or normalizing away differences.

## Anchors
- `src/RoslynMcp.Roslyn/Services/NativeConditionEvidence.cs:66` --- PrepareConditions native root preparation.
- `src/RoslynMcp.Roslyn/Services/NativeConditionEvidence.cs:74` --- DeepClone().RawXml serialization before safe reload.
- Proposed `tests/RoslynMcp.Tests/NativeConditionSerializationTests.cs` --- independent original/native-instrumented comparison and readonly serialization controls.
- `tests/RoslynMcp.Tests/NativeConditionProtocolTests.cs` --- existing import/cache/condition protocol controls.

## Acceptance
- [ ] Confirm the inspected clone CDATA-loss mechanism against the executing native version and corrected runtime result.
- [ ] Preserve authored property values, item metadata, duplicate declarations, selected Choose branches, same-line origins, and source file bytes.
- [ ] Support readonly SDK roots without invoking prohibited Save APIs or renewing source bytes from disk.
- [ ] Preserve cache object ownership, exact condition evidence/occurrence matching, cancellation, and all authority checks.
- [ ] Observe independent semantic controls fail on old production and pass on the correction; run the existing native cache/condition controls unchanged.
- [ ] Compare exact native identity output on a fixed SDK graph; investigate differences without blanket normalization.

## Evidence
- Parent run217 baseline: 100 controls, 98 passed, 2 failed at the independent Value comparison (expected `A < B & C`, actual empty).
- Both failing cases are InstrumentedRoot_PreservesIndependentNativeSemanticsAndSourceBytes, differing only in same-line XML formatting.
- Receipt: `%USERPROFILE%/.codex/scratch/roslyn-prerequisites-20261016-resume/condition-outerelement-proposal/abba-d6e1d3881d7e46cdb307c872378395e1/sdk-1-baseline/run.log`.
- Candidate-only verification is pending; no performance or root-cause success claim follows from the baseline result.
- Static native source evidence: `ProjectElementContainer-descriptor-review.cs:575` recursively clones property elements; `ProjectElement-allocation-review.cs:365` copies sole Text nodes only; `condition-outerelement-proposal/ProjectPropertyElement.cs` has no CopyFrom override and creates empty properties before inherited copying. These files are under the same absolute session scratch root.

- Candidate221: all 100 controls passed; exact SDK identity comparison differs and is under field-level investigation. No equivalence or performance acceptance claimed.

2026-10-17 candidate221: same 100 runtime controls passed, including both independent CDATA cases red on baseline217. Guarded source restoration verified SHA17DEA81D595FF17BF40F2410F987A114C1177E4940340296F397D8A47D87A367. Subsequent exact SDK identity comparison failed; decoding isolates one PathEvidence declaration ordinal and the evaluated-property digest. Investigation and metadata/mixed-content closure controls pending; candidate not installed, no performance claim. Receipt condition-outerelement-proposal/candidate-only-98a59d54bd944afda13d153d3a79c646 in session scratch.

## Current-session mechanism and qualification

| Mechanism/evidence | Result |
|---|---|
| Native ProjectElement.CopyFrom | Copies sole Text child; property and metadata factories otherwise start empty, dropping CDATA/mixed/nested content |
| Native empty attribute metadata | NuGet.targets148-151 Version="" exists in original and in-memory clone; clone.RawXml reparse loses its node |
| Independent original construction sequence | 399 nodes; OuterElement reparse exactly399 with complete element type/name order preserved; clone reparse398 |
| Same-process evaluated properties | No differing names/values; earlier cross-process property digest mismatch does not prove a serialization difference |
| Old semantic controls217 | 2 failures, expected nonempty CDATA Value became empty |
| Candidate221 | 100/100 existing and new controls passed |
| Expanded old/new closure224 | Old1pass10fail; corrected11pass |
| Final controls | Add empty/nonempty attribute metadata, exact raw metadata representation, complete ordinal sequence; 226 old1pass12fail; corrected13pass, including complete sequence and metadata controls |

- Production correction: use the existing root's public OuterElement and the existing safe in-memory reader reload; preserve exact cached root ownership and all authority checks.
- Do not normalize native identity differences: the ordinal difference is corrected fidelity to independent original native indexing.
- No latency improvement claimed. This is a correctness fix with original semantic regression evidence.
