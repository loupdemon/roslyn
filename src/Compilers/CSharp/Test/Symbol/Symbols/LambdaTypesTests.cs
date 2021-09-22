// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Symbols
{
    public class LambdaTypesTests : CSharpTestBase
    {
        [Fact]
        public void LambdaType_01()
        {
            var source = @"
class C
{
    void M(string (int x, object y) z)
    {
    }
}";
            var compilation = CreateCompilation(source).VerifyDiagnostics();
            var method = compilation.GetMember<MethodSymbol>("C.M");
            var param = method.Parameters.Single();
            var paramType = param.Type;
            // PROTOTYPE(lambda-types): should eventually be a tuple-like "LambdaTypeSymbol"
            AssertEx.Equal("System.Func<System.Object, System.Int32, System.String>", paramType.ToTestDisplayString());
        }
    }
}
