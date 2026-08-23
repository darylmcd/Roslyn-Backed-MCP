using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// tunit-mtp-native-test-run: dotnet test resolves its execution mode from the nearest
/// global.json's "test.runner" setting, walking up from the target project's directory exactly
/// like the dotnet CLI's own SDK-resolution walk. DotnetTestModeResolver mirrors that walk so
/// TestRunnerService can tell whether a repo has opted into the .NET 10 SDK's native MTP
/// dotnet-test mode before attempting to run an MTP-only test framework (TUnit) — verified by
/// direct repro that there is no working fallback when it hasn't.
/// </summary>
[TestClass]
public sealed class DotnetTestModeResolverTests
{
    private readonly List<string> _createdRoots = [];

    [TestCleanup]
    public void Cleanup()
    {
        // tunit-fixture-cleanup-failure-observability: attempt every root's delete even after one
        // fails, but surface the failure(s) at the end rather than swallowing them — a silently
        // leaked locked-file delete previously hid the actual defect it exists to catch.
        List<Exception>? failures = null;
        foreach (var root in _createdRoots)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("Failed to delete one or more temp directories created by this test.", failures);
        }
    }

    private string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "DotnetTestModeResolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _createdRoots.Add(root);
        return root;
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_NoGlobalJsonAnywhere_ReturnsFalse()
    {
        var projectDir = CreateTempDirectory();

        Assert.IsFalse(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_GlobalJsonInSameDirectory_OptsIn_ReturnsTrue()
    {
        var projectDir = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(projectDir, "global.json"),
            """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");

        Assert.IsTrue(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_GlobalJsonInParentDirectory_WalksUp_ReturnsTrue()
    {
        var repoRoot = CreateTempDirectory();
        var projectDir = Path.Combine(repoRoot, "src", "MyTests");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(
            Path.Combine(repoRoot, "global.json"),
            """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");

        Assert.IsTrue(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_GlobalJsonWithoutTestSection_ReturnsFalse()
    {
        var projectDir = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(projectDir, "global.json"),
            """{ "sdk": { "version": "10.0.100" } }""");

        Assert.IsFalse(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_GlobalJsonRunnerNotMtp_ReturnsFalse()
    {
        var projectDir = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(projectDir, "global.json"),
            """{ "test": { "runner": "VSTest" } }""");

        Assert.IsFalse(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_MalformedGlobalJson_ReturnsFalseWithoutThrowing()
    {
        var projectDir = CreateTempDirectory();
        File.WriteAllText(Path.Combine(projectDir, "global.json"), "{ not valid json");

        Assert.IsFalse(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_NearestGlobalJsonWins_StopsAtFirstMatch()
    {
        // The nearest global.json to the project must win, even if an ancestor further up
        // opts into native MTP mode — this mirrors the dotnet CLI's own SDK-resolution walk,
        // which stops at the first global.json found.
        var repoRoot = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(repoRoot, "global.json"),
            """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
        var projectDir = Path.Combine(repoRoot, "src", "MyTests");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(
            Path.Combine(Path.Combine(repoRoot, "src"), "global.json"),
            """{ "sdk": { "version": "10.0.100" } }""");

        Assert.IsFalse(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_GlobalJsonWithComments_StillParses()
    {
        // global.json officially allows JS/C#-style comments and trailing commas
        // (learn.microsoft.com/dotnet/core/tools/global-json#comments-in-globaljson); a naive
        // JsonDocument.Parse rejects them, which would silently misclassify a validly-commented,
        // correctly-opted-in repo as VSTest mode.
        var projectDir = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(projectDir, "global.json"),
            """
            {
              // opt into the native MTP test runner
              "test": { "runner": "Microsoft.Testing.Platform", /* trailing comma below */ },
            }
            """);

        Assert.IsTrue(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void UsesNativeMtpDotnetTest_RunnerValueIsNotAString_ReturnsFalseWithoutThrowing()
    {
        // JsonElement.GetString() throws InvalidOperationException for a non-string ValueKind,
        // and that exception type isn't in ReadsNativeMtpRunner's catch filter — an unguarded
        // call would let a malformed "runner" value crash out of what's otherwise documented as
        // a safe, best-effort "assume VSTest mode" fallback.
        var projectDir = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(projectDir, "global.json"),
            """{ "test": { "runner": 1 } }""");

        Assert.IsFalse(DotnetTestModeResolver.UsesNativeMtpDotnetTest(projectDir));
    }

    [TestMethod]
    public void Cleanup_DeleteFailureForOneRoot_SurfacesAggregateExceptionAfterAttemptingAllRoots()
    {
        var root1 = CreateTempDirectory();
        var root2 = CreateTempDirectory();
        var lockedFile = Path.Combine(root1, "locked.txt");
        File.WriteAllText(lockedFile, "content");
        using var lockHandle = File.Open(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

        var ex = Assert.ThrowsExactly<AggregateException>(Cleanup);

        Assert.AreEqual(1, ex.InnerExceptions.Count);
        Assert.IsFalse(Directory.Exists(root2), "The second root's delete must still be attempted despite the first root's failure.");
    }
}
