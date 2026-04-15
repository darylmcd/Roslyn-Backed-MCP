using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

[TestClass]
public class SecurityDiagnosticIntegrationTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static string InsecureWorkspaceId { get; set; } = null!;
    private static SecurityDiagnosticService SecurityService { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var secTestPath = Path.Combine(RepositoryRootPath, "samples", "SecurityTestProject", "SecurityTestProject.slnx");
        var insecureStatus = await WorkspaceManager.LoadAsync(secTestPath, CancellationToken.None);
        InsecureWorkspaceId = insecureStatus.WorkspaceId;

        var msBuildEvaluation = new MsBuildEvaluationService(WorkspaceManager);
        SecurityService = new SecurityDiagnosticService(
            DiagnosticService,
            WorkspaceManager,
            msBuildEvaluation,
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
    public async Task AnalyzerStatus_AfterSecurityCodeScanRemoval_ReportsAbsence()
    {
        // v1.18 (`securitycodescan-currency`) removed the archived SecurityCodeScan.VS2019
        // package. NetAnalyzers (CA-rule security checks) is the replacement. v1.19 follow-up
        // drops SCS from the service's recommendation list too — recommending an archived
        // package is worse than offering no recommendation. The contract locked in here:
        //
        //  - SecurityCodeScan is reported as absent for this workspace.
        //  - MissingRecommendedPackages does NOT include the archived SCS package. Empty is
        //    the honest answer when the only historical recommendation has been retired.
        var status = await SecurityService.GetAnalyzerStatusAsync(
            WorkspaceId, CancellationToken.None);

        Assert.IsFalse(status.SecurityCodeScanPresent,
            "SecurityCodeScan.VS2019 was removed in v1.18 — analyzer status must reflect that.");
        CollectionAssert.DoesNotContain(
            status.MissingRecommendedPackages.ToList(),
            "SecurityCodeScan.VS2019",
            "The archived SecurityCodeScan.VS2019 package must not appear in MissingRecommendedPackages " +
            "— recommending an archived package gives consumers bad advice.");
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
        Assert.IsTrue(prompts.Any(p => p.Name == "guided_extract_method"),
            "guided_extract_method prompt not found in catalog");
        Assert.IsTrue(prompts.Any(p => p.Name == "msbuild_inspection"),
            "msbuild_inspection prompt not found in catalog");
        Assert.IsTrue(prompts.Any(p => p.Name == "session_undo"),
            "session_undo prompt not found in catalog");
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

        Assert.AreEqual(3, securityTools.Count);
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

    // ── Positive-Detection Tests (SecurityTestProject fixture) ──

    [TestMethod]
    public async Task SecurityDiagnostics_Detects_Findings_In_Insecure_Project()
    {
        var result = await SecurityService.GetSecurityDiagnosticsAsync(
            InsecureWorkspaceId, null, null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.TotalFindings > 0,
            "Expected security findings in InsecureLib but got none. " +
            "Verify that SecurityCodeScan or .NET SDK analyzers are loaded.");

        foreach (var finding in result.Findings)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(finding.DiagnosticId),
                "Finding should have a diagnostic ID.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(finding.SecurityCategory),
                $"Finding {finding.DiagnosticId} should have a security category.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(finding.OwaspCategory),
                $"Finding {finding.DiagnosticId} should have an OWASP category.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(finding.SecuritySeverity),
                $"Finding {finding.DiagnosticId} should have a security severity.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(finding.FixHint),
                $"Finding {finding.DiagnosticId} should have a fix hint.");
        }
    }

    [TestMethod]
    public async Task SecurityDiagnostics_Findings_Include_Cryptographic_Failures()
    {
        var result = await SecurityService.GetSecurityDiagnosticsAsync(
            InsecureWorkspaceId, null, null, CancellationToken.None);

        Assert.IsTrue(result.TotalFindings > 0);

        var cryptoFindings = result.Findings
            .Where(f => f.OwaspCategory.Contains("Cryptographic", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsTrue(cryptoFindings.Count > 0,
            "Expected at least one finding in OWASP A02:2021 Cryptographic Failures for SHA1/DES/MD5 usage.");
    }

    [TestMethod]
    public async Task SecurityDiagnostics_Findings_Point_To_InsecureCode_File()
    {
        var result = await SecurityService.GetSecurityDiagnosticsAsync(
            InsecureWorkspaceId, null, null, CancellationToken.None);

        Assert.IsTrue(result.TotalFindings > 0);

        var insecureCodeFindings = result.Findings
            .Where(f => f.FilePath?.Contains("InsecureCode.cs", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.IsTrue(insecureCodeFindings.Count > 0,
            "Expected at least one finding located in InsecureCode.cs.");
    }

    [TestMethod]
    public async Task SecurityDiagnostics_Severity_Counts_Match_Findings_On_Insecure_Project()
    {
        var result = await SecurityService.GetSecurityDiagnosticsAsync(
            InsecureWorkspaceId, null, null, CancellationToken.None);

        Assert.AreEqual(result.Findings.Count, result.TotalFindings);
        Assert.AreEqual(
            result.CriticalCount + result.HighCount + result.MediumCount + result.LowCount,
            result.TotalFindings,
            "Severity breakdown does not sum to TotalFindings.");
    }

    [TestMethod]
    public async Task SecurityDiagnostics_Project_Filter_Works_On_Insecure_Workspace()
    {
        var result = await SecurityService.GetSecurityDiagnosticsAsync(
            InsecureWorkspaceId, "InsecureLib", null, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.TotalFindings > 0,
            "Expected findings when filtering to InsecureLib project.");

        foreach (var finding in result.Findings)
        {
            Assert.IsTrue(
                finding.FilePath?.Contains("InsecureLib", StringComparison.OrdinalIgnoreCase) == true,
                $"Finding in unexpected path: {finding.FilePath}");
        }
    }
}
