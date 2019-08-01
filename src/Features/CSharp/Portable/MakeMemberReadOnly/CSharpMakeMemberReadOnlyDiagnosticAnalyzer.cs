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
                   new LocalizableResourceString(nameof(FeaturesResources.Make_member_readonly), FeaturesResources.ResourceManager, typeof(FeaturesResources)))
        {
        }

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory()
            => DiagnosticAnalyzerCategory.SemanticSpanAnalysis;

        protected override void InitializeWorker(AnalysisContext context) =>
            context.RegisterSyntaxNodeAction(AnalyzeSyntaxNode,
                SyntaxKind.MethodDeclaration,
                SyntaxKind.PropertyDeclaration);

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

            switch (symbol, node)
            {
                case (IMethodSymbol methodSymbol, MethodDeclarationSyntax methodDeclaration):
                    var methodBody = (CSharpSyntaxNode)methodDeclaration.ExpressionBody?.Expression ?? methodDeclaration.Body;
                    if (CanMethodBeReadOnly(model, methodSymbol, methodBody))
                    {
                        reportDiagnostic(methodDeclaration);
                    }
                    break;

                case (IPropertySymbol propertySymbol, PropertyDeclarationSyntax propertyDeclaration):
                    var getterDeclaration = propertyDeclaration.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                    var getterBody = (CSharpSyntaxNode)getterDeclaration?.Body ?? getterDeclaration?.ExpressionBody?.Expression ?? propertyDeclaration.ExpressionBody?.Expression;
                    var getterCanBeReadOnly = getterBody is object && CanMethodBeReadOnly(model, propertySymbol.GetMethod, getterBody);

                    var setterDeclaration = propertyDeclaration.AccessorList?.Accessors.FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration));
                    var setterBody = (CSharpSyntaxNode)setterDeclaration?.Body ?? setterDeclaration?.ExpressionBody?.Expression;
                    var setterCanBeReadOnly = setterBody is object && CanMethodBeReadOnly(model, propertySymbol.SetMethod, setterBody);

                    var propertyCanBeReadOnly = (getterBody is object || setterBody is object)
                        && (getterBody is null || getterCanBeReadOnly)
                        && (setterBody is null || setterCanBeReadOnly);

                    if (propertyCanBeReadOnly)
                    {
                        reportDiagnostic(propertyDeclaration);
                    }
                    else if (getterCanBeReadOnly)
                    {
                        reportDiagnostic(getterDeclaration);
                    }
                    else if (setterCanBeReadOnly)
                    {
                        reportDiagnostic(setterDeclaration);
                    }
                    break;
            };

            void reportDiagnostic(SyntaxNode node)
            {
                context.ReportDiagnostic(DiagnosticHelper.Create(
                    Descriptor,
                    node.GetLocation(),
                    ReportDiagnostic.Info,
                    additionalLocations: ImmutableArray<Location>.Empty,
                    properties: null));
            }
        }

        private bool CanMethodBeReadOnly(SemanticModel model, IMethodSymbol methodSymbol, CSharpSyntaxNode body)
        {
            if (methodSymbol == null || methodSymbol.IsReadOnly)
            {
                // error case or already readonly, no point in returning
                return false;
            }

            var containingType = methodSymbol.ContainingType;
            if (!containingType.IsStructType())
            {
                // can't be readonly
                return false;
            }

            var dataFlow = model.AnalyzeDataFlow(body);
            if (dataFlow.Succeeded && !dataFlow.WrittenInside.Any(symbol => symbol is IParameterSymbol parameterSymbol && parameterSymbol.IsThis))
            {
                return true;
            }

            return false;
        }
    }
}
