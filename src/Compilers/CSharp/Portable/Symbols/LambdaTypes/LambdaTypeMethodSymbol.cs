// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Cci;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal sealed class LambdaTypeMethodSymbol : MethodSymbol
    {

        // PROTOTYPE: perhaps for lambda types we should try first simply binding them as their "underlying"
        // type, then go back in and create a real tuple-like symbol for them.

        // PROTOTYPE(lambda-types): LambdaParameterSymbol?
        private readonly ImmutableArray<ParameterSymbol> _parameters;

        public LambdaTypeMethodSymbol SubstituteParameterSymbols(
            TypeWithAnnotations substitutedReturnType,
            ImmutableArray<TypeWithAnnotations> substitutedParameterTypes,
            ImmutableArray<CustomModifier> refCustomModifiers = default,
            ImmutableArray<ImmutableArray<CustomModifier>> paramRefCustomModifiers = default)
            => new LambdaTypeMethodSymbol(
                this.CallingConvention,
                this.RefKind,
                substitutedReturnType,
                refCustomModifiers.IsDefault ? this.RefCustomModifiers : refCustomModifiers,
                this.Parameters,
                substitutedParameterTypes,
                paramRefCustomModifiers);

        internal LambdaTypeMethodSymbol MergeEquivalentTypes(LambdaTypeMethodSymbol signature, VarianceKind variance)
        {
            Debug.Assert(RefKind == signature.RefKind);
            var returnVariance = RefKind == RefKind.None ? variance : VarianceKind.None;
            var mergedReturnType = ReturnTypeWithAnnotations.MergeEquivalentTypes(signature.ReturnTypeWithAnnotations, returnVariance);

            var mergedParameterTypes = ImmutableArray<TypeWithAnnotations>.Empty;
            bool hasParamChanges = false;
            if (_parameters.Length > 0)
            {
                var paramMergedTypesBuilder = ArrayBuilder<TypeWithAnnotations>.GetInstance(_parameters.Length);
                for (int i = 0; i < _parameters.Length; i++)
                {
                    var thisParam = _parameters[i];
                    var otherParam = signature._parameters[i];
                    Debug.Assert(thisParam.RefKind == otherParam.RefKind);
                    var paramVariance = (variance, thisParam.RefKind) switch
                    {
                        (VarianceKind.In, RefKind.None) => VarianceKind.Out,
                        (VarianceKind.Out, RefKind.None) => VarianceKind.In,
                        _ => VarianceKind.None,
                    };

                    var mergedParameterType = thisParam.TypeWithAnnotations.MergeEquivalentTypes(otherParam.TypeWithAnnotations, paramVariance);
                    paramMergedTypesBuilder.Add(mergedParameterType);
                    if (!mergedParameterType.IsSameAs(thisParam.TypeWithAnnotations))
                    {
                        hasParamChanges = true;
                    }
                }

                if (hasParamChanges)
                {
                    mergedParameterTypes = paramMergedTypesBuilder.ToImmutableAndFree();
                }
                else
                {
                    paramMergedTypesBuilder.Free();
                    mergedParameterTypes = ParameterTypesWithAnnotations;
                }
            }

            if (hasParamChanges || !mergedReturnType.IsSameAs(ReturnTypeWithAnnotations))
            {
                return SubstituteParameterSymbols(mergedReturnType, mergedParameterTypes);
            }
            else
            {
                return this;
            }
        }

        public LambdaTypeMethodSymbol SetNullabilityForReferenceTypes(Func<TypeWithAnnotations, TypeWithAnnotations> transform)
        {
            var transformedReturn = transform(ReturnTypeWithAnnotations);

            var transformedParameterTypes = ImmutableArray<TypeWithAnnotations>.Empty;
            bool hasParamChanges = false;
            if (_parameters.Length > 0)
            {
                var paramTypesBuilder = ArrayBuilder<TypeWithAnnotations>.GetInstance(_parameters.Length);
                foreach (var param in _parameters)
                {
                    var transformedType = transform(param.TypeWithAnnotations);
                    paramTypesBuilder.Add(transformedType);
                    if (!transformedType.IsSameAs(param.TypeWithAnnotations))
                    {
                        hasParamChanges = true;
                    }
                }

                if (hasParamChanges)
                {
                    transformedParameterTypes = paramTypesBuilder.ToImmutableAndFree();
                }
                else
                {
                    paramTypesBuilder.Free();
                    transformedParameterTypes = ParameterTypesWithAnnotations;
                }

            }

            if (hasParamChanges || !transformedReturn.IsSameAs(ReturnTypeWithAnnotations))
            {
                return SubstituteParameterSymbols(transformedReturn, transformedParameterTypes);
            }
            else
            {
                return this;
            }
        }

        // PROTOTYPE(lambda-types)
        private LambdaTypeMethodSymbol(
            CallingConvention callingConvention,
            RefKind refKind,
            TypeWithAnnotations returnType,
            ImmutableArray<CustomModifier> refCustomModifiers,
            ImmutableArray<ParameterSymbol> originalParameters,
            ImmutableArray<TypeWithAnnotations> substitutedParameterTypes,
            ImmutableArray<ImmutableArray<CustomModifier>> substitutedRefCustomModifiers)
        {
            Debug.Assert(originalParameters.Length == substitutedParameterTypes.Length);
            Debug.Assert(substitutedRefCustomModifiers.IsDefault || originalParameters.Length == substitutedRefCustomModifiers.Length);
            RefCustomModifiers = refCustomModifiers;
            // PROTOTYPE(lambda-types)
            // CallingConvention = callingConvention;
            RefKind = refKind;
            ReturnTypeWithAnnotations = returnType;

            if (originalParameters.Length > 0)
            {
                var paramsBuilder = ArrayBuilder<ParameterSymbol>.GetInstance(originalParameters.Length);
                for (int i = 0; i < originalParameters.Length; i++)
                {
                    var originalParam = originalParameters[i];
                    var substitutedType = substitutedParameterTypes[i];
                    var customModifiers = substitutedRefCustomModifiers.IsDefault ? originalParam.RefCustomModifiers : substitutedRefCustomModifiers[i];
                    paramsBuilder.Add(new LambdaTypeParameterSymbol(
                        substitutedType,
                        originalParam.RefKind,
                        originalParam.Ordinal,
                        originalParam.Name,
                        containingSymbol: this,
                        customModifiers));
                }

                _parameters = paramsBuilder.ToImmutableAndFree();
            }
            else
            {
                _parameters = ImmutableArray<ParameterSymbol>.Empty;
            }
        }

        public LambdaTypeMethodSymbol(
            // RefKind refKind,
            TypeWithAnnotations returnType,
            ImmutableArray<CustomModifier> refCustomModifiers,
            LambdaTypeSyntax syntax,
            Binder typeBinder,
            BindingDiagnosticBag diagnostics)
        {
            RefCustomModifiers = refCustomModifiers;
            // RefKind = refKind;
            RefKind = RefKind.None; // PROTOTYPE(lambda-types): should we allow ref-returning lambdas?
            ReturnTypeWithAnnotations = returnType;

            _parameters = ParameterHelpers.MakeParameters(
                typeBinder,
                this,
                syntax.ParameterList,
                out _,
                diagnostics,
                allowRefOrOut: false, // PROTOTYPE(lambda-types)
                allowThis: false,
                addRefReadOnlyModifier: false);
        }

        internal void AddNullableTransforms(ArrayBuilder<byte> transforms)
        {
            ReturnTypeWithAnnotations.AddNullableTransforms(transforms);
            foreach (var param in Parameters)
            {
                param.TypeWithAnnotations.AddNullableTransforms(transforms);
            }
        }

        internal LambdaTypeMethodSymbol ApplyNullableTransforms(byte defaultTransformFlag, ImmutableArray<byte> transforms, ref int position)
        {
            bool madeChanges = ReturnTypeWithAnnotations.ApplyNullableTransforms(defaultTransformFlag, transforms, ref position, out var newReturnType);
            var newParamTypes = ImmutableArray<TypeWithAnnotations>.Empty;
            if (!Parameters.IsEmpty)
            {
                var paramTypesBuilder = ArrayBuilder<TypeWithAnnotations>.GetInstance(Parameters.Length);
                bool madeParamChanges = false;
                foreach (var param in Parameters)
                {
                    madeParamChanges |= param.TypeWithAnnotations.ApplyNullableTransforms(defaultTransformFlag, transforms, ref position, out var newParamType);
                    paramTypesBuilder.Add(newParamType);
                }

                if (madeParamChanges)
                {
                    newParamTypes = paramTypesBuilder.ToImmutableAndFree();
                    madeChanges = true;
                }
                else
                {
                    paramTypesBuilder.Free();
                    newParamTypes = ParameterTypesWithAnnotations;
                }
            }

            if (madeChanges)
            {
                return SubstituteParameterSymbols(newReturnType, newParamTypes);
            }
            else
            {
                return this;
            }
        }

        public override bool Equals(Symbol other, TypeCompareKind compareKind)
        {
            if (!(other is LambdaTypeMethodSymbol method))
            {
                return false;
            }

            return Equals(method, compareKind);
        }

        internal bool Equals(LambdaTypeMethodSymbol other, TypeCompareKind compareKind)
        {
            return ReferenceEquals(this, other) ||
                (EqualsNoParameters(other, compareKind)
                 && _parameters.SequenceEqual(other._parameters, compareKind,
                     (param1, param2, compareKind) => param1.Equals(param2, compareKind))); // PROTOTYPE(lambda-types): param1.Equals(...) might need a more specific helper that we put in LambdaTypeParameterSymbol
        }

        private bool EqualsNoParameters(LambdaTypeMethodSymbol other, TypeCompareKind compareKind)
        {
            if (CallingConvention != other.CallingConvention
                || !FunctionPointerTypeSymbol.RefKindEquals(compareKind, RefKind, other.RefKind)
                || !ReturnTypeWithAnnotations.Equals(other.ReturnTypeWithAnnotations, compareKind))
            {
                return false;
            }

            if (!RefCustomModifiers.SequenceEqual(other.RefCustomModifiers))
            {
                return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            var currentHash = GetHashCodeNoParameters();
            foreach (var param in _parameters)
            {
                currentHash = Hash.Combine(param.GetHashCode(), currentHash); // PROTOTYPE(lambda-types): param.GetHashCode(...) might need a more specific helper that we put in LambdaTypeParameterSymbol
            }
            return currentHash;
        }

        internal int GetHashCodeNoParameters()
            => Hash.Combine(ReturnType, Hash.Combine(CallingConvention.GetHashCode(), FunctionPointerTypeSymbol.GetRefKindForHashCode(RefKind).GetHashCode()));

        internal override CallingConvention CallingConvention => CallingConvention.Default;
        public override bool ReturnsVoid => ReturnTypeWithAnnotations.IsVoidType();
        public override RefKind RefKind { get; }
        public override TypeWithAnnotations ReturnTypeWithAnnotations { get; }
        public override ImmutableArray<ParameterSymbol> Parameters => _parameters;
        public override ImmutableArray<CustomModifier> RefCustomModifiers { get; }
        public override MethodKind MethodKind => MethodKind.LambdaTypeMethod;

        internal override UseSiteInfo<AssemblySymbol> GetUseSiteInfo()
        {
            UseSiteInfo<AssemblySymbol> info = default;
            CalculateUseSiteDiagnostic(ref info);

            if (CallingConvention.IsCallingConvention(CallingConvention.ExtraArguments))
            {
                MergeUseSiteInfo(ref info, new UseSiteInfo<AssemblySymbol>(new CSDiagnosticInfo(ErrorCode.ERR_UnsupportedCallingConvention, this)));
            }

            return info;
        }

        internal bool GetUnificationUseSiteDiagnosticRecursive(ref DiagnosticInfo? result, Symbol owner, ref HashSet<TypeSymbol> checkedTypes)
        {
            return ReturnType.GetUnificationUseSiteDiagnosticRecursive(ref result, owner, ref checkedTypes)
                || GetUnificationUseSiteDiagnosticRecursive(ref result, RefCustomModifiers, owner, ref checkedTypes)
                || GetUnificationUseSiteDiagnosticRecursive(ref result, Parameters, owner, ref checkedTypes);
        }

        public override bool IsVararg
        {
            get
            {
                var isVararg = CallingConvention.IsCallingConvention(CallingConvention.ExtraArguments);
                Debug.Assert(!isVararg || HasUseSiteError);
                return isVararg;
            }
        }

        public override Symbol? ContainingSymbol => null;
        // Function pointers cannot have type parameters
        public override int Arity => 0;
        public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;
        public override bool IsExtensionMethod => false;
        public override bool HidesBaseMethodsByName => false;
        public override bool IsAsync => false;
        public override ImmutableArray<MethodSymbol> ExplicitInterfaceImplementations => ImmutableArray<MethodSymbol>.Empty;
        public override Symbol? AssociatedSymbol => null;
        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;
        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => ImmutableArray<SyntaxReference>.Empty;
        public override Accessibility DeclaredAccessibility => Accessibility.NotApplicable;
        public override bool IsStatic => false;
        public override bool IsVirtual => false;
        public override bool IsOverride => false;
        public override bool IsAbstract => false;
        public override bool IsSealed => false;
        public override bool IsExtern => false;
        public override bool IsImplicitlyDeclared => true;
        public override ImmutableArray<TypeWithAnnotations> TypeArgumentsWithAnnotations => ImmutableArray<TypeWithAnnotations>.Empty;
        internal override bool HasSpecialName => false;
        internal override MethodImplAttributes ImplementationAttributes => default;
        internal override bool HasDeclarativeSecurity => false;
        internal override MarshalPseudoCustomAttributeData? ReturnValueMarshallingInformation => null;
        internal override bool RequiresSecurityObject => false;
        internal override bool IsDeclaredReadOnly => false;
        internal override bool IsInitOnly => false;
        internal override ImmutableArray<string> GetAppliedConditionalSymbols() => ImmutableArray<string>.Empty;
        public override FlowAnalysisAnnotations ReturnTypeFlowAnalysisAnnotations => FlowAnalysisAnnotations.None;
        public override ImmutableHashSet<string> ReturnNotNullIfParameterNotNull => ImmutableHashSet<string>.Empty;
        public override FlowAnalysisAnnotations FlowAnalysisAnnotations => FlowAnalysisAnnotations.None;
        internal override bool IsMetadataNewSlot(bool ignoreInterfaceImplementationChanges = false) => false;
        internal override bool IsMetadataVirtual(bool ignoreInterfaceImplementationChanges = false) => false;
        internal sealed override UnmanagedCallersOnlyAttributeData? GetUnmanagedCallersOnlyAttributeData(bool forceComplete) => null;

        internal override bool GenerateDebugInfo => throw ExceptionUtilities.Unreachable;
        internal override ObsoleteAttributeData? ObsoleteAttributeData => throw ExceptionUtilities.Unreachable;

        public override bool AreLocalsZeroed => throw ExceptionUtilities.Unreachable;
        public override DllImportData GetDllImportData() => throw ExceptionUtilities.Unreachable;
        internal override int CalculateLocalSyntaxOffset(int localPosition, SyntaxTree localTree) => throw ExceptionUtilities.Unreachable;
        internal override IEnumerable<SecurityAttribute> GetSecurityInformation() => throw ExceptionUtilities.Unreachable;
        internal sealed override bool IsNullableAnalysisEnabled() => throw ExceptionUtilities.Unreachable;
    }
}
