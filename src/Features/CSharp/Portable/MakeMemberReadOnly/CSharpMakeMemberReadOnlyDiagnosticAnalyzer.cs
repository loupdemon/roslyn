// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.MakeMemberReadOnly
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class CSharpMakeMemberReadOnlyDiagnosticAnalyzer : AbstractBuiltInCodeStyleDiagnosticAnalyzer
    {
        public CSharpMakeMemberReadOnlyDiagnosticAnalyzer()
            : base(IDEDiagnosticIds.InlineDeclarationDiagnosticId,
                   CodeStyleOptions.PreferInlinedVariableDeclaration,
                   new LocalizableResourceString(nameof(FeaturesResources.Inline_variable_declaration), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
                   new LocalizableResourceString(nameof(FeaturesResources.Variable_declaration_can_be_inlined), FeaturesResources.ResourceManager, typeof(FeaturesResources)))
        {
        }

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory()
            => DiagnosticAnalyzerCategory.SemanticSpanAnalysis;

        protected override void InitializeWorker(AnalysisContext context) =>
            context.RegisterSyntaxNodeAction(AnalyzeSyntaxNode, SyntaxKind.MethodDeclaration); // TODO: properties, accessors and events

        private void AnalyzeSyntaxNode(SyntaxNodeAnalysisContext context)
        {
            var csOptions = (CSharpParseOptions)context.Node.SyntaxTree.Options;
            if (csOptions.LanguageVersion < LanguageVersion.CSharp8)
            {
                // readonly members are not supported prior to C# 8.0.
                return;
            }

            var node = context.Node;
            var model = context.SemanticModel;
            var symbol = model.GetDeclaredSymbol(node);
            var methodSymbol = symbol switch
            {
                // TODO: properties, events, nulls?
                IMethodSymbol ms => ms,
                _ => throw ExceptionUtilities.Unreachable
            };

            if (methodSymbol == null || methodSymbol.IsReadOnly)
            {
                // error case or already readonly, no point in returning
                return;
            }

            var containingType = model.GetDeclaredSymbol(node)?.ContainingType;
            if (!containingType.IsStructType())
            {
                // can't be readonly
                return;
            }

            var dataFlow = context.SemanticModel.AnalyzeDataFlow(node);
            if (dataFlow.Succeeded && !dataFlow.WrittenInside.Any(symbol => symbol is IParameterSymbol parameterSymbol && parameterSymbol.IsThis))
            {
                context.ReportDiagnostic(DiagnosticHelper.Create(
                    Descriptor,
                    node.GetLocation(),
                    ReportDiagnostic.Info,
                    additionalLocations: ImmutableArray<Location>.Empty,
                    properties: null));
            }
        }

        private bool WouldCauseDefiniteAssignmentErrors(
            SemanticModel semanticModel,
            LocalDeclarationStatementSyntax localStatement,
            BlockSyntax enclosingBlock,
            ILocalSymbol outLocalSymbol)
        {
            // See if we have something like:
            //
            //      int i = 0;
            //      if (Goo() || Bar(out i))
            //      {
            //          Console.WriteLine(i);
            //      }
            //
            // In this case, inlining the 'i' would cause it to longer be definitely
            // assigned in the WriteLine invocation.

            var dataFlow = semanticModel.AnalyzeDataFlow(
                localStatement.GetNextStatement(),
                enclosingBlock.Statements.Last());
            return dataFlow.DataFlowsIn.Contains(outLocalSymbol);
        }

        private SyntaxNode GetOutArgumentScope(SyntaxNode argumentExpression)
        {
            for (var current = argumentExpression; current != null; current = current.Parent)
            {
                if (current.Parent is LambdaExpressionSyntax lambda &&
                    current == lambda.Body)
                {
                    // We were in a lambda.  The lambda body will be the new scope of the 
                    // out var.
                    return current;
                }

                // Any loop construct defines a scope for out-variables, as well as each of the following:
                // * Using statements
                // * Fixed statements
                // * Try statements (specifically for exception filters)
                switch (current.Kind())
                {
                    case SyntaxKind.WhileStatement:
                    case SyntaxKind.DoStatement:
                    case SyntaxKind.ForStatement:
                    case SyntaxKind.ForEachStatement:
                    case SyntaxKind.UsingStatement:
                    case SyntaxKind.FixedStatement:
                    case SyntaxKind.TryStatement:
                        return current;
                }

                if (current is StatementSyntax)
                {
                    // We hit a statement containing the out-argument.  Statements can have one of 
                    // two forms.  They're either parented by a block, or by another statement 
                    // (i.e. they're an embedded statement).  If we're parented by a block, then
                    // that block will be the scope of the new out-var.
                    //
                    // However, if our containing statement is not parented by a block, then that
                    // means we have something like:
                    //
                    //      if (x)
                    //          if (Try(out y))
                    //
                    // In this case, there is a 'virtual' block scope surrounding the embedded 'if'
                    // statement, and that will be the scope the out-var goes into.
                    return current.IsParentKind(SyntaxKind.Block)
                        ? current.Parent
                        : current;
                }
            }

            return null;
        }

        private bool IsAccessed(
            SemanticModel semanticModel,
            ISymbol outSymbol,
            BlockSyntax enclosingBlockOfLocalStatement,
            LocalDeclarationStatementSyntax localStatement,
            ArgumentSyntax argumentNode,
            CancellationToken cancellationToken)
        {
            var localStatementStart = localStatement.Span.Start;
            var argumentNodeStart = argumentNode.Span.Start;
            var variableName = outSymbol.Name;

            // Walk the block that the local is declared in looking for accesses.
            // We can ignore anything prior to the actual local declaration point,
            // and we only need to check up until we reach the out-argument.
            foreach (var descendentNode in enclosingBlockOfLocalStatement.DescendantNodes())
            {
                var descendentStart = descendentNode.Span.Start;
                if (descendentStart <= localStatementStart)
                {
                    // This node is before the local declaration.  Can ignore it entirely as it could
                    // not be an access to the local.
                    continue;
                }

                if (descendentStart >= argumentNodeStart)
                {
                    // We reached the out-var.  We can stop searching entirely.
                    break;
                }

                if (descendentNode.IsKind(SyntaxKind.IdentifierName, out IdentifierNameSyntax identifierName))
                {
                    // See if this looks like an accessor to the local variable syntactically.
                    if (identifierName.Identifier.ValueText == variableName)
                    {
                        // Confirm that it is a access of the local.
                        var symbol = semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol;
                        if (outSymbol.Equals(symbol))
                        {
                            // We definitely accessed the local before the out-argument.  We 
                            // can't inline this local.
                            return true;
                        }
                    }
                }
            }

            // No accesses detected
            return false;
        }
    }
}
