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
        => Assert.ThrowsExactly<ArgumentException>(
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
    public void ValidateTypeKind_Valid_Values_Do_Not_Throw(string kind)
        => ParameterValidation.ValidateTypeKind(kind);

    [TestMethod]
    public void ValidateTypeKind_Invalid_Throws()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ParameterValidation.ValidateTypeKind("struct"));

    // ── ValidateBulkReplaceScope ──

    [TestMethod]
    [DataRow("parameters")]
    [DataRow("fields")]
    [DataRow("all")]
    public void ValidateBulkReplaceScope_Valid_Values_Do_Not_Throw(string scope)
        => ParameterValidation.ValidateBulkReplaceScope(scope);

    [TestMethod]
    public void ValidateBulkReplaceScope_Null_Does_Not_Throw()
        => ParameterValidation.ValidateBulkReplaceScope(null);

    [TestMethod]
    public void ValidateBulkReplaceScope_Invalid_Throws()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ParameterValidation.ValidateBulkReplaceScope("none"));

    // ── ValidatePagination ──

    [TestMethod]
    public void ValidatePagination_Valid_Values_Do_Not_Throw()
        => ParameterValidation.ValidatePagination(0, 50);

    [TestMethod]
    public void ValidatePagination_Negative_Offset_Throws()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ParameterValidation.ValidatePagination(-1, 50));

    [TestMethod]
    public void ValidatePagination_Zero_Limit_Throws()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ParameterValidation.ValidatePagination(0, 0));

    [TestMethod]
    public void ValidatePagination_Negative_Limit_Throws()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ParameterValidation.ValidatePagination(0, -5));

    [TestMethod]
    public void ValidatePagination_Limit_At_Max_Does_Not_Throw()
        => ParameterValidation.ValidatePagination(0, 1000);

    [TestMethod]
    public void ValidatePagination_Limit_Above_Max_Throws()
        => Assert.ThrowsExactly<ArgumentException>(
            () => ParameterValidation.ValidatePagination(0, 1001));
}
