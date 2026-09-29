# ADR 0011: Bound editorconfig writes to the loaded workspace

Status: Accepted, 2026-09-29.

## Context

`set_editorconfig_option` is a stable write tool. Its applicable `.editorconfig` may
be an ancestor of the loaded solution. Previously, the tool could edit that file
even when its physical path was outside the loaded workspace, affecting other
projects and bypassing the workspace boundary used for write attribution.

## Decision

Require the selected `.editorconfig` to resolve within the loaded workspace's
physical root before writing. The tool refuses an outside target without a
partial write. Ancestor configuration remains readable and watchable, so
diagnostics still reflect settings supplied from outside the workspace.

Successful in-workspace writes use the workspace's coordinated write path to
invalidate relevant snapshots and distinguish the server's own write from an
external edit. No compatibility switch permits an outside write.

## Compatibility and migration

This changes the behavior of a stable tool for clients that previously edited
an ancestor `.editorconfig`; ship it in the next major release. A refused call
must be handled as a boundary error. To edit the ancestor file, use an
operator-authorized editor outside this tool. To keep edits inside the loaded
workspace, create or select an applicable `.editorconfig` under its physical
root and call `set_editorconfig_option` again. Keep any intended ancestor
inheritance in mind when placing the in-workspace file.

## Consequences

- A workspace-scoped request cannot mutate configuration shared by sibling
  projects outside that workspace.
- Consumers relying on ancestor writes need an explicit migration path.
- Reads and file watching continue to include applicable ancestor settings.
