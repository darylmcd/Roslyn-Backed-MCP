# stdio-shutdown-flush-transport-ownership — Remove ineffective Console.Out shutdown flushing

**row:** `stdio-shutdown-flush-transport-ownership` · **pri:** `Medium` · **size:** `M` · **deps:** `host-shutdown-di-owned-workspace-disposal,public-exception-detail-policy`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/StdioShutdownFlusher.cs`
- `tests/RoslynMcp.Tests/StdioFlushOnExitTests.cs`

## Acceptance

- [ ] Document the pinned SDK 2.1 stdout ownership and remove `StdioShutdownFlusher` plus every `Console.Out` shutdown hook that cannot reach the SDK-owned buffered stream.
- [ ] Rely on the SDK's per-send transport flush and normal bounded host shutdown; do not introduce a second stdout owner.
- [ ] Publish/launch the host in one subprocess regression, initialize it, complete a request, close stdin, drain stdout/stderr to EOF, and observe the final response plus a clean bounded exit.
- [ ] The transport-disconnected path writes only a stable secret-safe category/type/correlation diagnostic to stderr, never raw `ex.Message`, path, or secret-bearing value, and leaves no orphan process.
- [ ] Replace source-text tests that merely assert `Program.cs` contains flush calls with the subprocess regression.

## Evidence

- SDK 2.1 stdio transport constructs its own `BufferedStream(Console.OpenStandardOutput())` and flushes that stream after each send.
- Current production shutdown code flushes `Console.Out` instead, while tests explicitly admit they do not exercise a real process or transport.
- The current transport-disconnect catch writes `ex.Message` directly to stderr; removal of the ineffective flusher must not preserve that separate disclosure.
