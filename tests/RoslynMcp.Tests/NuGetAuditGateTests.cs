namespace RoslynMcp.Tests;

[TestClass]
public sealed class NuGetAuditGateTests
{
    [TestMethod]
    public void LocalAndHostedGates_UseSharedFailClosedRestoreAudit()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "verify-nuget-audit.ps1"));
        // Lowercase 'justfile' matches the on-disk name. A capitalized spelling resolves on
        // case-insensitive Windows but throws on the case-sensitive Linux publish runner.
        var justfile = File.ReadAllText(Path.Combine(repositoryRoot, "justfile"));
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));

        StringAssert.Contains(script, "--force-evaluate");
        StringAssert.Contains(script, "-p:NuGetAudit=true");
        StringAssert.Contains(script, "-p:NuGetAuditMode=all");
        foreach (var warningCode in new[] { "NU1900", "NU1901", "NU1902", "NU1903", "NU1904" })
        {
            StringAssert.Contains(script, warningCode);
        }

        StringAssert.Contains(justfile, "./eng/verify-nuget-audit.ps1");
        StringAssert.Contains(workflow, "./eng/verify-nuget-audit.ps1 -SolutionPath RoslynMcp.slnx");
        Assert.IsFalse(justfile.Contains("package list", StringComparison.Ordinal));
        Assert.IsFalse(workflow.Contains("package list --project RoslynMcp.slnx --vulnerable", StringComparison.Ordinal));
    }
}
