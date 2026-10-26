using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Checks compiler-bound handler calls before an extraction publishes a preview.</summary>
internal static class InterfaceHandlerBindingValidator
{
    public static async Task ValidateAsync(Solution solution, DocumentId sourceId, DocumentId interfaceId,
        INamedTypeSymbol concrete, CancellationToken ct)
    {
        var contractDocument = solution.GetDocument(interfaceId)
            ?? throw InvalidOperationErrors.Internal("The extracted interface document is missing.");
        var contractRoot = await contractDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var declaration = contractRoot?.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();
        if (declaration is null) return;
        if (!declaration.DescendantNodes().OfType<ParameterSyntax>().Any(parameter => parameter.AttributeLists.Count > 0)) return;
        var contractModel = await contractDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("The extracted interface semantic model is missing.");
        var contract = contractModel.GetDeclaredSymbol(declaration, ct)
            ?? throw InvalidOperationErrors.Internal("The extracted interface symbol is missing.");
        var candidates = declaration.Members.Select(member =>
        {
            var parameters = member switch
            {
                MethodDeclarationSyntax method => method.ParameterList.Parameters,
                IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters,
                _ => default,
            };
            var symbols = parameters.Select(parameter => (Syntax: parameter, Symbol: contractModel.GetDeclaredSymbol(parameter, ct) as IParameterSymbol)).ToArray();
            var handlers = symbols.Where(parameter => parameter.Symbol?.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
                "System.Runtime.CompilerServices.InterpolatedStringHandlerArgumentAttribute") == true).Select(parameter => parameter.Syntax).ToHashSet();
            var dynamicParameters = symbols.Where(parameter => parameter.Symbol?.Type.TypeKind == TypeKind.Dynamic).Select(parameter => parameter.Syntax).ToHashSet();
            return (Member: member, Parameters: parameters, Handlers: handlers, DynamicParameters: dynamicParameters);
        }).Where(candidate => candidate.Handlers.Count > 0).ToArray();
        if (candidates.Length == 0) return;
        var sourceDocument = solution.GetDocument(sourceId)
            ?? throw InvalidOperationErrors.Internal("The source document is missing.");
        var compilation = await sourceDocument.Project.GetCompilationAsync(ct).ConfigureAwait(false) as CSharpCompilation
            ?? throw InvalidOperationErrors.Internal("The source C# compilation is missing.");
        var generator = SyntaxGenerator.GetGenerator(sourceDocument);

