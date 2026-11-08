# ADR 0033 — Composite preview source fingerprints

- **Status:** Accepted (2026-11-07)
- **Deciders:** repository maintainers
- **Scope:** composite preview tokens (in-memory and persisted under `ROSLYNMCP_PREVIEW_PERSIST_DIR`) and their redemption through `apply_composite`
- **Supersedes:** nothing. Closes the "content drift" residual risk recorded in [ADR 0032](0032-persisted-composite-token-workspace-identity.md).
- **Superseded by:** nothing

## Context

A composite preview stores the post-state of every file it will write (`CompositeFileMutation`) and
nothing about the files as they are on disk. Redemption validated the token's lifecycle and, from ADR
0032, the workspace identity, but it never looked at the target files. Two earlier changes removed
the only guard that happened to cover drift:

- On one host, the workspace-version check was removed from composite redemption (the version counts
  one session's reloads and is not a content check).
- Across hosts, ADR 0032 made the version meaningless by design.

So a file edited between `*_preview` and `apply_composite` was silently overwritten, whether the edit
came from a person, another agent, or a second host that shares the persist directory.

## Decision

`CompositePreviewStore.Store` records a fingerprint of every distinct target file, and redemption
refuses when a target no longer matches. `CompositeFileMutation` and the tool contracts are
unchanged, so all six composite producers are covered without edits.

| Item | Rule |
|---|---|
| Fingerprint | Per distinct target path (platform path comparer, first occurrence wins): `Exists` plus a SHA-256 of the raw file bytes, absent when the file does not exist. Raw bytes, so BOM, encoding and line-ending changes count as edits. |
| Created / deleted files | A created file fingerprints as absent and must still be absent at apply. A deleted file fingerprints as present with its hash. |
| Already-applied state | A target already in the state the mutation would produce (a written file whose text equals the mutation's content, a deleted file that is gone) is not drift. Overwriting it destroys nothing, and it keeps the in-memory retry after a partial apply working. |
| Unreadable target | A target that exists but cannot be read (a directory in its place, access denied, a sharing violation) cannot be fingerprinted: `Store` refuses the preview with an authored message that names the file only. At redemption an unreadable target counts as drift and is refused with the token and payload preserved. |
| Persisted fingerprint paths | Untrusted input. A format 3 record whose fingerprints name a path that no mutation writes is unreadable, so a record cannot make a host read and report on an arbitrary file. |
| Check point | Inside the store, before the token is consumed. Persisted path: under the claim lock, after identity resolution, before the payload is moved. In-memory path: before the entry is returned. A refusal therefore writes nothing and leaves the token redeemable, like every ADR 0032 refusal. |
| Refusal | `PreviewSourceChangedException` (a `PreviewTokenStaleException`, category `PreviewTokenStale`, retry-able). The envelope names the changed file by name only (never the absolute path or the token) and says to restore the file or re-issue the paired `*_preview` call. |
| Storage format | Persisted records move to `FormatVersion` 3 with a `SourceFingerprints` list. Records of format 1 and 2 still read, with no fingerprints, and are redeemed unchecked. A format 3 record must carry a well-formed list; a malformed one is unreadable. |

### Format 3 and mixed-version hosts

For a reader at format 3 or later the change is additive: format 1 and 2 records keep working. It is
not additive for a reader still at format 2. ADR 0032 makes a format 2 reader refuse any record above
2 before claiming it, so a host running the previous release cannot redeem a record written by a host
running this one (it returns "invalid, expired or stale"; the payload is kept). During a rolling
upgrade of hosts that share one persist directory, a token created by an upgraded host cannot be
redeemed by a host still on the previous release; the reverse works, unchecked. Persisted tokens expire after the preview TTL (5 minutes by default), so
no data migration exists; same-host redemption is unaffected. Rollback to the previous release has the
same effect on records written while this one ran.

### Assumptions and limits

- Cross-host redemption assumes both hosts see the target files at the same fully qualified paths,
  the same assumption ADR 0032 makes for the solution path. A host that resolves the same logical
  file at a different path sees it as absent or different and refuses.
- The fingerprint is captured when `Store` runs, from the disk at that moment. It is not a hash of
  the bytes the preview was computed from: previews are built from the loaded workspace snapshot,
  and a file edited on disk before the preview but not yet reloaded is fingerprinted in its edited
  state and then overwritten. Closing that needs the producers to hash the text they read; it is a
  separate defect tracked as its own backlog row.
- Time of check to time of use: the comparison runs milliseconds before the write, under the claim
  lock but with no compare-and-swap in the atomic writer. An edit inside that window is not caught.
  A second check after the claim would require consuming the token first, and a refusal could then
  no longer keep it redeemable.
- Tamper: the fingerprints sit in the same directory as the mutations. They detect drift; they do
  not authenticate a record. Anyone who can write the persist directory can write a matching
  fingerprint, so the ADR 0032 threat note (the directory is a trusted location) is unchanged. A
  record downgraded to format 1 or 2 by hand is redeemed unchecked, which is the same trust.

## Consequences

- `apply_composite` can now refuse with `PreviewTokenStale` for a reason other than a reload or
  expiry. The recovery is the same (re-issue the preview); the message says which cause applies.
- Other `PreviewTokenStaleException` throw sites keep the existing "workspace was reloaded" envelope
  text, which is inaccurate for some of them. Only drift refusals carry their own message.
- The solution-store `*_apply` routes and `apply_project_mutation` are memory-only and unchanged.
  `apply_project_mutation` documents last-apply-wins; whether a project file edit between preview
  and apply should refuse is a product decision that stays open.
- `CompositePreviewStore.Entry` gained a `SourceFingerprints` member (null only for format 1 and 2
  records); it is an internal seam of this server, not a tool contract.
