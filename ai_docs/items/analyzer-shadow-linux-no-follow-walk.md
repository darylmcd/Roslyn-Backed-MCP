# analyzer-shadow-linux-no-follow-walk — Add a no-follow directory walk for non-Windows

**row:** `analyzer-shadow-linux-no-follow-walk` · **pri:** `Medium` · **size:** `M` · **deps:** `analyzer-shadow-lease-disposal-no-follow`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/NoFollowDirectoryDeleter.cs (DeletePortable)`
- `tests/RoslynMcp.Tests/AnalyzerShadowLeaseNoFollowTests.cs`

## Acceptance

- [ ] Replace the portable path-based walk with an fd-relative walk (openat/unlinkat with O_NOFOLLOW, via libc interop) so a symlink swap at any depth cannot redirect deletion to an external target.
- [ ] Deterministic Linux regression: swap an enumerated directory to a symlink to an external directory before child processing; assert the external target is untouched. Observe it red on the path-based walk first.
- [ ] Windows behavior and the existing hooks stay unchanged.

## Evidence

- PR 1964 states the Linux gap in its body, changelog fragment and class remarks. .NET has no openat/unlinkat, so this needs libc interop. Parent-chain components of the root itself are followed by the OS on both platforms (caller trust boundary).
