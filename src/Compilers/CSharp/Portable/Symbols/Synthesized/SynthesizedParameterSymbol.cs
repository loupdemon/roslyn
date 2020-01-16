// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Represents a simple compiler generated parameter of a given type.
    /// </summary>
    internal abstract class SynthesizedParameterSymbolBase : SourceParameterSymbolBase
    {
        private readonly TypeWithAnnotations _type;
        private readonly string _name;
        private readonly RefKind _refKind;

        public SynthesizedParameterSymbolBase(
            MethodSymbol container,
            TypeWithAnnotations type,
            int ordinal,
            RefKind refKind,
            string name = "")
            : base(container, ordinal)
        {
            RoslynDebug.Assert(type.HasType);
            RoslynDebug.Assert(name != null);
            RoslynDebug.Assert(ordinal >= 0);

            _type = type;
            _refKind = refKind;
            _name = name;
        }

        public override TypeWithAnnotations TypeWithAnnotations => _type;

        public override RefKind RefKind => _refKind;

        public sealed override bool IsDiscard => false;

        internal override bool IsMetadataIn => RefKind == RefKind.In;

        internal override bool IsMetadataOut => RefKind == RefKind.Out;

        internal override MarshalPseudoCustomAttributeData? MarshallingInformation
        {
            get { return null; }
        }

        public override string Name
        {
            get { return _name; }
        }

        public abstract override ImmutableArray<CustomModifier> RefCustomModifiers { get; }

        public override bool IsParams
        {
            get { return false; }
        }

        internal override bool IsMetadataOptional
        {
            get { return false; }
        }

        public override bool IsImplicitlyDeclared
        {
            get { return true; }
        }

        internal override ConstantValue? ExplicitDefaultConstantValue
        {
            get { return null; }
        }

        internal override bool IsIDispatchConstant
        {
            get { return false; }
        }

        internal override bool IsIUnknownConstant
        {
            get { return false; }
        }

        internal override bool IsCallerLineNumber
        {
            get { return false; }
        }

        internal override bool IsCallerFilePath
        {
            get { return false; }
        }

        internal override bool IsCallerMemberName
        {
            get { return false; }
        }

        internal override FlowAnalysisAnnotations FlowAnalysisAnnotations
        {
            get { return FlowAnalysisAnnotations.None; }
        }

        internal override ImmutableHashSet<string> NotNullIfParameterNotNull
        {
            get { return ImmutableHashSet<string>.Empty; }
        }

        public override ImmutableArray<Location> Locations
        {
            get { return ImmutableArray<Location>.Empty; }
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return ImmutableArray<SyntaxReference>.Empty;
            }
        }

        internal override ParameterSymbol WithCustomModifiersAndParams(TypeSymbol newType, ImmutableArray<CustomModifier> newCustomModifiers, ImmutableArray<CustomModifier> newRefCustomModifiers, bool newIsParams)
        {
            throw ExceptionUtilities.Unreachable;
        }

        internal override void AddSynthesizedAttributes(PEModuleBuilder moduleBuilder, ref ArrayBuilder<SynthesizedAttributeData> attributes)
        {
            // Emit [Dynamic] on synthesized parameter symbols when the original parameter was dynamic 
            // in order to facilitate debugging.  In the case the necessary attributes are missing 
            // this is a no-op.  Emitting an error here, or when the original parameter was bound, would
            // adversely effect the compilation or potentially change overload resolution.  
            var compilation = this.DeclaringCompilation;
            var type = this.TypeWithAnnotations;
            if (type.Type.ContainsDynamic() && compilation.HasDynamicEmitAttributes() && compilation.CanEmitBoolean())
            {
                AddSynthesizedAttribute(ref attributes, compilation.SynthesizeDynamicAttribute(type.Type, type.CustomModifiers.Length + this.RefCustomModifiers.Length, this.RefKind));
            }

            if (type.Type.ContainsTupleNames() &&
                compilation.HasTupleNamesAttributes &&
                compilation.CanEmitSpecialType(SpecialType.System_String))
            {
                AddSynthesizedAttribute(ref attributes,
                    compilation.SynthesizeTupleNamesAttribute(type.Type));
            }

            if (compilation.ShouldEmitNullableAttributes(this))
            {
                AddSynthesizedAttribute(ref attributes, moduleBuilder.SynthesizeNullableAttributeIfNecessary(this, GetNullableContextValue(), type));
            }

            if (this.RefKind == RefKind.RefReadOnly)
            {
                AddSynthesizedAttribute(ref attributes, moduleBuilder.SynthesizeIsReadOnlyAttribute(this));
            }
        }
    }

    internal sealed class SynthesizedParameterSymbol : SynthesizedParameterSymbolBase
    {
        private SynthesizedParameterSymbol(
            MethodSymbol container,
            TypeWithAnnotations type,
            int ordinal,
            RefKind refKind,
            string name)
            : base(container, type, ordinal, refKind, name)
        {
        }

        public static ParameterSymbol Create(
            MethodSymbol container,
            TypeWithAnnotations type,
            int ordinal,
            RefKind refKind,
            string name = "")
        {
            return new SynthesizedParameterSymbol(container, type, ordinal, refKind, name);
        }

        public static ParameterSymbol Create(MethodSymbol container, TypeWithAnnotations type, SourceParameterSymbolBase baseParameter, bool inheritAttributes)

        {
            return new SynthesizedComplexParameterSymbol(container, type, baseParameter, inheritAttributes);
        }

        /// <summary>
        /// For each parameter of a source method, construct a corresponding synthesized parameter
        /// for a destination method.
        /// </summary>
        /// <param name="sourceMethod">Has parameters.</param>
        /// <param name="destinationMethod">Needs parameters.</param>
        /// <returns>Synthesized parameters to add to destination method.</returns>
        internal static ImmutableArray<ParameterSymbol> DeriveParameters(MethodSymbol sourceMethod, MethodSymbol destinationMethod)
        {
            var builder = ArrayBuilder<ParameterSymbol>.GetInstance();

            foreach (var oldParam in sourceMethod.Parameters)
            {
                //same properties as the old one, just change the owner
                builder.Add(
                    Create(
                        destinationMethod, oldParam.TypeWithAnnotations, oldParam.Ordinal, oldParam.RefKind, oldParam.Name));
            }

            return builder.ToImmutableAndFree();
        }

        public override ImmutableArray<CustomModifier> RefCustomModifiers
        {
            get { return ImmutableArray<CustomModifier>.Empty; }
        }

        internal override bool HasEnumeratorCancellationAttribute
        {
            get { return false; }
        }

        internal override ParameterSymbol WithCustomModifiersAndParams(TypeSymbol newType, ImmutableArray<CustomModifier> newCustomModifiers, ImmutableArray<CustomModifier> newRefCustomModifiers, bool newIsParams)
        {
            throw new NotImplementedException();
        }

        internal override ConstantValue DefaultValueFromAttributes => throw new NotImplementedException();

        private sealed class SynthesizedComplexParameterSymbol : SynthesizedParameterSymbolBase
        {
            private readonly SourceParameterSymbolBase _baseParameter;
            private readonly bool _inheritAttributes;

            public SynthesizedComplexParameterSymbol(
                MethodSymbol container,
                TypeWithAnnotations type,
                SourceParameterSymbolBase baseParameter,
                bool inheritAttributes)
                : base(container,
                      type,
                      baseParameter.Ordinal,
                      baseParameter.RefKind,
                      baseParameter.Name)
            {
                _baseParameter = baseParameter;
                _inheritAttributes = inheritAttributes;

            }

            public override ImmutableArray<CustomModifier> RefCustomModifiers
            {
                get { return _baseParameter.RefCustomModifiers; }
            }

            public override ImmutableArray<CSharpAttributeData> GetAttributes()
            {
                return _inheritAttributes ? _baseParameter.GetAttributes() : ImmutableArray<CSharpAttributeData>.Empty;
            }

            internal override ConstantValue? DefaultValueFromAttributes => _inheritAttributes ? _baseParameter.DefaultValueFromAttributes : null;

            internal override bool HasEnumeratorCancellationAttribute => _inheritAttributes && _baseParameter.HasEnumeratorCancellationAttribute;

            // PROTOTYPE(local-function-attributes): add overrides for other well-known parameter attributes used after lowering
        }
    }
}
