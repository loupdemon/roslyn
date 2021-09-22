// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Roslyn.Utilities;
using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Represents a lambda type such as 'int (int x, int y)'.
    /// </summary>
    internal sealed class LambdaTypeSymbol : TypeSymbol
    {
        internal LambdaTypeMethodSymbol MethodSymbol { get; }
        internal LambdaTypeSymbol(LambdaTypeMethodSymbol methodSymbol)
        {
            this.MethodSymbol = methodSymbol;
        }

        public override Accessibility DeclaredAccessibility
        {
            get { return Accessibility.NotApplicable; }
        }

        public override bool IsStatic
        {
            get
            {
                return false;
            }
        }

        public override bool IsAbstract
        {
            get
            {
                return false;
            }
        }

        public override bool IsSealed
        {
            get
            {
                return false;
            }
        }

        internal override NamedTypeSymbol? BaseTypeNoUseSiteDiagnostics
        {
            get
            {
                // PROTOTYPE(lambda-types): System.Delegate?
                return null;
            }
        }

        internal override ImmutableArray<NamedTypeSymbol> InterfacesNoUseSiteDiagnostics(ConsList<TypeSymbol> basesBeingResolved)
        {
            // Pointers do not support boxing, so they really have no interfaces
            return ImmutableArray<NamedTypeSymbol>.Empty;
        }

        public override bool IsReferenceType
        {
            get
            {
                return false;
            }
        }

        public override bool IsValueType
        {
            get
            {
                return true;
            }
        }

        internal sealed override ManagedKind GetManagedKind(ref CompoundUseSiteInfo<AssemblySymbol> useSiteInfo) => ManagedKind.Unmanaged;

        public sealed override bool IsRefLikeType
        {
            get
            {
                return false;
            }
        }

        public sealed override bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        internal sealed override ObsoleteAttributeData? ObsoleteAttributeData
        {
            get { return null; }
        }

        public override ImmutableArray<Symbol> GetMembers()
        {
            return ImmutableArray<Symbol>.Empty;
        }

        public override ImmutableArray<Symbol> GetMembers(string name)
        {
            return ImmutableArray<Symbol>.Empty;
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers()
        {
            return ImmutableArray<NamedTypeSymbol>.Empty;
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name)
        {
            return ImmutableArray<NamedTypeSymbol>.Empty;
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            return ImmutableArray<NamedTypeSymbol>.Empty;
        }

        public override SymbolKind Kind
        {
            get
            {
                return SymbolKind.LambdaType;
            }
        }

        public override TypeKind TypeKind
        {
            get
            {
                return TypeKind.LambdaType;
            }
        }

        public override Symbol? ContainingSymbol
        {
            get
            {
                return null;
            }
        }

        public override ImmutableArray<Location> Locations
        {
            get
            {
                return ImmutableArray<Location>.Empty;
            }
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return ImmutableArray<SyntaxReference>.Empty;
            }
        }

        internal override TResult Accept<TArgument, TResult>(CSharpSymbolVisitor<TArgument, TResult> visitor, TArgument argument)
        {
            // PROTOTYPE(lambda-types)
            // return visitor.VisitPointerType(this, argument);
            return visitor.DefaultVisit(this, argument);
        }

        public override void Accept(CSharpSymbolVisitor visitor)
        {
            // PROTOTYPE(lambda-types)
            // visitor.VisitPointerType(this);
            visitor.DefaultVisit(this);
        }

        public override TResult Accept<TResult>(CSharpSymbolVisitor<TResult> visitor)
        {
            // PROTOTYPE(lambda-types)
            // return visitor.VisitPointerType(this);
            return visitor.DefaultVisit(this);
        }

        public override int GetHashCode()
        {
            return this.MethodSymbol.GetHashCode();
        }

        internal override bool Equals(TypeSymbol t2, TypeCompareKind comparison)
        {
            return this.Equals(t2 as LambdaTypeSymbol, comparison);
        }

        private bool Equals(LambdaTypeSymbol? other, TypeCompareKind comparison)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if ((object?)other == null || !other.MethodSymbol.Equals(this.MethodSymbol, comparison))
            {
                return false;
            }

            return true;
        }

        internal override void AddNullableTransforms(ArrayBuilder<byte> transforms)
        {
            MethodSymbol.AddNullableTransforms(transforms);
        }

        internal override bool ApplyNullableTransforms(byte defaultTransformFlag, ImmutableArray<byte> transforms, ref int position, out TypeSymbol result)
        {
            // PROTOTYPE(lambda-types):
            throw new NotImplementedException();
            // TypeWithAnnotations oldPointedAtType = PointedAtTypeWithAnnotations;
            // TypeWithAnnotations newPointedAtType;

            // if (!oldPointedAtType.ApplyNullableTransforms(defaultTransformFlag, transforms, ref position, out newPointedAtType))
            // {
            //     result = this;
            //     return false;
            // }

            // result = WithPointedAtType(newPointedAtType);
            // return true;
        }

        internal override TypeSymbol SetNullabilityForReferenceTypes(Func<TypeWithAnnotations, TypeWithAnnotations> transform)
        {
            // PROTOTYPE(lambda-types):
            throw new NotImplementedException();
            // return WithPointedAtType(transform(PointedAtTypeWithAnnotations));
        }

        internal override TypeSymbol MergeEquivalentTypes(TypeSymbol other, VarianceKind variance)
        {
            // PROTOTYPE(lambda-types):
            throw new NotImplementedException();
            // Debug.Assert(this.Equals(other, TypeCompareKind.IgnoreDynamicAndTupleNames | TypeCompareKind.IgnoreNullableModifiersForReferenceTypes));
            // TypeWithAnnotations pointedAtType = PointedAtTypeWithAnnotations.MergeEquivalentTypes(((PointerTypeSymbol)other).PointedAtTypeWithAnnotations, VarianceKind.None);
            // return WithPointedAtType(pointedAtType);
        }

        internal override UseSiteInfo<AssemblySymbol> GetUseSiteInfo()
        {
            // PROTOTYPE(lambda-types):
            throw new NotImplementedException();
            // UseSiteInfo<AssemblySymbol> result = default;

            // // Check type, custom modifiers
            // DeriveUseSiteInfoFromType(ref result, this.PointedAtTypeWithAnnotations, AllowedRequiredModifierType.None);
            // return result;
        }

        internal override bool GetUnificationUseSiteDiagnosticRecursive(ref DiagnosticInfo result, Symbol owner, ref HashSet<TypeSymbol> checkedTypes)
        {
            // PROTOTYPE(lambda-types):
            throw new NotImplementedException();
            // return this.PointedAtTypeWithAnnotations.GetUnificationUseSiteDiagnosticRecursive(ref result, owner, ref checkedTypes);
        }

        protected override ISymbol CreateISymbol()
        {
            // PROTOTYPE(lambda-types):
            throw new NotImplementedException();
            // return new PublicModel.PointerTypeSymbol(this, DefaultNullableAnnotation);
        }

        protected override ITypeSymbol CreateITypeSymbol(CodeAnalysis.NullableAnnotation nullableAnnotation)
        {
            // PROTOTYPE(lambda-types):
            throw new NotImplementedException();
            // Debug.Assert(nullableAnnotation != DefaultNullableAnnotation);
            // return new PublicModel.PointerTypeSymbol(this, nullableAnnotation);
        }

        internal override bool IsRecord => false;

        internal override bool IsRecordStruct => false;

        internal sealed override IEnumerable<(MethodSymbol Body, MethodSymbol Implemented)> SynthesizedInterfaceMethodImpls()
        {
            return SpecializedCollections.EmptyEnumerable<(MethodSymbol Body, MethodSymbol Implemented)>();
        }
    }
}
