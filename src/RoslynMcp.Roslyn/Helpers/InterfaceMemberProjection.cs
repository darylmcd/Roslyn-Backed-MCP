using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Shared declaration projection for interface extraction.</summary>
internal static class InterfaceMemberProjection
{
    public static SeparatedSyntaxList<ParameterSyntax> Parameters(
        SyntaxGenerator generator, IEnumerable<IParameterSymbol> parameters) =>
        SyntaxFactory.SeparatedList(parameters.Select(parameter => Parameter(generator, parameter)));

    public static ParameterListSyntax MethodParameters(SyntaxGenerator generator, IMethodSymbol method)
    {
        var parameters = Parameters(generator, method.Parameters);
        if (method.IsVararg)
            parameters = parameters.Add(SyntaxFactory.ParseParameterList("(__arglist)").Parameters.Single());
        return SyntaxFactory.ParameterList(parameters);
    }

    public static TypeSyntax ReturnType(SyntaxGenerator generator, ITypeSymbol type, RefKind refKind)
    {
        var syntax = (TypeSyntax)generator.TypeExpression(type);
        if (refKind == RefKind.None) return syntax;
        var reference = SyntaxFactory.RefType(syntax);
        return refKind == RefKind.RefReadOnly
            ? reference.WithReadOnlyKeyword(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)) : reference;
    }

    public static TDeclaration WithReturnContract<TDeclaration>(SyntaxGenerator generator, ISymbol symbol, TDeclaration declaration)
        where TDeclaration : SyntaxNode =>
        (TDeclaration)generator.AddAttributes(declaration, symbol.GetAttributes().Where(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute")
            .Select(attribute => generator.Attribute(attribute)));

    public static InterfaceDeclarationSyntax WithUnsafeContext(InterfaceDeclarationSyntax declaration, Project targetProject)
    {
        if (!declaration.DescendantNodes().Any(node => node is PointerTypeSyntax or FunctionPointerTypeSyntax)) return declaration;
        if (targetProject.CompilationOptions is not CSharpCompilationOptions { AllowUnsafe: true })
            throw new PublicInvalidOperationException("The extracted interface requires unsafe signatures, but the receiving project does not enable AllowUnsafeBlocks.");
        return declaration.WithModifiers(declaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword)));
    }

    public static TypeDeclarationSyntax AddBaseType(TypeDeclarationSyntax declaration, string interfaceName, bool multilineContinuation)
    {
        if (declaration.BaseList?.Types.Any(baseType =>
                string.Equals(baseType.Type.ToString(), interfaceName, StringComparison.Ordinal)) == true)
            return declaration;

        // Header trivia belongs after the inserted base type, whether its old
        // owner is a base type, primary constructor, generic list or type name.
        var anchor = declaration.BaseList?.Types.Last().GetLastToken()
            ?? declaration.ParameterList?.CloseParenToken
            ?? declaration.TypeParameterList?.GreaterThanToken
            ?? declaration.Identifier;
        var trailing = anchor.TrailingTrivia;
        declaration = declaration.ReplaceToken(anchor, anchor.WithTrailingTrivia(default(SyntaxTriviaList)));
        if (trailing.Count == 0 && declaration.ConstraintClauses.Count > 0)
            trailing = SyntaxFactory.TriviaList(SyntaxFactory.Space);
        var interfaceType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(interfaceName)).WithTrailingTrivia(trailing);
        if (declaration.BaseList is { } existing)
        {
            var leading = multilineContinuation
                ? SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(Environment.NewLine), SyntaxFactory.Whitespace("    "))
                : SyntaxFactory.TriviaList(SyntaxFactory.Space);
            declaration = declaration.WithBaseList(existing.AddTypes(interfaceType.WithLeadingTrivia(leading)));
        }
        else
        {
            declaration = declaration.WithBaseList(SyntaxFactory.BaseList(
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceType.WithLeadingTrivia(SyntaxFactory.Space)))
                .WithLeadingTrivia(SyntaxFactory.Space));
        }

        var brace = declaration.OpenBraceToken;
        if (!brace.IsMissing && !brace.LeadingTrivia.Any(SyntaxKind.EndOfLineTrivia) &&
            !brace.GetPreviousToken().TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia))
            declaration = declaration.WithOpenBraceToken(brace.WithLeadingTrivia(
                SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(Environment.NewLine)).AddRange(brace.LeadingTrivia)));
        return declaration;
    }

    private static ParameterSyntax Parameter(SyntaxGenerator generator, IParameterSymbol parameter)
    {
        var declaration = (ParameterSyntax)generator.ParameterDeclaration(parameter);
        var attributes = parameter.GetAttributes().Where(IsParameterContractAttribute).ToArray();
        // Attribute-backed defaults must keep their original encoding. C# forbids an
        // initializer together with Optional/DefaultParameterValue; DateTimeConstant
        // can represent values that a C# default expression cannot.
        if (attributes.Any(attribute => attribute.AttributeClass?.ToDisplayString() is
                "System.Runtime.InteropServices.OptionalAttribute" or
                "System.Runtime.InteropServices.DefaultParameterValueAttribute" or
                "System.Runtime.CompilerServices.DateTimeConstantAttribute" or
                "System.Runtime.CompilerServices.DecimalConstantAttribute"))
            declaration = declaration.WithDefault(null);
        return (ParameterSyntax)generator.AddAttributes(declaration, attributes.Select(attribute => generator.Attribute(attribute)));
    }

    // SyntaxGenerator emits the modifiers and default values. Carry only source-level
    // call/flow contracts; implementation annotations and compiler encoding attributes
    // (ParamArray, ParamCollection, IsReadOnly, ScopedRef) do not belong on this projection.
    private static bool IsParameterContractAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() is
            "System.Runtime.CompilerServices.CallerMemberNameAttribute" or
            "System.Runtime.CompilerServices.CallerLineNumberAttribute" or
            "System.Runtime.CompilerServices.CallerFilePathAttribute" or
            "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute" or
            "System.Runtime.CompilerServices.InterpolatedStringHandlerArgumentAttribute" or
            "System.Runtime.CompilerServices.DateTimeConstantAttribute" or
            "System.Runtime.CompilerServices.DecimalConstantAttribute" or
            "System.Runtime.InteropServices.OptionalAttribute" or
            "System.Runtime.InteropServices.DefaultParameterValueAttribute"
        || attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Diagnostics.CodeAnalysis";

    public static BasePropertyDeclarationSyntax Property(SyntaxGenerator generator, IPropertySymbol property)
    {
        var type = ReturnType(generator, property.Type, property.RefKind);
        BasePropertyDeclarationSyntax declaration = property.IsIndexer
            ? SyntaxFactory.IndexerDeclaration(type).WithParameterList(
                SyntaxFactory.BracketedParameterList(Parameters(generator, property.Parameters)))
            : SyntaxFactory.PropertyDeclaration(type, EscapeIdentifier(property.Name));
        var accessors = new List<AccessorDeclarationSyntax>();
        if (property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
            accessors.Add(Accessor(generator, property.GetMethod, SyntaxKind.GetAccessorDeclaration));
        if (property.SetMethod?.DeclaredAccessibility == Accessibility.Public)
            accessors.Add(Accessor(generator, property.SetMethod,
                property.SetMethod.IsInitOnly ? SyntaxKind.InitAccessorDeclaration : SyntaxKind.SetAccessorDeclaration));
        return WithReturnContract(generator, property,
            declaration.WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors))));
    }

    private static AccessorDeclarationSyntax Accessor(SyntaxGenerator generator, IMethodSymbol accessor, SyntaxKind kind) =>
        WithReturnContract(generator, accessor,
            SyntaxFactory.AccessorDeclaration(kind).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

    public static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
