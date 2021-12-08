// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Shared.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeAnalysis.CSharp.UseParameterNullChecking
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class UseParameterNullCheckingDiagnosticAnalyzer
        : AbstractBuiltInCodeStyleDiagnosticAnalyzer
    {
        private const string ArgumentNullExceptionName = $"{nameof(System)}.{nameof(ArgumentNullException)}";

        public UseParameterNullCheckingDiagnosticAnalyzer()
            : base(IDEDiagnosticIds.UseParameterNullCheckingId,
                   EnforceOnBuildValues.UseParameterNullChecking,
                   CodeStyleOptions2.PreferParameterNullChecking,
                   CSharpAnalyzersResources.Use_parameter_null_checking,
                   new LocalizableResourceString(nameof(AnalyzersResources.Null_check_can_be_simplified), AnalyzersResources.ResourceManager, typeof(AnalyzersResources)))
        {
        }

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory()
            => DiagnosticAnalyzerCategory.SemanticSpanAnalysis;

        protected override void InitializeWorker(AnalysisContext context)
            => context.RegisterCompilationStartAction(context =>
            {
                if (((CSharpCompilation)context.Compilation).LanguageVersion < LanguageVersionExtensions.CSharpNext)
                {
                    return;
                }

                var compilation = context.Compilation;
                var argumentNullException = compilation.GetTypeByMetadataName(ArgumentNullExceptionName);
                if (argumentNullException is null)
                {
                    return;
                }

                var objectType = compilation.GetSpecialType(SpecialType.System_Object);
                var referenceEqualsMethod = objectType
                    .GetMembers(nameof(ReferenceEquals))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 2);
                // This seems sufficiently rare that it's not a big deal to just not perform analysis in this case.
                if (referenceEqualsMethod is null)
                {
                    return;
                }

                context.RegisterSyntaxNodeAction(context => AnalyzeSyntax(context, argumentNullException, referenceEqualsMethod), SyntaxKind.MethodDeclaration);
            });

        private void AnalyzeSyntax(SyntaxNodeAnalysisContext context, ITypeSymbol argumentNullException, IMethodSymbol referenceEqualsMethod)
        {
            var cancellationToken = context.CancellationToken;

            var semanticModel = context.SemanticModel;
            var syntaxTree = semanticModel.SyntaxTree;

            var option = context.Options.GetOption(CodeStyleOptions2.PreferParameterNullChecking, semanticModel.Language, syntaxTree, cancellationToken);
            if (!option.Value)
            {
                return;
            }

            var node = (MethodDeclarationSyntax)context.Node;
            if (node.Body is not BlockSyntax block)
            {
                return;
            }

            var methodSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (methodSymbol is null)
            {
                return;
            }

            foreach (var statement in block.Statements)
            {
                switch (statement)
                {
                    case IfStatementSyntax ifStatement:
                        ExpressionSyntax left, right;
                        switch (ifStatement)
                        {
                            case { Condition: BinaryExpressionSyntax { OperatorToken.RawKind: (int)SyntaxKind.EqualsEqualsToken } binary }:
                                left = binary.Left;
                                right = binary.Right;
                                break;
                            case { Condition: IsPatternExpressionSyntax { Expression: var patternInput, Pattern: ConstantPatternSyntax { Expression: var patternExpression } } }:
                                left = patternInput;
                                right = patternExpression;
                                break;
                            case { Condition: InvocationExpressionSyntax { Expression: var receiver, ArgumentList.Arguments: { Count: 2 } arguments } }:
                                if (!referenceEqualsMethod.Equals(semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol))
                                {
                                    continue;
                                }

                                left = arguments[0].Expression;
                                right = arguments[1].Expression;
                                break;

                            default:
                                continue;
                        }

                        if (!areOperandsApplicable(left, right)
                            && !areOperandsApplicable(right, left))
                        {
                            continue;
                        }

                        var throwStatement = ifStatement.Statement switch
                        {
                            ThrowStatementSyntax @throw => @throw,
                            BlockSyntax { Statements: { Count: 1 } statements } => statements[0] as ThrowStatementSyntax,
                            _ => null
                        };

                        if (throwStatement is null
                            || throwStatement.Expression is not ObjectCreationExpressionSyntax thrownInIf
                            || !argumentNullException.Equals(semanticModel.GetTypeInfo(thrownInIf, cancellationToken).Type))
                        {
                            continue;
                        }

                        break;

                    case ExpressionStatementSyntax
                    {
                        Expression: AssignmentExpressionSyntax
                        {
                            Right: BinaryExpressionSyntax
                            {
                                OperatorToken.RawKind: (int)SyntaxKind.QuestionQuestionToken,
                                Left: ExpressionSyntax maybeParameter,
                                Right: ThrowExpressionSyntax { Expression: ObjectCreationExpressionSyntax thrownInNullCoalescing }
                            }
                        }
                    }:
                        if (!isParameter(maybeParameter) || !argumentNullException.Equals(semanticModel.GetTypeInfo(thrownInNullCoalescing, cancellationToken).Type))
                        {
                            continue;
                        }

                        break;
                }

                context.ReportDiagnostic(DiagnosticHelper.Create(Descriptor, statement.GetLocation(), option.Notification.Severity, additionalLocations: null, properties: null));
            }

            bool areOperandsApplicable(ExpressionSyntax maybeParameter, ExpressionSyntax maybeNullLiteral)
            {
                if (!maybeNullLiteral.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    return false;
                }

                return isParameter(maybeParameter);
            }

            bool isParameter(ExpressionSyntax maybeParameter)
            {
                if (maybeParameter is CastExpressionSyntax { Type: var type, Expression: var operand })
                {
                    if (semanticModel.GetTypeInfo(type).Type?.SpecialType != SpecialType.System_Object)
                    {
                        return false;
                    }

                    maybeParameter = operand;
                }

                if (semanticModel.GetSymbolInfo(maybeParameter).Symbol is not IParameterSymbol { ContainingSymbol: { } containingSymbol } || !containingSymbol.Equals(methodSymbol))
                {
                    return false;
                }

                return true;
            }
        }
    }
}
