using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Preserves edited construction and runtime-zero construction as separate entry points.</summary>
internal sealed class ChangeSignatureStructConstruction
{
    internal const string OwnerTag = "signature-construction-owner";
    internal const string OriginalOnlyAttributeTag = "signature-original-constructor-attribute";
    private readonly bool _removingLast;
    private readonly string _markerName;
    private readonly string _parameterName;
    private readonly SyntaxReference _implementation;
    private readonly bool _partial;
    private readonly ParameterListSyntax _updatedParameters;
    internal SyntaxAnnotation Annotation { get; } = new(OwnerTag);
    internal IReadOnlyList<AttributeSyntax> OriginalOnlyAttributes { get; }

    private ChangeSignatureStructConstruction(IMethodSymbol constructor, bool removingLast, ParameterListSyntax updated)
    {
        _removingLast = removingLast;
        _updatedParameters = updated;
        _partial = constructor.PartialDefinitionPart is not null || constructor.PartialImplementationPart is not null;
        _implementation = (constructor.PartialImplementationPart ?? constructor).DeclaringSyntaxReferences.Single();
        OriginalOnlyAttributes = constructor.GetAttributes()
            .Where(attribute => !IsBridgeCallContract(attribute.AttributeClass))
            .Select(attribute => attribute.ApplicationSyntaxReference?.GetSyntax())
            .OfType<AttributeSyntax>().ToArray();
        var names = constructor.ContainingType.DeclaringSyntaxReferences
            .SelectMany(reference => reference.GetSyntax().DescendantTokens())
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText).ToHashSet(StringComparer.Ordinal);
        foreach (var member in constructor.ContainingType.GetMembers()) names.Add(member.Name);
        _markerName = Fresh(names, "__SignatureConstructionMarker");
        _parameterName = Fresh(names, "__signatureConstruction");
    }

    internal static ChangeSignatureStructConstruction? Create(
        IMethodSymbol constructor, ParameterListSyntax original, ParameterListSyntax updated)
    {
        if (constructor.MethodKind != MethodKind.Constructor || constructor.ContainingType.TypeKind != TypeKind.Struct)
            return null;
        var removingLast = original.Parameters.Count > 0 && updated.Parameters.Count == 0;
        var addingToZero = original.Parameters.Count == 0 && updated.Parameters.Count > 0;
        if (!removingLast && !addingToZero) return null;
        var selector = constructor.GetAttributes().Select(attribute => RuntimeSelectorName(attribute.AttributeClass))
            .FirstOrDefault(name => name is not null);
        if (removingLast && selector is not null)
            throw new PublicInvalidOperationException($"Removing this constructor's last parameter cannot preserve its {selector} parameter binding with an explicit construction marker. Retain a parameter or change the activation contract separately.");
        return new ChangeSignatureStructConstruction(constructor, removingLast, updated);
    }

    private static string? RuntimeSelectorName(INamedTypeSymbol? type) => type?.ToDisplayString() switch
    {
        "System.Text.Json.Serialization.JsonConstructorAttribute" when type.ContainingAssembly.Identity.Name == "System.Text.Json" => "JsonConstructor",
        "Newtonsoft.Json.JsonConstructorAttribute" when type.ContainingAssembly.Identity.Name == "Newtonsoft.Json" => "JsonConstructor",
        "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute"
            when type.ContainingAssembly.Identity.Name == "Microsoft.Extensions.DependencyInjection.Abstractions" => "ActivatorUtilitiesConstructor",
        _ => null,
    };

    // Runtime attributes belong to the retained initialization owner. Copy only call-site
    // contracts understood by the compiler/analyzers; duplicating arbitrary runtime attributes
    // can create multiple selected constructors, even for a consumer unknown to this server.
    private static bool IsBridgeCallContract(INamedTypeSymbol? type) => type?.ToDisplayString() is
        "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute" or "System.ObsoleteAttribute"
        or "System.Diagnostics.CodeAnalysis.ExperimentalAttribute"
        or "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute"
        or "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute"
        or "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"
        or "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute";

    private static string Fresh(HashSet<string> names, string prefix)
    {
        var name = prefix;
        for (var suffix = 1; !names.Add(name); suffix++) name = prefix + suffix;
        return name;
    }

    private ParameterListSyntax Parameters(ParameterListSyntax updated) => updated.WithParameters(
        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Parameter(SyntaxFactory.Identifier(_parameterName))
            .WithType(SyntaxFactory.ParseTypeName(_markerName).WithTrailingTrivia(SyntaxFactory.Space))));

    internal bool EmitsAt(SyntaxNode declaration) => !_removingLast || !_partial
        || declaration.SyntaxTree == _implementation.SyntaxTree && declaration.Span == _implementation.Span;

    internal ConstructorDeclarationSyntax RewriteConstructor(ConstructorDeclarationSyntax declaration,
        ParameterListSyntax updated) => _removingLast ? declaration.WithParameterList(Parameters(updated)) : declaration;

    internal SeparatedSyntaxList<ArgumentSyntax> Arguments(
        SeparatedSyntaxList<ArgumentSyntax> updated, string containingType) => _removingLast
        ? updated.Add(SyntaxFactory.Argument(SyntaxFactory.DefaultExpression(
            SyntaxFactory.ParseTypeName(containingType + "." + _markerName))))
        : updated;

    internal TypeDeclarationSyntax RewritePrimary(TypeDeclarationSyntax declaration, ParameterListSyntax original, ParameterListSyntax updated, string declarationTag)
    {
        var methodAttributes = declaration.AttributeLists.Where(list => list.Target?.Identifier.IsKind(SyntaxKind.MethodKeyword) == true).ToArray();
        var constructor = SyntaxFactory.ConstructorDeclaration(declaration.Identifier.WithoutTrivia()
                .WithoutAnnotations(ChangeSignatureAddRemovePreviewBuilder.DiagnosticTag))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
            .WithParameterList(_removingLast ? Parameters(updated) : original).WithBody(SyntaxFactory.Block())
            .WithAttributeLists(SyntaxFactory.List(methodAttributes.Select(list => list.WithTarget(null)
                .WithOpenBracketToken(list.OpenBracketToken.WithTrailingTrivia(
                    list.OpenBracketToken.TrailingTrivia.AddRange(list.Target!.DescendantTrivia()))))))
            .WithAdditionalAnnotations(declaration.GetAnnotations(declarationTag));
        return EnsureBraces(declaration).WithParameterList(null).WithoutAnnotations(declarationTag)
            .WithAttributeLists(SyntaxFactory.List(declaration.AttributeLists.Except(methodAttributes)))
            .WithMembers(declaration.Members.Insert(0, constructor));
    }

    internal TypeDeclarationSyntax Complete(TypeDeclarationSyntax declaration, string declarationTag)
    {
        declaration = EnsureBraces(declaration);
        if (!_removingLast)
        {
            var originals = declaration.Members.OfType<ConstructorDeclarationSyntax>()
                .Where(constructor => constructor.HasAnnotations(declarationTag)).ToArray();
            var additions = originals.Select(original =>
            {
                var attributes = SyntaxFactory.List(original.AttributeLists
                    .Select(list => list.WithAttributes(SyntaxFactory.SeparatedList(list.Attributes.Where(attribute =>
                        !attribute.HasAnnotations(OriginalOnlyAttributeTag)))))
                    .Where(list => list.Attributes.Count > 0));
                var added = original.WithParameterList(_updatedParameters).WithAttributeLists(attributes);
                if (original.Body is not null || original.ExpressionBody is not null
                    || original.Modifiers.Any(SyntaxKind.ExternKeyword))
                    added = added.WithInitializer(SyntaxFactory.ConstructorInitializer(SyntaxKind.ThisConstructorInitializer,
                            SyntaxFactory.ArgumentList()))
                        .WithModifiers(SyntaxFactory.TokenList(added.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.ExternKeyword))))
                        .WithBody(SyntaxFactory.Block()).WithExpressionBody(null).WithSemicolonToken(default);
                return ChangeSignatureAddRemovePreviewBuilder.PrepareGeneratedConstructor(added);
            }).ToArray();
            // Original partial declaration/implementation pairs stay intact; the added pair delegates to them.
            declaration = declaration.ReplaceNodes(originals, (_, rewritten) => rewritten.WithoutAnnotations(declarationTag));
            return declaration.WithMembers(declaration.Members.AddRange(additions));
        }
        var marker = SyntaxFactory.StructDeclaration(_markerName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));
        return declaration.WithMembers(declaration.Members.Add(marker.NormalizeWhitespace(eol: " ")
            .WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space)));
    }

    private static TypeDeclarationSyntax EnsureBraces(TypeDeclarationSyntax declaration) =>
        declaration.OpenBraceToken.IsMissing || declaration.OpenBraceToken.RawKind == 0
            ? declaration.WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(declaration.SemicolonToken.LeadingTrivia)
                    .WithTrailingTrivia(declaration.SemicolonToken.TrailingTrivia)).WithSemicolonToken(default)
            : declaration;
}
