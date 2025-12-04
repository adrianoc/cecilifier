using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.OutputBased;

[TestFixture(typeof(MonoCecilContext))]
public class InvocationTests<TContext> : OutputBasedTestBase<TContext> where TContext : IVisitorContext
{
    [Test]
    public void ImplicitDelegateInvocation() => AssertOutput("System.Action a = M; a(); static void M() => System.Console.Write(42);", "42");
    
    [Test]
    public void ExplicitDelegateInvocation() => AssertOutput("System.Action a = M; a.Invoke(); static void M() => System.Console.Write(42);", "42");
    
    [TestCase(".Invoke", TestName = "Explicit")]
    [TestCase("", TestName = "Implicit")]
    public void EventRising(string calledMethod) => AssertOutput($$"""
                                                                    var x = new SimpleEvent();
                                                                    
                                                                    x.TheEvent += Handle;
                                                                    x.Trigger();
                                                                    
                                                                    static void Handle(object? sender, System.EventArgs e) => System.Console.Write(42);
                                                                    
                                                                    public class SimpleEvent
                                                                    {
                                                                        public event System.EventHandler TheEvent;
                                                                        public void Trigger() => TheEvent{{calledMethod}}(this, null);
                                                                    }
                                                                    """, "42");
}
