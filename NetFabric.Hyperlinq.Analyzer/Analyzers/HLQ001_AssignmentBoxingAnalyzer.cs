using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NetFabric.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetFabric.Hyperlinq.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AssignmentBoxingAnalyzer : DiagnosticAnalyzer
    {
        const string DiagnosticId = DiagnosticIds.AssignmentBoxingId;

        static readonly LocalizableString Title =
            new LocalizableResourceString(nameof(Resources.AssignmentBoxing_Title), Resources.ResourceManager, typeof(Resources));
        static readonly LocalizableString MessageFormat =
            new LocalizableResourceString(nameof(Resources.AssignmentBoxing_MessageFormat), Resources.ResourceManager, typeof(Resources));
        static readonly LocalizableString Description =
            new LocalizableResourceString(nameof(Resources.AssignmentBoxing_Description), Resources.ResourceManager, typeof(Resources));
        const string Category = "Performance";

        static readonly DiagnosticDescriptor rule =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, 
                isEnabledByDefault: true, description: Description,
                helpLinkUri: "https://github.com/NetFabric/NetFabric.Hyperlinq.Analyzer/tree/master/docs/reference/HLQ001_AssignmentBoxing.md");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.RegisterSyntaxNodeAction(AnalyzeSimpleAssignment, SyntaxKind.SimpleAssignmentExpression);
            context.RegisterSyntaxNodeAction(AnalyzeEqualsValueClause, SyntaxKind.EqualsValueClause);
        }

        static void AnalyzeSimpleAssignment(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is AssignmentExpressionSyntax assignmentExpression))
                return;

            var semanticModel = context.SemanticModel;

            var rightTypeSymbol = semanticModel.GetTypeInfo(assignmentExpression.Right).Type as INamedTypeSymbol;
            if (!IsEnumerableType(rightTypeSymbol, context.Compilation, out var enumeratorType) || enumeratorType.IsReferenceType)
                return;

            var leftSymbol = semanticModel.GetSymbolInfo(assignmentExpression.Left).Symbol;
            if (leftSymbol is null)
                return;

            switch (leftSymbol)
            {
                case IFieldSymbol fieldSymbol:
                    if (fieldSymbol.DeclaredAccessibility == Accessibility.Public)
                        return;
                    break;
                case IPropertySymbol propertySymbol:
                    if (propertySymbol.DeclaredAccessibility == Accessibility.Public)
                        return;
                    break;
            }

            var leftTypeSymbol = semanticModel.GetTypeInfo(assignmentExpression.Left).Type as INamedTypeSymbol;
            if (!IsEnumerableType(leftTypeSymbol, context.Compilation, out enumeratorType) || enumeratorType.IsValueType)
                return;

            var diagnostic = Diagnostic.Create(rule, assignmentExpression.Left.GetLocation(), rightTypeSymbol.MetadataName, leftTypeSymbol.MetadataName);
            context.ReportDiagnostic(diagnostic);
        }

        static void AnalyzeEqualsValueClause(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is EqualsValueClauseSyntax equalsValueClauseSyntax))
                return;

            var semanticModel = context.SemanticModel;

            var typeSymbol = semanticModel.GetTypeInfo(equalsValueClauseSyntax.Value).Type as INamedTypeSymbol; 
            if (!IsEnumerableType(typeSymbol, context.Compilation, out var enumeratorType) || enumeratorType.IsReferenceType)
                return;

            if (equalsValueClauseSyntax.Parent is PropertyDeclarationSyntax propertyDeclarationSyntax)
            {
                AnalyzePropertyDeclaration(context, propertyDeclarationSyntax, typeSymbol);
                return;
            }

            if (!(equalsValueClauseSyntax.Parent is VariableDeclaratorSyntax variableDeclaratorSyntax))
                return;

            if (!(variableDeclaratorSyntax.Parent is VariableDeclarationSyntax variableDeclarationSyntax))
                return;

            switch (variableDeclarationSyntax.Parent)
            {
                case LocalDeclarationStatementSyntax localDeclarationStatementSyntax:
                    AnalyzeLocalDeclaration(context, localDeclarationStatementSyntax, typeSymbol);
                    break;

                case FieldDeclarationSyntax fieldDeclarationSyntax:
                    AnalyzeFieldDeclaration(context, fieldDeclarationSyntax, typeSymbol);
                    break;
            }
        }

        static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context, PropertyDeclarationSyntax propertyDeclarationSyntax, ITypeSymbol enumerableTypeSymbol)
        {
            if (propertyDeclarationSyntax.Modifiers.Any(token => token.Text == "public"))
                return;

            var semanticModel = context.SemanticModel;

            var typeSymbol = semanticModel.GetDeclaredSymbol(propertyDeclarationSyntax).Type;
            if (!IsEnumerableType(typeSymbol, context.Compilation, out var enumeratorType) || enumeratorType.IsValueType)
                return;

            var diagnostic = Diagnostic.Create(rule, propertyDeclarationSyntax.Type.GetLocation(), enumerableTypeSymbol.MetadataName, typeSymbol.MetadataName);
            context.ReportDiagnostic(diagnostic);
        }

        static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context, LocalDeclarationStatementSyntax localDeclarationStatementSyntax, ITypeSymbol enumerableTypeSymbol)
        {
            var typeSyntax = localDeclarationStatementSyntax.Declaration.Type;
            var semanticModel = context.SemanticModel;
            var typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
            if (!IsEnumerableType(typeSymbol, context.Compilation, out var enumeratorType) || enumeratorType.IsValueType)
                return;

            var diagnostic = Diagnostic.Create(rule, typeSyntax.GetLocation(), enumerableTypeSymbol.MetadataName, typeSymbol.MetadataName);
            context.ReportDiagnostic(diagnostic);
        }

        static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context, FieldDeclarationSyntax fieldDeclarationSyntax, ITypeSymbol enumerableTypeSymbol)
        {
            if (fieldDeclarationSyntax.Modifiers.Any(token => token.Text == "public"))
                return;

            var typeSyntax = fieldDeclarationSyntax.Declaration.Type;
            var semanticModel = context.SemanticModel;
            var typeSymbol = semanticModel.GetTypeInfo(typeSyntax).Type as INamedTypeSymbol;
            if (!IsEnumerableType(typeSymbol, context.Compilation, out var enumeratorType) || enumeratorType.IsValueType)
                return;

            var diagnostic = Diagnostic.Create(rule, typeSyntax.GetLocation(), enumerableTypeSymbol.MetadataName, typeSymbol.MetadataName);
            context.ReportDiagnostic(diagnostic);
        }

        static bool IsEnumerableType(ITypeSymbol typeSymbol, Compilation compilation, out ITypeSymbol enumeratorType)
        {
            if (typeSymbol is object)
            {
                if (typeSymbol.IsEnumerable(compilation, out var enumerableSymbols))
                {
                    enumeratorType = enumerableSymbols.GetEnumerator.ReturnType;
                    return true;
                }

                if (typeSymbol.IsAsyncEnumerable(compilation, out var asyncEnumerableSymbols))
                {
                    enumeratorType = asyncEnumerableSymbols.GetAsyncEnumerator.ReturnType;
                    return true;
                }
            }

            enumeratorType = default;
            return false;
        }
    }
}