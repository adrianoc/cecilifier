using System.Collections.Generic;
using Cecilifier.Core.Tests.Framework;
using Cecilifier.Core.Tests.Framework.AssemblyDiff;
using Mono.Cecil;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Integration
{
    public class GenericsTestCase : IntegrationResourceBasedTest
    {
        [Test]
        public void TestInstanceNonGenericMethodsOnGenericTypes()
        {
            AssertResourceTest(@"Generics/InstanceNonGenericMethodsOnGenericTypes");
        }

        [Test]
        public void TestGenericInferredStaticMethods()
        {
            AssertResourceTest(@"Generics/StaticInferredMethods");
        }

        [Test]
        public void TestGenericExplicitStaticMethods()
        {
            AssertResourceTest(@"Generics/StaticExplicitMethods");
        }

        [Test]
        public void TestGenericTypesAsMembers()
        {
            AssertResourceTest(@"Generics/GenericTypesAsMembers");
        }
        
        [Test]
        public void TestSimplestGenericTypeDefinition()
        {
            AssertResourceTest(@"Generics/SimplestGenericTypeDefinition");
        }

        [Test]
        public void TestGenericTypeDefinitionWithMembers()
        {
            AssertResourceTest(@"Generics/GenericTypeDefinitionWithMembers");
        }
        
        [Test, Ignore("Not Working")]
        public void TestGenericTypesInheritance()
        {
            AssertResourceTest(@"Generics/GenericTypesInheritance");
        }
        
        [Test]
        public void TestGenericMethods()
        {
            AssertResourceTest(@"Generics/GenericMethods");
        }

        [Test, Ignore("Not Working")]
        public void TestGenericMethodConstraints()
        {
            AssertResourceTest(@"Generics/GenericMethodConstraints");
        }
        
        [Test]
        public void TestGenericTypeConstraints()
        {
            var toBeIgnored = new[]
            {
                "System.Runtime.CompilerServices.NullableAttribute",
                "Microsoft.CodeAnalysis.EmbeddedAttribute",
                "System.Runtime.CompilerServices.IsUnmanagedAttribute"
            };
            
            AssertResourceTest(@"Generics/GenericTypeConstraints", TestKind.Integration, new CompilerInjectedAttributesIgnorer(toBeIgnored));
        }
    }

    public class CompilerInjectedAttributesIgnorer : IAssemblyDiffVisitor, ITypeDiffVisitor
    {
        public bool VisitModules(AssemblyDefinition source, AssemblyDefinition target)
        {
            return other.VisitModules(source, target);
        }

        public ITypeDiffVisitor VisitType(TypeDefinition sourceType)
        {
            typeVisitor = other.VisitType(sourceType);
            return this;
        }

        public bool VisitAttributes(TypeDefinition source, TypeDefinition target)
        {
            return typeVisitor.VisitAttributes(source, target);
        }

        public bool VisitMissing(TypeDefinition source, ModuleDefinition target)
        {
            return toBeIgnored.Contains(source.FullName) || typeVisitor.VisitMissing(source, target);
        }

        public bool VisitBaseType(TypeDefinition baseType, TypeDefinition target)
        {
            return VisitBaseType(baseType, target);
        }

        public bool VisitCustomAttributes(TypeDefinition source, TypeDefinition target)
        {
            return typeVisitor.VisitCustomAttributes(source, target);
        }

        public bool VisitGenerics(TypeDefinition source, TypeDefinition target)
        {
            return typeVisitor.VisitGenerics(source, target);
        }

        public IFieldDiffVisitor VisitMember(FieldDefinition field)
        {
            return typeVisitor.VisitMember(field);
        }

        public IMethodDiffVisitor VisitMember(MethodDefinition method)
        {
            return typeVisitor.VisitMember(method);
        }

        public string Reason => other.Reason;

        internal CompilerInjectedAttributesIgnorer(string[] toBeIgnored)
        {
            other = new StrictAssemblyDiffVisitor();
            this.toBeIgnored = new HashSet<string>(toBeIgnored);
        }

        private IAssemblyDiffVisitor other;
        private ITypeDiffVisitor typeVisitor;
        private ISet<string> toBeIgnored;
    }
}
