using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public class ParameterValidationTests
{
    // ── ValidateSeverity ──

    [TestMethod]
    [DataRow("Error")]
    [DataRow("Warning")]
    [DataRow("Info")]
    [DataRow("Hidden")]
    public void ValidateSeverity_Valid_Values_Do_Not_Throw(string severity)
        => ParameterValidation.ValidateSeverity(severity);

    [TestMethod]
    public void ValidateSeverity_Null_Does_Not_Throw()
        => ParameterValidation.ValidateSeverity(null);

    [TestMethod]
    public void ValidateSeverity_Invalid_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidateSeverity("Critical"));

    [TestMethod]
    public void ValidateSeverity_Case_Insensitive()
        => ParameterValidation.ValidateSeverity("error");

    // ── ValidateTypeKind ──

    [TestMethod]
    [DataRow("class")]
    [DataRow("interface")]
    [DataRow("record")]
    [DataRow("enum")]
    [DataRow("CLASS")]
    public void ValidateTypeKind_Valid_Values_Do_Not_Throw(string kind)
        => ParameterValidation.ValidateTypeKind(kind);

    [TestMethod]
    public void ValidateTypeKind_Invalid_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidateTypeKind("struct"));

    // ── ValidateBulkReplaceScope ──

    [TestMethod]
    [DataRow("parameters")]
    [DataRow("fields")]
    [DataRow("FIELDS")]
    [DataRow("all")]
    public void ValidateBulkReplaceScope_Valid_Values_Do_Not_Throw(string scope)
        => ParameterValidation.ValidateBulkReplaceScope(scope);

    [TestMethod]
    public void ValidateBulkReplaceScope_Null_Does_Not_Throw()
        => ParameterValidation.ValidateBulkReplaceScope(null);

    [TestMethod]
    public void ValidateBulkReplaceScope_Invalid_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidateBulkReplaceScope("none"));

    // ── ValidatePagination ──

    [TestMethod]
    public void ValidatePagination_Valid_Values_Do_Not_Throw()
        => ParameterValidation.ValidatePagination(0, 50, "offset", "limit");

    [TestMethod]
    public void ValidatePagination_Negative_Offset_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidatePagination(-1, 50, "offset", "limit"));

    [TestMethod]
    public void ValidatePagination_Zero_Limit_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidatePagination(0, 0, "offset", "limit"));

    [TestMethod]
    public void ValidatePagination_Negative_Limit_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidatePagination(0, -5, "offset", "limit"));

    [TestMethod]
    public void ValidatePagination_Limit_At_Max_Does_Not_Throw()
        => ParameterValidation.ValidatePagination(0, 1000, "offset", "limit");

    [TestMethod]
    public void ValidatePagination_Limit_Above_Max_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidatePagination(0, 1001, "offset", "limit"));

    // ── ValidateBulkSize ──

    [TestMethod]
    public void ValidateBulkSize_Below_Max_Does_Not_Throw()
        => ParameterValidation.ValidateBulkSize(10, 50, "symbols");

    [TestMethod]
    public void ValidateBulkSize_At_Max_Does_Not_Throw()
        => ParameterValidation.ValidateBulkSize(50, 50, "symbols");

    [TestMethod]
    public void ValidateBulkSize_Above_Max_Throws()
        => Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidateBulkSize(51, 50, "symbols"));

    [TestMethod]
    [DataRow("severity", "Error, Warning, Info, Hidden")]
    [DataRow("typeKind", "class, interface, record, enum")]
    [DataRow("bulkScope", "parameters, fields, all")]
    [DataRow("invocationScope", "all")]
    public void InvalidEnums_PublishSafeChoices(string field, string choices)
    {
        const string hostile = "C:/private/secret-token";
        Action validate = field switch
        {
            "severity" => () => ParameterValidation.ValidateSeverity(hostile),
            "typeKind" => () => ParameterValidation.ValidateTypeKind(hostile),
            "bulkScope" => () => ParameterValidation.ValidateBulkReplaceScope(hostile),
            _ => () => ParameterValidation.ValidateReplaceInvocationScope(hostile)
        };
        var exception = Assert.ThrowsExactly<PublicArgumentException>(validate);
        AssertPublicEnvelope(exception, field.EndsWith("Scope", StringComparison.Ordinal) ? "scope" : field, choices, hostile);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("all")]
    [DataRow("ALL")]
    public void ReplaceInvocationScope_AcceptsOptionalCaseInsensitiveValues(string? scope)
        => ParameterValidation.ValidateReplaceInvocationScope(scope);

    internal static void AssertPublicEnvelope(PublicArgumentException exception, string parameter, string messagePart, string? excluded = null)
    {
        var result = StructuredCallToolFilter.BuildErrorResult("symbol_search", exception);
        Assert.IsTrue(result.IsError);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.IsTrue(wire.RootElement.GetProperty("isError").GetBoolean());
        using var json = JsonDocument.Parse(Assert.IsInstanceOfType<TextContentBlock>(result.Content[0]).Text);
        var envelope = json.RootElement;
        Assert.AreEqual("InvalidArgument", envelope.GetProperty("category").GetString());
        Assert.IsTrue(envelope.GetProperty("error").GetBoolean());
        Assert.AreEqual("ArgumentException", envelope.GetProperty("exceptionType").GetString());
        Assert.AreEqual(parameter, exception.ParamName);
        StringAssert.Contains(envelope.GetProperty("message").GetString()!, messagePart);
        Assert.IsTrue(envelope.GetProperty("message").GetString()!.Contains(parameter, StringComparison.OrdinalIgnoreCase));
        if (excluded is not null)
            Assert.IsFalse(envelope.GetProperty("message").GetString()!.Contains(excluded, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(-1, 50, "offset", "greater than or equal to 0")]
    [DataRow(0, 0, "limit", "greater than 0")]
    [DataRow(0, -3, "limit", "greater than 0")]
    [DataRow(0, 1001, "limit", "1000")]
    public void Pagination_PublishesBounds(int offset, int limit, string parameter, string bound)
    {
        var exception = Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidatePagination(offset, limit, nameof(offset), nameof(limit)));
        AssertPublicEnvelope(exception, parameter, bound);
    }

    [TestMethod]
    public void Pagination_MinimumLimit_IsAccepted()
        => ParameterValidation.ValidatePagination(0, 1, "offset", "limit");

    [TestMethod]
    public void BulkSize_PublishesBound()
    {
        var exception = Assert.ThrowsExactly<PublicArgumentException>(
            () => ParameterValidation.ValidateBulkSize(51, 50, "symbols"));
        AssertPublicEnvelope(exception, "symbols", "50");
    }
}
