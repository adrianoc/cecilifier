using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Cecilifier.Core.Naming;
using Cecilifier.Core.Variables;
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
        public string MainMethodDefinitionVariable { get; private set; }

        public override void VisitCompilationUnit(CompilationUnitSyntax node)
        {
            HandleAttributesInMemberDeclaration(node.AttributeLists, "assembly");
            base.VisitCompilationUnit(node);
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            try
            {
                Context.CurrentNamespace = NamespaceOf(node);
                base.VisitNamespaceDeclaration(node);
            }
            finally
            {
                Context.CurrentNamespace = string.Empty;
            }

            string NamespaceOf(NamespaceDeclarationSyntax namespaceDeclarationSyntax)
            {
                var namespaceHierarchy = namespaceDeclarationSyntax.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>().Reverse();
                return string.Join('.', namespaceHierarchy.Select(curr => curr.Name.WithoutTrivia()));
            }
        }
     
        public override void VisitGlobalStatement(GlobalStatementSyntax node)
        {
            if (_globalStatementHandler == null)
            {
                Context.WriteComment("Begin of global statements.");
                _globalStatementHandler = new GlobalStatementHandler(Context, node);
            }

            if (_globalStatementHandler.HandleGlobalStatement(node))
            {
                Context.WriteNewLine();
                Context.WriteComment("End of global statements.");
                _globalStatementHandler = null;
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            new TypeDeclarationVisitor(Context).Visit(node);
            UpdateTypeInformation(node);
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
            Context.WriteNewLine();
            Context.WriteComment($"Delegate: {node.Identifier.Text}");
            var typeVar = Context.Naming.Delegate(node);
            var accessibility = ModifiersToCecil(node.Modifiers, "TypeAttributes", "Private");

            var typeDef = CecilDefinitionsFactory.Type(
                Context, 
                typeVar, 
                node.Identifier.ValueText, 
                 CecilDefinitionsFactory.DefaultTypeAttributeFor(node.Kind(), false).AppendModifier(accessibility), 
                Context.TypeResolver.Bcl.System.MulticastDelegate, 
                false,
                Array.Empty<string>(),
                node.TypeParameterList, 
                "IsAnsiClass = true");
            
            AddCecilExpressions(typeDef);
            HandleAttributesInMemberDeclaration(node.AttributeLists, typeVar);

            using (Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, VariableMemberKind.Type, typeVar))
            {
                var ctorLocalVar = Context.Naming.Delegate(node);
                
                // Delegate ctor
                AddCecilExpression(CecilDefinitionsFactory.Constructor(Context, ctorLocalVar, node.Identifier.Text, "MethodAttributes.FamANDAssem | MethodAttributes.Family",
                    new[] {"System.Object", "System.IntPtr"}, "IsRuntime = true"));
                AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.Object}));");
                AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.IntPtr}));");
                AddCecilExpression($"{typeVar}.Methods.Add({ctorLocalVar});");

                AddDelegateMethod(
                    typeVar, 
                    "Invoke", 
                    ResolveType(node.ReturnType), 
                    node.ParameterList.Parameters,
                    (methodVar, param) => CecilDefinitionsFactory.Parameter(param, Context.SemanticModel, methodVar, Context.Naming.Parameter(param) , ResolveType(param.Type), param.Accept(DefaultParameterExtractorVisitor.Instance)));

                // BeginInvoke() method
                var methodName = "BeginInvoke";
                var beginInvokeMethodVar = Context.Naming.SyntheticVariable(methodName, ElementKind.Method);
                AddCecilExpression(
                    $@"var {beginInvokeMethodVar} = new MethodDefinition(""{methodName}"", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, {Context.TypeResolver.Bcl.System.IAsyncResult})
					{{
						HasThis = true,
						IsRuntime = true,
					}};");

                foreach (var param in node.ParameterList.Parameters)
                {
                    var paramExps = CecilDefinitionsFactory.Parameter(param, Context.SemanticModel, beginInvokeMethodVar, Context.Naming.Parameter(param), ResolveType(param.Type), param.Accept(DefaultParameterExtractorVisitor.Instance));
                    AddCecilExpressions(paramExps);
                }

                AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.AsyncCallback}));");
                AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({Context.TypeResolver.Bcl.System.Object}));");
                AddCecilExpression($"{typeVar}.Methods.Add({beginInvokeMethodVar});");

                // EndInvoke() method
                var endInvokeMethodVar =  Context.Naming.SyntheticVariable("EndInvoke", ElementKind.Method);

                var endInvokeExps = CecilDefinitionsFactory.Method(
                    Context,
                    endInvokeMethodVar,
                    "EndInvoke",
                    "MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual",
                    Context.GetTypeInfo(node.ReturnType).Type,
                    false,
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
                    endInvokeMethodVar,
                    Context.Naming.Parameter("ar", node.Identifier.Text),
                    Context.TypeResolver.Bcl.System.IAsyncResult,
                    Constants.ParameterAttributes.None,
                    defaultParameterValue: null);

                AddCecilExpressions(endInvokeExps);
                AddCecilExpressions(endInvokeParamExps);
                AddCecilExpression($"{typeVar}.Methods.Add({endInvokeMethodVar});");
               
                base.VisitDelegateDeclaration(node);
            }

            void AddDelegateMethod(string typeLocalVar, string methodName, string returnTypeName, in SeparatedSyntaxList<ParameterSyntax> parameters, Func<string, ParameterSyntax, IEnumerable<string>> parameterHandler)
            {
                var methodLocalVar = Context.Naming.SyntheticVariable(methodName, ElementKind.Method);
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
        
        private void UpdateTypeInformation(BaseTypeDeclarationSyntax node)
        {
            if (mainType == null)
                mainType = node;

            var typeSymbol = Context.SemanticModel.GetDeclaredSymbol(node) as ITypeSymbol;
            if (typeSymbol == null)
                return;

            if (MainMethodDefinitionVariable == null)
            {
                var mainMethod = (IMethodSymbol) typeSymbol.GetMembers().SingleOrDefault(m => m is IMethodSymbol {IsStatic: true, Name: "Main", ReturnsVoid: true});
                if (mainMethod != null)
                    MainMethodDefinitionVariable = Context.DefinitionVariables.GetMethodVariable(mainMethod.AsMethodDefinitionVariable());
            }

            var mainTypeSymbol = (ITypeSymbol) Context.SemanticModel.GetDeclaredSymbol(mainType);
            if (typeSymbol.GetMembers().Length > mainTypeSymbol?.GetMembers().Length)
            {
                mainType = node;
            }
        }

        private BaseTypeDeclarationSyntax mainType;
        private GlobalStatementHandler _globalStatementHandler;
    }
}
