# ADR 0018: Extraction selections and safe public argument diagnostics

- Status: Accepted
- Date: 2026-10-07
- Release classification: major
- Supersedes: incidental coordinate failures and type-position shared-expression matching

## Context

The stable extraction tools previously exposed generic argument corrections, internal coordinate parameter names, or successful previews for columns outside their selected line. A null member list reached the actual type-extraction service through the SDK binder and became an internal error.

Roslyn type and namespace names inherit expression syntax. Shared-expression extraction therefore selected type nodes and matched them against value expressions, producing internal casts or false capture-set disagreements.

## Decision

Publish fixed corrective guidance through the existing public argument carriers. Share one lexical validator while retaining the stricter existing type/rename policy. Extraction member names reject malformed names and reserved keywords, accept valid contextual identifiers, and emit parsed escaped tokens where grammar could reinterpret a contextual name; qualified helper calls use that token spelling. Retain internal-invariant exceptions and redact malformed destination-path failures with the actual caller parameter and underlying exception.

Validate both extraction endpoints against the resolved source before indexing or adding offsets: lines are 1-based and within the text; columns are 1-based and at most the selected line length plus one. Endpoints must be ordered; never clamp or interpret an oversized column as an offset into later lines. Bound offset arithmetic before performing it.

For shared-expression extraction, refuse an empty selection and selections identifying a type or namespace. Use the same syntax-context and semantic-binding predicate for the selected expression and structural matches; value identifiers remain eligible even though their syntax inherits TypeSyntax. Method extraction retains its equal-span downstream refusal. A nonempty partial overlap that resolves to a value expression retains the existing fallback; type, namespace, and no-expression selections receive caller corrections.

## Observed contract changes

Measured against immutable base 6599be65a67de3ba24065c980118f6c4e6cc00a5 with actual services and production-parity raw MCP frames in both supported protocol eras.

| Input | Previous observation | New contract |
|---|---|---|
| Null type-extraction memberNames | NullReferenceException; InternalError on service and both wire eras | InvalidArgument; ArgumentNullException; named memberNames |
| Invalid newTypeName; empty memberNames | ArgumentException; generic public correction | InvalidArgument; ArgumentException; fixed identifier/member guidance |
| Record metadata/name/type null | InvalidArgument; named ArgumentNullException | Preserved |
| Record metadata/name/type blank; ordinary class selected | ArgumentException; generic public correction | Same category and identity; required-input/record-kind guidance |
| Namespace destination outside the project | ArgumentException; generic correction, with paths only in server detail | Same category and identity; inside-source-project guidance without paths |
| NUL destination path in type extraction or namespace relocation | BCL ArgumentException with path identity; InvalidArgument on both wire eras | Same category and identity; named newFilePath, underlying exception retained |
| Windows destination beyond the normalization length limit | PathTooLongException; InternalError on both wire eras for type extraction and namespace relocation | InvalidArgument; ArgumentException with named newFilePath and underlying PathTooLongException |
| Reversed extraction endpoints | ArgumentException with no caller parameter | Same category and identity; named startLine/startColumn or example counterpart |
| Zero/negative/past-end lines | Incidental ArgumentOutOfRangeException with index, or unnamed reversed-range ArgumentException | InvalidArgument; ArgumentOutOfRangeException; actual line parameter |
| Column zero/negative | Method previews sometimes succeeded; shared extraction sometimes reached InvalidOperation | InvalidArgument; ArgumentOutOfRangeException; actual column parameter |
| Integer extrema in columns | Arithmetic wrap; ArgumentOutOfRangeException with start/end | InvalidArgument; ArgumentOutOfRangeException; actual column parameter |
| Columns beyond their line | Cross-line offsets could reach downstream selection logic, succeed, or fail incidentally | InvalidArgument; ArgumentOutOfRangeException; actual column parameter |
| Method equal span | InvalidOperation; no complete statements | Preserved |
| Shared empty span or nonempty var/int/string type selection | Internal InvalidCastException while replacing TypeSyntax with an invocation | InvalidArgument; ArgumentException; named exampleStartColumn |
| Shared namespace or no-expression selection | InvalidOperationException; InvalidOperation | InvalidArgument; ArgumentException; named exampleStartColumn |
| Value identifier sharing a type name | False capture-set disagreement with type-position matches | Valid value matches produce a compiling preview; type/namespace matches excluded |
| Malformed method/helper name or reserved keyword | Preview/apply succeeded but emitted source had parse errors and failed compilation | InvalidArgument; ArgumentException; named methodName/helperName before preview |
| Contextual member name async | Produced compiling previews | Preserved symbol name and compilation; grammar-safe spelling |
| Contextual member name await in async callers | Preview/apply produced CS4003 and CS1061; compilation failed | Escaped declaration and calls, including cross-file calls; ValueText remains await |
| Unsupported helper accessibility | ArgumentException naming internal accessibility | Same category and identity; helperAccessibility and supported choices |

## Consumer migration

Ship this correction in a major release. The safe-message changes and new selection refusals are observable behavior changes even where the BCL exception identity stays the same.

- Supply a non-null, nonempty memberNames list and valid identifiers. Type/rename policy still requires verbatim contextual keywords; extraction method/helper names retain valid contextual names such as async and await through grammar-safe spelling. Unicode and verbatim identifiers remain supported.
- Calculate each column relative to its own line. The end coordinate is exclusive, and a line-length-plus-one endpoint remains valid.
- Handle InvalidArgument using the advertised caller parameter and corrective message; stop depending on incidental index/start/end identities, arithmetic wrap, or accepted cross-line offsets.
- Select a nonempty runtime value expression for shared extraction; type and namespace names are not helper bodies.
- Supply record metadata for a record class or struct and a destination file strictly inside the source project. Null/blank namespace destinations keep the source path, and blank helper accessibility keeps the private default.

Both 2025-11-25 and 2026-07-28 retain one text-content error envelope with isError=true, normalized BCL exceptionType, and the real tool schema hint. Only the newer era emits resultType=complete. Public messages contain no submitted paths, hostile names, or accessibility values.

## Validation

Service regressions were observed failing before repair, including nonempty type selections and the same-name type/value scan collision. Regression coverage includes null/blank distinctions, coordinate bounds and extrema, reversed/equal spans, valid line ends, defaults, Unicode/verbatim identifiers, and compiling value-identifier previews.

ExtractionArgumentRefusalWireTests exercises actual services through ProductionParityMcpHarness in both eras and pins raw frames, category, schema guidance, normalized exception identity, era shape, and non-disclosure. Required hosted Windows/Linux validation, SDK-floor, release/package, and audit gates remain mandatory before landing.
