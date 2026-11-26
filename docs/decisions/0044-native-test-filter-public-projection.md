# ADR 0044 — Remove caller filter text from test-run responses

- **Status:** Accepted (2026-11-25)
- **Deciders:** repository maintainer (approved A3 design)
- **Scope:** `native-test-filter-public-projection`
- **Refines:** ADR 0041, Rule 2
- **Supersedes:** nothing
- **Superseded by:** nothing

## Context

The runner passes native Microsoft.Testing.Platform filters through `--treenode-filter`. The public projection previously recognized only `--filter`. Both options also lost complete-literal context when capture retained its final 12,000 characters or the TRX parser shortened a message, stack trace, or display name. Removing those echoes after shortening could leave part of the caller's filter visible.

Masking text before recognizing filesystem paths would break existing path privacy. For example, masking `C` in a Windows drive path prevents the path recognizer from identifying the original drive. Replacing a known target path with an unmasked metadata basename could also restore caller text that had already been masked.

## Decision

- Recognize both option names, split and equals forms, with ordinal case-insensitive comparisons. Keep literal child-process arguments unchanged. Public option values remain `<redacted>`.
- Find every complete literal occurrence in original text, including overlapping occurrences. Replace its UTF-16 positions with `*` without changing length.
- Keep the original bounded output pump. Each filtered stream has one collector on its existing decoded-chunk callback. Retain at most the longest literal minus one original characters, their discovered mask bits, and the existing output limit of safe text. Complete only after the pump, including decoder flush, finishes successfully.
- Carry internal, nonserialized safe companions through the existing records. Preserve raw diagnostics and original cut coordinates, counts, classifications, and truncation markers. Record equality and hashing include this internal state; `with` copies it.
- Recognize paths in a local raw/safe pair. Preserve the existing normalization and replacement order; copy masks from actual surviving source positions. Generated markers and separators are never treated as caller source. Known-target basename replacements transport masks from each actual occurrence into the existing canonical spelling.
- Do not reconstruct unread bytes, arbitrary externally shortened or encoded filter fragments, or path roots removed before the existing projector sees them. Preserve cancellation, early-kill scanning, stream draining, and process ownership.

## Release classification

This is a 5.x minor `Fixed` change under Rule 2. The changed datum is leaked caller filter text. Public field sets and types, unrelated messages and error categories, counts, pagination, and generated markers remain unchanged. It does not qualify under Rule 1 alone because callers could observe the earlier public filter value. ADR 0041's Rule 2 explicitly permits this removal without a major release or deprecation window.

Qualification covers the connection-scoped `2025-11-25` protocol and the request-scoped `2026-07-28` protocol, full and compact `test_run` responses, and embedded test-run serialization. Regression tests exercise real runner capture and parser cuts, raw diagnostics, original child argv, overlap, path recognition, maximum accepted retention limits, record copies, and public property sets.

## Migration

Consumers must not recover filters from public command arguments or diagnostics. Keep the original request filter if it is needed locally. Both filter options now publish `<redacted>` as their value; matching source positions in diagnostic text publish asterisks, including fragments retained by server-owned cuts. Existing path markers and truncation text retain their meaning. Internal diagnostic consumers continue to receive the original bounded strings.

## Consequences

Public diagnostic text intentionally loses caller filter content. Internal records retain bounded companion strings and their equality reflects that state. No public field, interface, constructor restriction, package dependency, protocol version, or output limit is added.
