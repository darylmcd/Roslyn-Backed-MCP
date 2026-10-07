# ADR 0019: Suppression public argument diagnostics

Status: Accepted 2026-10-07 for the next major release.

The stable suppression tools currently redact server-authored correction text along with arbitrary exception messages. The severity producer also accepts blank and unsupported values, and dereferences null before dispatch; these behaviors are observable public contracts that need correction.

Use the existing Core public-message exceptions at the suppression producer. Publish only fixed server-authored guidance and the actual parameter name; never echo a supplied diagnostic identifier, severity token, source path, or lower-layer exception text. Keep internal dependency and pinned-path guards and the host's lower-layer redaction policy.

| Input | Previous behavior | Decision |
|---|---|---|
| Missing diagnostic identifier or verify/widen source path | Generic `InvalidArgument` text, `ArgumentException` identity | Publish the fixed required-field correction; retain category and BCL identity |
| Non-positive pragma line | Generic `InvalidArgument` text, `ArgumentOutOfRangeException` identity | Publish the positive 1-based correction; retain category and BCL identity |
| Null severity | `InternalError` after null dereference | Refuse as `InvalidArgument`, `ArgumentException`, parameter `severity`, before editorconfig dispatch |
| Blank or unsupported severity | Dispatch to editorconfig and potentially write the supplied value | Refuse as `InvalidArgument`, `ArgumentException`, parameter `severity`, before dispatch or mutation |
| Supported severity | Trim and dispatch the token | Accept `error`, `warning`, `suggestion`, `silent`, `none` case-insensitively; retain trimmed token casing |

The severity acceptance correction ships in a major release because clients may rely on the prior acceptance or null failure category. Retaining those paths during deprecation would continue accepting invalid diagnostic configuration; the correction changes acceptance immediately at the major boundary. The generic editorconfig tool keeps its arbitrary key/value and reset semantics.

Clients must send an advertised severity value and interpret refusals by category and schema parameter, rather than matching message prose. No request fields, registered tool names, or catalog counts change. Tool and parameter descriptions must advertise the same supported values.

`SuppressionArgumentRefusalWireTests` covers the actual production service and host composition under protocols `2025-11-25` and `2026-07-28`: tool-result `isError`, one text-content error payload, corrective message, category, schema parameter, BCL identity, era-specific `resultType`, sensitive-input redaction, and unchanged file bytes/inventory. Service tests separately prove invalid severity never dispatches and supported trimmed/case variants preserve successful payloads.
