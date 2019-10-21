// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Base class to represent all source method-like symbols. This includes
    /// things like ordinary methods and constructors, and functions
    /// like lambdas and local functions.
    /// </summary>
    internal abstract class SourceMethodSymbol : MethodSymbol
    {
        /// <summary>
        /// If there are no constraints, returns an empty immutable array. Otherwise, returns an immutable
        /// array of clauses, indexed by the constrained type parameter in <see cref="MethodSymbol.TypeParameters"/>.
        /// If a type parameter does not have constraints, the corresponding entry in the array is null.
        /// </summary>
        public abstract ImmutableArray<TypeParameterConstraintClause> GetTypeParameterConstraintClauses();

        protected static void ReportBadRefToken(TypeSyntax returnTypeSyntax, DiagnosticBag diagnostics)
        {
            if (!returnTypeSyntax.HasErrors)
            {
                var refKeyword = returnTypeSyntax.GetFirstToken();
                diagnostics.Add(ErrorCode.ERR_UnexpectedToken, refKeyword.GetLocation(), refKeyword.ToString());
            }
        }

        internal void GenerateExternalMethodWarnings(DiagnosticBag diagnostics)
        {
            if (this.GetAttributes().IsEmpty && !this.ContainingType.IsComImport)
            {
                // external method with no attributes
                var errorCode = (this.MethodKind == MethodKind.Constructor || this.MethodKind == MethodKind.StaticConstructor) ?
                    ErrorCode.WRN_ExternCtorNoImplementation :
                    ErrorCode.WRN_ExternMethodNoImplementation;
                diagnostics.Add(errorCode, this.Locations[0], this);
            }
        }

        /// <summary>
        /// Symbol to copy bound attributes from, or null if the attributes are not shared among multiple source method symbols.
        /// </summary>
        /// <remarks>
        /// Used for example for event accessors. The "remove" method delegates attribute binding to the "add" method. 
        /// The bound attribute data are then applied to both accessors.
        /// </remarks>
        protected virtual SourceMethodSymbol BoundAttributesSource
        {
            get
            {
                return null;
            }
        }

        internal virtual CustomAttributesBag<CSharpAttributeData> GetAttributesBag()
        {
            return null;
        }

        internal virtual CustomAttributesBag<CSharpAttributeData> GetReturnTypeAttributesBag()
        {
            return null;
        }
    }
}
