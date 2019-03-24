using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cecilifier.Core.AST
{
	class CompilationUnitVisitor : SyntaxWalkerBase
	{
		internal CompilationUnitVisitor(IVisitorContext ctx) : base(ctx)
		{
		}

		public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
		{
			try
			{
				var namespaceHierarchy = node.AncestorsAndSelf().OfType<NamespaceDeclarationSyntax>().Reverse();
				var @namespace = namespaceHierarchy.Aggregate("",(acc, curr) => acc + "." + curr.Name.WithoutTrivia().ToString());

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
			var typeDef = CecilDefinitionsFactory.Type(Context, typeVar, node.Identifier.ValueText, DefaultTypeAttributeFor(node).AppendModifier(accessibility), ResolveType("System.MulticastDelegate"), false, Array.Empty<string>(), "IsAnsiClass = true");
			AddCecilExpressions(typeDef);
			
			using(Context.DefinitionVariables.WithCurrent("", node.Identifier.ValueText, MemberKind.Type, typeVar))
			{
				// Delegate ctor
				AddCecilExpression(CecilDefinitionsFactory.Constructor(Context, out var ctorLocalVar, node.Identifier.Text, "MethodAttributes.FamANDAssem | MethodAttributes.Family", new[] {"System.Object", "System.IntPtr"}, "IsRuntime = true"));
				AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({ResolvePredefinedType("Object")}));");
				AddCecilExpression($"{ctorLocalVar}.Parameters.Add(new ParameterDefinition({ResolvePredefinedType("IntPtr")}));");
				AddCecilExpression($"{typeVar}.Methods.Add({ctorLocalVar});");
	
				AddDelegateMethod(typeVar, "Invoke", ResolveType(node.ReturnType), node.ParameterList.Parameters, (methodVar, param) => CecilDefinitionsFactory.Parameter(param, Context.SemanticModel, methodVar, TempLocalVar($"{param.Identifier.ValueText}"), ResolveType(param.Type)));
	
				// BeginInvoke() method
				var beginInvokeMethodVar = TempLocalVar("beginInvoke");
				AddCecilExpression(
					$@"var {beginInvokeMethodVar} = new MethodDefinition(""BeginInvoke"", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, {ResolveType("System.IAsyncResult")})
					{{
						HasThis = true,
						IsRuntime = true,
					}};");
	
				foreach (var param in node.ParameterList.Parameters)
				{
					var paramExps = CecilDefinitionsFactory.Parameter(param, Context.SemanticModel, beginInvokeMethodVar, TempLocalVar($"{param.Identifier.ValueText}"), ResolveType(param.Type));
					AddCecilExpressions(paramExps);
				}
	
				AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({ResolveType("System.AsyncCallback")}));");
				AddCecilExpression($"{beginInvokeMethodVar}.Parameters.Add(new ParameterDefinition({ResolvePredefinedType("Object")}));");
				AddCecilExpression($"{typeVar}.Methods.Add({beginInvokeMethodVar});");
	
				AddDelegateMethod(typeVar, "EndInvoke", ResolveType(node.ReturnType), node.ParameterList.Parameters,
					(methodVar, param) => CecilDefinitionsFactory.Parameter(param, Context.SemanticModel, methodVar, TempLocalVar("ar"), ResolveType("System.IAsyncResult")));
	
				base.VisitDelegateDeclaration(node);
			}
			
			void AddDelegateMethod(string typeLocalVar, string methodName, string returnTypeName, in SeparatedSyntaxList<ParameterSyntax> parameters, Func<string, ParameterSyntax, IEnumerable<string>> paramterHandler)
			{
				var methodLocalVar = TempLocalVar(methodName.ToLower());

				AddCecilExpression($@"var {methodLocalVar} = new MethodDefinition(""{methodName}"", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, {returnTypeName})
				{{
					HasThis = true,
					IsRuntime = true,
				}};");

				foreach (var param in parameters)
				{
					AddCecilExpressions(paramterHandler(methodLocalVar, param));
				}

				AddCecilExpression($"{typeLocalVar}.Methods.Add({methodLocalVar});");
			}
		}
	}
}
