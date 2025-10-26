using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Tests.Tests.Unit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

#nullable enable

[TestFixture]
internal class MethodExtensionsTests : CecilifierContextBasedTestBase<MonoCecilContext>
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

        var testType = context.SemanticModel.Compilation.GetTypeByMetadataName("Cecilifier.Core.Tests.Tests.Unit.TestScenario").EnsureNotNull();
        var testMethod = testType.GetMembers("UseNestedType").Single().EnsureNotNull<ISymbol, IMethodSymbol>();

        var resolvedMethod = testMethod.MethodResolverExpression(context);
        Assert.That(resolvedMethod, Does.Match(""".+ImportReference\(.+ResolveMethod\(typeof\(.+TestScenario\), "UseNestedType",.+, "Cecilifier.Core.Tests.Tests.Unit.SomeClass`1\[\[System.Int32\]\]\+Cecilifier.Core.Tests.Tests.Unit.Inner"""));
    }

    [Test]
    public void OverloadResolution_DifferentNumberOfArguments()
    {
        var context = NewContext();

        var testType = context.SemanticModel.Compilation.GetTypeByMetadataName("Cecilifier.Core.Tests.Tests.Unit.SomeClass`1").EnsureNotNull();
        var testMethod = testType.GetMembers("Overload1").OfType<IMethodSymbol>().First(m => m.Parameters.Length == 2).EnsureNotNull<ISymbol, IMethodSymbol>();

        var resolvedMethod = testMethod.MethodResolverExpression(context);
        Assert.That(resolvedMethod, Is.EqualTo("r_overload1_2"));
        
        Assert.That(context.Output, Does.Match("""
                                               \s+var (?<open_method>l_openOverload\d+_\d+) = .+ImportReference\(typeof\(.+SomeClass<>\)\)\.Resolve\(\)\.Methods.First\(m => m.Name == "Overload1" && m.Parameters.Count == 2 && !m.Parameters.Select\(p => p.ParameterType.FullName\).Except\(\["T","System.String",\]\).Any\(\)\);
                                               \s+var r_overload1_2 = new MethodReference\("Overload1", assembly.MainModule.ImportReference\(\k<open_method>\).ReturnType\)
                                               \s+{
                                               \s+DeclaringType = l_someClass_0,
                                               \s+HasThis = \k<open_method>.HasThis,
                                               \s+ExplicitThis = \k<open_method>.ExplicitThis,
                                               \s+CallingConvention = \k<open_method>.CallingConvention,
                                               \s+};
                                               \s+r_overload1_2.Parameters.Add\(new ParameterDefinition\("t", l_openOverload1_1.Parameters\[0\].Attributes, \k<open_method>.Parameters\[0\].ParameterType\)\);
                                               \s+r_overload1_2.Parameters.Add\(new ParameterDefinition\("s", l_openOverload1_1.Parameters\[1\].Attributes, \k<open_method>.Parameters\[1\].ParameterType\)\);
                                               """));
    }
    
    [Test]
    public void OverloadResolution_SameNumberOfArguments()
    {
        var context = NewContext();

        var testType = context.SemanticModel.Compilation.GetTypeByMetadataName("Cecilifier.Core.Tests.Tests.Unit.SomeClass`1").EnsureNotNull();
        var testMethod = testType.GetMembers("Overload2").OfType<IMethodSymbol>().First(m => m.Parameters.Length == 2).EnsureNotNull<ISymbol, IMethodSymbol>();

        var resolvedMethod = testMethod.MethodResolverExpression(context);
        Assert.That(context.Output, Does.Match("""
                                               \s+var (?<open_method>l_openOverload\d+_\d+) = .+ImportReference\(typeof\(.+SomeClass<>\)\)\.Resolve\(\)\.Methods.First\(m => m.Name == "Overload2" && m.Parameters.Count == 2 && !m.Parameters.Select\(p => p.ParameterType.FullName\).Except\(\["T","System.Boolean",\]\).Any\(\)\);
                                               \s+var r_overload2_2 = new MethodReference\("Overload2", assembly.MainModule.ImportReference\(\k<open_method>\).ReturnType\)
                                               \s+{
                                               \s+DeclaringType = l_someClass_0,
                                               \s+HasThis = \k<open_method>.HasThis,
                                               \s+ExplicitThis = \k<open_method>.ExplicitThis,
                                               \s+CallingConvention = \k<open_method>.CallingConvention,
                                               \s+};
                                               \s+r_overload2_2.Parameters.Add\(new ParameterDefinition\("t", l_openOverload2_1.Parameters\[0\].Attributes, \k<open_method>.Parameters\[0\].ParameterType\)\);
                                               \s+r_overload2_2.Parameters.Add\(new ParameterDefinition\("b", l_openOverload2_1.Parameters\[1\].Attributes, \k<open_method>.Parameters\[1\].ParameterType\)\);
                                               """));
    }
}

public class TestScenario
{
    public void UseNestedType(SomeClass<int>.Inner inner) {}
}

public class SomeClass<T>
{
    public class Inner { }
    
    public void Overload1(T t) {}
    public void Overload1(T t, string s) {}
    
    public void Overload2(T t, bool b) {}
    public void Overload2(T t, string s) {}
}
