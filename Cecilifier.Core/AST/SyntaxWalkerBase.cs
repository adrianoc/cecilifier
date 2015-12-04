using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Cecil.Cil;

namespace Cecilifier.Core.AST
{
	class SyntaxWalkerBase : CSharpSyntaxWalker
	{
		private readonly IVisitorContext ctx;

		internal SyntaxWalkerBase(IVisitorContext ctx)
		{
			this.ctx = ctx;
		}

		protected IVisitorContext Context
		{
			get { return ctx; }
		}

		protected void AddCecilExpression(string format, params object[] args)
		{
			Context.WriteCecilExpression("{0}\r\n", string.Format(format, args));
		}
		
        protected void AddMethodCall(string ilVar, IMethodSymbol method)
        {
        	OpCode opCode = method.IsVirtual || method.IsAbstract ? OpCodes.Callvirt : OpCodes.Call;
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, {2}));", ilVar, opCode.ConstantName(), method.MethodResolverExpression(Context));
		}

        protected void AddCilInstruction(string ilVar, OpCode opCode, ITypeSymbol type)
        {
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, {2}));", ilVar, opCode.ConstantName(), ResolveType(type));
		}

        protected void AddCilInstruction<T>(string ilVar, OpCode opCode, T arg)
        {
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, {2}));", ilVar, opCode.ConstantName(), arg);
		}

        protected internal void AddCilInstructionCastOperand<T>(string ilVar, OpCode opCode, T arg)
        {
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, ({2}) {3}));", ilVar, opCode.ConstantName(), typeof(T).Name, arg);
		}

        protected void AddCilInstruction(string ilVar, OpCode opCode)
		{
			AddCecilExpression(@"{0}.Append({0}.Create({1}));", ilVar, opCode.ConstantName());
		}

		protected IMethodSymbol DeclaredSymbolFor<T>(T node) where T : BaseMethodDeclarationSyntax
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected ITypeSymbol DeclaredSymbolFor(TypeDeclarationSyntax node)
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected void WithCurrentNode(MemberDeclarationSyntax node, string localVariable, string typeName, Action<string> action)
		{
			try
			{
				Context.PushLocalVariable(new LocalVariable(node, localVariable));
				action(typeName);
			}
			finally
			{
				Context.PopLocalVariable();
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

		protected string LocalVariableNameForCurrentNode()
		{
			var node = Context.CurrentLocalVariable;
			return node.VarName;
		}

		protected int NextLocalVariableId()
		{
			return Context.NextFieldId();
		}

		protected int NextLocalVariableTypeId()
		{
			return Context.NextLocalVariableTypeId();
		}

		protected string ImportExpressionFor(Type type)
		{
			return ImportExpressionFor(type.FullName);
		}

		protected string ImportExpressionFor(string typeName)
		{
			return string.Format("assembly.MainModule.Import(typeof({0}))", typeName);
		}

		protected string TypeModifiersToCecil(TypeDeclarationSyntax node)
		{
			var typeAttributes = DefaultTypeAttributeFor(node);

			if (IsNestedTypeDeclaration(node))
			{
				return typeAttributes.AppendModifier(ModifiersToCecil(node.Modifiers, m => "TypeAttributes.Nested" + m.ValueText.CamelCase()));
			}

			var convertedModifiers = ModifiersToCecil("TypeAttributes", node.Modifiers, "NotPublic", ExcludeHasNoCILRepresentationInTypes);
			return typeAttributes.AppendModifier(convertedModifiers);
		}

		protected static bool IsNestedTypeDeclaration(TypeDeclarationSyntax node)
		{
			return node.Parent.Kind() != SyntaxKind.NamespaceDeclaration && node.Parent.Kind() != SyntaxKind.CompilationUnit;
		}

		private static string DefaultTypeAttributeFor(TypeDeclarationSyntax node)
		{
			const string basicClassAttrs = "TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit";
			switch (node.Kind())
			{
				case SyntaxKind.StructDeclaration:		return "TypeAttributes.SequentialLayout | TypeAttributes.Sealed |" + basicClassAttrs; 
				case SyntaxKind.ClassDeclaration:		return basicClassAttrs;
				case SyntaxKind.InterfaceDeclaration:	return "TypeAttributes.Interface | TypeAttributes.Abstract";
				
				case SyntaxKind.EnumDeclaration: throw new NotImplementedException();
			}

			throw new Exception("Not supported type declaration: " + node);
		}

		protected static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default)
		{
			return ModifiersToCecil(targetEnum, modifiers, @default, ExcludeHasNoCILRepresentation);
		}

		private static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default,
		                                       Func<SyntaxToken, bool> meaninglessModifiersFilter)
		{
			var validModifiers = modifiers.Where(meaninglessModifiersFilter);
			if (!validModifiers.Any()) return targetEnum + "." + @default;

			return ModifiersToCecil(validModifiers, m => m.MapModifier(targetEnum));
		}

		private static string ModifiersToCecil(IEnumerable<SyntaxToken> modifiers, Func<SyntaxToken, string> mapp)
		{
			var cecilModifierStr = modifiers.Aggregate("",  (acc, token) =>
			                                                acc + (ModifiersSeparator + mapp(token)));

			return cecilModifierStr.Substring(ModifiersSeparator.Length);
		}

		private static bool ExcludeHasNoCILRepresentationInTypes(SyntaxToken token)
		{
			return ExcludeHasNoCILRepresentation(token) && token.Kind() != SyntaxKind.PrivateKeyword;
		}

		protected static bool ExcludeHasNoCILRepresentation(SyntaxToken token)
		{
			return token.Kind() != SyntaxKind.PartialKeyword && token.Kind() != SyntaxKind.VolatileKeyword;
		}

		protected string ResolveTypeLocalVariable(BaseTypeDeclarationSyntax typeDeclaration)
		{
			return ResolveTypeLocalVariable(typeDeclaration.Identifier.ValueText);
		}

		protected string ResolveTypeLocalVariable(string typeName)
		{
			return Context.ResolveTypeLocalVariable(typeName);
		}

		protected string ResolveExpressionType(ExpressionSyntax expression)
		{
			if (expression == null)
			{
				throw new ArgumentNullException("expression");
			}

			var info = Context.GetTypeInfo(expression);
			return ResolveType(info.Type.FullyQualifiedName());
		}

		protected string ResolveType(ITypeSymbol type)
		{
			return ResolveTypeLocalVariable(type.Name) 
					?? ResolvePredefinedAndArrayTypes(type) 
					?? ResolveType(type.Name);
		}
		
		protected string ResolveType(TypeSyntax type)
		{
			return ResolveTypeLocalVariable(type.ToString()) 
					?? ResolvePredefinedAndArrayTypes(type) 
					?? ResolveType(type.ToString());
		}

		private string ResolvePredefinedAndArrayTypes(ITypeSymbol type)
		{
			if (type.SpecialType == SpecialType.None) return null;

			if (type.SpecialType == SpecialType.System_Array)
			{
				var ats = (IArrayTypeSymbol) type;
				return "new ArrayType(" + ResolveType(ats.ElementType) + ")";
			}

			return ResolvePredefinedType(type);
		}
		
		private string ResolvePredefinedAndArrayTypes(TypeSyntax type)
		{
			switch (type.Kind())
			{
				case SyntaxKind.PredefinedType: return ResolvePredefinedType(Context.GetTypeInfo(type).Type);
				case SyntaxKind.ArrayType: return "new ArrayType(" + ResolveType(type.DescendantNodes().OfType<TypeSyntax>().Single()) + ")";
			}
			return null;
		}

		protected string ResolvePredefinedType(ITypeSymbol type)
		{
			return "assembly.MainModule.TypeSystem." + type.Name;
		}

		protected string ResolveType(string typeName)
		{
			return ImportExpressionFor(typeName);
		}

		protected void RegisterTypeLocalVariable(TypeDeclarationSyntax node, string varName)
		{
			Context.RegisterTypeLocalVariable(node, varName);
		}

		protected INamedTypeSymbol GetSpecialType(SpecialType specialType)
		{
			return Context.GetSpecialType(specialType);
		}

		protected string LocalVariableIndex(string methodVar, ILocalSymbol localVariable)
		{
			return LocalVariableIndex(methodVar, localVariable.Name);
		}

		private static string LocalVariableIndex(string methodVar, string name)
		{
			return string.Format("{0}.Body.Variables.Where(v => v.Name == \"{1}\").Single().Index", methodVar, name);
		}

		protected string LocalVariableIndex(string localVariable)
		{
			return LocalVariableIndex(LocalVariableNameForCurrentNode(), localVariable);
		}

		protected string LocalVariableIndexWithCast<TCast>(string localVariable)
		{
			return "(" + typeof(TCast).Name +")" + LocalVariableIndex(LocalVariableNameForCurrentNode(), localVariable);
		}

		protected void ProcessParameter(string ilVar, ExpressionSyntax node, IParameterSymbol paramSymbol)
		{
			var parent = (CSharpSyntaxNode) node.Parent;
			//TODO: Get rid of code duplication in ExpressionVisitor.ProcessLocalVariable(IdentifierNameSyntax localVar, SymbolInfo varInfo)
			if (paramSymbol.Type.IsValueType && parent.Accept(new UsageVisitor()) == UsageKind.CallTarget)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarga, paramSymbol.Ordinal.ToCecilIndex());
				return;
			}

			OpCode[] optimizedLdArgs = {OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3};

			var method = paramSymbol.ContainingSymbol as IMethodSymbol;
			if (node.Parent.Kind() == SyntaxKind.SimpleMemberAccessExpression && paramSymbol.ContainingType.IsValueType)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarga, paramSymbol.Ordinal + +(method.IsStatic ? 0 : 1));
			}
			else if (paramSymbol.Ordinal > 3)
			{
				AddCilInstruction(ilVar, OpCodes.Ldarg, paramSymbol.Ordinal.ToCecilIndex());
			}
			else
			{
				var loadOpCode = optimizedLdArgs[paramSymbol.Ordinal + (method.IsStatic ? 0 : 1)];
				AddCilInstruction(ilVar, loadOpCode);
			}
		}
		
		protected const string ModifiersSeparator = " | ";

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
