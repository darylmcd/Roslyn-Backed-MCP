# env-binding-policy-unify — Unify ROSLYNMCP_* binding and invalid-value policy

**row:** `env-binding-policy-unify` · **pri:** `Low` · **size:** `M` · **deps:** `env-var-reference-single-source-and-drift-gate`


## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs`
- `src/RoslynMcp.Host.Stdio/Configuration/ValidationServiceOptionsEnvironmentBinder.cs`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs`

## Acceptance

- [ ] One shared binder; invalid-value behavior documented per variable in environment-variables.md.
- [ ] Existing option tests green.

## Evidence

wb read the four binders; deps: documenting the policy first is covered by env-var-reference-single-source-and-drift-gate. (doc-audit 2026-11-17)
