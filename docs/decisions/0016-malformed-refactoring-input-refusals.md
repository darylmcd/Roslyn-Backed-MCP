# ADR 0016: Refuse malformed refactoring input before dispatch

Status: Accepted, 2026-10-06. Publish in the next major release.

## Context

The published tools/call error envelope is observable even for malformed requests. Null operations escape through a second null dereference in the composite wrapper; malformed partition entries reach dereference or lookup failures instead of caller validation.

## Decision

Reject null operations inside the composite try with a typed public argument refusal naming operations. Wrap it through the existing PublicInvalidOperationException convention with a bounded operation index and safe guidance.

Reject null partition entries and null or blank member names before dereference or lookup, with safe PublicArgumentException guidance naming partitions. The actual producer and Host raw-wire matrix establishes these observable changes on protocol 2025-11-25 and 2026-07-28:

| Malformed input | Previous envelope | New envelope |
|---|---|---|
| symbol_refactor_preview operations: [null] | InternalError from a private NullReferenceException; no exceptionType | InvalidOperation / InvalidOperationException |
| split_service_with_di_preview null partition entry | InternalError from a private NullReferenceException; no exceptionType | InvalidArgument / ArgumentException |
| split_service_with_di_preview null member name with a valid source type | InvalidArgument / ArgumentNullException from dictionary insertion | InvalidArgument / ArgumentException |
| split_service_with_di_preview blank member name with a valid source type | InvalidOperation / InvalidOperationException from missing-member lookup | InvalidArgument / ArgumentException |

These observable corrections ship in a major release under the release policy. No entry is removed, renamed or deprecated; no security exception to the deprecation policy is claimed.

## Migration and validation

Clients must remove null operations or supply complete rename, edit or restructure objects. Supply non-null partition objects with TypeName and nonblank MemberNames; treat the refusals as request corrections rather than internal failures or stale workspace errors. Update malformed-input category and exceptionType handling using the table above.

The dual-era raw-wire matrix covers the actual producer through Host tools/call, isError, era-specific resultType, safe guidance, application metadata, parameter hints and absence of private types, paths and stack traces. Other composite failures retain their categories; cancellation propagates and the three null record-field inputs retain ArgumentNullException identity. Structural matching, valid previews, regex execution and public regex guidance remain unchanged.
