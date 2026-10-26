# dockerfile-restore-project-graph — Copy the complete project graph before Docker restore

**row:** `dockerfile-restore-project-graph` · **pri:** `Low` · **size:** `M`

## Anchors

- `Dockerfile:9` (project copies before restore)
- `RoslynMcp.slnx` (canonical solution project inventory)
- `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj` (analyzer project dependency)

## Acceptance

- [ ] Observe a clean image build fail on the current incomplete COPY graph, then restore/build/publish the same solution successfully with its complete referenced project inputs.
- [ ] Keep layer caching correct when project/build inputs change; preserve runtime SDK, non-root behavior and shipped host functionality. Do not silently drop projects from the solution or suppress restore failures.
- [ ] Qualify a clean Docker build and representative host execution on the reviewed head; no claim from a prepopulated context or local bin/obj.

## Evidence

- Static current-source inspection: Dockerfile copies the solution and only Core/Roslyn/Host project files, then restores RoslynMcp.slnx. That solution also contains analyzer and test/probe projects; Host has an analyzer ProjectReference. Source directories are copied only AFTER restore. No Docker build was run for this sighting; reproduce before repair.
- Existing dockerfile-run-comment-and-sanctioned-roots-env and docs-build-run-package-coverage-gaps do not own this restore graph mechanism. Separate unselected finding; no inline repair in the CI scheduling slice.
