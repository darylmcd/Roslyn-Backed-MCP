# git-script-process-reader-ownership — Own the Git and script redirected readers

**row:** `git-script-process-reader-ownership` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/GitChangedFilesCollector.cs:110-143`
- `src/RoslynMcp.Roslyn/Services/ScriptWorkerProcess.cs:66-69`

## Acceptance

- [ ] Qualify redirected-reader disposal with real owned subprocesses after success, cancellation, and partial startup. Prove both reader tasks and their opened handles are reclaimed.
- [ ] Preserve independent observation of both streams, each owner's cleanup budget, Git path projection, and script response semantics.

## Evidence

- At `c705e2e1`, both owners access Process.StandardOutput and Process.StandardError and rely on Process.Dispose. The .NET Process.Close implementation leaves streams opened in synchronous access mode for the caller to close. DotnetCommandRunner cancellation ownership has the existing dotnet-command-runner-unobserved-failure-paths row. This is a source finding.

## Context

- Coordinate both reader lifetimes with their subprocess owners. The Windows worker-capacity investigation is tracked separately.

## Notes

2026-11-17 runtime control: an owned Windows --version process on .NET 10.0.15 exposed two synchronous redirected FileStreams. Both SafeFileHandles stayed open after Process.Dispose and closed when each explicit reader was disposed. The control exercised BCL streams through an owned dotnet --version child.
