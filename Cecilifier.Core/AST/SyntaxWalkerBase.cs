using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Mono.Cecil.Cil;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;
using TypeInfo = Roslyn.Compilers.CSharp.TypeInfo;

namespace Cecilifier.Core.AST
{
	class SyntaxWalkerBase : SyntaxWalker
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
		
        protected void AddMethodCall(string ilVar, MethodSymbol method)
        {
        	OpCode opCode = method.IsVirtual || method.IsAbstract ? OpCodes.Callvirt : OpCodes.Call;
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, {2}));", ilVar, opCode.ConstantName(), method.MethodResolverExpression(Context));
		}

        protected void AddCilInstruction(string ilVar, OpCode opCode, TypeSymbol type)
        {
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, {2}));", ilVar, opCode.ConstantName(), ResolveType(type));
		}

        protected void AddCilInstruction<T>(string ilVar, OpCode opCode, T arg)
        {
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, {2}));", ilVar, opCode.ConstantName(), arg);
		}

        protected void AddCilInstructionCastOperand<T>(string ilVar, OpCode opCode, T arg)
        {
        	AddCecilExpression(@"{0}.Append({0}.Create({1}, ({2}) {3}));", ilVar, opCode.ConstantName(), typeof(T).Name, arg);
		}

        protected void AddCilInstruction(string ilVar, OpCode opCode)
		{
			AddCecilExpression(@"{0}.Append({0}.Create({1}));", ilVar, opCode.ConstantName());
		}

		protected MethodSymbol DeclaredSymbolFor<T>(T node) where T : BaseMethodDeclarationSyntax
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected TypeSymbol DeclaredSymbolFor(TypeDeclarationSyntax node)
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected TypeInfo SemanticInfoFor(TypeSyntax node)
		{
			return Context.GetTypeInfo(node);
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

		//FIXME: Duplicated in MethodExtensions
		protected string LocalVariableNameFor(string prefix, params string[] parts)
		{
			return parts.Aggregate(prefix, (acc, curr) => acc + "_" + curr);
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
			return node.Parent.Kind != SyntaxKind.NamespaceDeclaration && node.Parent.Kind != SyntaxKind.CompilationUnit;
		}

		private static string DefaultTypeAttributeFor(TypeDeclarationSyntax node)
		{
			const string basicClassAttrs = "TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit";
			switch (node.Kind)
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
			return ExcludeHasNoCILRepresentation(token) && token.Kind != SyntaxKind.PrivateKeyword;
		}

		protected static bool ExcludeHasNoCILRepresentation(SyntaxToken token)
		{
			return token.Kind != SyntaxKind.PartialKeyword && token.Kind != SyntaxKind.VolatileKeyword;
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

		protected string ResolveType(TypeSymbol type)
		{
			var resolved = ResolveTypeLocalVariable(type.Name);
			return resolved 
					?? ResolvePredefinedAndArrayTypes(type) 
					?? ResolveType(type.Name);
		}
		
		protected string ResolveType(TypeSyntax type)
		{
			var resolved = ResolveTypeLocalVariable(type.ToString());
			return resolved 
					?? ResolvePredefinedAndArrayTypes(type) 
					?? ResolveType(type.ToString());
		}

		private string ResolvePredefinedAndArrayTypes(TypeSymbol type)
		{
			if (type.SpecialType == SpecialType.None) return null;

			if (type.SpecialType == SpecialType.System_Array)
			{
				var ats = (ArrayTypeSymbol) type;
				return "new ArrayType(" + ResolveType(ats.ElementType) + ")";
			}

			return ResolvePredefinedType(type);
		}
		
		private string ResolvePredefinedAndArrayTypes(TypeSyntax type)
		{
			switch (type.Kind)
			{
				case SyntaxKind.PredefinedType: return ResolvePredefinedType(Context.GetTypeInfo(type).Type);
				case SyntaxKind.ArrayType: return "new ArrayType(" + ResolveType(type.DescendantNodes().OfType<TypeSyntax>().Single()) + ")";
			}
			return null;
		}

		protected string ResolvePredefinedType(TypeSymbol typeSymbol)
		{
			return "assembly.MainModule.TypeSystem." + typeSymbol.Name;
		}

		protected string ResolveType(string typeName)
		{
			return ImportExpressionFor(typeName);
		}

		protected void RegisterTypeLocalVariable(TypeDeclarationSyntax node, string varName, Action<string, BaseTypeDeclarationSyntax> ctorInjector)
		{
			Context.RegisterTypeLocalVariable(node, varName, ctorInjector);
		}

		protected NamedTypeSymbol GetSpecialType(SpecialType specialType)
		{
			return Context.GetSpecialType(specialType);
		}

		protected string LocalVariableIndex(string methodVar, LocalSymbol localVariable)
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

		protected string LocalVariableIndexWithCast<CASTTYPE>(string localVariable)
		{
			return "(" + typeof(CASTTYPE).Name +")" + LocalVariableIndex(LocalVariableNameForCurrentNode(), localVariable);
		}

		protected const string ModifiersSeparator = " | ";

	}
}
