# Unix cleanup ownership decision

| Field | Value |
|---|---|
| Status | APPROVED by live operator reply 2026-10-16; implement conservative POSIX final-name retention and Windows handle-bound deletion |
| Owning row | `physical-graph-cleanup-path-authority` remains OPEN |
| Scope | Final directory-entry removal in staging, fork cleanup, analyzer retirement and expired-directory sweep |
| Recommendation | Retain POSIX content-free directory skeletons when exact object-bound final removal cannot be proved; preserve all wrong-owner and no-follow protections |
| Verification | Parent verified Windows OLD failure and exact-DLL Linux OLD 1 valid failure/51ms, SDK10.0.400; initial Windows core run refused unsupported ReOpenFile with native error5; root matrix2 proved pinned-name witness acquisition; corrected core runtime pending; parent owns runtime |

## Current contract and defect

| Source anchor | Current behavior / contract |
|---|---|
| `PhysicalDirectoryScope.cs:67-80` | OwnedRoot releases its original handle before final removal and retains only numeric identity for retries. |
| `PhysicalDirectoryScope.cs:120-131` | Opens/checks current child, releases checked/original handles, then deletes by parent/name; replacement can occur between comparison and deletion. |
| `PhysicalDirectoryRetryAuthorityTests.cs:41,147-150` | Even an ordinary empty replacement is unrelated and must survive; ownership is the original object, not every object later occupying the spelling. |
| `PhysicalDirectoryScope.cs:82-92` | Recreate assumes successful removal then creates/reopens the same issued name. |
| Kernel evidence | Linux `fs/namei.c:5404-5412` accepts only AT_REMOVEDIR for unlinkat; syscall takes parent descriptor/name and no expected inode. |

Holding a POSIX child descriptor preserves object lifetime but does not prevent rename or bind unlinkat to that object. Random quarantine, renameat2, advisory flock and mode0700 do not establish enforced namespace exclusion against an uncooperative actor with the same UID.

## Platform implementation boundary

| Platform | Required design |
|---|---|
| Windows | While the original effective LIST/no-share-delete pin is still held, acquire a read-attributes share-READ/WRITE/DELETE witness by name and compare both LIVE handle identities; retain it through retries. Open issued-name directory with DELETE and no share-delete, validate live identity/type/no-reparse, then SetFileInformationByHandle(FileDispositionInfo) on that SAME handle; no pathname fallback. |
| Windows retry | Existing conflicting no-delete readers produce retryable failure; retain witness. Do not treat recycled numeric FileId as ownership after releasing all kernel references. |
| POSIX recommended | Clean original held directories descriptor-relatively; retain final directory entries rather than perform unbound name unlink. Nested retained leaves mean a content-free skeleton, not necessarily an empty root. |
| POSIX boundary | Do not silently claim `Deleted`, `Recreated`, or reclaimed storage; preserve original identity and current policy checks. No recursive reentry through a replaced name. |

## Required complete consequences before implementation/merge

