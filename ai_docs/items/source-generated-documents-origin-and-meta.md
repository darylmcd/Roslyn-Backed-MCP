# source-generated-documents-origin-and-meta — Tag source_generated_documents entries by origin and wrap in an envelope

**row:** `source-generated-documents-origin-and-meta` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:840`

## Acceptance

- [ ] GlobalUsings.g.cs entries carry origin=msbuild (or are excluded)
- [ ] Response is an object with _meta

## Evidence

- source_generated_documents(W_RW) → bare array of 3 GlobalUsings.g.cs (MSBuild), no _meta. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`
