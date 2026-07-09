# nugetversionchecker-httpclient-factory — Register NuGetVersionChecker's HttpClient via IHttpClientFactory

**row:** `nugetversionchecker-httpclient-factory` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Services/NuGetVersionChecker.cs:70`

## Acceptance

- [ ] HttpClient consumed by NuGetVersionChecker is obtained via IHttpClientFactory/AddHttpClient, not a bare singleton
- [ ] NuGetVersionChecker tests still pass unchanged

## Evidence

- Refactor Harness 2.0 pass 1 (`/refactorv2`), Roslyn-backed scoring + adversarial verify where applicable. Full per-cell detail: `ai_docs/reports/20260708T234500Z_roslyn-backed-mcp_refactor-matrix-pass1.md`.
- Contributing cells: S04e-host-server-infrastructure::DG7-config-deps-ergo (evidence 2)
