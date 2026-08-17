param(
    [string]$SolutionPath = "RoslynMcp.slnx"
)

$ErrorActionPreference = "Stop"

# NuGet's restore audit covers direct and transitive packages when NuGetAuditMode=all.
# Promote both advisory-source failures and every vulnerability severity to errors so
# an unavailable audit service or a finding can never produce a successful gate.
$auditWarningCodes = "NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904"

dotnet restore $SolutionPath `
    --force-evaluate `
    --verbosity minimal `
    "-p:NuGetAudit=true" `
    "-p:NuGetAuditMode=all" `
    "-p:WarningsAsErrors=$auditWarningCodes"

if ($LASTEXITCODE -ne 0) {
    throw "NuGet vulnerability audit failed with exit code $LASTEXITCODE."
}

Write-Host "NuGet vulnerability audit completed without findings."
