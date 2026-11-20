# change-signature-builder-decompose — Decompose the 1300-line change-signature preview builder

**row:** `change-signature-builder-decompose` · **pri:** `Low` · **size:** `L` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa` · **deps:** `change-signature-unrelated-invocation-rewrite`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:53`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureCallScanner.cs` (new)
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureCallRewriter.cs` (new)
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAliasPlan.cs` (new)
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureCandidateValidator.cs` (new)

## Acceptance

- [ ] Split `ChangeSignatureAddRemovePreviewBuilder.cs` (1297 lines) into scan / rewrite / alias plan / validation types; the builder composes them.
- [ ] Replace the pipe-encoded alias plan strings (`"@source|..."`, `"@project|..."`, `"@newglobal|..."`, built at `:339-340`, split at `:546-565` and `:1057-1065`) with a record type.
- [ ] `NameOriginalArguments` (`:141`) no longer over-freezes edited calls (only parameters that need it are named).
- [ ] `ValidateCandidateAsync` (`:1174`) no longer silently `continue`s on lost binding evidence (`:1240`): the skip is limited to an intentionally removed argument and everything else refuses.
- [ ] No behaviour change beyond the two fixes above; existing change-signature tests stay green.

## Evidence

- Seen during PR 2173 review: one file mixes four responsibilities and passes structured data as `|`-joined strings.

## Context

- Depends on `change-signature-unrelated-invocation-rewrite`: its PR2 rewrites the same regions (gate at `:428`, cleanup `:466-470`, drift check); merge it before widening the file set.
