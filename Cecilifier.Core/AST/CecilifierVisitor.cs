using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Cecilifier.Core.Extensions;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;

namespace Cecilifier.Core.AST
{
	class CecilifierVisitor : SyntaxWalker
	{
		public CecilifierVisitor(SemanticModel semanticModel)
		{
			this.semanticModel = semanticModel;
		}


		protected override void VisitBlock(BlockSyntax node)
		{
			var parent = nodeStack.Peek();
			if (parent.Kind == SyntaxKind.MethodDeclaration)
			{
				//Console.WriteLine("------ BLOCK --------> {0}",node);
			}

			base.VisitBlock(node);
		}

		protected override void VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			EnsureCurrentTypeHasADefaultCtor();

			var varName = LocalVariableNameForId(NextLocalVariableTypeId());

			var baseType = ProcessBase(node.BaseListOpt);
			AddCecilExpression("TypeDefinition {0} = new TypeDefinition(\"\", \"{1}\", {2}{3});", varName, node.Identifier.Value, TypeModifiersToCecil(node), ", " + baseType);

			foreach(var itfName in ImplementedInterfacesFor(node.BaseListOpt))
			{
				AddCecilExpression("{0}.Interfaces.Add({1});", varName, ResolveType(itfName));
			}

			AddCecilExpression("assembly.MainModule.Types.Add({0});", varName);

			SetDeclaringType(node, varName);

			base.VisitClassDeclaration(node);
			typeToTypeInfo[node].CtorInjector = DefaultCtorInjector;
		}

		protected override void VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			foreach (var field in node.Declaration.Variables)
			{
				var fieldAttributes = FieldModifiersToCecil(node);

				var type = EnsureWrappedIfArray(ResolveType(node.Declaration.Type), node.Declaration.Type);
				var fieldId = string.Format("ft{0}", NextLocalVariableId());
				var fieldType = ProcessRequiredModifiers(node, type) ?? type;
				var fieldDeclaration = string.Format("var {0} = new FieldDefinition(\"{1}\", {2}, {3});", 
																fieldId, 
																field.Identifier.Value, 
																fieldAttributes, 
																fieldType);
				
				AddCecilExpression(fieldDeclaration);
				AddCecilExpression("{0}.Fields.Add({1});", LocalVariableNameForId(currentTypeId), fieldId);
			}

