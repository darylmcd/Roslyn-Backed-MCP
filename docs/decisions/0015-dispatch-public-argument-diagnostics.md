# ADR 0015: Safe public dispatch and argument diagnostics

Status: Accepted 2026-10-06

## Context

Several dispatch refusals throw ordinary `ArgumentException` despite containing corrective guidance. The host's generic redaction removes that guidance, while special message-sniffing branches couple project and discovery diagnostics to exception prose.

Ambiguous solution discovery already throws `PublicArgumentException`, but its public message includes candidate absolute paths. Shared project filtering and the edit-target pin also construct messages containing submitted values or private paths.

## Decision

Use the existing Core `PublicArgumentException` contract for missing resolved workspace identity, misspelled `workspace_load` path arguments, unknown project filters, and divergent edit-target pins. Each producer supplies fixed corrective text with its real parameter name and no submitted values or private paths.

Ambiguous discovery publishes the candidate count and instructions to call `workspace_load` with an explicit operator-supplied solution or project path. Candidate paths remain outside the public error envelope. Remove the handler's obsolete candidate-solution prose-sniffing branch. Retain its safe project-name fallback for other producer families that still throw ordinary argument exceptions; migrated producers bypass that fallback through the public marker.

Project matching, workspace binding order, path comparisons, and refusal-before-write behavior remain unchanged. Tool errors retain `InvalidArgument`, the BCL `ArgumentException` wire identity, `schemaHint`, request metrics in `_meta`, and the negotiated protocol era's result framing.

## Compatibility and migration

This is a breaking public diagnostic correction under the [release policy](../release-policy.md). Ship it in a major release. The security exception omits the normal minor-release deprecation window because retaining candidate paths would preserve disclosure of sensitive implementation detail.

Clients must stop parsing candidate paths or project names from error prose. Obtain an explicit solution or project path from the operator, call `workspace_load`, and retry with the returned `workspaceId`. Use `workspace_status` to inspect available project names, then correct or omit `projectName`. Correct path argument spelling using `schemaHint`; reissue divergent edit paths without traversing a link through `..`.

Treat `category`, parameter/schema guidance, and protocol framing as the structured failure contract. Human correction text may change. The initiative's breaking migration fragment is consumed into `CHANGELOG.md` at release cut.

## Validation

Regressions exercise the real discovery resolver and raw wire frames in the legacy 2025-11-25 and modern July 2026 protocol eras. They cover private-path non-disclosure, misspelled path refusal without elicitation or dispatch, project filters containing malicious free text, null/empty workspace identity, and divergent target pins without writes. Generic argument controls retain redaction; matching project filters, pin agreement, and workspace recovery retain successful behavior.
