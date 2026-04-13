using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class IdentifierValidationTests
{
    [TestMethod]
    public void Valid_Simple_Name_Does_Not_Throw()
        => IdentifierValidation.ThrowIfInvalidIdentifier("MyClass");

    [TestMethod]
    public void Valid_Underscore_Prefix_Does_Not_Throw()
        => IdentifierValidation.ThrowIfInvalidIdentifier("_myField");

    [TestMethod]
    public void Valid_Unicode_Identifier_Does_Not_Throw()
        => IdentifierValidation.ThrowIfInvalidIdentifier("café");

    [TestMethod]
    public void Verbatim_Keyword_Does_Not_Throw()
        => IdentifierValidation.ThrowIfInvalidIdentifier("@class");

    [TestMethod]
    public void Verbatim_Contextual_Keyword_Does_Not_Throw()
        => IdentifierValidation.ThrowIfInvalidIdentifier("@var");

    [TestMethod]
    public void Empty_String_Throws()
        => Assert.ThrowsException<InvalidOperationException>(
            () => IdentifierValidation.ThrowIfInvalidIdentifier(""));

    [TestMethod]
    public void Null_Throws()
        => Assert.ThrowsException<InvalidOperationException>(
            () => IdentifierValidation.ThrowIfInvalidIdentifier(null!));

    [TestMethod]
    public void At_Sign_Alone_Throws()
        => Assert.ThrowsException<InvalidOperationException>(
            () => IdentifierValidation.ThrowIfInvalidIdentifier("@"));

    [TestMethod]
    public void Numeric_Prefix_Throws()
        => Assert.ThrowsException<InvalidOperationException>(
            () => IdentifierValidation.ThrowIfInvalidIdentifier("2InvalidName"));

    [TestMethod]
    public void Reserved_Keyword_Throws()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => IdentifierValidation.ThrowIfInvalidIdentifier("class"));
        StringAssert.Contains(ex.Message, "reserved");
    }

    [TestMethod]
    public void Contextual_Keyword_Throws()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => IdentifierValidation.ThrowIfInvalidIdentifier("var"));
        StringAssert.Contains(ex.Message, "contextual");
    }

    [TestMethod]
    public void Custom_Parameter_Label_Appears_In_Error()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => IdentifierValidation.ThrowIfInvalidIdentifier("", "type name"));
        StringAssert.Contains(ex.Message, "type name");
    }
}
