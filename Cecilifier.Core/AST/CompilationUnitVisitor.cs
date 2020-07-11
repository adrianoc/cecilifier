using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
    internal class CompilationUnitVisitor : SyntaxWalkerBase
    {
        internal CompilationUnitVisitor(IVisitorContext ctx) : base(ctx)
        {
        }

        public BaseTypeDeclarationSyntax MainType => mainType;

        public override void VisitCompilationUnit(CompilationUnitSyntax node)
        {
            HandleAttributesInMemberDeclaration(node.AttributeLists, "assembly");
            base.VisitCompilationUnit(node);
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            try
            {
                var namespaceHierarchy = node.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>().Reverse();
                var @namespace = namespaceHierarchy.Aggregate("", (acc, curr) => acc + "." + curr.Name.WithoutTrivia().ToString());

                Context.Namespace = @namespace.StartsWith(".") ? @namespace.Substring(1) : @namespace;
                base.VisitNamespaceDeclaration(node);
            }
            finally
            {
                Context.Namespace = string.Empty;
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            UpdateMainType(node);
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            new EnumDeclarationVisitor(Context).Visit(node);
        }

        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            var typeVar = LocalVariableNameForId(NextLocalVariableTypeId());
            var accessibility = ModifiersToCecil("TypeAttributes", node.Modifiers, "Private");
            var typeDef = CecilDefinitionsFactory.Type(
                Context, 
                typeVar, 
                node.Identifier.ValueText, 
                DefaultTypeAttributeFor(node).AppendModifier(accessibility), 
                Context.TypeResolver.Resolve("System.MulticastDelegate"), 
                false,
                Array.Empty<string>(),
                node.TypeParameterList, 
                "IsAnsiClass = true");
            
            AddCecilExpressions(typeDef);
            HandleAttributesInMemberDeclaration(node.AttributeLists, typeVar);

            using (Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, typeVar))
            {
                // Delegate ctor
                AddCecilExpression(CecilDefinitionsFactory.Constructor(Context, out var ctorLocalVar, node.Identifier.Text, "MethodAttributes.FamANDAssem | MethodAttributes.Family",
                    new[] {"System.Object", "System.IntPtr"}, "IsRuntime = true"));
                AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.ResolvePredefinedType("Object")}));");
                AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.ResolvePredefinedType("IntPtr")}));");
                AddCecilExpression($"{typeVar}.Methods.Add({ctorLocalVar});");

                AddDelegateMethod(
                    typeVar, 
                    "Invoke", 
                    ResolveType(node.ReturnType), 
                    node.ParameterList.Parameters,
                    (methodVar, param) => CecilDefinitionsFactory.Parameter(param, Context.SemanticModel, methodVar, TempLocalVar($"{param.Identifier.ValueText}"), ResolveType(param.Type)));

                // BeginInvoke() method
                var methodName = "BeginInvoke";
                var beginInvokeMethodVar = TempLocalVar("beginInvoke");
                AddCecilExpression(
                    $@"var {beginInvokeMethodVar} = new MethodDefinition(""{methodName}"", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, {Context.TypeResolver.Resolve("System.IAsyncResult")})
					{{
						HasThis = true,
						IsRuntime = true,
					}};");

                foreach (var param in node.ParameterList.Parameters)
                {
                    var paramExps = CecilDefinitionsFactory.Parameter(param, Context.SemanticModel, beginInvokeMethodVar, TempLocalVar($"{param.Identifier.ValueText}"), ResolveType(param.Type));
                    AddCecilExpressions(paramExps);
                }

                AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Resolve("System.AsyncCallback")}));");
                AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.ResolvePredefinedType("Object")}));");
                AddCecilExpression($"{typeVar}.Methods.Add({beginInvokeMethodVar});");

                // EndInvoke() method
                var endInvokeMethodVar = TempLocalVar("endInvoke");

                var endInvokeExps = CecilDefinitionsFactory.Method(
                    Context,
                    endInvokeMethodVar,
                    "EndInvoke",
                    "MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual",
                    ResolveType(node.ReturnType),
                    Array.Empty<TypeParameterSyntax>()
                );
                endInvokeExps = endInvokeExps.Concat(new[]
                {
                    $"{endInvokeMethodVar}.HasThis = true;",
                    $"{endInvokeMethodVar}.IsRuntime = true;",
                });
                
                var endInvokeParamExps = CecilDefinitionsFactory.Parameter(
                    "ar", 
                    RefKind.None,
                    false,
                    false,
                    Context.SemanticModel,
                    endInvokeMethodVar,
                    TempLocalVar("ar"),
                    Context.TypeResolver.Resolve("System.IAsyncResult"));

                AddCecilExpressions(endInvokeExps);
                AddCecilExpressions(endInvokeParamExps);
                AddCecilExpression($"{typeVar}.Methods.Add({endInvokeMethodVar});");
               
                base.VisitDelegateDeclaration(node);
            }

            void AddDelegateMethod(string typeLocalVar, string methodName, string returnTypeName, in SeparatedSyntaxList<ParameterSyntax> parameters, Func<string, ParameterSyntax, IEnumerable<string>> parameterHandler)
            {
                var methodLocalVar = TempLocalVar(methodName.ToLower());

                AddCecilExpression(
                    $@"var {methodLocalVar} = new MethodDefinition(""{methodName}"", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, {returnTypeName})
				{{
					HasThis = true,
					IsRuntime = true,
				}};");

                foreach (var param in parameters)
                {
                    AddCecilExpressions(parameterHandler(methodLocalVar, param));
                }

                AddCecilExpression($"{typeLocalVar}.Methods.Add({methodLocalVar});");
            }
        }
        
        private void UpdateMainType(BaseTypeDeclarationSyntax node)
        {
            if (mainType == null)
                mainType = node;

            var typeInfo = Context.SemanticModel.GetTypeInfo(node);
            if (typeInfo.Type == null)
                return;

            var mainTypeInfo = Context.SemanticModel.GetTypeInfo(mainType);
            if (typeInfo.Type.GetMembers().Length > mainTypeInfo.Type?.GetMembers().Length)
            {
                mainType = node;
            }
        }

        private BaseTypeDeclarationSyntax mainType;
    }
}
