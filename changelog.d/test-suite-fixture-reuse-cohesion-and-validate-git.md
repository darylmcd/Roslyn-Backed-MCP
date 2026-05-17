---
category: Maintenance
---

- **Maintenance:** Converted `CohesionAnalysisTests`, `ValidateRecentGitChangesTests`, and `ChangeSignaturePreviewMetadataNameShapeTests` to `IsolatedWorkspaceTestBase`, replacing manual `CreateSampleSolutionCopy()` + `try/finally` cleanup with the `IsolatedWorkspaceScope` RAII pattern introduced in PR #795. Also hardened `TestFixtureFileSystem.DeleteDirectoryIfExists` to clear the read-only attribute (equivalent to PowerShell `Remove-Item -Recurse -Force`) so `IsolatedWorkspaceScope.Dispose()` reliably removes git-init'd temp roots on Windows.
