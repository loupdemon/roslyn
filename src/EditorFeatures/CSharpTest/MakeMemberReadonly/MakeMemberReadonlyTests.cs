// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.MakeMemberReadOnly;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.MakeFieldReadonly
{
    public class MakeMemberReadonlyTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
    {
        internal override (DiagnosticAnalyzer, CodeFixProvider) CreateDiagnosticProviderAndFixer(Workspace workspace)
            => (new CSharpMakeMemberReadOnlyDiagnosticAnalyzer(), new CSharpMakeMemberReadOnlyCodeFixProvider());

        [Theory, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        [InlineData("public")]
        [InlineData("internal")]
        [InlineData("protected")]
        [InlineData("protected internal")]
        [InlineData("private protected")]
        public async Task NonPrivateMethod(string accessibility)
        {
            await TestInRegularAndScriptAsync(
$@"struct MyStruct
{{
    {accessibility} int [|M()|] => 42;
}}", $@"struct MyStruct
{{
    {accessibility} readonly int M() => 42;
}}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task MethodDoesNotAssignThis()
        {
            await TestInRegularAndScriptAsync(
@"struct MyStruct
{
    private int [|M()|] => 42;
}",
@"struct MyStruct
{
    private readonly int M() => 42;
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task MethodAssignsInstanceField()
        {
            await TestMissingInRegularAndScriptAsync(
@"struct MyStruct
{
    private int _goo;
    private void [|M()|]
    {
        _goo++;
    }
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task MethodAssignsStaticField()
        {
            await TestInRegularAndScriptAsync(
@"struct MyStruct
{
    private static int _goo;
    private void [|M()|]
    {
        _goo++;
    }
}", @"struct MyStruct
{
    private static int _goo;
    private readonly void [|M()|]
    {
        _goo++;
    }
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task MethodAssignsInnerFieldOfReferenceTypeField()
        {
            await TestInRegularAndScriptAsync(
@"class MyClass
{
    internal int _goo;
}

struct MyStruct
{
    private MyClass _field;
    private void [|M()|]
    {
        _field._goo++;
    }
}", @"class MyClass
{
    internal int _goo;
}

struct MyStruct
{
    private MyClass _field;
    private readonly void M()
    {
        _field._goo++;
    }
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task Constructor()
        {
            await TestMissingInRegularAndScriptAsync(
@"struct MyStruct
{
    private static int _i;
    internal [|MyStruct(int i)|] { _i = i; }
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task AutoProperty()
        {
            await TestMissingInRegularAndScriptAsync(
@"struct MyStruct
{
    [|int Prop { get; set; }|]
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task AutoProperty_GetterOnly()
        {
            await TestMissingInRegularAndScriptAsync(
@"struct MyStruct
{
    [|int Prop { get; }|]
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task Property_GetterCanBeReadOnly()
        {
            await TestInRegularAndScriptAsync(
@"struct MyStruct
{
    int _field;
    [|int Prop { get => field; set => _field = value; }|]
}", @"struct MyStruct
{
    int _field;
    [|int Prop { readonly get => field; set => _field = value; }|]
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task Property_SetterCanBeReadOnly()
        {
            await TestInRegularAndScriptAsync(
@"struct MyStruct
{
    int _field;
    [|int Prop { get => _field++; set => _ = value; }|]
}", @"struct MyStruct
{
    int _field;
    [|int Prop { get => _field++; readonly set => _ = value; }|]
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task Property_GetterOnly_CanBeReadOnly()
        {
            await TestInRegularAndScriptAsync(
@"struct MyStruct
{
    int _field;
    [|int Prop { get => _field;  }|]
}", @"struct MyStruct
{
    int _field;
    [|readonly int Prop { get => _field;  }|]
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task Property_ExpressionBody_CanBeReadOnly()
        {
            await TestInRegularAndScriptAsync(
@"struct MyStruct
{
    [|int Prop => 42;|]
}", @"struct MyStruct
{
    [|readonly int Prop => 42;|]
}");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task FixAll()
        {
            await TestInRegularAndScriptAsync(
@"class MyClass
{
    private int {|FixAllInDocument:_goo|} = 0, _bar = 0;
    private int _x = 0, _y = 0, _z = 0;
    private int _fizz = 0;

    void Method() { _z = 1; }
}",
@"class MyClass
{
    private readonly int _goo = 0, _bar = 0;
    private readonly int _x = 0;
    private readonly int _y = 0;
    private int _z = 0;
    private readonly int _fizz = 0;

    void Method() { _z = 1; }
}");
        }

        [Fact]
        [Trait(Traits.Feature, Traits.Features.CodeActionsMakeFieldReadonly)]
        public async Task FixAll2()
        {
            await TestInRegularAndScriptAsync(
@"  using System;

    partial struct MyClass
    {
        private static Func<int, bool> {|FixAllInDocument:_test1|} = x => x > 0;
        private static Func<int, bool> _test2 = x => x < 0;

        private static Func<int, bool> _test3 = x =>
        {
            return x == 0;
        };

        private static Func<int, bool> _test4 = x =>
        {
            return x != 0;
        };
    }

    partial struct MyClass { }",
@"  using System;

    partial struct MyClass
    {
        private static readonly Func<int, bool> _test1 = x => x > 0;
        private static readonly Func<int, bool> _test2 = x => x < 0;

        private static readonly Func<int, bool> _test3 = x =>
        {
            return x == 0;
        };

        private static readonly Func<int, bool> _test4 = x =>
        {
            return x != 0;
        };
    }

    partial struct MyClass { }");
        }
    }
}