        foreach (var (member, parameters, handlers, dynamicParameters) in candidates)
        {
            var original = Probe(member, parameters, handlers, dynamicParameters, generator.TypeExpression(concrete), "Original");
            var projected = Probe(member, parameters, handlers, dynamicParameters, generator.TypeExpression(contract), "Projected");
            var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            if (compilation.Options.AllowUnsafe) modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword));
            var probeClass = SyntaxFactory.ClassDeclaration("__InterfaceHandlerProbe" + Guid.NewGuid().ToString("N"))
                .WithModifiers(modifiers).WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>([original, projected]));
            var root = SyntaxFactory.CompilationUnit().AddMembers(WithContainingTypeContext(probeClass, concrete, generator)).NormalizeWhitespace();
            var tree = CSharpSyntaxTree.Create(root, sourceDocument.Project.ParseOptions as CSharpParseOptions,
                path: "__interface_handler_validation__.cs");
            var checkedCompilation = compilation.AddSyntaxTrees(tree);
            var errors = checkedCompilation.GetDiagnostics(ct).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.SourceTree == tree).ToArray();
            if (errors.Length > 0)
                throw new PublicInvalidOperationException("The extracted interface cannot preserve interpolated string handler binding: " +
                    string.Join(", ", errors.Select(error => error.Id).Distinct().Order(StringComparer.Ordinal).Take(5)) +
                    ". Keep the handler receiver and forwarded arguments compatible with the extracted interface.");

            var model = checkedCompilation.GetSemanticModel(tree);
            var methods = tree.GetRoot(ct).DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var expectedContract = ResolveInCompilation(contractModel.GetDeclaredSymbol(member, ct), checkedCompilation);
            var expectedConcrete = ResolveInCompilation(concrete, checkedCompilation) as INamedTypeSymbol;
            var implementation = expectedContract is null ? null : expectedConcrete?.FindImplementationForInterfaceMember(expectedContract);
            if (expectedContract is null || implementation is null ||
                !SymbolEqualityComparer.Default.Equals(BoundMember(model, methods[0], ct)?.OriginalDefinition, implementation.OriginalDefinition) ||
                !SymbolEqualityComparer.Default.Equals(BoundMember(model, methods[1], ct)?.OriginalDefinition, expectedContract.OriginalDefinition))
                throw new PublicInvalidOperationException("The extracted interface cannot preserve the intended interpolated string handler member binding. " +
                    "Select a contract whose handler calls bind to the same member.");
            var originalConstructors = HandlerConstructors(model.GetOperation(methods[0].Body!, ct)).ToArray();
            var projectedConstructors = HandlerConstructors(model.GetOperation(methods[1].Body!, ct)).ToArray();
            if (originalConstructors.Length != handlers.Count || projectedConstructors.Length != handlers.Count || originalConstructors.Where((constructor, index) =>
                    !SymbolEqualityComparer.Default.Equals(constructor.OriginalDefinition, projectedConstructors[index].OriginalDefinition)).Any())
                throw new PublicInvalidOperationException("The extracted interface changes an interpolated string handler constructor binding. " +
                    "The projected receiver cannot preserve the original constructor selection.");
        }
    }

    private static ClassDeclarationSyntax WithContainingTypeContext(ClassDeclarationSyntax probe, INamedTypeSymbol concrete,
        SyntaxGenerator generator)
    {
        var container = probe;
        for (var context = concrete; context is not null; context = context.ContainingType)
        {
            if (context.TypeParameters.Length == 0) continue;
            var declaration = (TypeDeclarationSyntax)generator.Declaration(context);
            if (!SymbolEqualityComparer.Default.Equals(context, concrete))
                container = SyntaxFactory.ClassDeclaration("__InterfaceHandlerContext" + Guid.NewGuid().ToString("N"))
                    .WithModifiers(probe.Modifiers).AddMembers(container);
            container = container.WithTypeParameterList(declaration.TypeParameterList).WithConstraintClauses(declaration.ConstraintClauses);
        }
        return container;
    }

    private static MethodDeclarationSyntax Probe(MemberDeclarationSyntax member, SeparatedSyntaxList<ParameterSyntax> parameters,
        HashSet<ParameterSyntax> handlers, HashSet<ParameterSyntax> dynamicParameters, SyntaxNode receiverType, string name)
    {
        var names = parameters.Select(parameter => parameter.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        if (member is MethodDeclarationSyntax { TypeParameterList: { } genericParameters })
            names.UnionWith(genericParameters.Parameters.Select(parameter => parameter.Identifier.ValueText));
        var receiverName = "__receiver";
        while (names.Contains(receiverName)) receiverName += "_";
        var assignedName = "__assigned";
        while (names.Contains(assignedName) || receiverName == assignedName) assignedName += "_";
        var receiver = SyntaxFactory.IdentifierName(receiverName);
        var probeParameters = parameters.Where(parameter => !handlers.Contains(parameter))
            .Select(parameter =>
            {
                var modifiers = SyntaxFactory.TokenList(parameter.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.ParamsKeyword)));
                // dynamic and object have the same declaration signature. Use a static
                // object argument so the probe binds a handler call rather than a dynamic invocation.
                var type = dynamicParameters.Contains(parameter) ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)) : parameter.Type;
                return parameter.WithType(type).WithAttributeLists(default).WithDefault(null).WithModifiers(modifiers);
            }).Prepend(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(receiverName)).WithType((TypeSyntax)receiverType)).ToList();
        var arguments = parameters.Select(parameter =>
        {
            if (parameter.Type is null) return SyntaxFactory.Argument(SyntaxFactory.ParseExpression("__arglist()"));
            var argument = SyntaxFactory.Argument(handlers.Contains(parameter) ? SyntaxFactory.ParseExpression("$\"\"") :
                SyntaxFactory.IdentifierName(parameter.Identifier)).WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(parameter.Identifier)));
            if (handlers.Contains(parameter)) return argument;
            if (parameter.Modifiers.Any(SyntaxKind.OutKeyword)) return argument.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword));
            if (parameter.Modifiers.Any(SyntaxKind.InKeyword) || parameter.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                return argument.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.InKeyword));
            return parameter.Modifiers.Any(SyntaxKind.RefKeyword)
                ? argument.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)) : argument;
        });
        StatementSyntax statement;
        TypeParameterListSyntax? typeParameters = null;
        SyntaxList<TypeParameterConstraintClauseSyntax> constraints = default;
        if (member is MethodDeclarationSyntax method)
        {
            typeParameters = method.TypeParameterList;
            constraints = method.ConstraintClauses;
            SimpleNameSyntax methodName = typeParameters is null ? SyntaxFactory.IdentifierName(method.Identifier)
                : SyntaxFactory.GenericName(method.Identifier, SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList<TypeSyntax>(
                    typeParameters.Parameters.Select(parameter => SyntaxFactory.IdentifierName(parameter.Identifier)))));
            statement = SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, methodName),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))));
        }
        else
        {
            var indexer = (IndexerDeclarationSyntax)member;
            var access = SyntaxFactory.ElementAccessExpression(receiver,
                SyntaxFactory.BracketedArgumentList(SyntaxFactory.SeparatedList(arguments)));
            if (indexer.AccessorList?.Accessors.Any(SyntaxKind.GetAccessorDeclaration) == true)
            {
                statement = SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .AddVariables(SyntaxFactory.VariableDeclarator(assignedName).WithInitializer(SyntaxFactory.EqualsValueClause(access))));
            }
            else
            {
                probeParameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(assignedName)).WithType(indexer.Type));
                statement = SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    access, SyntaxFactory.IdentifierName(assignedName)));
            }
        }
        return SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), name)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithTypeParameterList(typeParameters).WithConstraintClauses(constraints)
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(probeParameters)))
            .WithBody(SyntaxFactory.Block(statement));
    }

    private static ISymbol? ResolveInCompilation(ISymbol? symbol, Compilation compilation) =>
        symbol?.GetDocumentationCommentId() is { } id ? DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) : null;

    private static ISymbol? BoundMember(SemanticModel model, MethodDeclarationSyntax method, CancellationToken ct)
    {
        var call = method.Body!.DescendantNodes().FirstOrDefault(node => node is InvocationExpressionSyntax or ElementAccessExpressionSyntax);
        return call is null ? null : model.GetOperation(call, ct) switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IPropertyReferenceOperation property => property.Property,
            _ => null,
        };
    }

    private static IEnumerable<IMethodSymbol> HandlerConstructors(IOperation? operation)
    {
        if (operation is null) yield break;
        if (operation is IInterpolatedStringHandlerCreationOperation { HandlerCreation: IObjectCreationOperation { Constructor: { } constructor } })
            yield return constructor;
        foreach (var child in operation.ChildOperations)
            foreach (var nested in HandlerConstructors(child)) yield return nested;
    }
}
