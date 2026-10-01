using RoslynMcp.Host.Stdio.Configuration;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>workspace-restore-budget</c>: <c>ROSLYNMCP_RESTORE_TIMEOUT_SECONDS</c> must bind
/// to <see cref="ValidationServiceOptions.RestoreTimeout"/>, and an unset, non-numeric, zero, or
/// negative value must keep the 90 s default. Uses the value-reader seam; no process environment mutation.
/// </summary>
[TestClass]
public sealed class ValidationServiceOptionsEnvironmentBinderTests
{
    [TestMethod]
    public void Bind_RestoreTimeoutVariable_BindsRestoreTimeoutOnly()
    {
        var actual = Bind(new() { [ValidationServiceOptionsEnvironmentBinder.RestoreTimeoutVariable] = "45" });

        Assert.AreEqual(TimeSpan.FromSeconds(45), actual.RestoreTimeout);
        Assert.AreEqual(new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromSeconds(45) }, actual);
    }

    [TestMethod]
    public void Bind_RestoreTimeoutUnset_KeepsNinetySecondDefault()
    {
        var actual = Bind([]);

        Assert.AreEqual(TimeSpan.FromSeconds(90), actual.RestoreTimeout);
        Assert.AreEqual(new ValidationServiceOptions(), actual);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("abc")]
    [DataRow("")]
    [DataRow("1.5")]
    [DataRow("${user_config.RESTORE_TIMEOUT_SECONDS}")]
    public void Bind_RestoreTimeoutNonPositiveOrInvalid_KeepsNinetySecondDefault(string raw)
    {
        var actual = Bind(new() { [ValidationServiceOptionsEnvironmentBinder.RestoreTimeoutVariable] = raw });

        Assert.AreEqual(TimeSpan.FromSeconds(90), actual.RestoreTimeout);
    }

    [TestMethod]
    public void Bind_SiblingTimeoutVariables_BindTheirOwnOptions()
    {
        var actual = Bind(new()
        {
            [ValidationServiceOptionsEnvironmentBinder.BuildTimeoutVariable] = "11",
            [ValidationServiceOptionsEnvironmentBinder.TestTimeoutVariable] = "12",
            [ValidationServiceOptionsEnvironmentBinder.MaxRelatedFilesVariable] = "13",
            [ValidationServiceOptionsEnvironmentBinder.VulnerabilityScanTimeoutVariable] = "14",
            [ValidationServiceOptionsEnvironmentBinder.ApplyRevertTimeoutVariable] = "15",
            [ValidationServiceOptionsEnvironmentBinder.GitStatusTimeoutVariable] = "16",
        });

        Assert.AreEqual(TimeSpan.FromSeconds(11), actual.BuildTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(12), actual.TestTimeout);
        Assert.AreEqual(13, actual.MaxRelatedFiles);
        Assert.AreEqual(TimeSpan.FromSeconds(14), actual.VulnerabilityScanTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(15), actual.ApplyRevertTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(16), actual.GitStatusTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(90), actual.RestoreTimeout);
    }

    private static ValidationServiceOptions Bind(Dictionary<string, string?> values) =>
        ValidationServiceOptionsEnvironmentBinder.Bind(name => values.GetValueOrDefault(name));
}
