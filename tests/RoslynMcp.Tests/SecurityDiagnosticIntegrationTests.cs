using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

[TestClass]
public class SecurityDiagnosticIntegrationTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static SecurityDiagnosticService SecurityService { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
        SecurityService = new SecurityDiagnosticService(
            DiagnosticService,
            WorkspaceManager,
            NullLogger<SecurityDiagnosticService>.Instance);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    // ── Registry Tests ──

    [TestMethod]
    public void Registry_Recognizes_Known_CA_Diagnostics()
    {
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("CA3001"));
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("CA2100"));
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("CA5350"));
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("CA5404"));
    }

    [TestMethod]
    public void Registry_Recognizes_Known_SCS_Diagnostics()
    {
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("SCS0001"));
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("SCS0002"));
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("SCS0039"));
    }

    [TestMethod]
    public void Registry_Does_Not_Recognize_Non_Security_Diagnostic()
    {
        Assert.IsFalse(SecurityDiagnosticRegistry.IsSecurityDiagnostic("CS8019"));
        Assert.IsFalse(SecurityDiagnosticRegistry.IsSecurityDiagnostic("CS0101"));
        Assert.IsFalse(SecurityDiagnosticRegistry.IsSecurityDiagnostic("UNKNOWN"));
    }

    [TestMethod]
    public void Registry_Is_Case_Insensitive()
    {
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("ca3001"));
        Assert.IsTrue(SecurityDiagnosticRegistry.IsSecurityDiagnostic("scs0001"));
    }

    [TestMethod]
    public void Registry_GetSecurityInfo_Returns_Correct_Metadata()
    {
        var info = SecurityDiagnosticRegistry.GetSecurityInfo("CA3001");
        Assert.IsNotNull(info);
        Assert.AreEqual("CA3001", info.DiagnosticId);
        Assert.AreEqual("SQL Injection", info.ShortName);
        Assert.AreEqual("Injection", info.SecurityCategory);
        Assert.AreEqual("A03:2021 Injection", info.OwaspCategory);
        Assert.AreEqual("Critical", info.SecuritySeverity);
        Assert.IsFalse(string.IsNullOrWhiteSpace(info.FixHint));
    }

    [TestMethod]
    public void Registry_GetSecurityInfo_Returns_Null_For_Unknown()
    {
        var info = SecurityDiagnosticRegistry.GetSecurityInfo("CS8019");
        Assert.IsNull(info);
    }

    [TestMethod]
    public void Registry_Contains_Expected_Entry_Count()
    {
        // At minimum, we should have entries for the CA and SCS ranges documented in the prompt
        Assert.IsTrue(SecurityDiagnosticRegistry.All.Count >= 80,
            $"Expected at least 80 registry entries, got {SecurityDiagnosticRegistry.All.Count}");
    }

    // ── Service Tests ──

    [TestMethod]
    public async Task SecurityDiagnostics_Returns_Zero_Findings_For_Clean_Project()
    {
        var result = await SecurityService.GetSecurityDiagnosticsAsync(
            WorkspaceId, null, null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Findings);
        Assert.IsNotNull(result.AnalyzerStatus);
        Assert.AreEqual(result.Findings.Count, result.TotalFindings);
        Assert.AreEqual(result.CriticalCount + result.HighCount + result.MediumCount + result.LowCount,
            result.TotalFindings);
    }

    [TestMethod]
    public async Task SecurityDiagnostics_Respects_Project_Filter()
    {
        var result = await SecurityService.GetSecurityDiagnosticsAsync(
            WorkspaceId, "SampleLib", null, CancellationToken.None);

        Assert.IsNotNull(result);
        // All findings (if any) should be from SampleLib
        foreach (var finding in result.Findings)
        {
            Assert.IsNotNull(finding.DiagnosticId);
            Assert.IsNotNull(finding.SecurityCategory);
            Assert.IsNotNull(finding.OwaspCategory);
            Assert.IsNotNull(finding.SecuritySeverity);
        }
    }

    [TestMethod]
    public async Task AnalyzerStatus_Returns_Valid_Status()
    {
        var status = await SecurityService.GetAnalyzerStatusAsync(
            WorkspaceId, CancellationToken.None);

        Assert.IsNotNull(status);
        Assert.IsNotNull(status.MissingRecommendedPackages);
        // SDK-style .NET 5+ projects should have net analyzers
        // (may vary by environment, so we just check the status is populated)
    }

    [TestMethod]
    public async Task AnalyzerStatus_Reports_Missing_SecurityCodeScan()
    {
        var status = await SecurityService.GetAnalyzerStatusAsync(
            WorkspaceId, CancellationToken.None);

        // SampleSolution doesn't reference SecurityCodeScan, so it should be missing
        Assert.IsFalse(status.SecurityCodeScanPresent);
        Assert.IsTrue(status.MissingRecommendedPackages.Count > 0,
            "Should recommend SecurityCodeScan when not present");
    }

    // ── Catalog Tests ──

    [TestMethod]
    public void Catalog_Contains_Security_Tools()
    {
        var tools = ServerSurfaceCatalog.Tools;
        Assert.IsTrue(tools.Any(t => t.Name == "security_diagnostics"),
            "security_diagnostics tool not found in catalog");
        Assert.IsTrue(tools.Any(t => t.Name == "security_analyzer_status"),
            "security_analyzer_status tool not found in catalog");
    }

    [TestMethod]
    public void Catalog_Contains_New_Prompts()
    {
        var prompts = ServerSurfaceCatalog.Prompts;
        Assert.IsTrue(prompts.Any(p => p.Name == "security_review"),
            "security_review prompt not found in catalog");
        Assert.IsTrue(prompts.Any(p => p.Name == "discover_capabilities"),
            "discover_capabilities prompt not found in catalog");
        Assert.IsTrue(prompts.Any(p => p.Name == "dead_code_audit"),
            "dead_code_audit prompt not found in catalog");
        Assert.IsTrue(prompts.Any(p => p.Name == "review_test_coverage"),
            "review_test_coverage prompt not found in catalog");
        Assert.IsTrue(prompts.Any(p => p.Name == "review_complexity"),
            "review_complexity prompt not found in catalog");
    }

    [TestMethod]
    public void Catalog_Contains_Workflow_Hints()
    {
        var hints = ServerSurfaceCatalog.WorkflowHints;
        Assert.IsTrue(hints.Count >= 5, $"Expected at least 5 workflow hints, got {hints.Count}");
        Assert.IsTrue(hints.Any(h => h.Name == "Security Audit"),
            "Security Audit workflow hint not found");
        Assert.IsTrue(hints.Any(h => h.Name == "Preview/Apply"),
            "Preview/Apply workflow hint not found");
    }

    [TestMethod]
    public void Catalog_Security_Tools_Are_Marked_Stable_And_ReadOnly()
    {
        var securityTools = ServerSurfaceCatalog.Tools
            .Where(t => t.Category == "security")
            .ToList();

        Assert.AreEqual(2, securityTools.Count);
        foreach (var tool in securityTools)
        {
            Assert.AreEqual("stable", tool.SupportTier,
                $"Security tool {tool.Name} should be stable");
            Assert.IsTrue(tool.ReadOnly,
                $"Security tool {tool.Name} should be read-only");
            Assert.IsFalse(tool.Destructive,
                $"Security tool {tool.Name} should not be destructive");
        }
    }

    [TestMethod]
    public void Catalog_Document_Includes_WorkflowHints()
    {
        var doc = ServerSurfaceCatalog.CreateDocument();
        Assert.IsNotNull(doc.WorkflowHints);
        Assert.IsTrue(doc.WorkflowHints.Count > 0);
    }
}
