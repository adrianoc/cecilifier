using Cecilifier.ApiDriver.MonoCecil;
using Cecilifier.Core.AST;
using Cecilifier.Core.Tests.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration.Types
{
    [TestFixture(typeof(MonoCecilContext))]
    internal class ArrayTests<TResource> : ResourceTestBase<TResource> where TResource : IVisitorContext
    {
        [Test]
        public void SmokeTests()
        {
            AssertResourceTest(@"Types/ArraySmoke");
        }
        
        [Test]
        public void ArrayInitializationOptimization()
        {
            var resource = @"Types/ArrayInitialization";
            AssertResourceTest(resource, new CecilifyTestOptions()
            {
                ToBeCecilified = ReadResource(resource, "cs"),
                
                InstructionComparer = (lhs, rhs) =>
                {
                    // cecilifier does not use the same algorithm to generate the name of the field
                    // that holds the optimized raw initialization data for arrays; in this case
                    // it is enough to check that the field types matches.
                    if (lhs.OpCode != OpCodes.Ldtoken || rhs.OpCode != OpCodes.Ldtoken)
                        return null;

                    var leftField = (FieldReference) lhs.Operand;
                    var rightField = (FieldReference) rhs.Operand;
                    
                    return leftField.FieldType.FullName == rightField.FieldType.FullName;
                }
            });
        }
    }
}
