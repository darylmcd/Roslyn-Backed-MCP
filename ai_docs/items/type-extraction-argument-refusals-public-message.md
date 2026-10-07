# type-extraction-argument-refusals-public-message — safe extraction argument contracts

**row:** `type-extraction-argument-refusals-public-message` · **pri:** `Medium` · **size:** `L` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `src/RoslynMcp.Roslyn/Services/RecordFieldAdditionService.cs`
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs`
- `src/RoslynMcp.Roslyn/Helpers/IdentifierValidation.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`
- `tests/RoslynMcp.Tests/ExtractSharedExpressionTests.cs`
- `tests/RoslynMcp.Tests/RecordFieldAdditionImpactTests.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`
- `tests/RoslynMcp.Tests/ExtractMethodTests.cs`
- `tests/RoslynMcp.Tests/ExtractMethodFormatRegressionTests.cs`
- `tests/RoslynMcp.Tests/ExtractMethodThisExclusionTests.cs`
- (new) `tests/RoslynMcp.Tests/ExtractionArgumentRefusalWireTests.cs`

## Acceptance

- [ ] Publish fixed safe corrections with actual caller parameters; preserve internal exceptions, redacted inner exceptions, established BCL identities, record null guards and destination/accessibility defaults.
- [ ] Reject null memberNames before Count; validate both extraction spans before indexing/arithmetic without cross-line offsets, overflow or clamping.
- [ ] Preserve method equal-span refusal; shared empty/type/namespace selections become named caller refusals. Use one value-expression predicate in example validation and matching scan; compile same-name type/value previews.
- [ ] Share lexical identifier validation; refuse malformed/reserved method/helper names before preview mutation. Preserve stricter type/rename policy and compiled Unicode/verbatim/contextual member controls.
- [ ] Migrate invalid legacy fixture coordinates while retaining semantic assertions. Test actual services and dual-era raw wire; retain observed old-code failures and source/DLL-bound final green evidence.
- [ ] Deliver ADR 0018 and a major migration fragment; pass required hosted Windows/Linux, SDK-floor, release/package/audit validation and cold review before closure.

## Evidence

- Historical survey: 12 argument construction sites at f7b33b85; superseded incomplete acceptance is re-derived by the current sealed stanza.
- Immutable source base `6599be65a67de3ba24065c980118f6c4e6cc00a5`: `if (string.IsNullOrWhiteSpace(methodName))`, `if (string.IsNullOrWhiteSpace(helperName))`, and `SyntaxFactory.Identifier(methodName)` / `SyntaxFactory.Identifier(helperName)` permit malformed nonblank names.
- Current-session actual producer probes apply malformed/reserved names: method parse errors 28/17, helper 16/30, CompileCheck=false. Unicode/verbatim controls compile; contextual async also compiles, refuting blanket contextual rejection.
- Base service/wire regressions: 49 failures/15 passes; exact four-production-file base probe: 8 failures/1 preserved partial-overlap control. Shared type nodes can reach InvalidCastException or false capture-set disagreement; null memberNames reaches production wire as InternalError in both eras.
- Scoped1: 13 failures/144 passes exposed legacy out-of-line fixture coordinates. Companions are required to preserve the same extraction semantics under bounded coordinates.

## Context

- Family policy: public/redacted/internal classification and Host normalization; reuse existing Core carriers.
- Sealed plan: `ai_docs/plans/20261004T123100Z_backlog-remediate/plan/type-extraction-argument-refusals-public-message.md` amendment 4 owns exact scope and validation.
- Keep unrelated PreviewStore provenance/truncation loss on its existing row. Do not infer a solution/version race without a live concurrency failure.

Current-session async-name proof: full private-env solution restore and explicit nonincremental Release build passed with zero warnings/errors. Actual method/helper await probes both fail compilation (CS4003/CS1061), including shared helper calls from another type/file. Preserve contextual names by parsed escaped tokens with unchanged ValueText for declarations/unqualified calls and token.Text for qualified ParseExpression. Self-contained async fixture stays in already scoped wire test; sample fixture has no async enclosing method.

Actual immutable-five-production-file base path probe: malformed NUL destination reaches ArgumentException with ParamName path; both actual production wire eras report InvalidArgument/ArgumentException, internal path guidance and whole-tool schemaHint. Three new expectations fail on old code. Correct caller-owned destination canonicalization with named newFilePath redaction retaining inner/BCL identity; preserve default/internal paths and boundary markers. This is the same already selected named argument/path contract, not a new category migration.

Additional exact five-source base long-path build passed; four expectations fail old code. Actual Type and Namespace Windows long destinations normalize to InternalError with no public BCL identity in both eras; the correct caller-path wrapper intentionally migrates to InvalidArgument/ArgumentException retaining PathTooLongException as inner. ADR must distinguish this major migration from NUL category/identity preservation. No unproven NotSupported catch is introduced.