			base.VisitFieldDeclaration(node);
		}

		protected override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			var declaringType = (TypeDeclarationSyntax)node.Parent;
			typeToTypeInfo[declaringType].CtorInjector = delegate { };
		}

		protected override void VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			var methodVar = LocalVariableNameFor("method", node.Identifier.ValueText);
			AddCecilExpression("var {0} = new MethodDefinition(\"{1}\", {2}, {3});", methodVar, MethodNameOf(node), MethodModifiersToCecil(node), ResolveType(node.ReturnType));

			var declaringType = (TypeDeclarationSyntax)node.Parent;
			var id = ResolveType(declaringType.Identifier.ValueText);
			AddCecilExpression("{0}.Methods.Add({1});", id, methodVar);

			WithCurrentNode(node, () => base.VisitMethodDeclaration(node));
		}

		protected override void VisitParameter(ParameterSyntax node)
		{
			var methodVar = LocalVariableNameForCurrentNode("method");
			AddCecilExpression("{0}.Parameters.Add(new ParameterDefinition(\"{1}\", ParameterAttributes.None, {2}));", methodVar, node.Identifier.ValueText, ResolveType(node.TypeOpt));
			base.VisitParameter(node);
		}

		protected override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			AddCecilExpression("[PropertyDeclaration] {0}", node);
			base.VisitPropertyDeclaration(node);
		}

		protected override void VisitAccessorList(AccessorListSyntax node)
		{
			AddCecilExpression("[AccessorListSyntax] {0}", node);
			base.VisitAccessorList(node);
		}

		protected override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
		{
			AddCecilExpression("[AnonymousMethodExpression] {0}", node);
			base.VisitAnonymousMethodExpression(node);
		}

		protected override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
		{
			AddCecilExpression("[AnonymousObjectCreationExpression] {0}", node);
			base.VisitAnonymousObjectCreationExpression(node);
		}

		protected override void VisitArgument(ArgumentSyntax node)
		{
			AddCecilExpression("[Argument] {0}", node);
			base.VisitArgument(node);
		}

		protected override void VisitArgumentList(ArgumentListSyntax node)
		{
			AddCecilExpression("[ArgumentList] {0}", node);
			base.VisitArgumentList(node);
		}

		public string Output
		{
			get
			{
				EnsureCurrentTypeHasADefaultCtor();
				return buffer.ToString();
			}
		}

		private string EnsureWrappedIfArray(string elementType, TypeSyntax typeSyntax)
		{
			return typeSyntax.Kind == SyntaxKind.ArrayType 
								? "new ArrayType(" + elementType + ")" 
								: elementType;
		}

		private string ProcessRequiredModifiers(FieldDeclarationSyntax fieldDeclaration, string originalType)
		{
			if (fieldDeclaration.Modifiers.Any(m => m.ContextualKind == SyntaxKind.VolatileKeyword))
			{
				var id = string.Format("mod_req{0}", NextLocalVariableId());
				var mod_req = string.Format("var {0} = new RequiredModifierType({1}, {2});", id, originalType, ImportExpressionFor(typeof(IsVolatile)));
				AddCecilExpression(mod_req);
				return id;
			}

			return null;
		}

		private string MethodModifiersToCecil(MethodDeclarationSyntax methodDeclaration)
		{
			var cecilModifiersStr = ModifiersToCecil("MethodAttributes", methodDeclaration.Modifiers);
			var methodSymbol = (MethodSymbol) semanticModel.GetDeclaredSymbol(methodDeclaration);

			//TypeSymbol itf = semanticModel.Compilation.GetSpecialType(SpecialType.System_IDisposable);

			var x = methodSymbol.ContainingType.Interfaces.SelectMany(i => i.GetMembers()).Where(m => m.Name == methodSymbol.Name);
			
			//TypeSymbol declaringType = itf;
			var declaringType = methodSymbol.ContainingType.FindImplementationForInterfaceMember(methodSymbol.ConstructedFrom);
			//Console.WriteLine("{0} -> {1}", declaringType.Name, declaringType.TypeKind);
			
			
			return (cecilModifiersStr == string.Empty ? "MethodAttributes.Private" : cecilModifiersStr) + " | MethodAttributes.HideBySig";
		}

		private static string TypeModifiersToCecil(ClassDeclarationSyntax node)
		{
			var convertedModifiers = ModifiersToCecil("TypeAttributes", node.Modifiers);
			return "TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit".AppendModifier(convertedModifiers);
		}

		private static string FieldModifiersToCecil(FieldDeclarationSyntax node)
		{
			var modifiers = ModifiersToCecil("FieldAttributes", node.Modifiers);
			return modifiers;
		}

		private static string ModifiersToCecil(string targetEnum, SyntaxTokenList modifiers)
		{
			var validModifiers = modifiers.Where(ExcludeHasNoMetadataRepresentation);
			if (validModifiers.Count() == 0) return string.Empty;

			var cecilModifierStr = validModifiers.Aggregate("", (acc, token) => acc + (ModifiersSeparator + targetEnum + "." + token.ValueText.CamelCase()));
			return cecilModifierStr.Substring(ModifiersSeparator.Length);
		}

		private static bool ExcludeHasNoMetadataRepresentation(SyntaxToken token)
		{
			return token.ContextualKind != SyntaxKind.PartialKeyword && token.ContextualKind != SyntaxKind.VolatileKeyword;
		}

		private void SetDeclaringType(ClassDeclarationSyntax classDeclaration, string localVariable)
		{
			typeToTypeInfo[classDeclaration] = new TypeInfo(localVariable);
			if (classDeclaration.Parent.Kind == SyntaxKind.ClassDeclaration)
			{
				AddCecilExpression("{0}.DeclaringType = {1};", localVariable, typeToTypeInfo[(TypeDeclarationSyntax) classDeclaration.Parent]);
			}
		}

		private int NextLocalVariableTypeId()
		{
			return ++currentTypeId;
		}

		private int NextLocalVariableId()
		{
			return ++currentFieldId;
		}

		private string ImportExpressionFor(Type type)
		{
			var typeName = type.FullName;
			return ImportExpressionFor(typeName);
		}

		private static string ImportExpressionFor(string typeName)
		{
			return string.Format("assembly.MainModule.Import(typeof({0}))", typeName);
		}

		private string ResolveType(string typeName)
		{
			var typeDeclaration = typeToTypeInfo.Keys.OfType<TypeDeclarationSyntax>().Where(td => td.Identifier.ValueText == typeName).SingleOrDefault();
			if (typeDeclaration != null)
			{
				return typeToTypeInfo[typeDeclaration].LocalVariable;
			}

			return ImportExpressionFor(typeName);
		}

		private string ResolveType(TypeSyntax type)
		{
			switch(type.Kind)
			{
				case SyntaxKind.PredefinedType: return "assembly.MainModule.TypeSystem." + semanticModel.GetSemanticInfo(type).Type.Name;
				case SyntaxKind.ArrayType: return "new ArrayType(" + ResolveType(type.DescendentNodes().OfType<TypeSyntax>().Single()) + ")";
			}

			return ResolveType(type.PlainName);
		}


		private string ProcessBase(BaseListSyntax bases)
		{
			if (bases == null) return ImportExpressionFor(typeof(object));

			TypeSyntax baseClass = FindBaseClass(bases);
			if (baseClass == null) return ImportExpressionFor(typeof(object));
			
			return ImportExpressionFor(baseClass.PlainName);
		}

		private IEnumerable<string> ImplementedInterfacesFor(BaseListSyntax bases)
		{
			if (bases == null) yield break;

			foreach (var @base in bases.Types)
			{
				var info = semanticModel.GetSemanticInfo(@base);
				if (info.Type.TypeKind == TypeKind.Interface)
				{
					var itfFQName = @base.DescendentTokens().OfType<SyntaxToken>().Aggregate("", (acc, curr) => acc + curr.ValueText);
					yield return itfFQName;
				}
			}
		}

		private TypeSyntax FindBaseClass(BaseListSyntax bases)
		{
			foreach (var @base in bases.Types)
			{
				var info = semanticModel.GetSemanticInfo(@base);
				if (info.Type.TypeKind == TypeKind.Class) return @base;
			}
			return null;
		}
		
		private string MethodNameOf(MethodDeclarationSyntax method)
		{
			var symbol = semanticModel.GetDeclaredSymbol(method);
			return symbol.Name;
		}

		private void EnsureCurrentTypeHasADefaultCtor()
		{
			foreach (var pair in typeToTypeInfo)
			{
				pair.Value.CtorInjector(pair.Value.LocalVariable, pair.Key);
				pair.Value.CtorInjector = delegate { };
			}
		}

		private void DefaultCtorInjector(string localVar, TypeDeclarationSyntax declaringClass)
		{
			var ctorLocalVar = TempLocalVar("ctor");
			AddCecilExpression(@"var {0} = new MethodDefinition("".ctor"", MethodAttributes.RTSpecialName | MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName, assembly.MainModule.TypeSystem.Void);", ctorLocalVar);
			AddCecilExpression(@"{0}.Methods.Add({1});", localVar, ctorLocalVar);
			var ilVar = TempLocalVar("il");
			AddCecilExpression(@"var {0} = {1}.Body.GetILProcessor();", ilVar, ctorLocalVar);

			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ldarg_0));", ctorLocalVar, ilVar);
			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Call, assembly.MainModule.Import(DefaultCtorFor({2}.BaseType.Resolve(), assembly))));", ctorLocalVar, ilVar, localVar);
			AddCecilExpression(@"{0}.Body.Instructions.Add({1}.Create(OpCodes.Ret));", ctorLocalVar, ilVar);
		}

		private void AddCecilExpression(string format, params object[] args)
		{
			buffer.AppendFormat("{0}\r\n", string.Format(format, args));
		}

		private void WithCurrentNode(MemberDeclarationSyntax node, Action action)
		{
			try
			{
				nodeStack.Push(node);
				action();
			}
			finally
			{
				nodeStack.Pop();
			}
		}

		private string TempLocalVar(string prefix)
		{
			return prefix + NextLocalVariableId();
		}

		private static string LocalVariableNameForId(int localVarId)
		{
			return "t" + localVarId;
		}

		private string LocalVariableNameFor(string prefix, string name)
		{
			return prefix + "_" + name;
		}

		private string LocalVariableNameForCurrentNode(string prefix)
		{
			var node = nodeStack.Peek();
			var identifier = node.ChildNodesAndTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken).SingleOrDefault();
			return LocalVariableNameFor(prefix, identifier.GetText());
		}

		private const string ModifiersSeparator = " | ";

		private StringBuilder buffer = new StringBuilder();
		private int currentTypeId;
		private int currentFieldId;
		private readonly SemanticModel semanticModel;
		private IDictionary<TypeDeclarationSyntax, TypeInfo> typeToTypeInfo = new Dictionary<TypeDeclarationSyntax, TypeInfo>();
		private Stack<SyntaxNode> nodeStack = new Stack<SyntaxNode>();


		private	class TypeInfo
		{
			public readonly string LocalVariable;
			public Action<string, TypeDeclarationSyntax> CtorInjector;

			public TypeInfo(string localVariable)
			{
				LocalVariable = localVariable;
				CtorInjector = delegate { };
			}

			public override string ToString()
			{
				return LocalVariable;
			}
		}
	}
}
