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
            : base(IDEDiagnosticIds.MakeMemberReadonlyDiagnosticId,
                   CodeStyleOptions.PreferReadonly, // TODO: does there need to be another option for readonly members?
                   new LocalizableResourceString(nameof(FeaturesResources.Add_readonly_modifier), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
                   new LocalizableResourceString(nameof(FeaturesResources.Make_field_readonly), FeaturesResources.ResourceManager, typeof(FeaturesResources)))
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

            var body = node switch
            {
                MethodDeclarationSyntax methodSyntax => (CSharpSyntaxNode)methodSyntax.ExpressionBody?.Expression ?? methodSyntax.Body,
                _ => throw ExceptionUtilities.UnexpectedValue(node)
            };

            var dataFlow = context.SemanticModel.AnalyzeDataFlow(body);
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
    }
}
