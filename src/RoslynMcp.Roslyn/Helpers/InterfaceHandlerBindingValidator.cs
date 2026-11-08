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
    private const string HandlerAttribute = "System.Runtime.CompilerServices.InterpolatedStringHandlerArgumentAttribute";

    public static async Task ValidateAsync(Document contractDocument, DocumentId? sourceId,
        IReadOnlyList<(ISymbol Source, ISymbol Projected, MemberDeclarationSyntax Declaration)> members, CancellationToken ct)
    {
        static bool HasHandler(IParameterSymbol parameter) => parameter.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == HandlerAttribute);
        var candidates = members.Where(member => member.Source switch
        {
            IMethodSymbol method => method.Parameters.Any(HasHandler),
            IPropertySymbol { IsIndexer: true } indexer => indexer.Parameters.Any(HasHandler),
            _ => false,
        }).ToArray();
        if (candidates.Length == 0) return;
        if (sourceId is null) throw InvalidOperationErrors.Internal("The interface source document was not registered for handler validation.");
        var sourceDocument = contractDocument.Project.Solution.GetDocument(sourceId)
            ?? throw InvalidOperationErrors.Internal("The source document is missing.");
        var compilation = await sourceDocument.Project.GetCompilationAsync(ct).ConfigureAwait(false) as CSharpCompilation
            ?? throw InvalidOperationErrors.Internal("The source C# compilation is missing.");
        var generator = SyntaxGenerator.GetGenerator(sourceDocument);
        var receivingOptions = contractDocument.Project.ParseOptions as CSharpParseOptions
            ?? throw InvalidOperationErrors.Internal("The interface receiving parse options are unavailable.");

        foreach (var (sourceMember, projectedMember, member) in candidates)
        {
            var originalParameters = sourceMember switch
            {
                IMethodSymbol method => method.Parameters,
                IPropertySymbol property => property.Parameters,
                _ => throw InvalidOperationErrors.Internal("Handler member kind is unsupported."),
            };
            var projectedParameters = projectedMember switch
            {
                IMethodSymbol method => method.Parameters,
                IPropertySymbol property => property.Parameters,
                _ => throw InvalidOperationErrors.Internal("Projected handler member kind is unsupported."),
            };
            var parameters = member switch
            {
                MethodDeclarationSyntax method => method.ParameterList.Parameters,
                IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters,
                _ => throw InvalidOperationErrors.Internal("Generated handler declaration kind is unsupported."),
            };
            if (parameters.Count != originalParameters.Length || parameters.Count != projectedParameters.Length)
                throw new PublicInvalidOperationException("The extracted interface changes interpolated string handler parameter binding.");
            var handlers = new HashSet<ParameterSyntax>();
            var dynamicParameters = new HashSet<ParameterSyntax>();
            for (var i = 0; i < parameters.Count; i++)
            {
                if (HasHandler(originalParameters[i]))
                {
                    if (!HasHandler(projectedParameters[i]))
                        throw new PublicInvalidOperationException("The extracted interface omits an interpolated string handler argument contract.");
                    handlers.Add(parameters[i]);
                }
                if (originalParameters[i].Type.TypeKind == TypeKind.Dynamic) dynamicParameters.Add(parameters[i]);
            }
            var concrete = sourceMember.ContainingType;
            var contract = projectedMember.ContainingType;
            var suffix = Guid.NewGuid().ToString("N");
            var originalName = "__HandlerOriginal_" + suffix;
            var projectedName = "__HandlerProjected_" + suffix;
            var containingNames = ContainingTypes(concrete).SelectMany(type => type.TypeParameters).Select(parameter => parameter.Name).ToArray();
            var original = Probe(member, parameters, handlers, dynamicParameters, containingNames, generator.TypeExpression(concrete), originalName);
            var projected = Probe(member, parameters, handlers, dynamicParameters, containingNames, generator.TypeExpression(contract), projectedName);
            var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
            if (compilation.Options.AllowUnsafe) modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword));
            var probeClass = SyntaxFactory.ClassDeclaration("__InterfaceHandlerProbe" + suffix)
                .WithModifiers(modifiers).WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>([original, projected]));
            var root = SyntaxFactory.CompilationUnit().AddMembers(WithContainingTypeContext(probeClass, concrete, generator)).NormalizeWhitespace();
            var tree = CSharpSyntaxTree.Create(root, receivingOptions,
                path: "__interface_handler_validation__.cs");
            var checkedCompilation = compilation.AddSyntaxTrees(tree);
            var errors = checkedCompilation.GetDiagnostics(ct).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.SourceTree == tree).ToArray();
            if (errors.Length > 0)
                throw new PublicInvalidOperationException("The extracted interface cannot preserve interpolated string handler binding: " +
                    FormatIds(errors) +
                    ". Keep the handler receiver and forwarded arguments compatible with the extracted interface.");

            var model = checkedCompilation.GetSemanticModel(tree);
            var methods = tree.GetRoot(ct).DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var expectedContract = ResolveInCompilation(projectedMember, checkedCompilation);
            var expectedOriginal = ResolveInCompilation(sourceMember, checkedCompilation);
            if (expectedContract is null || expectedOriginal is null || methods.Length != 2 ||
                !SymbolEqualityComparer.Default.Equals(BoundMember(model, methods[0], ct)?.OriginalDefinition, expectedOriginal.OriginalDefinition) ||
                !SymbolEqualityComparer.Default.Equals(BoundMember(model, methods[1], ct)?.OriginalDefinition, expectedContract.OriginalDefinition))
                throw new PublicInvalidOperationException("The extracted interface cannot preserve the intended interpolated string handler member binding. " +
                    "Select a contract whose handler calls bind to the same member.");
            var originalConstructors = HandlerConstructors(model.GetOperation(methods[0].Body!, ct)).ToArray();
            var projectedConstructors = HandlerConstructors(model.GetOperation(methods[1].Body!, ct)).ToArray();
            if (originalConstructors.Length != handlers.Count || projectedConstructors.Length != handlers.Count || originalConstructors.Where((constructor, index) =>
                    !SymbolEqualityComparer.Default.Equals(constructor, projectedConstructors[index])).Any())
                throw new PublicInvalidOperationException("The extracted interface changes an interpolated string handler constructor binding. " +
                    "The projected receiver cannot preserve the original constructor selection.");
        }
    }

    internal static string FormatIds(IEnumerable<Diagnostic> errors) =>
        string.Join(", ", errors.Select(error => error.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

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

    private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol concrete)
    {
        for (INamedTypeSymbol? current = concrete; current is not null; current = current.ContainingType)
            yield return current;
    }

    private static MethodDeclarationSyntax Probe(MemberDeclarationSyntax member, SeparatedSyntaxList<ParameterSyntax> parameters,
        HashSet<ParameterSyntax> handlers, HashSet<ParameterSyntax> dynamicParameters, IEnumerable<string> containingNames,
        SyntaxNode receiverType, string name)
    {
        var names = parameters.Select(parameter => parameter.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        names.UnionWith(containingNames);
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
