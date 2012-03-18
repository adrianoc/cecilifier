using System;
using System.Collections.Generic;
using System.Linq;
using Cecilifier.Core.Extensions;
using Cecilifier.Core.Misc;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

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
		
        protected void AddCilInstruction(string methodVar, string ilVar, string instruction, object arg)
		{
            AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create({2}, {3}));", methodVar, ilVar, instruction, arg);
		}

        protected void AddCilInstruction(string methodVar, string ilVar, string instruction)
		{
            AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create({2}));", methodVar, ilVar, instruction);
		}

		protected MethodSymbol DeclaredSymbolFor<T>(T node) where T : BaseMethodDeclarationSyntax
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected TypeSymbol DeclaredSymbolFor(ClassDeclarationSyntax node)
		{
			return Context.GetDeclaredSymbol(node);
		}

		protected SemanticInfo SemanticInfoFor(TypeSyntax node)
		{
			return Context.GetSemanticInfo(node);
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

		protected static string TypeModifiersToCecil(TypeDeclarationSyntax node)
		{
			var convertedModifiers = ModifiersToCecil("TypeAttributes", node.Modifiers, "NotPublic");
			var typeAttribute = node.Kind == SyntaxKind.ClassDeclaration
									? "TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit"
									: "TypeAttributes.Interface | TypeAttributes.Abstract";

			return typeAttribute.AppendModifier(convertedModifiers);
		}

		protected static string ModifiersToCecil(string targetEnum, IEnumerable<SyntaxToken> modifiers, string @default)
		{
            var validModifiers = modifiers.Where(ExcludeHasNoCILRepresentation);
			if (validModifiers.Count() == 0) return targetEnum + "." + @default;

            var cecilModifierStr = validModifiers.Aggregate("", (acc, token) => acc + (ModifiersSeparator + token.MapModifier(targetEnum)));
			return cecilModifierStr.Substring(ModifiersSeparator.Length);
		}

		protected static bool ExcludeHasNoCILRepresentation(SyntaxToken token)
		{
			return token.Kind != SyntaxKind.PartialKeyword 
                && token.Kind != SyntaxKind.VolatileKeyword;
		}

		protected string ResolveLocalVariable(BaseTypeDeclarationSyntax typeDeclaration)
		{
			return ResolveLocalVariable(typeDeclaration.Identifier.ValueText);
		}

		protected string ResolveLocalVariable(string typeName)
		{
			return Context.ResolveLocalVariable(typeName);
		}

		protected string ResolveType(TypeSyntax type)
		{
			switch (type.Kind)
			{
				case SyntaxKind.PredefinedType: return ResolvePredefinedType(Context.GetSemanticInfo(type).Type);
				case SyntaxKind.ArrayType: return "new ArrayType(" + ResolveType(type.DescendentNodes().OfType<TypeSyntax>().Single()) + ")";
			}

			return ResolveType(type.PlainName);
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

		protected const string ModifiersSeparator = " | ";
	}
}
