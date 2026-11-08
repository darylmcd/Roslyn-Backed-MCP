using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// The one place that turns a public instance member of a concrete type into an interface member
/// declaration, for every interface-extraction route. The member is derived from the symbol's own
/// declaration (its declaring syntax, or <see cref="SyntaxGenerator.Declaration"/> where no syntax
/// exists, e.g. a positional record property) and then stripped of everything an interface member
/// cannot carry: accessibility and implementation modifiers, bodies, initializers, non-public
/// accessors, and attributes that are not part of the call contract. Return kinds (ref / ref readonly),
/// indexers, generic parameters and their constraints, events, accessor kinds, escaped identifiers,
/// pointer types, parameter modifiers/defaults/attributes, type spelling and nullable annotations
/// therefore survive exactly as written, with no per-construct branch.
/// </summary>
internal sealed class InterfaceMemberProjector
{
    // Attributes that change how a caller binds or flows nullability through the member. Everything
    // else (implementation markers, ParamArray/ScopedRef the modifiers already carry, handler arguments
    // that need a binding probe) stays out of the contract.
    private static readonly HashSet<string> _contractAttributes = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.CallerMemberNameAttribute",
        "System.Runtime.CompilerServices.CallerLineNumberAttribute",
        "System.Runtime.CompilerServices.CallerFilePathAttribute",
        "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute",
        "System.Runtime.CompilerServices.DateTimeConstantAttribute",
        "System.Runtime.CompilerServices.DecimalConstantAttribute",
        "System.Runtime.InteropServices.OptionalAttribute",
        "System.Runtime.InteropServices.DefaultParameterValueAttribute",
        "System.Diagnostics.CodeAnalysis.AllowNullAttribute",
        "System.Diagnostics.CodeAnalysis.DisallowNullAttribute",
        "System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullAttribute",
        "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
        "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute",
        "System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute",
        "System.Diagnostics.CodeAnalysis.UnscopedRefAttribute",
        "System.Diagnostics.CodeAnalysis.ConstantExpectedAttribute",
        "System.Diagnostics.CodeAnalysis.StringSyntaxAttribute",
        "System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute",
    };

    private readonly Compilation _compilation;
    private readonly SyntaxGenerator _generator;
    private readonly InterfaceNullableProjection _nullable;
    private readonly List<ITypeSymbol> _referencedTypes = [];
    private readonly bool _receiverIsSourceAssembly;
    private readonly bool _receiverAllowsUnsafe;

    private InterfaceMemberProjector(
        Compilation compilation, SyntaxGenerator generator, InterfaceNullableProjection nullable, bool receiverIsSourceAssembly, bool receiverAllowsUnsafe)
    {
        _compilation = compilation;
        _generator = generator;
        _nullable = nullable;
        _receiverIsSourceAssembly = receiverIsSourceAssembly;
        _receiverAllowsUnsafe = receiverAllowsUnsafe;
    }

    /// <param name="receivingProject">
    /// The project the interface lands in. When it is the source type's own project, source-assembly names stay
    /// spellable; otherwise a constant default naming one is written as its value. Its compilation options decide
    /// whether a pointer or function-pointer signature can be emitted (<c>AllowUnsafeBlocks</c>).
    /// </param>
    internal static async Task<InterfaceMemberProjector> CreateAsync(
        Document sourceDocument, InterfaceNullableProjection nullable, Project receivingProject, CancellationToken ct)
    {
        var model = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("Semantic model for the interface source document is unavailable.");
        var options = receivingProject.CompilationOptions as CSharpCompilationOptions
            ?? throw InvalidOperationErrors.Internal("The receiving project must have C# compilation options.");
        return new InterfaceMemberProjector(
            model.Compilation, SyntaxGenerator.GetGenerator(sourceDocument), nullable,
            receivingProject.Id == sourceDocument.Project.Id, options.AllowUnsafe);
    }

    /// <summary>The members of a type that an extracted interface can carry.</summary>
    internal static bool IsProjectable(ISymbol member) =>
        !member.IsStatic && !member.IsImplicitlyDeclared && member.DeclaredAccessibility == Accessibility.Public &&
        member is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol;

    /// <summary>
    /// Projects <paramref name="member"/> and records every type the declaration now refers to so
    /// <see cref="RequiredNamespaces"/> can name the namespaces the interface file must import.
    /// </summary>
    internal MemberDeclarationSyntax Project(ISymbol member)
    {
        var source = DeclaringSyntax(member);
        var model = source is not null && _compilation.ContainsSyntaxTree(source.SyntaxTree)
            ? _compilation.GetSemanticModel(source.SyntaxTree)
            : null;
        source ??= _generator.Declaration(member) as MemberDeclarationSyntax
            ?? throw InvalidOperationErrors.Internal("Interface member symbol has no projectable declaration.");

        var qualifier = new ScopeQualifier(model, _generator, _referencedTypes, _receiverIsSourceAssembly);
        var declaration = Strip(source, member, model, qualifier);
        if (declaration.DescendantNodes().Any(n => n is PointerTypeSyntax or FunctionPointerTypeSyntax))
        {
            // CS0227: a pointer signature compiles only where unsafe code is allowed. Refuse here, before any caller
            // stores a preview or adds a document; the receiving project's policy is never changed implicitly.
            if (!_receiverAllowsUnsafe)
            {
                throw new PublicInvalidOperationException(
                    $"Interface member '{member.Name}' requires unsafe signatures; receiving project does not enable AllowUnsafeBlocks. " +
                    "Enable AllowUnsafeBlocks in the receiving project.");
            }

            declaration = declaration.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.UnsafeKeyword)));
        }

        declaration = declaration.ReplaceTokens(declaration.DescendantTokens(), (token, _) => token.WithoutTrivia());
        RecordReferencedTypes(member);
        return _nullable.Capture(declaration, member);
    }

    /// <summary>
    /// The namespaces of every type the projected members refer to, excluding the interface's own
    /// namespace (a self-referential using would be invalid).
    /// </summary>
    internal IReadOnlyCollection<string> RequiredNamespaces(string? ownNamespace)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in _referencedTypes) TypeNamespaceWalker.Collect(namespaces, type, ownNamespace);
        // A namespace reachable only through an extern alias cannot be imported as a plain
        // `using Namespace;` in the receiving project. Keep imports needed by a global reference.
        var aliasOnly = new HashSet<string>(StringComparer.Ordinal);
        var globallyAvailable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in ReferencedTypes)
        {
            if (type.ContainingNamespace is not { IsGlobalNamespace: false } typeNamespace) continue;
            var name = typeNamespace.ToDisplayString();
            (HasGlobalReference(type) ? globallyAvailable : aliasOnly).Add(name);
        }

        aliasOnly.ExceptWith(globallyAvailable);
        namespaces.ExceptWith(aliasOnly);
        namespaces.Remove(ownNamespace ?? string.Empty);
        return namespaces;
    }

    internal IReadOnlyCollection<ITypeSymbol> ReferencedTypes => _referencedTypes
        .SelectMany(TypeNamespaceWalker.Enumerate)
        .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default)
        .ToArray();

    private bool HasGlobalReference(ITypeSymbol type)
    {
        if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly)) return true;
        var references = _compilation.References.Where(reference =>
            SymbolEqualityComparer.Default.Equals(_compilation.GetAssemblyOrModuleSymbol(reference), type.ContainingAssembly));
        return !references.Any() || references.Any(reference => reference.Properties.Aliases.IsDefaultOrEmpty
            || reference.Properties.Aliases.Contains("global"));
    }

    private static MemberDeclarationSyntax? DeclaringSyntax(ISymbol member)
    {
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax())
            {
                case MethodDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax:
                    return (MemberDeclarationSyntax)reference.GetSyntax();
                case VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax field }:
                    return field;
            }
        }

        return null;
    }

    private MemberDeclarationSyntax Strip(MemberDeclarationSyntax source, ISymbol member, SemanticModel? model, ScopeQualifier qualifier)
    {
        T Qualified<T>(T node) where T : SyntaxNode => (T)qualifier.Visit(node)!;
        SyntaxList<AttributeListSyntax> Attributes(SyntaxList<AttributeListSyntax> lists) => ContractAttributes(lists, model);

        switch (source, member)
        {
            case (MethodDeclarationSyntax method, IMethodSymbol methodSymbol):
                var parameters = Qualified(method.ParameterList);
                return SyntaxFactory.MethodDeclaration(
                    Attributes(method.AttributeLists),
                    default,
                    Qualified(method.ReturnType),
                    explicitInterfaceSpecifier: null,
                    method.Identifier,
                    method.TypeParameterList,
                    parameters.WithParameters(ContractParameters(method.ParameterList.Parameters, parameters.Parameters, model)),
                    methodSymbol.IsOverride
                        ? InheritedConstraints(methodSymbol)
                        : SyntaxFactory.List(method.ConstraintClauses.Select(clause => Qualified(clause))),
                    body: null,
                    expressionBody: null,
                    SyntaxFactory.Token(SyntaxKind.SemicolonToken));
            case (PropertyDeclarationSyntax property, IPropertySymbol symbol):
                return SyntaxFactory.PropertyDeclaration(
                    Attributes(property.AttributeLists),
                    default,
                    Qualified(property.Type),
                    explicitInterfaceSpecifier: null,
                    property.Identifier,
                    Accessors(symbol, property.AccessorList, model),
                    expressionBody: null,
                    initializer: null,
                    default);
            case (IndexerDeclarationSyntax indexer, IPropertySymbol symbol):
                var bracketed = Qualified(indexer.ParameterList);
                return SyntaxFactory.IndexerDeclaration(
                    Attributes(indexer.AttributeLists),
                    default,
                    Qualified(indexer.Type),
                    explicitInterfaceSpecifier: null,
                    indexer.ThisKeyword,
                    bracketed.WithParameters(ContractParameters(indexer.ParameterList.Parameters, bracketed.Parameters, model)),
                    Accessors(symbol, indexer.AccessorList, model),
                    expressionBody: null,
                    default);
            case (EventDeclarationSyntax evt, IEventSymbol):
                return EventField(Attributes(evt.AttributeLists), Qualified(evt.Type), evt.Identifier);
            case (EventFieldDeclarationSyntax field, IEventSymbol symbol):
                var declarator = field.Declaration.Variables.Single(v => v.Identifier.ValueText == symbol.Name);
                return EventField(Attributes(field.AttributeLists), Qualified(field.Declaration.Type), declarator.Identifier);
            default:
                throw InvalidOperationErrors.Internal("Interface member declaration does not match its symbol kind.");
        }
    }

    /// <summary>An override inherits its constraints from the base method, so its syntax has none: rebuild them from the symbol.</summary>
    private static SyntaxList<TypeParameterConstraintClauseSyntax> InheritedConstraints(IMethodSymbol method)
    {
        static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
        var clauses = new List<string>();
        foreach (var parameter in method.TypeParameters)
        {
            var constraints = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
            else if (parameter.HasValueTypeConstraint) constraints.Add("struct");
            if (parameter.HasNotNullConstraint) constraints.Add("notnull");
            constraints.AddRange(parameter.ConstraintTypes.Select(t => t.ToDisplayString(_containingTypeFormat)));
            if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint && !parameter.HasUnmanagedTypeConstraint) constraints.Add("new()");
            if (constraints.Count > 0) clauses.Add($"where {Escape(parameter.Name)} : {string.Join(", ", constraints)}");
        }

        var names = string.Join(", ", method.TypeParameters.Select(p => Escape(p.Name)));
        var parsed = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration($"void M<{names}>() {string.Join(" ", clauses)};")!;
        return parsed.ConstraintClauses;
    }

    private static EventFieldDeclarationSyntax EventField(SyntaxList<AttributeListSyntax> attributes, TypeSyntax type, SyntaxToken identifier) =>
        SyntaxFactory.EventFieldDeclaration(
            attributes,
            default,
            SyntaxFactory.Token(SyntaxKind.EventKeyword),
            SyntaxFactory.VariableDeclaration(type, SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(identifier))),
            SyntaxFactory.Token(SyntaxKind.SemicolonToken));

    /// <summary>Public accessors only; <c>init</c> stays <c>init</c>; accessor attributes are filtered like any other.</summary>
    private AccessorListSyntax Accessors(IPropertySymbol property, AccessorListSyntax? sourceAccessors, SemanticModel? model)
    {
        var accessors = new List<AccessorDeclarationSyntax>();
        void Add(IMethodSymbol? method, SyntaxKind kind)
        {
            if (method is not { DeclaredAccessibility: Accessibility.Public }) return;
            var attributes = sourceAccessors?.Accessors.FirstOrDefault(a => a.IsKind(kind))?.AttributeLists ?? default;
            accessors.Add(SyntaxFactory.AccessorDeclaration(kind)
                .WithAttributeLists(ContractAttributes(attributes, model))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
        }

        Add(property.GetMethod, SyntaxKind.GetAccessorDeclaration);
        Add(property.SetMethod, property.SetMethod is { IsInitOnly: true } ? SyntaxKind.InitAccessorDeclaration : SyntaxKind.SetAccessorDeclaration);
        return SyntaxFactory.AccessorList(SyntaxFactory.List(accessors));
    }

    /// <summary>
    /// <paramref name="source"/> is the declaration's own parameter list, which the semantic model can bind;
    /// <paramref name="rewritten"/> is the same list after <see cref="ScopeQualifier"/> (same shape, but nodes
    /// the model no longer knows), and supplies the text that is emitted.
    /// </summary>
    private SeparatedSyntaxList<ParameterSyntax> ContractParameters(
        SeparatedSyntaxList<ParameterSyntax> source, SeparatedSyntaxList<ParameterSyntax> rewritten, SemanticModel? model) =>
        SyntaxFactory.SeparatedList(rewritten.Select((p, i) => p
            .WithAttributeLists(ContractAttributes(source[i].AttributeLists, p.AttributeLists, model))
            .WithModifiers(SyntaxFactory.TokenList(p.Modifiers.Where(m => !m.IsKind(SyntaxKind.ThisKeyword))))));

    private static SyntaxList<AttributeListSyntax> ContractAttributes(SyntaxList<AttributeListSyntax> lists, SemanticModel? model) =>
        ContractAttributes(lists, lists, model);

    /// <summary>
    /// Keeps only contract attributes: decided on <paramref name="source"/> (bound by the model), emitted from
    /// <paramref name="rewritten"/> (the same attribute lists after qualification). Without a semantic model
    /// (generator-built declarations) the attribute classes cannot be identified, so none are kept.
    /// </summary>
    private static SyntaxList<AttributeListSyntax> ContractAttributes(
        SyntaxList<AttributeListSyntax> source, SyntaxList<AttributeListSyntax> rewritten, SemanticModel? model)
    {
        if (model is null) return default;
        var kept = new List<AttributeListSyntax>();
        for (var i = 0; i < source.Count; i++)
        {
            // `field:` targets address a backing field the interface member does not have.
            if (source[i].Target?.Identifier.IsKind(SyntaxKind.FieldKeyword) == true) continue;
            var attributes = rewritten[i].Attributes
                .Where((_, j) => IsContractAttribute(model.GetSymbolInfo(source[i].Attributes[j]).Symbol?.ContainingType)).ToArray();
            if (attributes.Length > 0) kept.Add(rewritten[i].WithAttributes(SyntaxFactory.SeparatedList(attributes)));
        }

        return SyntaxFactory.List(kept);
    }

    private static bool IsContractAttribute(INamedTypeSymbol? attributeClass) =>
        attributeClass is not null && _contractAttributes.Contains(attributeClass.ToDisplayString());

    private void RecordReferencedTypes(ISymbol member)
    {
        void Attributes(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attribute in attributes)
            {
                if (IsContractAttribute(attribute.AttributeClass)) _referencedTypes.Add(attribute.AttributeClass!);
            }
        }

        void Parameters(IEnumerable<IParameterSymbol> parameters)
        {
            foreach (var parameter in parameters)
            {
                _referencedTypes.Add(parameter.Type);
                Attributes(parameter.GetAttributes());
            }
        }

        Attributes(member.GetAttributes());
        switch (member)
        {
            case IMethodSymbol method:
                _referencedTypes.Add(method.ReturnType);
                Attributes(method.GetReturnTypeAttributes());
                Parameters(method.Parameters);
                foreach (var constraint in method.TypeParameters.SelectMany(t => t.ConstraintTypes)) _referencedTypes.Add(constraint);
                break;
            case IPropertySymbol property:
                _referencedTypes.Add(property.Type);
                Parameters(property.Parameters);
                foreach (var accessor in new[] { property.GetMethod, property.SetMethod })
                {
                    if (accessor is { DeclaredAccessibility: Accessibility.Public }) Attributes(accessor.GetAttributes());
                }

                break;
            case IEventSymbol evt:
                _referencedTypes.Add(evt.Type);
                break;
        }
    }

    // MinimallyQualifiedFormat is NameOnly: it would print a nested type as `Inner<U>` and lose its containing type.
    private static readonly SymbolDisplayFormat _containingTypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayFormat.MinimallyQualifiedFormat.GenericsOptions,
        miscellaneousOptions: SymbolDisplayFormat.MinimallyQualifiedFormat.MiscellaneousOptions);

    /// <summary>
    /// Rewrites names that only mean something inside the declaring type (a nested type spelled by its
    /// simple name, a constant of the type or a base type used as a default) into names that bind from
    /// the interface's own scope, and records the types it qualifies with. A constant default value or
    /// attribute argument that names something the interface cannot spell (a <c>nameof</c>, or a member of
    /// the source assembly or a non-public member: the receiving project is usually another assembly) is
    /// written as its value instead: a compile-time constant is the same contract either way.
    /// </summary>
    private sealed class ScopeQualifier(SemanticModel? model, SyntaxGenerator generator, List<ITypeSymbol> referencedTypes, bool receiverIsSourceAssembly) : CSharpSyntaxRewriter
    {
        /// <summary>Public all the way up, and declared outside the source assembly unless the interface lands in it.</summary>
        private bool IsNameableFromInterface(ISymbol symbol)
        {
            if (!receiverIsSourceAssembly && SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, model!.Compilation.Assembly)) return false;
            for (var current = symbol; current is not null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility != Accessibility.Public) return false;
            }

            return true;
        }

        private bool NamesUnnameableSymbol(ExpressionSyntax expression)
        {
            foreach (var name in expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
            {
                var symbol = model!.GetSymbolInfo(name).Symbol;
                // An instance field only appears under `nameof`, and an interface has no fields to name.
                if (symbol is IFieldSymbol { IsStatic: false }) return true;
                if (symbol is IFieldSymbol or INamedTypeSymbol && !IsNameableFromInterface(symbol)) return true;
            }

            return false;
        }

        private ExpressionSyntax? InlineUnnameableConstant(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax) return null;
            var constant = model!.GetConstantValue(expression);
            if (!constant.HasValue || constant.Value is null || !NamesUnnameableSymbol(expression)) return null;

            var literal = (ExpressionSyntax)generator.LiteralExpression(constant.Value);
            if (model.GetTypeInfo(expression).Type is not { TypeKind: TypeKind.Enum } enumType) return literal.WithTriviaFrom(expression);

            referencedTypes.Add(enumType);
            // `(E)-1` parses as a subtraction, so a negative value needs its own parentheses.
            if (literal.ToString().StartsWith('-')) literal = SyntaxFactory.ParenthesizedExpression(literal);
            return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(enumType.ToDisplayString(_containingTypeFormat)), literal)
                .WithTriviaFrom(expression);
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (model is null) return node;
            return node is ExpressionSyntax { Parent: EqualsValueClauseSyntax or AttributeArgumentSyntax } expression &&
                InlineUnnameableConstant(expression) is { } inlined
                ? inlined
                : base.Visit(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => Qualify(node) ?? base.VisitIdentifierName(node);

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node) => Qualify(node) ?? base.VisitGenericName(node);

        /// <summary>A default value, or the argument of an attribute the interface keeps (a dropped attribute's names are not emitted).</summary>
        private bool InDefaultOrContractAttributeArgument(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                if (ancestor is EqualsValueClauseSyntax) return true;
                if (ancestor is AttributeArgumentSyntax { Parent.Parent: AttributeSyntax attribute })
                    return IsContractAttribute(model!.GetSymbolInfo(attribute).Symbol?.ContainingType);
            }

            return false;
        }

        private SyntaxNode? Qualify(SimpleNameSyntax node)
        {
            if (node.Parent is QualifiedNameSyntax qualified && qualified.Right == node ||
                node.Parent is AliasQualifiedNameSyntax or NameColonSyntax or NameEqualsSyntax or MemberBindingExpressionSyntax ||
                node.Parent is MemberAccessExpressionSyntax access && access.Name == node)
            {
                return null;
            }

            switch (model!.GetSymbolInfo(node).Symbol)
            {
                case INamedTypeSymbol { ContainingType: not null } nested:
                    referencedTypes.Add(nested);
                    return SyntaxFactory.ParseTypeName(nested.ToDisplayString(_containingTypeFormat)).WithTriviaFrom(node);
                case INamedTypeSymbol type when InDefaultOrContractAttributeArgument(node):
                    // A default such as `Limits.Cap` keeps its type name, so the interface file must import the type's namespace.
                    referencedTypes.Add(type);
                    return null;
                case IFieldSymbol { ContainingType: not null, IsStatic: true } or IPropertySymbol { ContainingType: not null, IsStatic: true }:
                    var member = model.GetSymbolInfo(node).Symbol!;
                    referencedTypes.Add(member.ContainingType);
                    return SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ParseName(member.ContainingType.ToDisplayString(_containingTypeFormat)),
                        SyntaxFactory.IdentifierName(SyntaxFacts.GetKeywordKind(member.Name) == SyntaxKind.None ? member.Name : "@" + member.Name)).WithTriviaFrom(node);
                default:
                    return null;
            }
        }
    }
}
