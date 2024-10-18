using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

#nullable enable

[TestFixture]
internal class MethodExtensionsTests : CecilifierContextBasedTestBase
{
    protected override string Snippet => """
                                         using System.Threading.Tasks;
                                         public class Foo
                                         {
                                            Task TopLevelType(TaskCompletionSource<int> tcs) => tcs.Task.ContinueWith(null);
                                         }
                                         """;
    protected override IEnumerable<MetadataReference> ExtraAssemblyReferences() => [MetadataReference.CreateFromFile(typeof(TestScenario).Assembly.Location)];

    [Test]
    public void ResolvingTypesFromExternalAssembly_UsesReflectionName()
    {
        var context = NewContext();
       
        var methodSyntax = GetMethodSyntax(context, "TopLevelType");
        var continueWithInvocation = methodSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        var methodSymbol = context.SemanticModel.GetSymbolInfo(continueWithInvocation.Expression).Symbol as IMethodSymbol;

        var resolvedMethod = methodSymbol.MethodResolverExpression(context);
        Assert.That(resolvedMethod, Does.Match(""".+ImportReference\(.+ResolveMethod\(typeof\(.+Task\<System.Int32\>\), "ContinueWith",.+, "System.Action`1\[\[System.Threading.Tasks.Task`1\[\[System.Int32\]\]\]\]"\)\)"""));
    }
    
    [Test]
    public void ResolvingInnerTypesFromExternalAssembly_UsesReflectionName()
    {
        var context = NewContext();

        var testType = context.SemanticModel.Compilation.GetTypeByMetadataName("Cecilifier.Core.Tests.Tests.Unit.TestScenario");
        var testMethod = testType.GetMembers("UseNestedType").Single().EnsureNotNull<ISymbol, IMethodSymbol>();

        var resolvedMethod = testMethod.MethodResolverExpression(context);
        Assert.That(resolvedMethod, Does.Match(""".+ImportReference\(.+ResolveMethod\(typeof\(.+TestScenario\), "UseNestedType",.+, "Cecilifier.Core.Tests.Tests.Unit.SomeClass`1\[\[System.Int32\]\]\+Cecilifier.Core.Tests.Tests.Unit.Inner"""));
    }
}

public class TestScenario
{
    public void UseNestedType(SomeClass<int>.Inner inner) {}
}

public class SomeClass<T>
{
    public class Inner { }
}
