﻿using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Cecilifier.Core.AST
{
	class SyntaxWalkerBase : CSharpSyntaxWalker
	{
		private readonly IVisitorContext ctx;

		internal SyntaxWalkerBase(IVisitorContext ctx)
		{
			this.ctx = ctx;
		}

		protected IVisitorContext Context => ctx;

		protected void AddCecilExpressions(IEnumerable<string> exps)
		{
			foreach (var exp in exps)
			{
				AddCecilExpression(exp);
			}	
		}
		
		protected void AddCecilExpression(string exp)
		{
			WriteCecilExpression(Context, exp);
		}
		
		protected void AddCecilExpression(string format, params object[] args)
		{
			WriteCecilExpression(Context, format, args);
		}
		
        protected void AddMethodCall(string ilVar, IMethodSymbol method, bool isAccessOnThisOrObjectCreation = false)
        {
			var opCode = (method.IsStatic || (method.IsDefinedInCurrentType(Context) && isAccessOnThisOrObjectCreation)|| method.ContainingType.IsValueType) && !(method.IsVirtual || method.IsAbstract) ? OpCodes.Call : OpCodes.Callvirt;
	        if (method.IsStatic)
		        opCode = OpCodes.Call;
	        
	        AddCilInstruction(ilVar, opCode, method.MethodResolverExpression(Context));
		}

        protected void AddCilInstruction(string ilVar, OpCode opCode, ITypeSymbol type)
        {
	        AddCilInstruction(ilVar, opCode, ResolveType(type));
        }

		protected void InsertCilInstructionAfter<T>(LinkedListNode<string> instruction, string ilVar, OpCode opCode, T arg = default)
		{
			var instVar = CreateCilInstruction(ilVar, opCode, arg);
			Context.MoveLineAfter(Context.CurrentLine, instruction);
			
			AddCecilExpression($"{ilVar}.Append({instVar});");
			Context.MoveLineAfter(Context.CurrentLine, instruction.Next);
		}
		
        protected void AddCilInstruction<T>(string ilVar, OpCode opCode, T arg)
        {
	        var instVar = CreateCilInstruction(ilVar, opCode, arg);
	        AddCecilExpression($"{ilVar}.Append({instVar});");
        }

		protected string AddCilInstruction(string ilVar, OpCode opCode)
		{
			var instVar = CreateCilInstruction(ilVar, opCode);
			AddCecilExpression($"{ilVar}.Append({instVar});");

			return instVar;
		}

		protected string CreateCilInstruction(string ilVar, OpCode opCode, object operand = null)
		{
			var operandStr = operand == null ? string.Empty : $", {operand}";
			var instVar = TempLocalVar(opCode.Code.ToString());
			AddCecilExpression($"var {instVar} = {ilVar}.Create({opCode.ConstantName()}{operandStr});");
			
			ctx.TriggerInstructionAdded(instVar);
			
			return ctx.DefinitionVariables.LastInstructionVar = instVar;
		}

		protected IMethodSymbol DeclaredSymbolFor<T>(T node) where T : BaseMethodDeclarationSyntax
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected ITypeSymbol DeclaredSymbolFor(TypeDeclarationSyntax node)
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected void WithCurrentMethod(string declaringTypeName, string localVariable, string methodName, string[] paramTypes, Action<string> action)
		{
			using (Context.DefinitionVariables.WithCurrentMethod(declaringTypeName, methodName, paramTypes, localVariable))
			{
				action(methodName);
			}
		}

		protected string TempLocalVar(string prefix)
		{
			return prefix + NextLocalVariableId();
		}

		protected static string LocalVariableNameForId(int localVarId)
		{
			return "t" + localVarId;
		}

		protected int NextLocalVariableId()
		{
			return Context.NextFieldId();
		}

		protected int NextLocalVariableTypeId()
		{
			return Context.NextLocalVariableTypeId();
		}

		protected string ImportExpressionForType(Type type)
		{
			return ImportExpressionForType(type.FullName);
		}

		protected string ImportExpressionForType(string typeName)
		{
			return string.Format("assembly.MainModule.Import(typeof({0}))", typeName);
		}

		protected string TypeModifiersToCecil(TypeDeclarationSyntax node)
		{
            var hasStaticCtor = node.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Any(d => d.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)));
			var typeAttributes = DefaultTypeAttributeFor(node, hasStaticCtor);
			if (IsNestedTypeDeclaration(node))
			{
				return typeAttributes.AppendModifier(ModifiersToCecil(node.Modifiers, m => "TypeAttributes.Nested" + m.ValueText.CamelCase()));
			}

			var convertedModifiers = ModifiersToCecil("TypeAttributes", node.Modifiers, "NotPublic", ExcludeHasNoCILRepresentationInTypes);
			return typeAttributes.AppendModifier(convertedModifiers);
		}

		private static bool IsNestedTypeDeclaration(SyntaxNode node)
		{
			return node.Parent.Kind() != SyntaxKind.NamespaceDeclaration && node.Parent.Kind() != SyntaxKind.CompilationUnit;
		}

		protected static string DefaultTypeAttributeFor(SyntaxNode node, bool hasStaticCtor = false)
		{
			var basicClassAttrs = "TypeAttributes.AnsiClass" + (hasStaticCtor ? "" : " | TypeAttributes.BeforeFieldInit");
			switch (node.Kind())
			{
				case SyntaxKind.StructDeclaration:		return "TypeAttributes.SequentialLayout | TypeAttributes.Sealed |" + basicClassAttrs; 
				case SyntaxKind.ClassDeclaration:		return basicClassAttrs;
				case SyntaxKind.InterfaceDeclaration:	return "TypeAttributes.Interface | TypeAttributes.Abstract";
				case SyntaxKind.DelegateDeclaration:	return "TypeAttributes.Sealed";
				
				case SyntaxKind.EnumDeclaration: throw new NotImplementedException();
			}

			throw new Exception("Not supported type declaration: " + node);
		}

		protected static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default)
		{
			return ModifiersToCecil(targetEnum, modifiers, @default, ExcludeHasNoCILRepresentation);
		}

		private static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default, Func<SyntaxToken, bool> meaninglessModifiersFilter)
		{
			var validModifiers = modifiers.Where(meaninglessModifiersFilter).ToList();

		    var hasAccessibilityModifier = validModifiers.Any(m =>
		        m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.PrivateKeyword) ||
		        m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

		    var modifiersStr = ModifiersToCecil(validModifiers, m => m.MapModifier(targetEnum));
		    if (!validModifiers.Any() || !hasAccessibilityModifier)
		        modifiersStr = modifiersStr.AppendModifier(targetEnum + "." + @default);

            return modifiersStr;
		}

	    private static string ModifiersToCecil(IEnumerable<SyntaxToken> modifiers, Func<SyntaxToken, string> map)
		{
			var cecilModifierStr = modifiers.Aggregate("",  (acc, token) =>
			                                                acc + (ModifiersSeparator + map(token)));

            if (cecilModifierStr.Length > 0)
                cecilModifierStr = cecilModifierStr.Substring(ModifiersSeparator.Length);

		    return cecilModifierStr;
		}

		private static bool ExcludeHasNoCILRepresentationInTypes(SyntaxToken token)
		{
			return ExcludeHasNoCILRepresentation(token) && token.Kind() != SyntaxKind.PrivateKeyword;
		}

		protected static void WriteCecilExpression(IVisitorContext context, string format, params object[] args)
		{
			context.WriteCecilExpression($"{string.Format(format, args)}\r\n");
		}

	    protected static void WriteCecilExpression(IVisitorContext context, string value)
		{
			context.WriteCecilExpression($"{value}\r\n");
		}

		protected static bool ExcludeHasNoCILRepresentation(SyntaxToken token)
		{
			return token.Kind() != SyntaxKind.PartialKeyword && token.Kind() != SyntaxKind.VolatileKeyword;
		}

		protected string ResolveTypeLocalVariable(string typeName)
		{
			return Context.DefinitionVariables.GetVariable(typeName, MemberKind.Type).VariableName;
		}

		protected string ResolveExpressionType(ExpressionSyntax expression)
		{
			if (expression == null)
			{
				throw new ArgumentNullException(nameof(expression));
			}

			var info = Context.GetTypeInfo(expression);
			return ResolveType(info.Type.FullyQualifiedName());
		}

		protected string ResolveType(ITypeSymbol type)
		{
			return ResolveTypeLocalVariable(type.Name) 
					?? ResolvePredefinedAndArrayTypes(type)
			        ?? ResolveGenericType(type)
					?? ResolveType(type.Name);
		}
		
		protected string ResolveType(TypeSyntax type)
		{
			return ResolveTypeLocalVariable(type.ToString()) 
					?? ResolvePredefinedAndArrayTypes(type) 
					?? ResolvePlainOrGenericType(type);
		}
		
		private string ResolvePlainOrGenericType(TypeSyntax type)
		{
			if (type is GenericNameSyntax genType)
			{
				var typeInfo = Context.GetTypeInfo(genType);
				return ResolveGenericType(typeInfo.Type);
			}

			return ResolveType(type.ToString());
		}

		private string ResolveGenericType(ITypeSymbol type)
		{
			var genericTypeSymbol = type as INamedTypeSymbol;
			if (genericTypeSymbol == null)
				return null;
			
			var genericType = ResolveType(OpenGenericTypeName(genericTypeSymbol.ConstructedFrom));
			var args = string.Join(",", genericTypeSymbol.TypeArguments.Select(a => ResolveType(a)));
			return $"{genericType}.MakeGenericInstanceType({args})";
		}

		private string OpenGenericTypeName(ITypeSymbol type)
		{
			var genericTypeWithTypeParameters = type.ToString();
			
			var genOpenBraceIndex = genericTypeWithTypeParameters.IndexOf('<');
			var genCloseBraceIndex = genericTypeWithTypeParameters.LastIndexOf('>');

			var nts = (INamedTypeSymbol) type;
			var commas = new string(',', nts.TypeParameters.Length -1);
			return genericTypeWithTypeParameters.Remove(genOpenBraceIndex + 1, genCloseBraceIndex - genOpenBraceIndex - 1).Insert(genOpenBraceIndex+1, commas);
		}
		
		private string ResolvePredefinedAndArrayTypes(ITypeSymbol type)
		{
			if (type.SpecialType == SpecialType.None) return null;

			if (type.SpecialType == SpecialType.System_Array)
			{
				var ats = (IArrayTypeSymbol) type;
				return "new ArrayType(" + ResolveType(ats.ElementType) + ")";
			}

			return ResolvePredefinedType(type.Name);
		}
		
		private string ResolvePredefinedAndArrayTypes(TypeSyntax type)
		{
			switch (type.Kind())
			{
				case SyntaxKind.PredefinedType: return ResolvePredefinedType(Context.GetTypeInfo(type).Type.Name);
				case SyntaxKind.ArrayType: return "new ArrayType(" + ResolveType(type.DescendantNodes().OfType<TypeSyntax>().Single()) + ")";
			}
			return null;
		}

		protected string ResolvePredefinedType(ITypeSymbol type)
		{
			return ResolvePredefinedType(type.Name);
		}		
		
		protected string ResolvePredefinedType(string typeName)
		{
			return "assembly.MainModule.TypeSystem." + typeName;
		}

		protected string ResolveType(string typeName)
		{
			return ImportExpressionForType(typeName);
		}

		protected INamedTypeSymbol GetSpecialType(SpecialType specialType)
		{
			return Context.GetSpecialType(specialType);
		}

		protected void ProcessParameter(string ilVar, SimpleNameSyntax node, IParameterSymbol paramSymbol)
		{
			var parent = (CSharpSyntaxNode) node.Parent;
			//TODO: Get rid of code duplication in ExpressionVisitor.ProcessLocalVariable(IdentifierNameSyntax localVar, SymbolInfo varInfo)
			if (paramSymbol.Type.IsValueType && parent.Accept(new UsageVisitor()) == UsageKind.CallTarget)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarga, Context.DefinitionVariables.GetVariable(paramSymbol.Name, MemberKind.Parameter).VariableName);
				return;
			}

			var method = paramSymbol.ContainingSymbol as IMethodSymbol;
			if (node.Parent.Kind() == SyntaxKind.SimpleMemberAccessExpression && paramSymbol.ContainingType.IsValueType)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarga, Context.DefinitionVariables.GetVariable(paramSymbol.Name, MemberKind.Parameter).VariableName);
			}
			else if (paramSymbol.Ordinal > 3)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarg, paramSymbol.Ordinal.ToCecilIndex());
				HandlePotentialDelegateInvocationOn(node, paramSymbol.Type, ilVar);
			}
			else
			{
				OpCode[] optimizedLdArgs = { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3 };
				var loadOpCode = optimizedLdArgs[paramSymbol.Ordinal + (method.IsStatic ? 0 : 1)];
				AddCilInstruction(ilVar, loadOpCode);
				HandlePotentialDelegateInvocationOn(node, paramSymbol.Type, ilVar);
			}
		}

		protected void HandlePotentialDelegateInvocationOn(SimpleNameSyntax node, ITypeSymbol typeSymbol, string ilVar)
		{
			var invocation = node.Parent as InvocationExpressionSyntax;
			if (invocation == null || invocation.Expression != node)
				return;

			var localDelegateDeclaration = ResolveTypeLocalVariable(typeSymbol.Name);
			if (localDelegateDeclaration != null)
			{
				AddCilInstruction(ilVar, OpCodes.Callvirt, $"{localDelegateDeclaration}.Methods.Single(m => m.Name == \"Invoke\")");
			}
			else
			{
				var declaringTypeName = typeSymbol.FullyQualifiedName();
				var methodInvocation =  $"assembly.MainModule.Import(TypeHelpers.ResolveMethod(\"{typeSymbol.ContainingAssembly.Name}\", \"{declaringTypeName}\", \"Invoke\"))";

				AddCilInstruction(ilVar, OpCodes.Callvirt, methodInvocation);
			}
		}

		protected void LogUnsupportedSyntax(SyntaxNode node)
		{
			var lineSpan = node.GetLocation().GetLineSpan();
			AddCecilExpression($"/* Syntax '{node.Kind()}' is not supported in {lineSpan.Path} ({lineSpan.Span.Start.Line + 1},{lineSpan.Span.Start.Character + 1}):\n------\n{node}\n----*/");
		}

		private const string ModifiersSeparator = " | ";

	}

	internal class UsageVisitor : CSharpSyntaxVisitor<UsageKind>
	{
		public override UsageKind VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
		{
			return UsageKind.CallTarget;
		}
	}

	internal enum UsageKind
	{
		None		= 0,
		CallTarget	= 1,
	}
}
