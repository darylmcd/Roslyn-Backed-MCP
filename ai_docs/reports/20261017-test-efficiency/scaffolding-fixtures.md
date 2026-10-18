# Scaffolding fixture efficiency

<!-- scope: in-repo -->

| Concern | Evidence / decision |
|---|---|
| Baseline | PR #1793, merged as 3be1740ff2c0477bcd205c3a97a062aefd0eb066 |
| Selection | Remaining scaffolding spans on hosted run 39086176888: Windows 452.503 / 277.415s; Linux 242.944 / 238.469s. |
| Ownership defect | The class inherited shared mutable production services and a no-op class cleanup. Each invocation now owns its service graph, preview capacity, watchers and copied files. |
| Type scaffolding | Six methods / seven cases use the existing fresh SampleLib fixture. |
| Test scaffolding | 29 methods / 31 cases use a fresh two-project solution containing SampleLib.Tests and its real SampleLib source reference. |
| Pure cases | Four methods / five cases perform no workspace load. |
| Watched root | Keep a solution at the common copied root; loading only SampleLib.Tests.csproj would narrow the watcher root. |
| Redundant setup | Fifteen cases formerly loaded, injected fixture files, then reloaded before the operation. Prepare those sources before the first load. |
| Lifecycle coverage | Retain the post-apply reload in the nullable-constructor batch case. Retain original load-before-filesystem-only-sibling-write ordering in sampling cases. |
| Coverage | All 39 test methods / 43 expanded cases and their assertions preserved. Syntax-token comparison covers those methods plus the duplicate-widget setup helper; only explicit fixture/service substitutions are normalized. |
| Reuse | Extend the existing partial-copy cleanup owner and owned workspace factory; no shared mutable reset protocol. |
| Scope | Five test/helper files; the copy factory, ownership adapter, consumer and two boundary-test files form one complete fixture change. No product or CI-policy changes. |
| Remaining wave | CrossProjectRefactoringIntegrationTests remains unchanged and open in workspace-fixture-load-amortization-wave-3. |

## Validation

| Check | Evidence |
|---|---|
| Negative control | New graph regression using the old full-solution factory failed: expected two projects, actual three. |
| Graph and compilation | Corrected fixture retains both project identities, the real project reference, common watched root, SampleLib.Dog symbol and error-free test-project compilation; excludes SampleApp. |
| Failed setup | Missing test-project file removes the partially copied root; existing full-solution and single-project failure cases retained. |
| Focused suites | 56 passed, six unchanged platform skips, zero failures across scaffolding, ownership and filesystem suites. |
| Complete qualification | Full unchanged local gate and hosted Windows/Linux matrix required; results belong to the implementation PR. |

## Measurements

| Run | Before seconds | After seconds | Passed | Skipped |
|---|---:|---:|---:|---:|
| 1 | 108.225 | 67.219 | 43 | 0 |
| 2 | 107.758 | 66.787 | 43 | 0 |
| Median | 107.992 | 67.003 | — | — |

- Median reduction: 38.0%; all four runs have the identical 43-case/outcome multiset.
- Interval: first case start to last case end from TRX, excluding build/restore/adapter startup.
- Release, eng/ci.runsettings, exact ClassName filter and restored fixtures. Before runs and the second after run used --no-build --no-restore; the first after run rebuilt outside the measured interval.
- Baselines ran on b4149dcf; its Git tree equals merged base 3be1740f.
- Class timing is distinct from overall CI duration. Repeated hosted results and complete qualification belong to the implementation PR.

- [Local timing samples and case identities](scaffolding-local-timings.json).
- [Fixture map](scaffolding-fixture-map.json).
- [Repository audit](report.md).
- [Output-drain prerequisite](output-drain.md).