| Callsite / surface | Required migration if recommendation accepted |
|---|---|
| `PhysicalDirectoryScope.cs:97-131` | Return a required typed outcome for final-name Removed/Retained, distinct from failures; propagate nested retained skeletons. Keep no-follow traversal and original object ownership. |
| `OwnedRoot.Delete/Recreate:67-92` | Replace implicit deletion success with outcome; Recreate must never reuse a retained skeleton because directory Exists/globs can observe stale structure. |
| `PhysicalGraphStage.cs:234,387-406` | For rebuild allocate a fresh exclusive staging coordinate and recompute ALL mappings, globals, copied paths and candidate context; no same-path clear/reuse assumption. Disposal reports retained skeleton separately from validation failure. |
| `WorkspaceForkApplyService.cs:443-450,453-491,724-753` | Keep new fork coordinates exclusive; successful content cleanup with retained skeleton produces explicit cleanup warning/outcome. Sweeper does not repeatedly claim removal of retained directories or gain authority from timestamp/name alone. |
| `AnalyzerReferenceIsolation.cs:245-260` | Lease/workspace sweeper propagates retained outcomes; log content reclaimed vs namespace removed precisely. Locks/lease retirement continue releasing; no recursive revisit of a replacement. |
| `AnalyzerReferenceIsolation.cs:377-469` | Retirement state distinguishes resource/file release from namespace removal, avoids treating retention as retryable deletion failure forever, and preserves original exception evidence. |
| `AnalyzerReferenceIsolation.cs:560-580` | Setup failure cleanup uses the same final-name outcome; retained empty artifact directory is reported without masking primary load failure. |
| `WorkspaceForkApplyResultDto.cs:8-16` | Existing CleanupWarnings can express nonfatal retained namespace without adding an optional field. If callers need structured cleanup status, add a required typed field and migrate ALL constructors/consumers; record public contract decision. |
| Storage/retention | Document that empty/skeleton names persist until removal under separately enforced exclusive authority; never add a later unsafe reaper. Decide operator-visible accounting and manual removal guidance. No hidden age-based deletion. |
| Public documentation | Record approved decision in docs/decisions and CHANGELOG migration note; explain POSIX retained directories, cleanup warning semantics and unchanged wrong-owner preservation. Update plan/backlog acceptance explicitly after approval. |

## Alternatives for operator

| Choice | Guarantees / consequences |
|---|---|
| Recommended conservative retention | Preserves exact object safety without extra OS privilege. Directory names/skeletons persist; implement complete fresh-coordinate staging/retirement/outcome flow above. This is an explicit product contract change. |
| Enforced exclusive namespace | Separate OS principal/helper owns parent and exclusively mutates names; capability-mediated operations provide enforceable exclusion. Preserves actual final removal but adds deployment, privilege, service lifetime and recovery architecture. |
| Knowingly accept name-race | Permit final nonrecursive removal of a different empty directory at issued name. Weaker than existing tests/authority; requires explicit operator acceptance of that precise loss. Not recommended. |

## Causal and acceptance matrix

| Case | Required evidence |
|---|---|
| Dispose-gap OLD | Scratch `PhysicalFinalUnlinkOwnership.draft.cs` delegates actual filesystem scopes; checked-current Dispose renames original and creates ordinary empty replacement. Assert replacement actually happened, original survived and replacement remains; OLD should fail preservation. |
| Fixed Windows | Actual replacement before DELETE-handle acquisition refuses; once correct DELETE handle is pinned, rename is denied and original is deleted by disposition. No test passes solely because a removed callback never executed. |
| Retry | Failed disposition/unlink followed by replacement preserves replacement; retained original witness remains live until final cleanup ownership release. |
| POSIX retained outcome | Files are reclaimed only through original held namespace; final names retained, replacement untouched, outcome observable. No assertion weakened before operator approves new expected contract. |
| Rebuild | Original staged directory contains stale nested directories; rebuilt mapping uses fresh coordinate and stale directories cannot influence Exists/globs. |
| Lifecycle | Successful and failing fork/analyzer cleanup preserve primary errors, release every handle/gate, and accurately surface retained status; repeated sweeps do not loop or claim namespace removal. |
| Cross-platform | Parent runs real Windows and Linux suites; macOS public support must be qualified against its own descriptor primitives before claiming parity. |

## Official primitive sources

- [CreateFile sharing and directory-handle semantics](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew); parent matrix2 qualified witness acquisition while the effective original pin remains held. ReOpenFile directory attempts all failed error5 and are removed.
- [SetFileInformationByHandle and required DELETE access](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle).
- [Linux unlinkat implementation](https://raw.githubusercontent.com/torvalds/linux/master/fs/namei.c), lines5404-5412 inspected this session.
- [renameat2 source-name semantics](https://man7.org/linux/man-pages/man2/renameat2.2.html).
