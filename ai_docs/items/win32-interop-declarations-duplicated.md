# win32-interop-declarations-duplicated — One shared Win32 file-handle interop surface

**row:** `win32-interop-declarations-duplicated` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/NoFollowDirectoryDeleter.cs:504`
- `src/RoslynMcp.Roslyn/Helpers/PhysicalPathResolver.cs:162`
- `src/RoslynMcp.Host.Stdio/Runtime/FinalPathResolver.cs:125`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:413`

## Acceptance

- [ ] CreateFileW / CloseHandle / GetFinalPathNameByHandleW / handle-info declarations live once per assembly boundary (or one shared internal helper) instead of one private copy per class.
- [ ] Every remaining declaration keeps DefaultDllImportSearchPaths(System32) and SetLastError; no behaviour change (existing no-follow, path-resolver and composite-apply tests green).

## Evidence

- PR 1964 added another private kernel32 declaration set; same mechanism as the sites above (rg DllImport src).
