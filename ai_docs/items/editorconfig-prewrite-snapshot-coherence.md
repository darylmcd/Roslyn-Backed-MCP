# editorconfig-prewrite-snapshot-coherence — Capture one byte snapshot before editorconfig mutation

**row:** `editorconfig-prewrite-snapshot-coherence` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:378-381`

## Acceptance

- [ ] Undo snapshot and parsed option lines derive from one pre-write byte snapshot
- [ ] An external edit between reads cannot mix old undo bytes with new parsed lines or misattribute the resulting write as Apply
- [ ] Deterministic interleaving regression and focused Release tests pass

## Evidence

- Cold review of PR #1678 found separate pre-write reads in `EditorConfigService.Write`: `File.ReadAllBytes` for undo, then `File.ReadAllLines` for mutation. An external writer between reads yields a torn operation and incorrect staleness attribution.
